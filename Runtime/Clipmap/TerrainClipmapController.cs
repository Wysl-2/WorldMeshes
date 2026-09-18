using System;
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
     * Transform that the runtime clipmap follows horizontally.
     *
     * Target Y is intentionally ignored.
     */
    [SerializeField]
    [HideInInspector]
    private Transform target;

    // =====================================================
    // DEBUG
    // =====================================================

    [Header("Debug")]

    [SerializeField]
    private bool logCoverageStalls =
        true;

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
    // SHARED LAYOUT STATE
    // =====================================================

    /*
     * Reused result object populated by the shared pure layout
     * utility. No per-frame layout array allocations are needed.
     */
    private readonly TerrainClipmapLayout desiredLayout =
        new TerrainClipmapLayout();

    /*
     * Shared hierarchy/stitch mutator.
     *
     * Runtime and the upcoming editor Scene View controller can
     * both use TerrainClipmapLayoutApplier so transform placement
     * and stitch offsets cannot drift into separate implementations.
     */
    private TerrainClipmapLayoutApplier layoutApplier;

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public event Action AppliedClipmapBoundsChanged;


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
            return
                desiredLayout.IsValid;
        }
    }

    public int DesiredLODAnchorCount
    {
        get
        {
            if (!desiredLayout.IsValid)
            {
                return 0;
            }

            return
                desiredLayout.LevelCount;
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
        return
            desiredLayout.TryGetLOD(
                level,
                out anchor,
                out spacing
            );
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

        if (!desiredLayout.IsValid)
        {
            return false;
        }

        minimumXZ =
            desiredLayout.MinimumXZ;

        maximumXZ =
            desiredLayout.MaximumXZ;

        return true;
    }

    // =====================================================
    // TRY GET APPLIED CLIPMAP BOUNDS
    // =====================================================

    public bool TryGetAppliedClipmapBounds(
        out Vector2 minimumXZ,
        out Vector2 maximumXZ
    )
    {
        minimumXZ =
            Vector2.zero;

        maximumXZ =
            Vector2.zero;

        EnsureLayoutApplier();

        return
            layoutApplier != null
            &&
            layoutApplier.TryGetAppliedBounds(
                out minimumXZ,
                out maximumXZ
            );
    }

    // =====================================================
    // EDITOR / HIERARCHY CONFIGURATION
    // =====================================================

    /*
     * Called by TerrainWorldHierarchyGenerator.
     *
     * The movement target is synchronized separately from the
     * authoritative TerrainWorldRuntime streaming source.
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
            EnsureLayoutApplier();

            return false;
        }

        worldSettings =
            settings;

        InvalidateLODState();

        EnsureLayoutApplier();

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
        EnsureLayoutApplier();

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

        if (
            Application.isPlaying
            &&
            layoutApplier != null
        )
        {
            /*
             * Preserve the old runtime shutdown semantics:
             * reset coarse child offsets and stitch transition
             * offsets while leaving the current root position.
             */
            layoutApplier.TryReset(
                out _
            );
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
            WarnMissingTarget();

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

            desiredLayout.Invalidate();

            return false;
        }

        hasWarnedMissingSettings =
            false;

        /*
         * Preserve the runtime controller's previous contract:
         * even CanTargetMoveTo(...) requires a configured target,
         * because the controller itself represents a Player-follow
         * system.
         */
        if (target == null)
        {
            WarnMissingTarget();

            desiredLayout.Invalidate();

            return false;
        }

        hasWarnedMissingTarget =
            false;

        bool success =
            TerrainClipmapLayoutUtility
                .TryCalculateLayout(
                    worldSettings,
                    targetWorldPosition,
                    fixedY,
                    desiredLayout,
                    out string errorMessage
                );

        if (
            !success
            &&
            !string.IsNullOrEmpty(
                errorMessage
            )
            &&
            !hasWarnedMissingSettings
        )
        {
            Debug.LogWarning(
                "TerrainClipmapController could not calculate " +
                "the desired clipmap layout.\n\n" +
                errorMessage,
                this
            );
        }

        return
            success;
    }

    // =====================================================
    // DESIRED COVERAGE CENTER
    // =====================================================

    private Vector3 GetDesiredCoverageCenter()
    {
        if (!desiredLayout.IsValid)
        {
            return
                transform.position;
        }

        return
            desiredLayout.CoverageCenter;
    }

    // =====================================================
    // APPLY DESIRED LOD GEOMETRY
    // =====================================================

    private void ApplyDesiredLODGeometry()
    {
        if (!desiredLayout.IsValid)
        {
            return;
        }

        EnsureLayoutApplier();

        if (layoutApplier == null)
        {
            WarnMissingLODHierarchy(
                "The shared TerrainClipmapLayoutApplier " +
                "could not be created."
            );

            return;
        }

        if (
            !layoutApplier.TryApply(
                desiredLayout,
                out string errorMessage
            )
        )
        {
            WarnMissingLODHierarchy(
                errorMessage
            );

            return;
        }

        hasWarnedMissingLODHierarchy =
            false;
    }

    // =====================================================
    // WARN MISSING TARGET
    // =====================================================

    private void WarnMissingTarget()
    {
        if (hasWarnedMissingTarget)
        {
            return;
        }

        Debug.LogWarning(
            "TerrainClipmapController has no target.\n\n" +
            "Assign a Streaming Source in World > World Hierarchy, " +
            "then run Apply Streaming Source or Setup / Repair " +
            "World Hierarchy.",
            this
        );

        hasWarnedMissingTarget =
            true;
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
            "\n\nRun Setup / Repair World Hierarchy.",
            this
        );

        hasWarnedMissingLODHierarchy =
            true;
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
    // INVALIDATE LOD STATE
    // =====================================================

    private void InvalidateLODState()
    {
        desiredLayout.Invalidate();

        if (layoutApplier != null)
        {
            layoutApplier
                .InvalidateHierarchyReferences();
        }
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

        EnsureLayoutApplier();

        if (
            layoutApplier == null
        )
        {
            Debug.LogError(
                "Cannot validate clipmap LOD anchors because " +
                "TerrainClipmapLayoutApplier is unavailable.",
                this
            );

            return;
        }

        /*
         * Resolve references up front through the public diagnostic
         * access path. This keeps hierarchy knowledge inside the
         * applier rather than leaking it back into this controller.
         */
        for (
            int level = 1;
            level < desiredLayout.LevelCount;
            level++
        )
        {
            if (
                !layoutApplier.TryGetLODTransform(
                    level,
                    out _,
                    out string hierarchyError
                )
            )
            {
                Debug.LogError(
                    "Cannot validate clipmap LOD anchors.\n\n" +
                    hierarchyError,
                    this
                );

                return;
            }
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
            $"{desiredLayout.LevelCount}\n" +

            $"Base Spacing: " +
            $"{baseSpacing:R}\n\n" +

            "Desired LOD Anchors:\n";

        // =================================================
        // GRID ALIGNMENT
        // =================================================

        for (
            int level = 0;
            level < desiredLayout.LevelCount;
            level++
        )
        {
            Vector3 anchor =
                desiredLayout.GetAnchor(
                    level
                );

            float spacing =
                desiredLayout.GetSpacing(
                    level
                );

            float expectedX =
                TerrainClipmapLayoutUtility
                    .SnapCoordinate(
                        anchor.x,
                        spacing
                    );

            float expectedZ =
                TerrainClipmapLayoutUtility
                    .SnapCoordinate(
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
                desiredLayout.LevelCount;
            coarseLevel++
        )
        {
            int fineLevel =
                coarseLevel - 1;

            Vector3 fineAnchor =
                desiredLayout.GetAnchor(
                    fineLevel
                );

            Vector3 coarseAnchor =
                desiredLayout.GetAnchor(
                    coarseLevel
                );

            float fineSpacing =
                desiredLayout.GetSpacing(
                    fineLevel
                );

            Vector3 offset =
                fineAnchor -
                coarseAnchor;

            float offsetDifference =
                Mathf.Max(
                    TerrainClipmapLayoutUtility
                        .DistanceFromValidAdjacentOffset(
                            offset.x,
                            fineSpacing
                        ),

                    TerrainClipmapLayoutUtility
                        .DistanceFromValidAdjacentOffset(
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
                desiredLayout.GetAnchor(
                    0
                )
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
            level < desiredLayout.LevelCount;
            level++
        )
        {
            if (
                !layoutApplier.TryGetLODTransform(
                    level,
                    out Transform levelTransform,
                    out string transformError
                )
            )
            {
                appliedTransformMismatchCount++;

                if (firstProblem == null)
                {
                    firstProblem =
                        transformError;
                }

                continue;
            }

            Vector3 actualPosition =
                levelTransform.position;

            float difference =
                Vector3.Distance(
                    actualPosition,
                    desiredLayout.GetAnchor(
                        level
                    )
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

        report +=
            "\nApplied Stitch Transition Offsets:\n";

        for (
            int coarseLevel = 1;
            coarseLevel <
                desiredLayout.LevelCount;
            coarseLevel++
        )
        {
            Vector3 expected =
                desiredLayout.GetAnchor(
                    coarseLevel - 1
                )
                -
                desiredLayout.GetAnchor(
                    coarseLevel
                );

            if (
                !layoutApplier
                    .TryGetStitchTransitionOffset(
                        coarseLevel,
                        out Vector4 actual,
                        out string stitchError
                    )
            )
            {
                stitchOffsetMismatchCount++;

                if (firstProblem == null)
                {
                    firstProblem =
                        stitchError;
                }

                continue;
            }

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
            $"({desiredLayout.MinimumXZ.x:R}, " +
            $"{desiredLayout.MinimumXZ.y:R}) -> " +
            $"({desiredLayout.MaximumXZ.x:R}, " +
            $"{desiredLayout.MaximumXZ.y:R})";

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

    // =====================================================
    // APPLIED BOUNDS CHANGED
    // =====================================================

    private void HandleAppliedBoundsChanged()
    {
        AppliedClipmapBoundsChanged?.Invoke();
    }

    // =====================================================
    // LAYOUT APPLIER
    // =====================================================

    private void EnsureLayoutApplier()
    {
        if (layoutApplier == null)
        {
            layoutApplier =
                new TerrainClipmapLayoutApplier();

            layoutApplier.AppliedBoundsChanged +=
                HandleAppliedBoundsChanged;
        }

        layoutApplier.Configure(
            transform,
            worldSettings
        );
    }
}
