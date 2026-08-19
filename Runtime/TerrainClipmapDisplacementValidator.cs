using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class TerrainClipmapDisplacementValidator :
    MonoBehaviour
{
    // =====================================================
    // VALIDATION SETTINGS
    // =====================================================

    [Header("Validation")]

    [SerializeField]
    private bool validateOnStart =
        true;

    /*
     * Maximum allowed difference between a clipmap
     * vertex X/Z position and the authoritative height
     * sample grid.
     */
    [SerializeField]
    [Min(0f)]
    private float sampleAlignmentTolerance =
        0.0001f;

    /*
     * Maximum allowed height difference when comparing
     * the GPU-cache lookup against the Preview mesh.
     */
    [SerializeField]
    [Min(0f)]
    private float heightTolerance =
        0.00001f;
    
    /*
     * Small tolerance when testing displaced vertices against
     * Renderer.localBounds.
     *
     * This avoids reporting a failure when a vertex lies on
     * a bounds edge and differs only because of floating-point
     * transform precision.
     */
    [SerializeField]
    [Min(0f)]
    private float boundsContainmentTolerance =
        0.0001f;

    /*
     * How long the validator waits for the streamer to
     * finish loading, validating and binding the cache.
     */
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
    
    private TerrainClipmapBoundsController boundsController;

    private Coroutine validationRoutine;

    private bool lastValidationPassed;

    // =====================================================
    // SHADER PROPERTY IDS
    // =====================================================

    private static readonly int HeightCachePropertyId =
        Shader.PropertyToID(
            "_HeightCache"
        );

    private static readonly int HeightCacheOriginTilePropertyId =
        Shader.PropertyToID(
            "_HeightCacheOriginTile"
        );

    private static readonly int HeightCacheSizePropertyId =
        Shader.PropertyToID(
            "_HeightCacheSize"
        );

    private static readonly int HeightTileSamplesPerSidePropertyId =
        Shader.PropertyToID(
            "_HeightTileSamplesPerSide"
        );

    private static readonly int HeightSampleSpacingPropertyId =
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

            // =====================================================
            // HEIGHTMAP STREAMER
            // =====================================================

            streamer =
                GetComponent<TerrainHeightmapStreamer>();

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

            // =====================================================
            // BOUNDS CONTROLLER
            // =====================================================

            boundsController =
                GetComponent<TerrainClipmapBoundsController>();

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

            // =====================================================
            // AUTO VALIDATION
            // =====================================================

            if (validateOnStart)
            {
                BeginValidation();
            }
        }

    // =====================================================
    // DISABLE
    // =====================================================

    private void OnDisable()
    {
        if (
            validationRoutine != null
        )
        {
            StopCoroutine(
                validationRoutine
            );

            validationRoutine =
                null;
        }
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

        if (streamer == null)
        {
            Debug.LogError(
                "Cannot validate clipmap displacement.\n\n" +
                "TerrainHeightmapStreamer was not found.",
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
    // WAIT FOR STREAMER / SHADER / BOUNDS
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

            if (
                streamerReady
                &&
                shaderReady
                &&
                boundsReady
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
                    "cache, clipmap shader bindings and renderer " +
                    "displacement bounds."
                );

                yield break;
            }

            yield return null;
        }

        /*
         * Give the renderer one complete frame after all
         * runtime state becomes ready.
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

        // =====================================================
        // CLIPMAP RENDERERS
        // =====================================================

        MeshRenderer[] allRenderers =
            GetComponentsInChildren<MeshRenderer>(
                true
            );

        List<RendererValidationSource>
            validationSources =
                new List<RendererValidationSource>();

        int bindingMismatchCount =
            0;

        string firstBindingMismatch =
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
                meshRenderer.GetComponent<MeshFilter>();

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
                    meshRenderer,
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
                        "shader metadata differs from the " +
                        "other clipmap renderers.";
                }
            }

            validationSources.Add(
                new RendererValidationSource(
                    meshRenderer,
                    meshFilter,
                    binding
                )
            );
        }

        if (
            validationSources.Count == 0
        )
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

        // =====================================================
        // GPU CACHE READBACK
        // =====================================================

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
                cacheSlices[slice] =
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

        // =====================================================
        // VERTEX VALIDATION
        // =====================================================

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
        
        // =====================================================
        // RENDERER BOUNDS STATISTICS
        // =====================================================

        long displacedVerticesOutsideBounds =
            0L;

        float maximumBoundsOverflow =
            0f;

        string firstBoundsMismatch =
            null;

        float maximumAlignmentDifference =
            0f;

        float minimumHeight =
            float.PositiveInfinity;

        float maximumHeight =
            float.NegativeInfinity;

        string firstVertexProblem =
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

        foreach (
            RendererValidationSource source
            in validationSources
        )
        {
            Mesh mesh =
                source.meshFilter.sharedMesh;

            Vector3[] vertices;

            try
            {
                vertices =
                    mesh.vertices;
            }
            catch (
                System.Exception exception
            )
            {
                FailValidation(
                    $"Could not read vertices from mesh " +
                    $"'{mesh.name}'.\n\n" +

                    exception.Message
                );

                yield break;
            }

            for (
                int vertexIndex = 0;
                vertexIndex < vertices.Length;
                vertexIndex++
            )
            {
                Vector3 worldPosition =
                    source.meshFilter.transform
                        .TransformPoint(
                            vertices[vertexIndex]
                        );

                verticesValidated++;

                // -------------------------------------------------
                // World bounds
                // -------------------------------------------------

                /*
                 * Clipmap geometry is intentionally allowed to extend
                 * beyond the authoritative terrain world.
                 *
                 * Terrain outside the world is removed by the fragment
                 * shader, so an out-of-world vertex is informational
                 * rather than a displacement failure.
                 *
                 * Continue validating the vertex below. The shader still
                 * executes its vertex stage and clamps height lookup
                 * coordinates to the authoritative world.
                 */
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

                // -------------------------------------------------
                // Source sample-grid alignment
                // -------------------------------------------------

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
                            $"World Position: " +
                            $"{worldPosition}\n" +

                            $"Grid Difference: " +
                            $"{alignmentDifference:R}";
                    }
                }

                // -------------------------------------------------
                // Reproduce shader lookup
                // -------------------------------------------------

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
                    float.IsNaN(
                        expectedHeight
                    )
                    ||
                    float.IsInfinity(
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

                // =================================================
                // DISPLACED RENDERER BOUNDS
                // =================================================

                /*
                 * Reproduce the world-space position produced by the
                 * vertex shader.
                 *
                 * The shader preserves world X/Z and replaces world Y
                 * with the sampled terrain height.
                 */

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

                            $"Original World Position: " +
                            $"{worldPosition}\n" +

                            $"Displaced World Position: " +
                            $"{displacedWorldPosition}\n" +

                            $"Displaced Local Position: " +
                            $"{displacedLocalPosition}\n\n" +

                            $"Renderer Local Bounds Center: " +
                            $"{rendererBounds.center}\n" +

                            $"Renderer Local Bounds Size: " +
                            $"{rendererBounds.size}\n" +

                            $"Renderer Local Bounds Min: " +
                            $"{rendererBounds.min}\n" +

                            $"Renderer Local Bounds Max: " +
                            $"{rendererBounds.max}\n\n" +

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

                // =================================================
                // SHARED SAMPLE / SEAM VALIDATION
                // =================================================

                if (
                    uniqueClipmapSamples.TryGetValue(
                        globalSample,
                        out ClipmapSample existingSample
                    )
                )
                {
                    /*
                     * Only count this as a cross-mesh shared
                     * sample when the same X/Z coordinate occurs
                     * on a different renderer.
                     */

                    if (
                        existingSample.rendererInstanceId
                        !=
                        source.renderer.GetInstanceID()
                    )
                    {
                        if (!existingSample.sharedAcrossRenderers)
                        {
                            sharedSamplePositions++;

                            existingSample.sharedAcrossRenderers =
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
                            source.renderer.GetInstanceID()
                        )
                    );
                }
            }
        }

        // =====================================================
        // PREVIEW COMPARISON
        // =====================================================

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
                        ref previewDuplicateHeightMismatches
                    );

            foreach (
                KeyValuePair<Vector2Int, ClipmapSample> pair
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
                            $"({pair.Key.x}, " +
                            $"{pair.Key.y})\n" +

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

        // =====================================================
        // RESULT
        // =====================================================

        bool passed =
            bindingMismatchCount == 0
            &&
            positionAlignmentMismatches == 0
            &&
            invalidMappings == 0
            &&
            invalidHeights == 0
            &&
            displacedVerticesOutsideBounds == 0
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

        if (passed)
        {
            Debug.Log(
                "Clipmap displacement validation passed.\n\n" +

                $"Renderers Validated: " +
                $"{validationSources.Count}\n" +

                $"Vertices Validated: " +
                $"{verticesValidated:N0}\n" +

                $"Unique Height Samples: " +
                $"{uniqueClipmapSamples.Count:N0}\n\n" +

                $"Shader Binding Mismatches: " +
                $"{bindingMismatchCount:N0}\n\n" +

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

                $"Maximum Preview Height Difference: " +
                $"{maximumPreviewHeightDifference:R}\n\n" +

                $"Minimum Terrain Height: " +
                $"{minimumHeight:R}\n" +

                $"Maximum Terrain Height: " +
                $"{maximumHeight:R}",
                this
            );
        }
        else
        {
            string details =
                "";

            if (
                firstBindingMismatch !=
                null
            )
            {
                details +=
                    "\n\nFirst Shader Binding Problem:\n" +
                    firstBindingMismatch;
            }

            if (
                firstVertexProblem !=
                null
            )
            {
                details +=
                    "\n\nFirst Vertex Problem:\n" +
                    firstVertexProblem;
            }

            if (
                firstBoundsMismatch !=
                null
            )
            {
                details +=
                    "\n\nFirst Renderer Bounds Mismatch:\n" +
                    firstBoundsMismatch;
            }

            if (
                firstSharedMismatch !=
                null
            )
            {
                details +=
                    "\n\nFirst Shared Sample Mismatch:\n" +
                    firstSharedMismatch;
            }

            if (
                firstPreviewMismatch !=
                null
            )
            {
                details +=
                    "\n\nFirst Preview Mismatch:\n" +
                    firstPreviewMismatch;
            }

            Debug.LogError(
                "Clipmap displacement validation FAILED.\n\n" +

                $"Renderers Validated: " +
                $"{validationSources.Count}\n" +

                $"Vertices Validated: " +
                $"{verticesValidated:N0}\n" +

                $"Unique Height Samples: " +
                $"{uniqueClipmapSamples.Count:N0}\n\n" +

                $"Shader Binding Mismatches: " +
                $"{bindingMismatchCount:N0}\n\n" +

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
                $"{maximumPreviewHeightDifference:R}" +

                details,
                this
            );
        }
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
                )
            );

        return true;
    }

    // =====================================================
    // VALIDATE BINDING AGAINST STREAMER
    // =====================================================

    private bool ValidateBindingAgainstStreamer(
        MeshRenderer renderer,
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
    // COMPARE BINDINGS
    // =====================================================

    private bool BindingsMatch(
        ShaderBindingState a,
        ShaderBindingState b
    )
    {
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
            cacheSlices[slice];

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
            Dictionary<Vector2Int, ClipmapSample>.KeyCollection
                requiredSamples,
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

        public ShaderBindingState(
            Texture2DArray cache,
            Vector2Int cacheOrigin,
            int cacheWidth,
            int cacheHeight,
            int samplesPerSide,
            float sampleSpacing,
            Vector2 worldSize
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

        public RendererValidationSource(
            MeshRenderer renderer,
            MeshFilter meshFilter,
            ShaderBindingState binding
        )
        {
            this.renderer =
                renderer;

            this.meshFilter =
                meshFilter;

            this.binding =
                binding;
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