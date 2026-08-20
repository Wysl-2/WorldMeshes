using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TerrainHeightmapStreamer))]
public class TerrainClipmapController :
    MonoBehaviour
{
    // =====================================================
    // SOURCE DATA
    // =====================================================

    [Header("Source Data")]

    [SerializeField]
    private WorldSettings worldSettings;

    // =====================================================
    // TARGET
    // =====================================================

    [Header("Target")]

    /*
     * Transform that the clipmap follows horizontally.
     *
     * Target Y is intentionally ignored.
     */
    [SerializeField]
    private Transform target;

    // =====================================================
    // DEBUG
    // =====================================================

    [Header("Debug")]

    [SerializeField]
    private bool logCoverageStalls =
        true;

    // =====================================================
    // SHADER PROPERTY IDS
    // =====================================================

    private static readonly int
        ClipmapTransitionOffsetPropertyId =
            Shader.PropertyToID(
                "_ClipmapTransitionOffset"
            );

    // =====================================================
    // RUNTIME STATE
    // =====================================================

    private TerrainHeightmapStreamer streamer;

    private float fixedY;

    private bool waitingForHeightData;

    private bool hasBlockedCoverageRequest;

    private Vector3 blockedCoverageCenter;

    private bool hasWarnedMissingSettings;

    private bool hasWarnedMissingTarget;

    private bool hasWarnedMissingStreamer;

    private bool hasWarnedMissingLODHierarchy;

    // =====================================================
    // LOD ANCHOR STATE
    // =====================================================

    /*
     * Desired absolute world-space center for every LOD.
     *
     * Stage 3C now applies these anchors to the generated
     * LOD hierarchy after height-cache safety is satisfied.
     */
    private Vector3[] desiredLODAnchors;

    /*
     * World-space vertex spacing for every LOD.
     */
    private float[] lodSpacings;

    private bool desiredLODAnchorsValid;

    /*
     * Actual world-space XZ bounds of the desired geometry.
     *
     * These are useful for validation/debugging. Height-cache
     * coverage itself can continue using the existing streamer
     * center API because the outermost ring has the same fixed
     * diameter used by TerrainHeightmapStreamer.
     */
    private Vector2 desiredClipmapMinimumXZ;

    private Vector2 desiredClipmapMaximumXZ;

    // =====================================================
    // GENERATED HIERARCHY REFERENCES
    // =====================================================

    /*
     * Index 0 is unused because LOD0 is represented by this
     * controller's root transform.
     *
     * Index N references the generated direct child "LODN".
     */
    private Transform[] lodLevelTransforms;

    /*
     * Index N references:
     *
     * Stitch_LOD(N-1)_LODN
     *
     * under generated child LODN.
     */
    private MeshRenderer[] stitchRenderers;

    private MaterialPropertyBlock
        transitionPropertyBlock;

    private bool lodHierarchyReferencesValid;

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public Transform Target
    {
        get
        {
            return target;
        }

        set
        {
            target =
                value;

            hasWarnedMissingTarget =
                false;

            InvalidateLODState();
        }
    }

    /*
     * True when gameplay movement is being held because the
     * desired clipmap footprint is not yet represented by the
     * active height cache.
     */
    public bool IsWaitingForHeightData
    {
        get
        {
            return waitingForHeightData;
        }
    }

    public bool DesiredLODAnchorsValid
    {
        get
        {
            return desiredLODAnchorsValid;
        }
    }

    public int DesiredLODAnchorCount
    {
        get
        {
            if (
                !desiredLODAnchorsValid
                ||
                desiredLODAnchors == null
            )
            {
                return 0;
            }

            return
                desiredLODAnchors.Length;
        }
    }

    // =====================================================
    // TRY GET DESIRED LOD ANCHOR
    // =====================================================

    public bool TryGetDesiredLODAnchor(
        int level,
        out Vector3 anchor,
        out float spacing
    )
    {
        anchor =
            Vector3.zero;

        spacing =
            0f;

        if (
            !desiredLODAnchorsValid
            ||
            desiredLODAnchors == null
            ||
            lodSpacings == null
            ||
            level < 0
            ||
            level >= desiredLODAnchors.Length
            ||
            level >= lodSpacings.Length
        )
        {
            return false;
        }

        anchor =
            desiredLODAnchors[
                level
            ];

        spacing =
            lodSpacings[
                level
            ];

        return true;
    }

    // =====================================================
    // TRY GET DESIRED CLIPMAP BOUNDS
    // =====================================================

    public bool TryGetDesiredClipmapBounds(
        out Vector2 minimumXZ,
        out Vector2 maximumXZ
    )
    {
        minimumXZ =
            Vector2.zero;

        maximumXZ =
            Vector2.zero;

        if (!desiredLODAnchorsValid)
        {
            return false;
        }

        minimumXZ =
            desiredClipmapMinimumXZ;

        maximumXZ =
            desiredClipmapMaximumXZ;

        return true;
    }

    // =====================================================
    // EDITOR / HIERARCHY CONFIGURATION
    // =====================================================

    /*
     * Called by TerrainWorldHierarchyGenerator.
     *
     * The movement target is deliberately not changed here so a
     * manually assigned Player reference survives hierarchy sync.
     */
    public bool Configure(
        WorldSettings settings
    )
    {
        if (
            worldSettings ==
            settings
        )
        {
            return false;
        }

        worldSettings =
            settings;

        InvalidateLODState();

        return true;
    }

    // =====================================================
    // ENABLE
    // =====================================================

    private void OnEnable()
    {
        fixedY =
            transform.position.y;

        waitingForHeightData =
            false;

        hasBlockedCoverageRequest =
            false;

        hasWarnedMissingSettings =
            false;

        hasWarnedMissingTarget =
            false;

        hasWarnedMissingStreamer =
            false;

        hasWarnedMissingLODHierarchy =
            false;

        InvalidateLODState();

        EnsureStreamerReference();

        if (!Application.isPlaying)
        {
            return;
        }

        UpdateClipmapPosition();
    }

    // =====================================================
    // DISABLE
    // =====================================================

    private void OnDisable()
    {
        if (
            Application.isPlaying
            &&
            streamer != null
        )
        {
            streamer.ClearCoverageRequest();
        }

        if (Application.isPlaying)
        {
            ResetLODGeometry();
        }

        waitingForHeightData =
            false;

        hasBlockedCoverageRequest =
            false;

        InvalidateLODState();
    }

    // =====================================================
    // LATE UPDATE
    // =====================================================

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        UpdateClipmapPosition();
    }

    // =====================================================
    // CAN TARGET MOVE TO
    // =====================================================

    /*
     * Gameplay movement gate.
     *
     * The proposed Player position is converted into the full
     * independently-snapped LOD layout BEFORE Player movement is
     * committed.
     *
     * Height-cache safety is evaluated around the OUTERMOST LOD
     * anchor rather than the LOD0/root anchor.
     *
     * The outermost ring has exactly the nominal clipmap diameter
     * already used by TerrainHeightmapStreamer, so this describes
     * the real Stage 3C outer footprint without changing the
     * streamer's cache architecture.
     */
    public bool CanTargetMoveTo(
        Vector3 targetWorldPosition
    )
    {
        if (
            !TryUpdateDesiredLODLayout(
                targetWorldPosition
            )
        )
        {
            return false;
        }

        EnsureStreamerReference();

        if (streamer == null)
        {
            return false;
        }

        Vector3 coverageCenter =
            GetDesiredCoverageCenter();

        streamer.RequestCoverageForClipmapCenter(
            coverageCenter
        );

        bool canMove =
            streamer.CanActiveCacheCoverClipmapAt(
                coverageCenter
            );

        if (!canMove)
        {
            hasBlockedCoverageRequest =
                true;

            blockedCoverageCenter =
                coverageCenter;

            SetWaitingForHeightData(
                true,
                coverageCenter
            );

            return false;
        }

        hasBlockedCoverageRequest =
            false;

        SetWaitingForHeightData(
            false,
            coverageCenter
        );

        return true;
    }

    // =====================================================
    // UPDATE CLIPMAP POSITION
    // =====================================================

    private void UpdateClipmapPosition()
    {
        EnsureStreamerReference();

        if (streamer == null)
        {
            if (!hasWarnedMissingStreamer)
            {
                Debug.LogWarning(
                    "TerrainClipmapController requires " +
                    "TerrainHeightmapStreamer on the same " +
                    "GameObject.",
                    this
                );

                hasWarnedMissingStreamer =
                    true;
            }

            return;
        }

        hasWarnedMissingStreamer =
            false;

        // =================================================
        // COMPLETE A BLOCKED GAMEPLAY COVERAGE REQUEST
        // =================================================

        /*
         * If CanTargetMoveTo() denied movement, preserve that
         * requested coverage instead of immediately replacing it
         * with the Player's still-unmoved current position.
         */
        if (hasBlockedCoverageRequest)
        {
            streamer.RequestCoverageForClipmapCenter(
                blockedCoverageCenter
            );

            if (
                !streamer.CanActiveCacheCoverClipmapAt(
                    blockedCoverageCenter
                )
            )
            {
                return;
            }

            hasBlockedCoverageRequest =
                false;

            SetWaitingForHeightData(
                false,
                blockedCoverageCenter
            );
        }

        // =================================================
        // CURRENT TARGET LAYOUT
        // =================================================

        if (target == null)
        {
            if (!hasWarnedMissingTarget)
            {
                Debug.LogWarning(
                    "TerrainClipmapController has no target.\n\n" +
                    "Assign the Player Transform to the Target " +
                    "field on the Clipmap GameObject.",
                    this
                );

                hasWarnedMissingTarget =
                    true;
            }

            return;
        }

        hasWarnedMissingTarget =
            false;

        if (
            !TryUpdateDesiredLODLayout(
                target.position
            )
        )
        {
            return;
        }

        Vector3 coverageCenter =
            GetDesiredCoverageCenter();

        // -------------------------------------------------
        // Request / prefetch desired cache coverage
        // -------------------------------------------------

        streamer.RequestCoverageForClipmapCenter(
            coverageCenter
        );

        // -------------------------------------------------
        // Active-cache safety
        // -------------------------------------------------

        if (
            !streamer.CanActiveCacheCoverClipmapAt(
                coverageCenter
            )
        )
        {
            SetWaitingForHeightData(
                true,
                coverageCenter
            );

            /*
             * Keep every clipmap transform and stitch offset at
             * its last safe state until the pending cache commits.
             */
            return;
        }

        SetWaitingForHeightData(
            false,
            coverageCenter
        );

        // =================================================
        // APPLY INDEPENDENT LOD PLACEMENT
        // =================================================

        ApplyDesiredLODGeometry();
    }

    // =====================================================
    // UPDATE DESIRED LOD LAYOUT
    // =====================================================

    private bool TryUpdateDesiredLODLayout(
        Vector3 targetWorldPosition
    )
    {
        if (worldSettings == null)
        {
            if (!hasWarnedMissingSettings)
            {
                Debug.LogWarning(
                    "TerrainClipmapController cannot calculate " +
                    "LOD placement because WorldSettings is not " +
                    "assigned.",
                    this
                );

                hasWarnedMissingSettings =
                    true;
            }

            InvalidateLODAnchorsOnly();

            return false;
        }

        hasWarnedMissingSettings =
            false;

        if (target == null)
        {
            if (!hasWarnedMissingTarget)
            {
                Debug.LogWarning(
                    "TerrainClipmapController has no target.\n\n" +
                    "Assign the Player Transform to the Target " +
                    "field on the Clipmap GameObject.",
                    this
                );

                hasWarnedMissingTarget =
                    true;
            }

            InvalidateLODAnchorsOnly();

            return false;
        }

        hasWarnedMissingTarget =
            false;

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                1,
                10
            );

        EnsureLODAnchorStorage(
            levelCount
        );

        float minimumX =
            float.PositiveInfinity;

        float minimumZ =
            float.PositiveInfinity;

        float maximumX =
            float.NegativeInfinity;

        float maximumZ =
            float.NegativeInfinity;

        int centerResolution =
            Mathf.Max(
                1,
                worldSettings.clipmapCenterResolution
            );

        for (
            int level = 0;
            level < levelCount;
            level++
        )
        {
            float spacing =
                GetLODSpacing(
                    level
                );

            lodSpacings[
                level
            ] =
                spacing;

            Vector3 anchor =
                new Vector3(
                    SnapCoordinate(
                        targetWorldPosition.x,
                        spacing
                    ),

                    fixedY,

                    SnapCoordinate(
                        targetWorldPosition.z,
                        spacing
                    )
                );

            desiredLODAnchors[
                level
            ] =
                anchor;

            /*
             * Every LOD uses centerResolution cells across its
             * outer square. LOD0 is the center square and outer
             * levels are rings with the same outer diameter for
             * their own spacing.
             */
            float halfExtent =
                centerResolution *
                spacing *
                0.5f;

            minimumX =
                Mathf.Min(
                    minimumX,
                    anchor.x -
                    halfExtent
                );

            maximumX =
                Mathf.Max(
                    maximumX,
                    anchor.x +
                    halfExtent
                );

            minimumZ =
                Mathf.Min(
                    minimumZ,
                    anchor.z -
                    halfExtent
                );

            maximumZ =
                Mathf.Max(
                    maximumZ,
                    anchor.z +
                    halfExtent
                );
        }

        desiredClipmapMinimumXZ =
            new Vector2(
                minimumX,
                minimumZ
            );

        desiredClipmapMaximumXZ =
            new Vector2(
                maximumX,
                maximumZ
            );

        desiredLODAnchorsValid =
            true;

        return true;
    }

    // =====================================================
    // DESIRED COVERAGE CENTER
    // =====================================================

    private Vector3 GetDesiredCoverageCenter()
    {
        if (
            !desiredLODAnchorsValid
            ||
            desiredLODAnchors == null
            ||
            desiredLODAnchors.Length == 0
        )
        {
            return transform.position;
        }

        /*
         * The outermost LOD ring defines the outside boundary
         * of the complete clipmap.
         *
         * Its diameter is exactly:
         *
         * centerResolution *
         * ClipmapBaseSpacing *
         * 2^(levelCount - 1)
         *
         * which is the diameter TerrainHeightmapStreamer already
         * uses for coverage checks.
         */
        return
            desiredLODAnchors[
                desiredLODAnchors.Length - 1
            ];
    }

    // =====================================================
    // APPLY DESIRED LOD GEOMETRY
    // =====================================================

    private void ApplyDesiredLODGeometry()
    {
        if (
            !desiredLODAnchorsValid
            ||
            desiredLODAnchors == null
            ||
            desiredLODAnchors.Length == 0
        )
        {
            return;
        }

        if (!EnsureLODHierarchyReferences())
        {
            return;
        }

        Vector3 lod0Anchor =
            desiredLODAnchors[0];

        // -------------------------------------------------
        // LOD0 / clipmap root
        // -------------------------------------------------

        if (
            transform.position !=
            lod0Anchor
        )
        {
            transform.position =
                lod0Anchor;
        }

        // -------------------------------------------------
        // Coarse LOD groups
        // -------------------------------------------------

        for (
            int level = 1;
            level < desiredLODAnchors.Length;
            level++
        )
        {
            Transform levelTransform =
                lodLevelTransforms[
                    level
                ];

            Vector3 localOffset =
                desiredLODAnchors[
                    level
                ]
                -
                lod0Anchor;

            /*
             * All generated clipmap groups remain at the root Y.
             */
            localOffset.y =
                0f;

            if (
                levelTransform.localPosition !=
                localOffset
            )
            {
                levelTransform.localPosition =
                    localOffset;
            }

            if (
                levelTransform.localRotation !=
                Quaternion.identity
            )
            {
                levelTransform.localRotation =
                    Quaternion.identity;
            }

            if (
                levelTransform.localScale !=
                Vector3.one
            )
            {
                levelTransform.localScale =
                    Vector3.one;
            }
        }

        // -------------------------------------------------
        // Adaptive stitch shader offsets
        // -------------------------------------------------

        if (transitionPropertyBlock == null)
        {
            transitionPropertyBlock =
                new MaterialPropertyBlock();
        }

        for (
            int coarseLevel = 1;
            coarseLevel <
                desiredLODAnchors.Length;
            coarseLevel++
        )
        {
            MeshRenderer stitchRenderer =
                stitchRenderers[
                    coarseLevel
                ];

            Vector3 fineMinusCoarse =
                desiredLODAnchors[
                    coarseLevel - 1
                ]
                -
                desiredLODAnchors[
                    coarseLevel
                ];

            stitchRenderer.GetPropertyBlock(
                transitionPropertyBlock
            );

            transitionPropertyBlock.SetVector(
                ClipmapTransitionOffsetPropertyId,
                new Vector4(
                    fineMinusCoarse.x,
                    0f,
                    fineMinusCoarse.z,
                    0f
                )
            );

            stitchRenderer.SetPropertyBlock(
                transitionPropertyBlock
            );
        }
    }

    // =====================================================
    // ENSURE LOD HIERARCHY REFERENCES
    // =====================================================

    private bool EnsureLODHierarchyReferences()
    {
        if (worldSettings == null)
        {
            return false;
        }

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                1,
                10
            );

        bool storageMatches =
            lodLevelTransforms != null
            &&
            stitchRenderers != null
            &&
            lodLevelTransforms.Length ==
                levelCount
            &&
            stitchRenderers.Length ==
                levelCount;

        if (
            lodHierarchyReferencesValid
            &&
            storageMatches
        )
        {
            bool referencesStillValid =
                true;

            for (
                int level = 1;
                level < levelCount;
                level++
            )
            {
                if (
                    lodLevelTransforms[
                        level
                    ] == null
                    ||
                    stitchRenderers[
                        level
                    ] == null
                )
                {
                    referencesStillValid =
                        false;

                    break;
                }
            }

            if (referencesStillValid)
            {
                return true;
            }
        }

        lodLevelTransforms =
            new Transform[
                levelCount
            ];

        stitchRenderers =
            new MeshRenderer[
                levelCount
            ];

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            string levelName =
                $"LOD{level}";

            Transform levelTransform =
                transform.Find(
                    levelName
                );

            if (levelTransform == null)
            {
                WarnMissingLODHierarchy(
                    $"Missing generated clipmap group '{levelName}'."
                );

                lodHierarchyReferencesValid =
                    false;

                return false;
            }

            string stitchName =
                $"Stitch_LOD{level - 1}_LOD{level}";

            Transform stitchTransform =
                levelTransform.Find(
                    stitchName
                );

            if (stitchTransform == null)
            {
                WarnMissingLODHierarchy(
                    $"Missing generated stitch '{stitchName}' " +
                    $"under '{levelName}'."
                );

                lodHierarchyReferencesValid =
                    false;

                return false;
            }

            MeshRenderer stitchRenderer =
                stitchTransform
                    .GetComponent<MeshRenderer>();

            if (stitchRenderer == null)
            {
                WarnMissingLODHierarchy(
                    $"Generated stitch '{stitchName}' has no " +
                    "MeshRenderer."
                );

                lodHierarchyReferencesValid =
                    false;

                return false;
            }

            lodLevelTransforms[
                level
            ] =
                levelTransform;

            stitchRenderers[
                level
            ] =
                stitchRenderer;
        }

        lodHierarchyReferencesValid =
            true;

        hasWarnedMissingLODHierarchy =
            false;

        return true;
    }

    // =====================================================
    // WARN MISSING LOD HIERARCHY
    // =====================================================

    private void WarnMissingLODHierarchy(
        string detail
    )
    {
        if (hasWarnedMissingLODHierarchy)
        {
            return;
        }

        Debug.LogWarning(
            "TerrainClipmapController could not apply " +
            "independent LOD placement.\n\n" +
            detail +
            "\n\nRun Sync World Hierarchy after generating " +
            "the clipmap meshes.",
            this
        );

        hasWarnedMissingLODHierarchy =
            true;
    }

    // =====================================================
    // RESET LOD GEOMETRY
    // =====================================================

    private void ResetLODGeometry()
    {
        if (!EnsureLODHierarchyReferences())
        {
            return;
        }

        if (transitionPropertyBlock == null)
        {
            transitionPropertyBlock =
                new MaterialPropertyBlock();
        }

        for (
            int level = 1;
            level < lodLevelTransforms.Length;
            level++
        )
        {
            if (
                lodLevelTransforms[
                    level
                ] != null
            )
            {
                lodLevelTransforms[
                    level
                ].localPosition =
                    Vector3.zero;
            }

            MeshRenderer stitchRenderer =
                stitchRenderers[
                    level
                ];

            if (stitchRenderer == null)
            {
                continue;
            }

            stitchRenderer.GetPropertyBlock(
                transitionPropertyBlock
            );

            transitionPropertyBlock.SetVector(
                ClipmapTransitionOffsetPropertyId,
                Vector4.zero
            );

            stitchRenderer.SetPropertyBlock(
                transitionPropertyBlock
            );
        }
    }

    // =====================================================
    // SET WAITING STATE
    // =====================================================

    private void SetWaitingForHeightData(
        bool waiting,
        Vector3 desiredCoverageCenter
    )
    {
        if (
            waitingForHeightData ==
            waiting
        )
        {
            return;
        }

        waitingForHeightData =
            waiting;

        if (!logCoverageStalls)
        {
            return;
        }

        if (waiting)
        {
            Debug.Log(
                "Terrain clipmap movement is waiting " +
                "for height-cache coverage.\n\n" +
                $"Desired Coverage Center: " +
                $"({desiredCoverageCenter.x}, " +
                $"{desiredCoverageCenter.y}, " +
                $"{desiredCoverageCenter.z})",
                this
            );

            return;
        }

        Debug.Log(
            "Terrain height-cache coverage is ready. " +
            "Clipmap movement resumed.",
            this
        );
    }

    // =====================================================
    // ENSURE LOD ANCHOR STORAGE
    // =====================================================

    private void EnsureLODAnchorStorage(
        int levelCount
    )
    {
        int safeLevelCount =
            Mathf.Max(
                1,
                levelCount
            );

        if (
            desiredLODAnchors == null
            ||
            desiredLODAnchors.Length !=
                safeLevelCount
        )
        {
            desiredLODAnchors =
                new Vector3[
                    safeLevelCount
                ];
        }

        if (
            lodSpacings == null
            ||
            lodSpacings.Length !=
                safeLevelCount
        )
        {
            lodSpacings =
                new float[
                    safeLevelCount
                ];
        }
    }

    // =====================================================
    // INVALIDATE LOD STATE
    // =====================================================

    private void InvalidateLODState()
    {
        InvalidateLODAnchorsOnly();

        lodHierarchyReferencesValid =
            false;

        lodLevelTransforms =
            null;

        stitchRenderers =
            null;
    }

    private void InvalidateLODAnchorsOnly()
    {
        desiredLODAnchorsValid =
            false;
    }

    // =====================================================
    // GET LOD SPACING
    // =====================================================

    private float GetLODSpacing(
        int level
    )
    {
        if (worldSettings == null)
        {
            return 1f;
        }

        float baseSpacing =
            Mathf.Max(
                0.0001f,
                worldSettings.ClipmapBaseSpacing
            );

        int safeLevel =
            Mathf.Max(
                0,
                level
            );

        return
            baseSpacing
            *
            Mathf.Pow(
                2f,
                safeLevel
            );
    }

    // =====================================================
    // SNAP COORDINATE
    // =====================================================

    private static float SnapCoordinate(
        float coordinate,
        float spacing
    )
    {
        float safeSpacing =
            Mathf.Max(
                0.0001f,
                spacing
            );

        return
            Mathf.Round(
                coordinate /
                safeSpacing
            )
            *
            safeSpacing;
    }

    // =====================================================
    // VALIDATE LOD ANCHORS / PLACEMENT
    // =====================================================

    [ContextMenu("Validate Clipmap LOD Anchors")]
    public void ValidateLODAnchors()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "Clipmap LOD anchor validation should be run " +
                "in Play Mode.",
                this
            );

            return;
        }

        if (waitingForHeightData)
        {
            Debug.LogWarning(
                "Clipmap LOD validation was not run because " +
                "movement is currently waiting for height-cache " +
                "coverage.",
                this
            );

            return;
        }

        if (
            worldSettings == null
            ||
            target == null
        )
        {
            Debug.LogError(
                "Cannot validate clipmap LOD anchors.\n\n" +
                "WorldSettings and Target must both be assigned.",
                this
            );

            return;
        }

        if (
            !TryUpdateDesiredLODLayout(
                target.position
            )
        )
        {
            return;
        }

        if (!EnsureLODHierarchyReferences())
        {
            return;
        }

        float baseSpacing =
            Mathf.Max(
                0.0001f,
                worldSettings.ClipmapBaseSpacing
            );

        float tolerance =
            Mathf.Max(
                0.00001f,
                baseSpacing *
                0.00001f
            );

        int gridMismatchCount =
            0;

        int adjacentOffsetMismatchCount =
            0;

        int appliedTransformMismatchCount =
            0;

        int stitchOffsetMismatchCount =
            0;

        float maximumGridDifference =
            0f;

        float maximumAdjacentOffsetDifference =
            0f;

        float maximumAppliedTransformDifference =
            0f;

        float maximumStitchOffsetDifference =
            0f;

        string firstProblem =
            null;

        string report =
            "Clipmap LOD anchor validation.\n\n" +

            $"Target Position: " +
            $"({target.position.x:R}, " +
            $"{target.position.y:R}, " +
            $"{target.position.z:R})\n\n" +

            $"Clipmap Root Position: " +
            $"({transform.position.x:R}, " +
            $"{transform.position.y:R}, " +
            $"{transform.position.z:R})\n\n" +

            $"LOD Levels: " +
            $"{desiredLODAnchors.Length}\n" +

            $"Base Spacing: " +
            $"{baseSpacing:R}\n\n" +

            "Desired LOD Anchors:\n";

        // =================================================
        // GRID ALIGNMENT
        // =================================================

        for (
            int level = 0;
            level < desiredLODAnchors.Length;
            level++
        )
        {
            Vector3 anchor =
                desiredLODAnchors[
                    level
                ];

            float spacing =
                lodSpacings[
                    level
                ];

            float expectedX =
                SnapCoordinate(
                    anchor.x,
                    spacing
                );

            float expectedZ =
                SnapCoordinate(
                    anchor.z,
                    spacing
                );

            float levelDifference =
                Mathf.Max(
                    Mathf.Abs(
                        anchor.x -
                        expectedX
                    ),
                    Mathf.Abs(
                        anchor.z -
                        expectedZ
                    )
                );

            maximumGridDifference =
                Mathf.Max(
                    maximumGridDifference,
                    levelDifference
                );

            if (
                levelDifference >
                tolerance
            )
            {
                gridMismatchCount++;

                if (firstProblem == null)
                {
                    firstProblem =
                        $"LOD{level} is not aligned to its " +
                        "own grid.";
                }
            }

            report +=
                $"LOD{level}: " +
                $"Spacing {spacing:R}, " +
                $"Center " +
                $"({anchor.x:R}, " +
                $"{anchor.y:R}, " +
                $"{anchor.z:R})\n";
        }

        // =================================================
        // ADJACENT OFFSETS
        // =================================================

        report +=
            "\nAdjacent LOD Offsets " +
            "(Fine Center - Coarse Center):\n";

        for (
            int coarseLevel = 1;
            coarseLevel <
                desiredLODAnchors.Length;
            coarseLevel++
        )
        {
            int fineLevel =
                coarseLevel - 1;

            Vector3 fineAnchor =
                desiredLODAnchors[
                    fineLevel
                ];

            Vector3 coarseAnchor =
                desiredLODAnchors[
                    coarseLevel
                ];

            float fineSpacing =
                lodSpacings[
                    fineLevel
                ];

            Vector3 offset =
                fineAnchor -
                coarseAnchor;

            float offsetDifference =
                Mathf.Max(
                    DistanceFromValidAdjacentOffset(
                        offset.x,
                        fineSpacing
                    ),
                    DistanceFromValidAdjacentOffset(
                        offset.z,
                        fineSpacing
                    )
                );

            maximumAdjacentOffsetDifference =
                Mathf.Max(
                    maximumAdjacentOffsetDifference,
                    offsetDifference
                );

            if (
                offsetDifference >
                tolerance
            )
            {
                adjacentOffsetMismatchCount++;

                if (firstProblem == null)
                {
                    firstProblem =
                        $"LOD{fineLevel} -> " +
                        $"LOD{coarseLevel} has an invalid " +
                        "relative center offset.";
                }
            }

            report +=
                $"LOD{fineLevel} -> " +
                $"LOD{coarseLevel}: " +
                $"({offset.x:R}, " +
                $"{offset.z:R})\n";
        }

        // =================================================
        // APPLIED TRANSFORMS
        // =================================================

        report +=
            "\nApplied LOD Transform Positions:\n";

        float rootDifference =
            Vector3.Distance(
                transform.position,
                desiredLODAnchors[0]
            );

        maximumAppliedTransformDifference =
            Mathf.Max(
                maximumAppliedTransformDifference,
                rootDifference
            );

        if (rootDifference > tolerance)
        {
            appliedTransformMismatchCount++;

            if (firstProblem == null)
            {
                firstProblem =
                    "Clipmap root does not match the desired " +
                    "LOD0 anchor.";
            }
        }

        report +=
            $"LOD0: " +
            $"({transform.position.x:R}, " +
            $"{transform.position.y:R}, " +
            $"{transform.position.z:R})\n";

        for (
            int level = 1;
            level < desiredLODAnchors.Length;
            level++
        )
        {
            Vector3 actualPosition =
                lodLevelTransforms[
                    level
                ].position;

            float difference =
                Vector3.Distance(
                    actualPosition,
                    desiredLODAnchors[
                        level
                    ]
                );

            maximumAppliedTransformDifference =
                Mathf.Max(
                    maximumAppliedTransformDifference,
                    difference
                );

            if (difference > tolerance)
            {
                appliedTransformMismatchCount++;

                if (firstProblem == null)
                {
                    firstProblem =
                        $"LOD{level} transform does not match " +
                        "its desired world anchor.";
                }
            }

            report +=
                $"LOD{level}: " +
                $"({actualPosition.x:R}, " +
                $"{actualPosition.y:R}, " +
                $"{actualPosition.z:R})\n";
        }

        // =================================================
        // STITCH PROPERTY BLOCK OFFSETS
        // =================================================

        if (transitionPropertyBlock == null)
        {
            transitionPropertyBlock =
                new MaterialPropertyBlock();
        }

        report +=
            "\nApplied Stitch Transition Offsets:\n";

        for (
            int coarseLevel = 1;
            coarseLevel <
                desiredLODAnchors.Length;
            coarseLevel++
        )
        {
            Vector3 expected =
                desiredLODAnchors[
                    coarseLevel - 1
                ]
                -
                desiredLODAnchors[
                    coarseLevel
                ];

            stitchRenderers[
                coarseLevel
            ].GetPropertyBlock(
                transitionPropertyBlock
            );

            Vector4 actual =
                transitionPropertyBlock.GetVector(
                    ClipmapTransitionOffsetPropertyId
                );

            float difference =
                Mathf.Max(
                    Mathf.Abs(
                        actual.x -
                        expected.x
                    ),
                    Mathf.Abs(
                        actual.z -
                        expected.z
                    )
                );

            maximumStitchOffsetDifference =
                Mathf.Max(
                    maximumStitchOffsetDifference,
                    difference
                );

            if (difference > tolerance)
            {
                stitchOffsetMismatchCount++;

                if (firstProblem == null)
                {
                    firstProblem =
                        $"Stitch_LOD{coarseLevel - 1}_LOD" +
                        $"{coarseLevel} has the wrong shader " +
                        "transition offset.";
                }
            }

            report +=
                $"LOD{coarseLevel - 1} -> " +
                $"LOD{coarseLevel}: " +
                $"({actual.x:R}, {actual.z:R})\n";
        }

        // =================================================
        // RESULT
        // =================================================

        bool passed =
            gridMismatchCount == 0
            &&
            adjacentOffsetMismatchCount == 0
            &&
            appliedTransformMismatchCount == 0
            &&
            stitchOffsetMismatchCount == 0;

        report +=
            "\n" +

            $"Grid Alignment Mismatches: " +
            $"{gridMismatchCount}\n" +

            $"Adjacent Offset Mismatches: " +
            $"{adjacentOffsetMismatchCount}\n" +

            $"Applied Transform Mismatches: " +
            $"{appliedTransformMismatchCount}\n" +

            $"Stitch Offset Mismatches: " +
            $"{stitchOffsetMismatchCount}\n\n" +

            $"Maximum Grid Difference: " +
            $"{maximumGridDifference:R}\n" +

            $"Maximum Adjacent Offset Difference: " +
            $"{maximumAdjacentOffsetDifference:R}\n" +

            $"Maximum Applied Transform Difference: " +
            $"{maximumAppliedTransformDifference:R}\n" +

            $"Maximum Stitch Offset Difference: " +
            $"{maximumStitchOffsetDifference:R}\n\n" +

            $"Desired Geometry Bounds XZ: " +
            $"({desiredClipmapMinimumXZ.x:R}, " +
            $"{desiredClipmapMinimumXZ.y:R}) -> " +
            $"({desiredClipmapMaximumXZ.x:R}, " +
            $"{desiredClipmapMaximumXZ.y:R})";

        if (passed)
        {
            Debug.Log(
                "Clipmap LOD anchor validation passed.\n\n" +
                report,
                this
            );

            return;
        }

        if (firstProblem != null)
        {
            report +=
                "\n\nFirst Problem:\n" +
                firstProblem;
        }

        Debug.LogError(
            "Clipmap LOD anchor validation FAILED.\n\n" +
            report,
            this
        );
    }

    // =====================================================
    // DISTANCE FROM VALID ADJACENT OFFSET
    // =====================================================

    private static float DistanceFromValidAdjacentOffset(
        float offset,
        float fineSpacing
    )
    {
        float negativeDifference =
            Mathf.Abs(
                offset +
                fineSpacing
            );

        float zeroDifference =
            Mathf.Abs(
                offset
            );

        float positiveDifference =
            Mathf.Abs(
                offset -
                fineSpacing
            );

        return
            Mathf.Min(
                negativeDifference,
                Mathf.Min(
                    zeroDifference,
                    positiveDifference
                )
            );
    }

    // =====================================================
    // STREAMER REFERENCE
    // =====================================================

    private void EnsureStreamerReference()
    {
        if (streamer != null)
        {
            return;
        }

        streamer =
            GetComponent<TerrainHeightmapStreamer>();
    }
}
