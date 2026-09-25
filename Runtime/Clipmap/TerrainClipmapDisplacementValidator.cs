using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TerrainClipmapDisplacementValidator :
    MonoBehaviour
{
    // =====================================================
    // VALIDATION SETTINGS
    // =====================================================

    [Header("Validation")]

    [SerializeField]
    private bool validateOnStart =
        true;

    [SerializeField]
    [Min(0f)]
    private float sampleAlignmentTolerance =
        0.0001f;

    [SerializeField]
    [Min(0f)]
    private float heightTolerance =
        0.00001f;

    [SerializeField]
    [Min(0f)]
    private float boundsContainmentTolerance =
        0.0001f;

    [SerializeField]
    [Min(0.1f)]
    private float startupTimeoutSeconds =
        15f;

    [SerializeField]
    private bool compareAgainstPreview =
        true;

    // =====================================================
    // RUNTIME STATE
    // =====================================================

    private TerrainHeightmapStreamer streamer;

    private TerrainClipmapBoundsController
        boundsController;

    private TerrainClipmapController
        clipmapController;

    private Coroutine validationRoutine;

    private bool lastValidationPassed;

    // =====================================================
    // SHADER PROPERTY IDS
    // =====================================================

    private static readonly int HeightCachePropertyId =
        Shader.PropertyToID(
            "_HeightCache"
        );

    private static readonly int
        HeightCacheOriginTilePropertyId =
            Shader.PropertyToID(
                "_HeightCacheOriginTile"
            );

    private static readonly int HeightCacheSizePropertyId =
        Shader.PropertyToID(
            "_HeightCacheSize"
        );

    private static readonly int
        HeightTileSamplesPerSidePropertyId =
            Shader.PropertyToID(
                "_HeightTileSamplesPerSide"
            );

    private static readonly int
        HeightSampleSpacingPropertyId =
            Shader.PropertyToID(
                "_HeightSampleSpacing"
            );

    private static readonly int WorldSizeXZPropertyId =
        Shader.PropertyToID(
            "_WorldSizeXZ"
        );

    private static readonly int HeightCacheReadyPropertyId =
        Shader.PropertyToID(
            "_HeightCacheReady"
        );

    private static readonly int
        ClipmapTransitionOffsetPropertyId =
            Shader.PropertyToID(
                "_ClipmapTransitionOffset"
            );

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public bool LastValidationPassed
    {
        get
        {
            return lastValidationPassed;
        }
    }

    public bool IsValidating
    {
        get
        {
            return validationRoutine != null;
        }
    }

    // =====================================================
    // START
    // =====================================================

    private void Start()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        streamer =
            GetComponent<TerrainHeightmapStreamer>();

        boundsController =
            GetComponent<TerrainClipmapBoundsController>();

        clipmapController =
            GetComponent<TerrainClipmapController>();

        if (streamer == null)
        {
            Debug.LogError(
                "TerrainClipmapDisplacementValidator requires " +
                "TerrainHeightmapStreamer on the same GameObject.",
                this
            );

            enabled =
                false;

            return;
        }

        if (boundsController == null)
        {
            Debug.LogError(
                "TerrainClipmapDisplacementValidator requires " +
                "TerrainClipmapBoundsController on the same " +
                "GameObject.",
                this
            );

            enabled =
                false;

            return;
        }

        if (clipmapController == null)
        {
            Debug.LogError(
                "TerrainClipmapDisplacementValidator requires " +
                "TerrainClipmapController on the same GameObject.",
                this
            );

            enabled =
                false;

            return;
        }

        /*
         * MRH07 runtime validation is intentionally manual.
         *
         * Keep the serialized validateOnStart field for scene/prefab
         * compatibility, but do not automatically begin GPU readback or
         * displacement validation during normal Play Mode startup.
         */
    }

    // =====================================================
    // DISABLE
    // =====================================================

    private void OnDisable()
    {
        if (validationRoutine != null)
        {
            StopCoroutine(
                validationRoutine
            );

            validationRoutine =
                null;
        }

        CancelMrh07Validation();
    }

    // =====================================================
    // BEGIN VALIDATION
    // =====================================================

    [ContextMenu("Validate Clipmap Displacement")]
    public void BeginValidation()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "Clipmap displacement validation can only " +
                "run in Play Mode.",
                this
            );

            return;
        }

        if (validationRoutine != null)
        {
            return;
        }

        if (streamer == null)
        {
            streamer =
                GetComponent<TerrainHeightmapStreamer>();
        }

        if (boundsController == null)
        {
            boundsController =
                GetComponent<TerrainClipmapBoundsController>();
        }

        if (clipmapController == null)
        {
            clipmapController =
                GetComponent<TerrainClipmapController>();
        }

        if (
            streamer == null
            ||
            boundsController == null
            ||
            clipmapController == null
        )
        {
            Debug.LogError(
                "Cannot validate clipmap displacement.\n\n" +
                "TerrainHeightmapStreamer, " +
                "TerrainClipmapBoundsController, and " +
                "TerrainClipmapController are required.",
                this
            );

            return;
        }

        if (clipmapController.IsWaitingForHeightData)
        {
            Debug.LogWarning(
                "Clipmap displacement validation was not started " +
                "because clipmap movement is currently waiting " +
                "for height-cache coverage.",
                this
            );

            return;
        }

        lastValidationPassed =
            false;

        validationRoutine =
            StartCoroutine(
                WaitForCacheAndValidate()
            );
    }

    // =====================================================
    // WAIT FOR READY STATE
    // =====================================================

    private IEnumerator WaitForCacheAndValidate()
    {
        float startTime =
            Time.realtimeSinceStartup;

        while (true)
        {
            bool streamerReady =
                streamer != null
                &&
                streamer.CacheReady
                &&
                streamer.HeightCache != null;

            bool shaderReady =
                streamerReady
                &&
                AreShaderBindingsReady();

            bool boundsReady =
                boundsController != null
                &&
                boundsController.BoundsApplied;

            bool movementReady =
                clipmapController != null
                &&
                !clipmapController
                    .IsWaitingForHeightData;

            if (
                streamerReady
                &&
                shaderReady
                &&
                boundsReady
                &&
                movementReady
            )
            {
                break;
            }

            if (
                Time.realtimeSinceStartup
                -
                startTime
                >
                startupTimeoutSeconds
            )
            {
                FailValidation(
                    "Timed out while waiting for the height " +
                    "cache, shader bindings, renderer bounds, " +
                    "and a stable clipmap movement state."
                );

                yield break;
            }

            yield return null;
        }

        /*
         * Give transforms, MaterialPropertyBlocks, and renderer
         * state one complete frame to settle.
         */
        yield return null;

        yield return
            ValidateDisplacementRoutine();
    }

    // =====================================================
    // ARE SHADER BINDINGS READY
    // =====================================================

    private bool AreShaderBindingsReady()
    {
        MeshRenderer[] renderers =
            GetComponentsInChildren<MeshRenderer>(
                true
            );

        if (renderers.Length == 0)
        {
            return false;
        }

        MaterialPropertyBlock block =
            new MaterialPropertyBlock();

        int clipmapRendererCount =
            0;

        foreach (
            MeshRenderer meshRenderer
            in renderers
        )
        {
            if (
                meshRenderer == null
                ||
                meshRenderer.sharedMaterial == null
            )
            {
                continue;
            }

            if (
                !meshRenderer.sharedMaterial.HasProperty(
                    HeightCacheReadyPropertyId
                )
            )
            {
                continue;
            }

            clipmapRendererCount++;

            meshRenderer.GetPropertyBlock(
                block
            );

            if (
                block.GetFloat(
                    HeightCacheReadyPropertyId
                )
                <
                0.5f
            )
            {
                return false;
            }
        }

        return
            clipmapRendererCount > 0;
    }

    // =====================================================
    // CHECK DISPLACED VERTEX AGAINST RENDERER BOUNDS
    // =====================================================

    private bool IsDisplacedVertexInsideRendererBounds(
        MeshRenderer renderer,
        Vector3 displacedWorldPosition,
        out Vector3 displacedLocalPosition,
        out float maximumOverflow
    )
    {
        displacedLocalPosition =
            renderer.transform
                .InverseTransformPoint(
                    displacedWorldPosition
                );

        Bounds bounds =
            renderer.localBounds;

        float tolerance =
            Mathf.Max(
                0f,
                boundsContainmentTolerance
            );

        Vector3 minimum =
            bounds.min
            -
            Vector3.one *
            tolerance;

        Vector3 maximum =
            bounds.max
            +
            Vector3.one *
            tolerance;

        float overflowX =
            Mathf.Max(
                0f,
                Mathf.Max(
                    minimum.x -
                    displacedLocalPosition.x,

                    displacedLocalPosition.x -
                    maximum.x
                )
            );

        float overflowY =
            Mathf.Max(
                0f,
                Mathf.Max(
                    minimum.y -
                    displacedLocalPosition.y,

                    displacedLocalPosition.y -
                    maximum.y
                )
            );

        float overflowZ =
            Mathf.Max(
                0f,
                Mathf.Max(
                    minimum.z -
                    displacedLocalPosition.z,

                    displacedLocalPosition.z -
                    maximum.z
                )
            );

        maximumOverflow =
            Mathf.Max(
                overflowX,
                Mathf.Max(
                    overflowY,
                    overflowZ
                )
            );

        return
            maximumOverflow <= 0f;
    }

    // =====================================================
    // VALIDATE DISPLACEMENT
    // =====================================================

    private IEnumerator ValidateDisplacementRoutine()
    {
        Texture2DArray heightCache =
            streamer.HeightCache;

        if (heightCache == null)
        {
            FailValidation(
                "The height cache became unavailable before " +
                "displacement validation began."
            );

            yield break;
        }

        // =================================================
        // CLIPMAP RENDERERS
        // =================================================

        MeshRenderer[] allRenderers =
            GetComponentsInChildren<MeshRenderer>(
                true
            );

        List<RendererValidationSource>
            validationSources =
                new List<RendererValidationSource>();

        Dictionary<string, int>
            rendererInstanceIdsByName =
                new Dictionary<string, int>();

        int bindingMismatchCount =
            0;

        int transitionDataMismatchCount =
            0;

        int transitionOffsetMismatchCount =
            0;

        string firstBindingMismatch =
            null;

        string firstTransitionDataMismatch =
            null;

        string firstTransitionOffsetMismatch =
            null;

        ShaderBindingState canonicalBinding =
            default;

        bool hasCanonicalBinding =
            false;

        foreach (
            MeshRenderer meshRenderer
            in allRenderers
        )
        {
            if (
                meshRenderer == null
                ||
                meshRenderer.sharedMaterial == null
            )
            {
                continue;
            }

            if (
                !meshRenderer.sharedMaterial.HasProperty(
                    HeightCachePropertyId
                )
                ||
                !meshRenderer.sharedMaterial.HasProperty(
                    HeightCacheReadyPropertyId
                )
            )
            {
                continue;
            }

            MeshFilter meshFilter =
                meshRenderer
                    .GetComponent<MeshFilter>();

            if (
                meshFilter == null
                ||
                meshFilter.sharedMesh == null
            )
            {
                bindingMismatchCount++;

                if (firstBindingMismatch == null)
                {
                    firstBindingMismatch =
                        $"{meshRenderer.name}: " +
                        "MeshFilter or Mesh is missing.";
                }

                continue;
            }

            if (
                !TryReadShaderBinding(
                    meshRenderer,
                    out ShaderBindingState binding,
                    out string bindingError
                )
            )
            {
                bindingMismatchCount++;

                if (firstBindingMismatch == null)
                {
                    firstBindingMismatch =
                        $"{meshRenderer.name}: " +
                        bindingError;
                }

                continue;
            }

            if (
                !ValidateBindingAgainstStreamer(
                    binding,
                    out string streamerBindingError
                )
            )
            {
                bindingMismatchCount++;

                if (firstBindingMismatch == null)
                {
                    firstBindingMismatch =
                        $"{meshRenderer.name}: " +
                        streamerBindingError;
                }
            }

            if (!hasCanonicalBinding)
            {
                canonicalBinding =
                    binding;

                hasCanonicalBinding =
                    true;
            }
            else if (
                !BindingsMatch(
                    canonicalBinding,
                    binding
                )
            )
            {
                bindingMismatchCount++;

                if (firstBindingMismatch == null)
                {
                    firstBindingMismatch =
                        $"{meshRenderer.name}: " +
                        "height-cache shader metadata differs " +
                        "from the other clipmap renderers.";
                }
            }

            Mesh mesh =
                meshFilter.sharedMesh;

            List<Vector4> clipmapData =
                new List<Vector4>();

            mesh.GetUVs(
                3,
                clipmapData
            );

            if (
                clipmapData.Count !=
                mesh.vertexCount
            )
            {
                transitionDataMismatchCount++;

                if (
                    firstTransitionDataMismatch ==
                    null
                )
                {
                    firstTransitionDataMismatch =
                        $"{meshRenderer.name}\n" +
                        $"Vertices: {mesh.vertexCount}\n" +
                        $"UV3/TEXCOORD3 Values: " +
                        $"{clipmapData.Count}";
                }

                continue;
            }

            bool isStitch =
                TryParseStitchLevels(
                    meshRenderer.name,
                    out int fineLevel,
                    out int coarseLevel
                );

            if (
                !ValidateTransitionOffset(
                    meshRenderer,
                    binding.transitionOffset,
                    isStitch,
                    fineLevel,
                    coarseLevel,
                    out string transitionOffsetError
                )
            )
            {
                transitionOffsetMismatchCount++;

                if (
                    firstTransitionOffsetMismatch ==
                    null
                )
                {
                    firstTransitionOffsetMismatch =
                        transitionOffsetError;
                }
            }

            validationSources.Add(
                new RendererValidationSource(
                    meshRenderer,
                    meshFilter,
                    binding,
                    clipmapData,
                    isStitch,
                    fineLevel,
                    coarseLevel
                )
            );

            if (
                !rendererInstanceIdsByName.ContainsKey(
                    meshRenderer.name
                )
            )
            {
                rendererInstanceIdsByName.Add(
                    meshRenderer.name,
                    meshRenderer.GetInstanceID()
                );
            }
        }

        if (validationSources.Count == 0)
        {
            FailValidation(
                "No clipmap renderers using the displacement " +
                "shader were found."
            );

            yield break;
        }

        if (!hasCanonicalBinding)
        {
            FailValidation(
                "Could not obtain clipmap shader metadata."
            );

            yield break;
        }

        // =================================================
        // GPU CACHE READBACK
        // =================================================

        AsyncGPUReadbackRequest readback;

        try
        {
            readback =
                AsyncGPUReadback.Request(
                    heightCache,
                    0,
                    TextureFormat.RFloat,
                    null
                );
        }
        catch (
            System.Exception exception
        )
        {
            FailValidation(
                "Could not begin GPU cache readback.\n\n" +
                exception.Message
            );

            yield break;
        }

        while (!readback.done)
        {
            yield return null;
        }

        if (readback.hasError)
        {
            FailValidation(
                "GPU readback failed during clipmap " +
                "displacement validation."
            );

            yield break;
        }

        int expectedSliceCount =
            canonicalBinding.cacheWidth
            *
            canonicalBinding.cacheHeight;

        if (
            heightCache.depth !=
            expectedSliceCount
        )
        {
            FailValidation(
                "The Texture2DArray depth does not match " +
                "the shader cache dimensions.\n\n" +
                $"Expected Slices: {expectedSliceCount}\n" +
                $"Actual Slices: {heightCache.depth}"
            );

            yield break;
        }

        NativeArray<float>[] cacheSlices =
            new NativeArray<float>[
                expectedSliceCount
            ];

        try
        {
            for (
                int slice = 0;
                slice < expectedSliceCount;
                slice++
            )
            {
                cacheSlices[
                    slice
                ] =
                    readback.GetData<float>(
                        slice
                    );
            }
        }
        catch (
            System.Exception exception
        )
        {
            FailValidation(
                "Could not access one or more height-cache " +
                "array slices.\n\n" +
                exception.Message
            );

            yield break;
        }

        // =================================================
        // STATISTICS
        // =================================================

        long verticesValidated =
            0L;

        long positionAlignmentMismatches =
            0L;

        long outOfWorldVertices =
            0L;

        long invalidMappings =
            0L;

        long invalidHeights =
            0L;

        long displacedVerticesOutsideBounds =
            0L;

        long adaptiveTriangleMismatches =
            0L;

        long stitchBoundaryMismatches =
            0L;

        float maximumBoundsOverflow =
            0f;

        float maximumAlignmentDifference =
            0f;

        float minimumHeight =
            float.PositiveInfinity;

        float maximumHeight =
            float.NegativeInfinity;

        string firstVertexProblem =
            null;

        string firstBoundsMismatch =
            null;

        string firstAdaptiveTriangleMismatch =
            null;

        string firstStitchBoundaryMismatch =
            null;

        Dictionary<Vector2Int, ClipmapSample>
            uniqueClipmapSamples =
                new Dictionary<Vector2Int, ClipmapSample>();

        long sharedSamplePositions =
            0L;

        long sharedSampleHeightMismatches =
            0L;

        float maximumSharedHeightDifference =
            0f;

        string firstSharedMismatch =
            null;

        Dictionary<Vector2Int, HashSet<int>>
            rendererIdsByWorldSample =
                new Dictionary<Vector2Int, HashSet<int>>();

        List<StitchBoundaryRequirement>
            stitchBoundaryRequirements =
                new List<StitchBoundaryRequirement>();

        float projectedAreaTolerance =
            Mathf.Max(
                0.0000001f,
                canonicalBinding.sampleSpacing *
                canonicalBinding.sampleSpacing *
                0.0000001f
            );

        // =================================================
        // EACH RENDERER
        // =================================================

        foreach (
            RendererValidationSource source
            in validationSources
        )
        {
            Mesh mesh =
                source.meshFilter.sharedMesh;

            Vector3[] vertices;
            int[] triangles;

            try
            {
                vertices =
                    mesh.vertices;

                triangles =
                    mesh.triangles;
            }
            catch (
                System.Exception exception
            )
            {
                FailValidation(
                    $"Could not read mesh data from " +
                    $"'{mesh.name}'.\n\n" +
                    exception.Message
                );

                yield break;
            }

            Vector3[] shaderWorldPositions =
                new Vector3[
                    vertices.Length
                ];

            for (
                int vertexIndex = 0;
                vertexIndex < vertices.Length;
                vertexIndex++
            )
            {
                Vector4 vertexClipmapData =
                    source.clipmapData[
                        vertexIndex
                    ];

                float transitionWeight =
                    vertexClipmapData.x;

                if (
                    !IsFinite(
                        transitionWeight
                    )
                    ||
                    (
                        Mathf.Abs(
                            transitionWeight
                        )
                        >
                        sampleAlignmentTolerance
                        &&
                        Mathf.Abs(
                            transitionWeight -
                            1f
                        )
                        >
                        sampleAlignmentTolerance
                    )
                )
                {
                    transitionDataMismatchCount++;

                    if (
                        firstTransitionDataMismatch ==
                        null
                    )
                    {
                        firstTransitionDataMismatch =
                            $"{source.renderer.name}\n" +
                            $"Vertex: {vertexIndex}\n" +
                            $"Transition Weight: " +
                            $"{transitionWeight:R}";
                    }

                    /*
                     * Continue with a clamped value so the
                     * remaining diagnostics can still run.
                     */
                    transitionWeight =
                        Mathf.Clamp01(
                            transitionWeight
                        );
                }
                else
                {
                    transitionWeight =
                        transitionWeight >= 0.5f
                            ? 1f
                            : 0f;
                }

                Vector3 worldPosition =
                    source.meshFilter.transform
                        .TransformPoint(
                            vertices[
                                vertexIndex
                            ]
                        );

                /*
                 * Reproduce Stage 3B/3C shader-side X/Z stitch
                 * deformation BEFORE height lookup.
                 */
                worldPosition.x +=
                    source.binding
                        .transitionOffset.x
                    *
                    transitionWeight;

                worldPosition.z +=
                    source.binding
                        .transitionOffset.y
                    *
                    transitionWeight;

                shaderWorldPositions[
                    vertexIndex
                ] =
                    worldPosition;

                verticesValidated++;

                // -----------------------------------------
                // World bounds
                // -----------------------------------------

                if (
                    worldPosition.x <
                        -sampleAlignmentTolerance
                    ||
                    worldPosition.z <
                        -sampleAlignmentTolerance
                    ||
                    worldPosition.x >
                        source.binding.worldSize.x
                        +
                        sampleAlignmentTolerance
                    ||
                    worldPosition.z >
                        source.binding.worldSize.y
                        +
                        sampleAlignmentTolerance
                )
                {
                    outOfWorldVertices++;
                }

                // -----------------------------------------
                // Source sample-grid alignment
                // -----------------------------------------

                int nearestSampleX =
                    Mathf.RoundToInt(
                        worldPosition.x /
                        source.binding.sampleSpacing
                    );

                int nearestSampleZ =
                    Mathf.RoundToInt(
                        worldPosition.z /
                        source.binding.sampleSpacing
                    );

                float alignedWorldX =
                    nearestSampleX *
                    source.binding.sampleSpacing;

                float alignedWorldZ =
                    nearestSampleZ *
                    source.binding.sampleSpacing;

                float alignmentDifference =
                    Mathf.Max(
                        Mathf.Abs(
                            worldPosition.x -
                            alignedWorldX
                        ),
                        Mathf.Abs(
                            worldPosition.z -
                            alignedWorldZ
                        )
                    );

                maximumAlignmentDifference =
                    Mathf.Max(
                        maximumAlignmentDifference,
                        alignmentDifference
                    );

                if (
                    alignmentDifference >
                    sampleAlignmentTolerance
                )
                {
                    positionAlignmentMismatches++;

                    if (firstVertexProblem == null)
                    {
                        firstVertexProblem =
                            $"{source.renderer.name}\n" +
                            $"Vertex: {vertexIndex}\n" +
                            $"Shader World Position: " +
                            $"{worldPosition}\n" +
                            $"Grid Difference: " +
                            $"{alignmentDifference:R}";
                    }
                }

                // -----------------------------------------
                // Exact world sample occupancy
                // -----------------------------------------

                Vector2Int worldSample =
                    new Vector2Int(
                        nearestSampleX,
                        nearestSampleZ
                    );

                if (
                    !rendererIdsByWorldSample.TryGetValue(
                        worldSample,
                        out HashSet<int> rendererIds
                    )
                )
                {
                    rendererIds =
                        new HashSet<int>();

                    rendererIdsByWorldSample.Add(
                        worldSample,
                        rendererIds
                    );
                }

                rendererIds.Add(
                    source.renderer.GetInstanceID()
                );

                // -----------------------------------------
                // Stitch boundary requirement
                // -----------------------------------------

                if (source.isStitch)
                {
                    string expectedRendererName =
                        transitionWeight >=
                            0.5f
                            ? GetFineRendererName(
                                source.fineLevel
                            )
                            : $"Ring_LOD" +
                              $"{source.coarseLevel}";

                    if (
                        rendererInstanceIdsByName.TryGetValue(
                            expectedRendererName,
                            out int expectedRendererId
                        )
                    )
                    {
                        stitchBoundaryRequirements.Add(
                            new StitchBoundaryRequirement(
                                source.renderer.name,
                                vertexIndex,
                                worldSample,
                                expectedRendererName,
                                expectedRendererId,
                                transitionWeight
                            )
                        );
                    }
                    else
                    {
                        stitchBoundaryMismatches++;

                        if (
                            firstStitchBoundaryMismatch ==
                            null
                        )
                        {
                            firstStitchBoundaryMismatch =
                                $"{source.renderer.name}\n" +
                                $"Expected matching renderer " +
                                $"'{expectedRendererName}' " +
                                "was not found.";
                        }
                    }
                }

                // -----------------------------------------
                // Reproduce shader height lookup
                // -----------------------------------------

                if (
                    !TryResolveShaderHeight(
                        source.binding,
                        worldPosition.x,
                        worldPosition.z,
                        cacheSlices,
                        out Vector2Int globalSample,
                        out float expectedHeight,
                        out string mappingError
                    )
                )
                {
                    invalidMappings++;

                    if (firstVertexProblem == null)
                    {
                        firstVertexProblem =
                            $"{source.renderer.name}\n" +
                            $"Vertex: {vertexIndex}\n" +
                            mappingError;
                    }

                    continue;
                }

                if (
                    !IsFinite(
                        expectedHeight
                    )
                )
                {
                    invalidHeights++;

                    if (firstVertexProblem == null)
                    {
                        firstVertexProblem =
                            $"{source.renderer.name}\n" +
                            $"Vertex: {vertexIndex}\n" +
                            $"Invalid Height: " +
                            $"{expectedHeight}";
                    }

                    continue;
                }

                // -----------------------------------------
                // Displaced renderer bounds
                // -----------------------------------------

                Vector3 displacedWorldPosition =
                    worldPosition;

                displacedWorldPosition.y =
                    expectedHeight;

                if (
                    !IsDisplacedVertexInsideRendererBounds(
                        source.renderer,
                        displacedWorldPosition,
                        out Vector3 displacedLocalPosition,
                        out float boundsOverflow
                    )
                )
                {
                    displacedVerticesOutsideBounds++;

                    maximumBoundsOverflow =
                        Mathf.Max(
                            maximumBoundsOverflow,
                            boundsOverflow
                        );

                    if (firstBoundsMismatch == null)
                    {
                        Bounds rendererBounds =
                            source.renderer.localBounds;

                        firstBoundsMismatch =
                            $"{source.renderer.name}\n" +
                            $"Vertex: {vertexIndex}\n" +
                            $"Shader World Position: " +
                            $"{worldPosition}\n" +
                            $"Displaced World Position: " +
                            $"{displacedWorldPosition}\n" +
                            $"Displaced Local Position: " +
                            $"{displacedLocalPosition}\n\n" +
                            $"Renderer Local Bounds Center: " +
                            $"{rendererBounds.center}\n" +
                            $"Renderer Local Bounds Size: " +
                            $"{rendererBounds.size}\n" +
                            $"Maximum Overflow: " +
                            $"{boundsOverflow:R}";
                    }
                }

                minimumHeight =
                    Mathf.Min(
                        minimumHeight,
                        expectedHeight
                    );

                maximumHeight =
                    Mathf.Max(
                        maximumHeight,
                        expectedHeight
                    );

                // -----------------------------------------
                // Shared sample / height validation
                // -----------------------------------------

                if (
                    uniqueClipmapSamples.TryGetValue(
                        globalSample,
                        out ClipmapSample existingSample
                    )
                )
                {
                    if (
                        existingSample.rendererInstanceId
                        !=
                        source.renderer.GetInstanceID()
                    )
                    {
                        if (
                            !existingSample
                                .sharedAcrossRenderers
                        )
                        {
                            sharedSamplePositions++;

                            existingSample
                                .sharedAcrossRenderers =
                                    true;
                        }

                        float sharedDifference =
                            Mathf.Abs(
                                existingSample.height -
                                expectedHeight
                            );

                        maximumSharedHeightDifference =
                            Mathf.Max(
                                maximumSharedHeightDifference,
                                sharedDifference
                            );

                        if (
                            sharedDifference >
                            heightTolerance
                        )
                        {
                            sharedSampleHeightMismatches++;

                            if (
                                firstSharedMismatch ==
                                null
                            )
                            {
                                firstSharedMismatch =
                                    $"Sample: " +
                                    $"({globalSample.x}, " +
                                    $"{globalSample.y})\n" +
                                    $"First Height: " +
                                    $"{existingSample.height:R}\n" +
                                    $"Second Height: " +
                                    $"{expectedHeight:R}\n" +
                                    $"Difference: " +
                                    $"{sharedDifference:R}";
                            }
                        }

                        uniqueClipmapSamples[
                            globalSample
                        ] =
                            existingSample;
                    }
                }
                else
                {
                    uniqueClipmapSamples.Add(
                        globalSample,
                        new ClipmapSample(
                            expectedHeight,
                            source.renderer
                                .GetInstanceID()
                        )
                    );
                }
            }

            // =============================================
            // ADAPTIVE PROJECTED TRIANGLE TOPOLOGY
            // =============================================

            for (
                int triangleOffset = 0;
                triangleOffset < triangles.Length;
                triangleOffset += 3
            )
            {
                Vector3 p0 =
                    shaderWorldPositions[
                        triangles[
                            triangleOffset
                        ]
                    ];

                Vector3 p1 =
                    shaderWorldPositions[
                        triangles[
                            triangleOffset + 1
                        ]
                    ];

                Vector3 p2 =
                    shaderWorldPositions[
                        triangles[
                            triangleOffset + 2
                        ]
                    ];

                float signedProjectedArea =
                    Vector3.Cross(
                        p1 - p0,
                        p2 - p0
                    ).y;

                if (
                    signedProjectedArea <=
                    projectedAreaTolerance
                )
                {
                    adaptiveTriangleMismatches++;

                    if (
                        firstAdaptiveTriangleMismatch ==
                        null
                    )
                    {
                        firstAdaptiveTriangleMismatch =
                            $"{source.renderer.name}\n" +
                            $"Triangle: " +
                            $"{triangleOffset / 3}\n" +
                            $"Signed Projected Area: " +
                            $"{signedProjectedArea:R}\n" +
                            $"Transition Offset: " +
                            $"({source.binding.transitionOffset.x:R}, " +
                            $"{source.binding.transitionOffset.y:R})";
                    }
                }
            }
        }

        // =================================================
        // STITCH BOUNDARY COINCIDENCE
        // =================================================

        foreach (
            StitchBoundaryRequirement requirement
            in stitchBoundaryRequirements
        )
        {
            if (
                !rendererIdsByWorldSample.TryGetValue(
                    requirement.worldSample,
                    out HashSet<int> rendererIds
                )
                ||
                !rendererIds.Contains(
                    requirement.expectedRendererInstanceId
                )
            )
            {
                stitchBoundaryMismatches++;

                if (
                    firstStitchBoundaryMismatch ==
                    null
                )
                {
                    string side =
                        requirement.transitionWeight >=
                            0.5f
                            ? "fine"
                            : "coarse";

                    firstStitchBoundaryMismatch =
                        $"{requirement.stitchRendererName}\n" +
                        $"Vertex: " +
                        $"{requirement.vertexIndex}\n" +
                        $"Boundary Side: {side}\n" +
                        $"World Sample: " +
                        $"({requirement.worldSample.x}, " +
                        $"{requirement.worldSample.y})\n" +
                        $"Expected Match: " +
                        $"{requirement.expectedRendererName}";
                }
            }
        }

        // =================================================
        // PREVIEW COMPARISON
        // =================================================

        int previewSamplesRequired =
            uniqueClipmapSamples.Count;

        int previewSamplesFound =
            0;

        int previewSamplesMissing =
            0;

        int previewHeightMismatches =
            0;

        int previewDuplicateHeightMismatches =
            0;

        float maximumPreviewHeightDifference =
            0f;

        string firstPreviewMismatch =
            null;

        if (compareAgainstPreview)
        {
            Transform previewRoot =
                FindPreviewRoot();

            if (previewRoot == null)
            {
                FailValidation(
                    "The Preview hierarchy could not be found."
                );

                yield break;
            }

            Dictionary<Vector2Int, float>
                previewHeights =
                    BuildPreviewHeightLookup(
                        previewRoot,
                        uniqueClipmapSamples.Keys,
                        canonicalBinding.sampleSpacing,
                        ref
                            previewDuplicateHeightMismatches
                    );

            foreach (
                KeyValuePair<Vector2Int, ClipmapSample>
                    pair
                in uniqueClipmapSamples
            )
            {
                if (
                    !previewHeights.TryGetValue(
                        pair.Key,
                        out float previewHeight
                    )
                )
                {
                    previewSamplesMissing++;

                    if (firstPreviewMismatch == null)
                    {
                        firstPreviewMismatch =
                            $"Missing Preview Sample: " +
                            $"({pair.Key.x}, {pair.Key.y})";
                    }

                    continue;
                }

                previewSamplesFound++;

                float difference =
                    Mathf.Abs(
                        pair.Value.height -
                        previewHeight
                    );

                maximumPreviewHeightDifference =
                    Mathf.Max(
                        maximumPreviewHeightDifference,
                        difference
                    );

                if (
                    difference >
                    heightTolerance
                )
                {
                    previewHeightMismatches++;

                    if (firstPreviewMismatch == null)
                    {
                        firstPreviewMismatch =
                            $"Sample: " +
                            $"({pair.Key.x}, {pair.Key.y})\n" +
                            $"Clipmap Height: " +
                            $"{pair.Value.height:R}\n" +
                            $"Preview Height: " +
                            $"{previewHeight:R}\n" +
                            $"Difference: " +
                            $"{difference:R}";
                    }
                }
            }
        }

        // =================================================
        // RESULT
        // =================================================

        bool passed =
            bindingMismatchCount == 0
            &&
            transitionDataMismatchCount == 0
            &&
            transitionOffsetMismatchCount == 0
            &&
            positionAlignmentMismatches == 0
            &&
            invalidMappings == 0
            &&
            invalidHeights == 0
            &&
            displacedVerticesOutsideBounds == 0
            &&
            adaptiveTriangleMismatches == 0
            &&
            stitchBoundaryMismatches == 0
            &&
            sharedSampleHeightMismatches == 0
            &&
            (
                !compareAgainstPreview
                ||
                (
                    previewSamplesFound ==
                        previewSamplesRequired
                    &&
                    previewSamplesMissing == 0
                    &&
                    previewHeightMismatches == 0
                    &&
                    previewDuplicateHeightMismatches == 0
                )
            );

        lastValidationPassed =
            passed;

        validationRoutine =
            null;

        string report =
            $"Renderers Validated: " +
            $"{validationSources.Count}\n" +

            $"Vertices Validated: " +
            $"{verticesValidated:N0}\n" +

            $"Unique Height Samples: " +
            $"{uniqueClipmapSamples.Count:N0}\n\n" +

            $"Shader Binding Mismatches: " +
            $"{bindingMismatchCount:N0}\n\n" +

            "Adaptive Stitch Data\n" +

            $"Transition Data Mismatches: " +
            $"{transitionDataMismatchCount:N0}\n" +

            $"Transition Offset Mismatches: " +
            $"{transitionOffsetMismatchCount:N0}\n" +

            $"Adaptive Triangle Mismatches: " +
            $"{adaptiveTriangleMismatches:N0}\n" +

            $"Stitch Boundary Mismatches: " +
            $"{stitchBoundaryMismatches:N0}\n\n" +

            $"Sample Alignment Mismatches: " +
            $"{positionAlignmentMismatches:N0}\n" +

            $"Maximum Alignment Difference: " +
            $"{maximumAlignmentDifference:R}\n\n" +

            $"Out-of-World Vertices: " +
            $"{outOfWorldVertices:N0}\n" +

            $"Invalid Height Mappings: " +
            $"{invalidMappings:N0}\n" +

            $"Invalid Heights: " +
            $"{invalidHeights:N0}\n\n" +

            "Renderer Bounds\n" +

            $"Displaced Vertices Outside Bounds: " +
            $"{displacedVerticesOutsideBounds:N0}\n" +

            $"Maximum Bounds Overflow: " +
            $"{maximumBoundsOverflow:R}\n\n" +

            $"Shared Cross-Mesh Samples: " +
            $"{sharedSamplePositions:N0}\n" +

            $"Shared Height Mismatches: " +
            $"{sharedSampleHeightMismatches:N0}\n" +

            $"Maximum Shared Height Difference: " +
            $"{maximumSharedHeightDifference:R}\n\n" +

            $"Preview Samples Required: " +
            $"{previewSamplesRequired:N0}\n" +

            $"Preview Samples Found: " +
            $"{previewSamplesFound:N0}\n" +

            $"Preview Samples Missing: " +
            $"{previewSamplesMissing:N0}\n" +

            $"Preview Height Mismatches: " +
            $"{previewHeightMismatches:N0}\n" +

            $"Preview Duplicate Mismatches: " +
            $"{previewDuplicateHeightMismatches:N0}\n" +

            $"Maximum Preview Height Difference: " +
            $"{maximumPreviewHeightDifference:R}\n\n" +

            $"Minimum Terrain Height: " +
            $"{minimumHeight:R}\n" +

            $"Maximum Terrain Height: " +
            $"{maximumHeight:R}";

        if (passed)
        {
            Debug.Log(
                "Clipmap displacement validation passed.\n\n" +
                report,
                this
            );

            yield break;
        }

        string details =
            "";

        AppendProblem(
            ref details,
            "First Shader Binding Problem",
            firstBindingMismatch
        );

        AppendProblem(
            ref details,
            "First Transition Data Problem",
            firstTransitionDataMismatch
        );

        AppendProblem(
            ref details,
            "First Transition Offset Problem",
            firstTransitionOffsetMismatch
        );

        AppendProblem(
            ref details,
            "First Adaptive Triangle Problem",
            firstAdaptiveTriangleMismatch
        );

        AppendProblem(
            ref details,
            "First Stitch Boundary Problem",
            firstStitchBoundaryMismatch
        );

        AppendProblem(
            ref details,
            "First Vertex Problem",
            firstVertexProblem
        );

        AppendProblem(
            ref details,
            "First Renderer Bounds Mismatch",
            firstBoundsMismatch
        );

        AppendProblem(
            ref details,
            "First Shared Sample Mismatch",
            firstSharedMismatch
        );

        AppendProblem(
            ref details,
            "First Preview Mismatch",
            firstPreviewMismatch
        );

        Debug.LogError(
            "Clipmap displacement validation FAILED.\n\n" +
            report +
            details,
            this
        );
    }

    // =====================================================
    // APPEND PROBLEM
    // =====================================================

    private static void AppendProblem(
        ref string details,
        string heading,
        string problem
    )
    {
        if (problem == null)
        {
            return;
        }

        details +=
            "\n\n" +
            heading +
            ":\n" +
            problem;
    }

    // =====================================================
    // READ SHADER BINDING
    // =====================================================

    private bool TryReadShaderBinding(
        MeshRenderer renderer,
        out ShaderBindingState binding,
        out string error
    )
    {
        binding =
            default;

        error =
            null;

        Material material =
            renderer.sharedMaterial;

        if (
            material == null
            ||
            !material.HasProperty(
                ClipmapTransitionOffsetPropertyId
            )
        )
        {
            error =
                "Clipmap shader does not expose " +
                "_ClipmapTransitionOffset.";

            return false;
        }

        MaterialPropertyBlock block =
            new MaterialPropertyBlock();

        renderer.GetPropertyBlock(
            block
        );

        Texture texture =
            block.GetTexture(
                HeightCachePropertyId
            );

        Texture2DArray cache =
            texture as Texture2DArray;

        if (cache == null)
        {
            error =
                "_HeightCache is not a Texture2DArray.";

            return false;
        }

        float ready =
            block.GetFloat(
                HeightCacheReadyPropertyId
            );

        if (ready < 0.5f)
        {
            error =
                "_HeightCacheReady is not enabled.";

            return false;
        }

        Vector4 origin =
            block.GetVector(
                HeightCacheOriginTilePropertyId
            );

        Vector4 cacheSize =
            block.GetVector(
                HeightCacheSizePropertyId
            );

        float samplesPerSideValue =
            block.GetFloat(
                HeightTileSamplesPerSidePropertyId
            );

        float sampleSpacing =
            block.GetFloat(
                HeightSampleSpacingPropertyId
            );

        Vector4 worldSize =
            block.GetVector(
                WorldSizeXZPropertyId
            );

        Vector4 transitionOffset =
            block.GetVector(
                ClipmapTransitionOffsetPropertyId
            );

        int samplesPerSide =
            Mathf.RoundToInt(
                samplesPerSideValue
            );

        int cacheWidth =
            Mathf.RoundToInt(
                cacheSize.x
            );

        int cacheHeight =
            Mathf.RoundToInt(
                cacheSize.y
            );

        if (
            samplesPerSide <= 1
            ||
            cacheWidth <= 0
            ||
            cacheHeight <= 0
            ||
            sampleSpacing <= 0f
            ||
            worldSize.x <= 0f
            ||
            worldSize.y <= 0f
            ||
            !IsFinite(
                transitionOffset.x
            )
            ||
            !IsFinite(
                transitionOffset.z
            )
        )
        {
            error =
                "One or more shader metadata values are invalid.";

            return false;
        }

        binding =
            new ShaderBindingState(
                cache,
                new Vector2Int(
                    Mathf.RoundToInt(
                        origin.x
                    ),
                    Mathf.RoundToInt(
                        origin.y
                    )
                ),
                cacheWidth,
                cacheHeight,
                samplesPerSide,
                sampleSpacing,
                new Vector2(
                    worldSize.x,
                    worldSize.y
                ),
                new Vector2(
                    transitionOffset.x,
                    transitionOffset.z
                )
            );

        return true;
    }

    // =====================================================
    // VALIDATE BINDING AGAINST STREAMER
    // =====================================================

    private bool ValidateBindingAgainstStreamer(
        ShaderBindingState binding,
        out string error
    )
    {
        error =
            null;

        if (
            binding.cache !=
            streamer.HeightCache
        )
        {
            error =
                "_HeightCache does not reference the streamer's " +
                "current Texture2DArray.";

            return false;
        }

        if (
            binding.cacheOrigin !=
            streamer.CacheOriginTile
        )
        {
            error =
                "_HeightCacheOriginTile does not match the " +
                "streamer's cache origin.";

            return false;
        }

        if (
            binding.cacheWidth !=
                streamer.CacheWidth
            ||
            binding.cacheHeight !=
                streamer.CacheHeight
        )
        {
            error =
                "_HeightCacheSize does not match the streamer's " +
                "cache dimensions.";

            return false;
        }

        if (
            binding.cache.width !=
                binding.samplesPerSide
            ||
            binding.cache.height !=
                binding.samplesPerSide
        )
        {
            error =
                "_HeightTileSamplesPerSide does not match the " +
                "Texture2DArray dimensions.";

            return false;
        }

        return true;
    }

    // =====================================================
    // VALIDATE TRANSITION OFFSET
    // =====================================================

    private bool ValidateTransitionOffset(
        MeshRenderer renderer,
        Vector2 transitionOffset,
        bool isStitch,
        int fineLevel,
        int coarseLevel,
        out string error
    )
    {
        error =
            null;

        Vector2 expectedOffset =
            Vector2.zero;

        if (isStitch)
        {
            if (
                !clipmapController.TryGetDesiredLODAnchor(
                    fineLevel,
                    out Vector3 fineAnchor,
                    out _
                )
                ||
                !clipmapController.TryGetDesiredLODAnchor(
                    coarseLevel,
                    out Vector3 coarseAnchor,
                    out _
                )
            )
            {
                error =
                    $"{renderer.name}: desired LOD anchors " +
                    "are unavailable.";

                return false;
            }

            expectedOffset =
                new Vector2(
                    fineAnchor.x -
                    coarseAnchor.x,

                    fineAnchor.z -
                    coarseAnchor.z
                );
        }

        float difference =
            Vector2.Distance(
                transitionOffset,
                expectedOffset
            );

        if (
            difference >
            sampleAlignmentTolerance
        )
        {
            error =
                $"{renderer.name}\n" +
                $"Expected Transition Offset: " +
                $"({expectedOffset.x:R}, " +
                $"{expectedOffset.y:R})\n" +
                $"Actual Transition Offset: " +
                $"({transitionOffset.x:R}, " +
                $"{transitionOffset.y:R})\n" +
                $"Difference: {difference:R}";

            return false;
        }

        return true;
    }

    // =====================================================
    // COMPARE HEIGHT BINDINGS
    // =====================================================

    private bool BindingsMatch(
        ShaderBindingState a,
        ShaderBindingState b
    )
    {
        /*
         * transitionOffset is intentionally NOT compared here.
         *
         * Every adaptive stitch may have a different offset.
         */
        return
            a.cache ==
                b.cache
            &&
            a.cacheOrigin ==
                b.cacheOrigin
            &&
            a.cacheWidth ==
                b.cacheWidth
            &&
            a.cacheHeight ==
                b.cacheHeight
            &&
            a.samplesPerSide ==
                b.samplesPerSide
            &&
            Mathf.Abs(
                a.sampleSpacing -
                b.sampleSpacing
            )
            <=
            sampleAlignmentTolerance
            &&
            Vector2.Distance(
                a.worldSize,
                b.worldSize
            )
            <=
            sampleAlignmentTolerance;
    }

    // =====================================================
    // REPRODUCE SHADER HEIGHT LOOKUP
    // =====================================================

    private bool TryResolveShaderHeight(
        ShaderBindingState binding,
        float worldX,
        float worldZ,
        NativeArray<float>[] cacheSlices,
        out Vector2Int globalSample,
        out float height,
        out string error
    )
    {
        globalSample =
            default;

        height =
            0f;

        error =
            null;

        float sampleSpacing =
            Mathf.Max(
                binding.sampleSpacing,
                0.000001f
            );

        int samplesPerSide =
            binding.samplesPerSide;

        int tileIntervals =
            samplesPerSide -
            1;

        float clampedWorldX =
            Mathf.Clamp(
                worldX,
                0f,
                binding.worldSize.x
            );

        float clampedWorldZ =
            Mathf.Clamp(
                worldZ,
                0f,
                binding.worldSize.y
            );

        globalSample =
            new Vector2Int(
                Mathf.FloorToInt(
                    clampedWorldX /
                    sampleSpacing
                    +
                    0.5f
                ),
                Mathf.FloorToInt(
                    clampedWorldZ /
                    sampleSpacing
                    +
                    0.5f
                )
            );

        Vector2Int worldMaxSample =
            new Vector2Int(
                Mathf.FloorToInt(
                    binding.worldSize.x /
                    sampleSpacing
                    +
                    0.5f
                ),
                Mathf.FloorToInt(
                    binding.worldSize.y /
                    sampleSpacing
                    +
                    0.5f
                )
            );

        Vector2Int totalTileCount =
            new Vector2Int(
                Mathf.Max(
                    1,
                    (
                        worldMaxSample.x
                        +
                        tileIntervals
                        -
                        1
                    )
                    /
                    tileIntervals
                ),
                Mathf.Max(
                    1,
                    (
                        worldMaxSample.y
                        +
                        tileIntervals
                        -
                        1
                    )
                    /
                    tileIntervals
                )
            );

        Vector2Int tileCoordinate =
            new Vector2Int(
                globalSample.x /
                tileIntervals,
                globalSample.y /
                tileIntervals
            );

        tileCoordinate.x =
            Mathf.Min(
                tileCoordinate.x,
                totalTileCount.x - 1
            );

        tileCoordinate.y =
            Mathf.Min(
                tileCoordinate.y,
                totalTileCount.y - 1
            );

        Vector2Int localSample =
            globalSample
            -
            tileCoordinate *
            tileIntervals;

        if (
            localSample.x < 0
            ||
            localSample.y < 0
            ||
            localSample.x >=
                samplesPerSide
            ||
            localSample.y >=
                samplesPerSide
        )
        {
            error =
                "Calculated local height sample is outside " +
                "the source tile.";

            return false;
        }

        Vector2Int cacheLocalTile =
            tileCoordinate
            -
            binding.cacheOrigin;

        if (
            cacheLocalTile.x < 0
            ||
            cacheLocalTile.y < 0
            ||
            cacheLocalTile.x >=
                binding.cacheWidth
            ||
            cacheLocalTile.y >=
                binding.cacheHeight
        )
        {
            error =
                "Required height tile is outside the current " +
                "GPU cache.";

            return false;
        }

        int slice =
            cacheLocalTile.x
            +
            cacheLocalTile.y *
            binding.cacheWidth;

        if (
            slice < 0
            ||
            slice >=
                cacheSlices.Length
        )
        {
            error =
                $"Calculated cache slice {slice} is invalid.";

            return false;
        }

        int sampleIndex =
            localSample.x
            +
            localSample.y *
            samplesPerSide;

        NativeArray<float> sliceData =
            cacheSlices[
                slice
            ];

        if (
            sampleIndex < 0
            ||
            sampleIndex >=
                sliceData.Length
        )
        {
            error =
                $"Calculated sample index {sampleIndex} " +
                "is outside the cache slice.";

            return false;
        }

        height =
            sliceData[
                sampleIndex
            ];

        return true;
    }

    // =====================================================
    // STITCH NAME PARSING
    // =====================================================

    private static bool TryParseStitchLevels(
        string rendererName,
        out int fineLevel,
        out int coarseLevel
    )
    {
        fineLevel =
            -1;

        coarseLevel =
            -1;

        if (
            string.IsNullOrEmpty(
                rendererName
            )
        )
        {
            return false;
        }

        string[] parts =
            rendererName.Split(
                '_'
            );

        if (
            parts.Length != 3
            ||
            parts[0] != "Stitch"
            ||
            !parts[1].StartsWith("LOD")
            ||
            !parts[2].StartsWith("LOD")
        )
        {
            return false;
        }

        if (
            !int.TryParse(
                parts[1].Substring(3),
                out fineLevel
            )
            ||
            !int.TryParse(
                parts[2].Substring(3),
                out coarseLevel
            )
        )
        {
            fineLevel =
                -1;

            coarseLevel =
                -1;

            return false;
        }

        return
            coarseLevel ==
            fineLevel + 1;
    }

    private static string GetFineRendererName(
        int fineLevel
    )
    {
        return
            fineLevel == 0
                ? "Center_LOD0"
                : $"Ring_LOD{fineLevel}";
    }

    // =====================================================
    // FIND PREVIEW ROOT
    // =====================================================

    private Transform FindPreviewRoot()
    {
        if (transform.parent == null)
        {
            return null;
        }

        return
            transform.parent.Find(
                "Preview"
            );
    }

    // =====================================================
    // BUILD PREVIEW HEIGHT LOOKUP
    // =====================================================

    private Dictionary<Vector2Int, float>
        BuildPreviewHeightLookup(
            Transform previewRoot,
            Dictionary<Vector2Int, ClipmapSample>
                .KeyCollection requiredSamples,
            float sampleSpacing,
            ref int duplicateHeightMismatches
        )
    {
        HashSet<Vector2Int> required =
            new HashSet<Vector2Int>(
                requiredSamples
            );

        Dictionary<Vector2Int, float> result =
            new Dictionary<Vector2Int, float>(
                required.Count
            );

        MeshFilter[] previewFilters =
            previewRoot
                .GetComponentsInChildren<MeshFilter>(
                    true
                );

        foreach (
            MeshFilter meshFilter
            in previewFilters
        )
        {
            if (
                meshFilter == null
                ||
                meshFilter.sharedMesh == null
            )
            {
                continue;
            }

            Vector3[] vertices;

            try
            {
                vertices =
                    meshFilter.sharedMesh.vertices;
            }
            catch
            {
                continue;
            }

            foreach (
                Vector3 localVertex
                in vertices
            )
            {
                Vector3 worldPosition =
                    meshFilter.transform
                        .TransformPoint(
                            localVertex
                        );

                Vector2Int sample =
                    new Vector2Int(
                        Mathf.RoundToInt(
                            worldPosition.x /
                            sampleSpacing
                        ),
                        Mathf.RoundToInt(
                            worldPosition.z /
                            sampleSpacing
                        )
                    );

                if (
                    !required.Contains(
                        sample
                    )
                )
                {
                    continue;
                }

                if (
                    result.TryGetValue(
                        sample,
                        out float existingHeight
                    )
                )
                {
                    if (
                        Mathf.Abs(
                            existingHeight -
                            worldPosition.y
                        )
                        >
                        heightTolerance
                    )
                    {
                        duplicateHeightMismatches++;
                    }

                    continue;
                }

                result.Add(
                    sample,
                    worldPosition.y
                );
            }
        }

        return result;
    }

    // =====================================================
    // FINITE
    // =====================================================

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value)
            &&
            !float.IsInfinity(value);
    }

    // =====================================================
    // FAIL VALIDATION
    // =====================================================

    private void FailValidation(
        string reason
    )
    {
        lastValidationPassed =
            false;

        validationRoutine =
            null;

        Debug.LogError(
            "Clipmap displacement validation FAILED.\n\n" +
            reason,
            this
        );
    }

    // =====================================================
    // SHADER BINDING STATE
    // =====================================================

    private readonly struct ShaderBindingState
    {
        public readonly Texture2DArray cache;

        public readonly Vector2Int cacheOrigin;

        public readonly int cacheWidth;

        public readonly int cacheHeight;

        public readonly int samplesPerSide;

        public readonly float sampleSpacing;

        public readonly Vector2 worldSize;

        /*
         * x = world X transition offset
         * y = world Z transition offset
         */
        public readonly Vector2 transitionOffset;

        public ShaderBindingState(
            Texture2DArray cache,
            Vector2Int cacheOrigin,
            int cacheWidth,
            int cacheHeight,
            int samplesPerSide,
            float sampleSpacing,
            Vector2 worldSize,
            Vector2 transitionOffset
        )
        {
            this.cache =
                cache;

            this.cacheOrigin =
                cacheOrigin;

            this.cacheWidth =
                cacheWidth;

            this.cacheHeight =
                cacheHeight;

            this.samplesPerSide =
                samplesPerSide;

            this.sampleSpacing =
                sampleSpacing;

            this.worldSize =
                worldSize;

            this.transitionOffset =
                transitionOffset;
        }
    }

    // =====================================================
    // RENDERER VALIDATION SOURCE
    // =====================================================

    private readonly struct RendererValidationSource
    {
        public readonly MeshRenderer renderer;

        public readonly MeshFilter meshFilter;

        public readonly ShaderBindingState binding;

        public readonly List<Vector4> clipmapData;

        public readonly bool isStitch;

        public readonly int fineLevel;

        public readonly int coarseLevel;

        public RendererValidationSource(
            MeshRenderer renderer,
            MeshFilter meshFilter,
            ShaderBindingState binding,
            List<Vector4> clipmapData,
            bool isStitch,
            int fineLevel,
            int coarseLevel
        )
        {
            this.renderer =
                renderer;

            this.meshFilter =
                meshFilter;

            this.binding =
                binding;

            this.clipmapData =
                clipmapData;

            this.isStitch =
                isStitch;

            this.fineLevel =
                fineLevel;

            this.coarseLevel =
                coarseLevel;
        }
    }

    // =====================================================
    // STITCH BOUNDARY REQUIREMENT
    // =====================================================

    private readonly struct StitchBoundaryRequirement
    {
        public readonly string stitchRendererName;

        public readonly int vertexIndex;

        public readonly Vector2Int worldSample;

        public readonly string expectedRendererName;

        public readonly int expectedRendererInstanceId;

        public readonly float transitionWeight;

        public StitchBoundaryRequirement(
            string stitchRendererName,
            int vertexIndex,
            Vector2Int worldSample,
            string expectedRendererName,
            int expectedRendererInstanceId,
            float transitionWeight
        )
        {
            this.stitchRendererName =
                stitchRendererName;

            this.vertexIndex =
                vertexIndex;

            this.worldSample =
                worldSample;

            this.expectedRendererName =
                expectedRendererName;

            this.expectedRendererInstanceId =
                expectedRendererInstanceId;

            this.transitionWeight =
                transitionWeight;
        }
    }

    // =====================================================
    // CLIPMAP SAMPLE
    // =====================================================

    private struct ClipmapSample
    {
        public float height;

        public int rendererInstanceId;

        public bool sharedAcrossRenderers;

        public ClipmapSample(
            float height,
            int rendererInstanceId
        )
        {
            this.height =
                height;

            this.rendererInstanceId =
                rendererInstanceId;

            sharedAcrossRenderers =
                false;
        }
    }
}
