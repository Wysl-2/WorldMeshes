using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Stage 9 generic GPU Terrain Analysis backend.
 *
 * Type-specific metadata now comes from TerrainAnalysisRegistry. Adding a new
 * registered analysis kernel no longer requires another field/switch branch
 * in this generator.
 */
[InitializeOnLoad]
public sealed class TerrainAnalysisGpuGenerator :
    ITerrainAnalysisGenerator
{
    public const string ComputeShaderAssetPath =
        "Assets/WorldMeshes/Shaders/Terrain/TerrainAnalysis.compute";

    private sealed class KernelState
    {
        public readonly TerrainAnalysisDefinition Definition;
        public readonly int Kernel;
        public readonly uint ThreadGroupSizeX;
        public readonly uint ThreadGroupSizeY;

        public KernelState(
            TerrainAnalysisDefinition definition,
            int kernel,
            uint threadGroupSizeX,
            uint threadGroupSizeY
        )
        {
            Definition =
                definition;

            Kernel =
                kernel;

            ThreadGroupSizeX =
                threadGroupSizeX;

            ThreadGroupSizeY =
                threadGroupSizeY;
        }
    }

    private static readonly TerrainAnalysisGpuGenerator Instance =
        new TerrainAnalysisGpuGenerator();

    private ComputeShader computeShader;

    private readonly Dictionary<
        TerrainAnalysisType,
        KernelState
    > kernelStates =
        new Dictionary<
            TerrainAnalysisType,
            KernelState
        >();

    /*
     * Used to distinguish ordinary in-place preview slice updates from a
     * complete TerrainAuthoringPreviewCache RenderTexture replacement.
     */
    private static int lastObservedHeightCacheInstanceId;

    /*
     * Interactive authoring may update the Height Preview many times during
     * one gesture. When the current visualization does not require live
     * Terrain Analysis, retain only the unique changed source tiles here and
     * submit them once after the final authoritative preview state.
     */
    private static readonly HashSet<Vector2Int>
        deferredSourceTiles =
            new HashSet<Vector2Int>();

    private static readonly List<Vector2Int>
        deferredSourceTileBuffer =
            new List<Vector2Int>();

    private static bool
        interactiveAnalysisDeferralPending;

    static TerrainAnalysisGpuGenerator()
    {
        TerrainAnalysisService.RegisterGenerator(
            Instance
        );

        TerrainAuthoringPreviewService.CompositeTilesUpdated +=
            OnCompositeTilesUpdated;

        TerrainAuthoringPreviewService.PreviewStateChanged +=
            OnPreviewStateChanged;

        TerrainAuthoringPreviewService.TerrainAnalysisSourceChanged +=
            OnTerrainAnalysisSourceChanged;

        AssemblyReloadEvents.beforeAssemblyReload +=
            Shutdown;

        EditorApplication.quitting +=
            Shutdown;
    }

    public bool TryGenerate(
        TerrainAnalysisKey key,
        out TerrainAnalysisGenerationResult result,
        out string errorMessage
    )
    {
        result = default;
        errorMessage = "";

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            errorMessage =
                "Terrain analysis authoring generation is unavailable " +
                "while entering or running Play Mode.";

            return false;
        }

        if (
            !TerrainAnalysisRegistry
                .TryValidateKey(
                    key,
                    out _,
                    out errorMessage
                )
        )
        {
            return false;
        }

        if (!TryPrepare(out errorMessage))
        {
            return false;
        }

        if (
            !TryGetSource(
                out TerrainAnalysisGpuSource source,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TerrainAnalysisWindowUtility
                .TryCalculateInteractiveOutputWindow(
                    source,
                    out TerrainHeightCacheWindow outputWindow,
                    out errorMessage
                )
        )
        {
            return false;
        }

        if (
            !TryGenerateFromPreparedSource(
                key,
                source,
                outputWindow,
                out result,
                out errorMessage
            )
        )
        {
            return false;
        }

        lastObservedHeightCacheInstanceId =
            source.SourceResourceIdentity;

        return true;
    }

    internal static bool TryGenerateFromSource(
        TerrainAnalysisKey key,
        TerrainAnalysisGpuSource source,
        TerrainHeightCacheWindow outputWindow,
        out TerrainAnalysisGenerationResult result,
        out string errorMessage
    )
    {
        result = default;
        errorMessage = "";

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            errorMessage =
                "Terrain analysis generation is unavailable while entering or running Play Mode.";

            return false;
        }

        if (
            !TerrainAnalysisRegistry
                .TryValidateKey(
                    key,
                    out _,
                    out errorMessage
                )
        )
        {
            return false;
        }

        if (!Instance.TryPrepare(out errorMessage))
        {
            return false;
        }

        return
            Instance.TryGenerateFromPreparedSource(
                key,
                source,
                outputWindow,
                out result,
                out errorMessage
            );
    }

    private bool TryGenerateFromPreparedSource(
        TerrainAnalysisKey key,
        TerrainAnalysisGpuSource source,
        TerrainHeightCacheWindow outputWindow,
        out TerrainAnalysisGenerationResult result,
        out string errorMessage
    )
    {
        result = default;
        errorMessage = "";

        if (
            !source.IsValid
            ||
            !outputWindow.IsValid
            ||
            !source.SourceWindow.Contains(
                outputWindow
            )
        )
        {
            errorMessage =
                "Terrain analysis received an invalid bounded source/output layout.";

            return false;
        }

        int sliceCount =
            outputWindow.TileCount;

        RenderTexture output =
            CreateAnalysisTexture(
                key,
                source.SamplesPerSide,
                sliceCount
            );

        if (
            output == null
            ||
            !output.IsCreated()
        )
        {
            DestroyCandidate(
                output
            );

            errorMessage =
                "The GPU terrain-analysis texture could not be created.";

            return false;
        }

        if (
            !TryGetKernel(
                key,
                out KernelState kernelState,
                out errorMessage
            )
        )
        {
            DestroyCandidate(
                output
            );

            return false;
        }

        int groupsX =
            DivideRoundUp(
                source.SamplesPerSide,
                kernelState.ThreadGroupSizeX
            );

        int groupsY =
            DivideRoundUp(
                source.SamplesPerSide,
                kernelState.ThreadGroupSizeY
            );

        if (
            groupsX <= 0
            ||
            groupsY <= 0
        )
        {
            DestroyCandidate(
                output
            );

            errorMessage =
                "Terrain analysis calculated an invalid compute dispatch.";

            return false;
        }

        try
        {
            SetCommonKernelParameters(
                kernelState.Kernel,
                key,
                source,
                output,
                outputWindow
            );

            computeShader.SetInt(
                "_OutputSliceOffset",
                0
            );

            computeShader.Dispatch(
                kernelState.Kernel,
                groupsX,
                groupsY,
                sliceCount
            );
        }
        catch (Exception exception)
        {
            DestroyCandidate(
                output
            );

            errorMessage =
                "Terrain analysis GPU dispatch failed for " +
                key +
                ".\n\n" +
                exception.Message;

            return false;
        }

        result =
            new TerrainAnalysisGenerationResult(
                output,
                outputWindow.OriginTile,
                outputWindow.Size,
                source.SourceWindow.OriginTile,
                source.SourceWindow.Size,
                source.SamplesPerSide,
                source.SampleSpacing,
                source.WorldSizeXZ,
                source.SourceSignature,
                source.SourceResourceIdentity,
                source.ResidencyGeneration
            );

        if (!result.IsValid)
        {
            DestroyCandidate(
                output
            );

            result = default;

            errorMessage =
                "Terrain analysis generation completed, but the generated layer layout was invalid.";

            return false;
        }

        return true;
    }

    public bool TryUpdateTiles(
        TerrainAnalysisLayer layer,
        IReadOnlyList<Vector2Int> tileCoordinates,
        out string sourceSignature,
        out string errorMessage
    )
    {
        sourceSignature = "";
        errorMessage = "";

        if (
            layer == null
            ||
            !layer.IsReady
            ||
            layer.Texture == null
            ||
            !layer.Texture.IsCreated()
        )
        {
            errorMessage =
                "Incremental terrain analysis requires a ready analysis layer.";

            return false;
        }

        if (
            tileCoordinates == null
            ||
            tileCoordinates.Count == 0
        )
        {
            sourceSignature = layer.SourceSignature;
            return true;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            errorMessage =
                "Terrain analysis authoring generation is unavailable while entering or running Play Mode.";

            return false;
        }

        if (
            !TerrainAnalysisRegistry
                .TryValidateKey(
                    layer.Key,
                    out _,
                    out errorMessage
                )
        )
        {
            return false;
        }

        if (!TryPrepare(out errorMessage))
        {
            return false;
        }

        if (
            !TryGetSource(
                out TerrainAnalysisGpuSource source,
                out errorMessage
            )
        )
        {
            return false;
        }

        sourceSignature =
            source.SourceSignature;

        if (
            source.SourceResourceIdentity !=
                layer.SourceHeightCacheInstanceId
            ||
            source.SourceWindow.OriginTile !=
                layer.SourceCacheOriginTile
            ||
            source.SourceWindow.Size !=
                layer.SourceCacheSize
            ||
            source.ResidencyGeneration !=
                layer.SourceResidencyGeneration
            ||
            source.SamplesPerSide !=
                layer.SamplesPerSide
            ||
            !Mathf.Approximately(
                source.SampleSpacing,
                layer.SampleSpacing
            )
            ||
            !VectorApproximately(
                source.WorldSizeXZ,
                layer.WorldSizeXZ
            )
        )
        {
            errorMessage =
                "The Terrain Authoring Height Preview was replaced or its source layout changed. This analysis layer requires a complete regeneration.";

            return false;
        }

        if (
            layer.Texture.width !=
                source.SamplesPerSide
            ||
            layer.Texture.height !=
                source.SamplesPerSide
            ||
            layer.Texture.volumeDepth !=
                layer.CacheSize.x *
                layer.CacheSize.y
        )
        {
            errorMessage =
                "The existing analysis texture no longer matches its local analysis-output layout.";

            return false;
        }

        if (
            !TryGetKernel(
                layer.Key,
                out KernelState kernelState,
                out errorMessage
            )
        )
        {
            return false;
        }

        int groupsX =
            DivideRoundUp(
                source.SamplesPerSide,
                kernelState.ThreadGroupSizeX
            );

        int groupsY =
            DivideRoundUp(
                source.SamplesPerSide,
                kernelState.ThreadGroupSizeY
            );

        if (
            groupsX <= 0
            ||
            groupsY <= 0
        )
        {
            errorMessage =
                "Terrain analysis calculated an invalid incremental compute dispatch.";

            return false;
        }

        HashSet<int> uniqueSlices =
            new HashSet<int>();

        for (
            int tileIndex = 0;
            tileIndex < tileCoordinates.Count;
            tileIndex++
        )
        {
            Vector2Int tile =
                tileCoordinates[tileIndex];

            Vector2Int localTile =
                tile -
                layer.CacheOriginTile;

            if (
                localTile.x < 0
                ||
                localTile.y < 0
                ||
                localTile.x >= layer.CacheSize.x
                ||
                localTile.y >= layer.CacheSize.y
            )
            {
                continue;
            }

            uniqueSlices.Add(
                localTile.x +
                localTile.y *
                layer.CacheSize.x
            );
        }

        if (uniqueSlices.Count == 0)
        {
            lastObservedHeightCacheInstanceId =
                source.SourceResourceIdentity;

            return true;
        }

        List<int> sortedSlices =
            new List<int>(
                uniqueSlices
            );

        sortedSlices.Sort();

        try
        {
            SetCommonKernelParameters(
                kernelState.Kernel,
                layer.Key,
                source,
                layer.Texture,
                new TerrainHeightCacheWindow(
                    layer.CacheOriginTile,
                    layer.CacheSize
                )
            );

            int sliceIndex = 0;

            while (sliceIndex < sortedSlices.Count)
            {
                int firstSlice = sortedSlices[sliceIndex];
                int lastSlice = firstSlice;
                int nextIndex = sliceIndex + 1;

                while (
                    nextIndex < sortedSlices.Count
                    &&
                    sortedSlices[nextIndex] == lastSlice + 1
                )
                {
                    lastSlice = sortedSlices[nextIndex];
                    nextIndex++;
                }

                int runLength =
                    lastSlice -
                    firstSlice +
                    1;

                computeShader.SetInt(
                    "_OutputSliceOffset",
                    firstSlice
                );

                computeShader.Dispatch(
                    kernelState.Kernel,
                    groupsX,
                    groupsY,
                    runLength
                );

                sliceIndex = nextIndex;
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                "Incremental terrain analysis GPU dispatch failed for " +
                layer.Key +
                ".\n\n" +
                exception.Message;

            return false;
        }

        lastObservedHeightCacheInstanceId =
            source.SourceResourceIdentity;

        return true;
    }

    // =====================================================
    // GENERATOR PREPARATION / REGISTRY KERNELS
    // =====================================================

    private bool TryPrepare(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            computeShader != null
            &&
            KernelStateIsComplete()
        )
        {
            return true;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "The current graphics device does not support compute shaders.";

            return false;
        }

        if (!SystemInfo.supports2DArrayTextures)
        {
            errorMessage =
                "The current graphics device does not support 2D texture arrays.";

            return false;
        }

        if (
            !SystemInfo.SupportsRenderTextureFormat(
                RenderTextureFormat.RHalf
            )
        )
        {
            errorMessage =
                "The current graphics device does not support RHalf render " +
                "textures required by Terrain Analysis.";

            return false;
        }

        computeShader =
            AssetDatabase
                .LoadAssetAtPath<ComputeShader>(
                    ComputeShaderAssetPath
                );

        if (computeShader == null)
        {
            ResetShaderState();

            errorMessage =
                "TerrainAnalysis.compute could not be loaded:\n\n" +
                ComputeShaderAssetPath;

            return false;
        }

        kernelStates.Clear();

        IReadOnlyList<TerrainAnalysisDefinition> definitions =
            TerrainAnalysisRegistry
                .Definitions;

        try
        {
            for (
                int index = 0;
                index < definitions.Count;
                index++
            )
            {
                TerrainAnalysisDefinition definition =
                    definitions[index];

                if (
                    definition == null
                    ||
                    string.IsNullOrEmpty(
                        definition.ComputeKernelName
                    )
                )
                {
                    throw new InvalidOperationException(
                        "A registered Terrain Analysis definition has no compute kernel name."
                    );
                }

                int kernel =
                    computeShader.FindKernel(
                        definition
                            .ComputeKernelName
                    );

                computeShader
                    .GetKernelThreadGroupSizes(
                        kernel,
                        out uint threadGroupSizeX,
                        out uint threadGroupSizeY,
                        out _
                    );

                if (
                    threadGroupSizeX == 0
                    ||
                    threadGroupSizeY == 0
                )
                {
                    throw new InvalidOperationException(
                        "Terrain Analysis kernel reported an invalid thread-group size: " +
                        definition.ComputeKernelName
                    );
                }

                kernelStates.Add(
                    definition.Type,
                    new KernelState(
                        definition,
                        kernel,
                        threadGroupSizeX,
                        threadGroupSizeY
                    )
                );
            }
        }
        catch (
            Exception exception
        )
        {
            ResetShaderState();

            errorMessage =
                "TerrainAnalysis.compute could not prepare all registered " +
                "Terrain Analysis kernels.\n\n" +
                exception.Message;

            return false;
        }

        if (!KernelStateIsComplete())
        {
            ResetShaderState();

            errorMessage =
                "Terrain Analysis kernel registration is incomplete.";

            return false;
        }

        return true;
    }

    private bool KernelStateIsComplete()
    {
        IReadOnlyList<TerrainAnalysisDefinition> definitions =
            TerrainAnalysisRegistry
                .Definitions;

        if (
            definitions == null
            ||
            kernelStates.Count !=
                definitions.Count
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < definitions.Count;
            index++
        )
        {
            TerrainAnalysisDefinition definition =
                definitions[index];

        if (
                definition == null
                ||
                !kernelStates.TryGetValue(
                    definition.Type,
                    out KernelState state
                )
                ||
                state == null
                ||
                state.Kernel < 0
                ||
                state.ThreadGroupSizeX == 0
                ||
                state.ThreadGroupSizeY == 0
            )
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetKernel(
        TerrainAnalysisKey key,
        out KernelState kernelState,
        out string errorMessage
    )
    {
        kernelState =
            null;

        errorMessage =
            "";

        if (
            !TerrainAnalysisRegistry
                .TryValidateKey(
                    key,
                    out TerrainAnalysisDefinition definition,
                    out errorMessage
                )
            ||
            definition == null
        )
        {
            return false;
        }

        if (
            !kernelStates.TryGetValue(
                definition.Type,
                out kernelState
            )
            ||
            kernelState == null
        )
        {
            errorMessage =
                "No prepared GPU kernel exists for terrain analysis " +
                definition.DisplayName +
                ".";

            return false;
        }

        return true;
    }

    // =====================================================
    // SOURCE / COMMON PARAMETERS
    // =====================================================

    private static bool TryGetSource(
        out TerrainAnalysisGpuSource source,
        out string errorMessage
    )
    {
        source = default;
        errorMessage = "";

        if (
            !TerrainAuthoringPreviewService
                .TryGetTerrainAnalysisGpuSource(
                    out source
                )
            ||
            !source.IsValid
        )
        {
            errorMessage =
                "Terrain analysis requires a ready bounded Terrain Authoring Height Preview source.";

            return false;
        }

        return true;
    }

    private void SetCommonKernelParameters(
        int kernel,
        TerrainAnalysisKey key,
        TerrainAnalysisGpuSource source,
        RenderTexture output,
        TerrainHeightCacheWindow outputWindow
    )
    {
        computeShader.SetTexture(
            kernel,
            "_HeightCache",
            source.HeightCache
        );

        computeShader.SetTexture(
            kernel,
            "_AnalysisOutput",
            output
        );

        computeShader.SetInts(
            "_HeightCacheOriginTile",
            source.SourceWindow.OriginTile.x,
            source.SourceWindow.OriginTile.y
        );

        computeShader.SetInts(
            "_HeightCacheSize",
            source.SourceWindow.Size.x,
            source.SourceWindow.Size.y
        );

        computeShader.SetInts(
            "_AnalysisOutputOriginTile",
            outputWindow.OriginTile.x,
            outputWindow.OriginTile.y
        );

        computeShader.SetInts(
            "_AnalysisOutputSize",
            outputWindow.Size.x,
            outputWindow.Size.y
        );

        computeShader.SetInt(
            "_SamplesPerSide",
            source.SamplesPerSide
        );

        computeShader.SetFloat(
            "_SampleSpacing",
            source.SampleSpacing
        );

        computeShader.SetVector(
            "_WorldSizeXZ",
            new Vector4(
                source.WorldSizeXZ.x,
                source.WorldSizeXZ.y,
                0f,
                0f
            )
        );

        computeShader.SetFloat(
            "_AnalysisScaleMeters",
            key.HasScale
                ? key.ScaleMeters
                : 0f
        );
    }

    private static RenderTexture CreateAnalysisTexture(
        TerrainAnalysisKey key,
        int samplesPerSide,
        int sliceCount
    )
    {
        RenderTextureDescriptor descriptor =
            new RenderTextureDescriptor(
                samplesPerSide,
                samplesPerSide,
                RenderTextureFormat.RHalf,
                0
            );

        descriptor.dimension =
            TextureDimension.Tex2DArray;

        descriptor.volumeDepth =
            sliceCount;

        descriptor.msaaSamples =
            1;

        descriptor.useMipMap =
            false;

        descriptor.autoGenerateMips =
            false;

        descriptor.enableRandomWrite =
            true;

        descriptor.sRGB =
            false;

        RenderTexture texture =
            new RenderTexture(
                descriptor
            );

        texture.name =
            "WorldMeshes Terrain Analysis - " +
            key;

        texture.filterMode =
            FilterMode.Bilinear;

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.hideFlags =
            HideFlags.HideAndDontSave;

        if (!texture.Create())
        {
            DestroyCandidate(
                texture
            );

            return null;
        }

        return texture;
    }

    private static int DivideRoundUp(
        int value,
        uint divisor
    )
    {
        if (
            value <= 0
            ||
            divisor == 0
        )
        {
            return 0;
        }

        return
            (
                value +
                (int)divisor -
                1
            )
            /
            (int)divisor;
    }

    // =====================================================
    // PREVIEW INVALIDATION / LIFETIME
    // =====================================================

    private static void OnCompositeTilesUpdated(
        IReadOnlyList<Vector2Int> tileCoordinates
    )
    {
        if (
            tileCoordinates == null
            ||
            tileCoordinates.Count == 0
        )
        {
            return;
        }

        bool interactiveEditActive =
            TerrainAuthoringPreviewService
                .HasActiveInteractiveTerrainAuthoringEdit;

        bool requiresLiveAnalysis =
            TerrainAuthoringVisualizationController
                .RequiresLiveTerrainAnalysisDuringInteractiveEdit;

        /*
         * Once a non-analysis interactive gesture starts deferring analysis,
         * keep that deferral latched until an authoritative PreviewStateChanged
         * boundary. This also covers a final height transaction that executes
         * just after MouseUp, when HasActiveInteractiveEdit is already false.
         */
        if (interactiveAnalysisDeferralPending)
        {
            AccumulateDeferredSourceTiles(
                tileCoordinates
            );

            if (
                interactiveEditActive
                &&
                requiresLiveAnalysis
            )
            {
                /*
                 * The user switched from Lit/Height into an analysis
                 * visualization while the gesture was still active. Catch the
                 * visible analysis up immediately, then resume normal live
                 * analysis updates for later drag samples.
                 */
                FlushDeferredSourceTiles();
            }

            return;
        }

        if (
            interactiveEditActive
            &&
            !requiresLiveAnalysis
        )
        {
            AccumulateDeferredSourceTiles(
                tileCoordinates
            );

            interactiveAnalysisDeferralPending =
                true;

            return;
        }

        TerrainAnalysisService
            .NotifySourceTilesChanged(
                tileCoordinates
            );
    }

    private static void OnTerrainAnalysisSourceChanged()
    {
        ResetDeferredAnalysisState();

        if (
            TerrainAuthoringPreviewService
                .TryGetTerrainAnalysisGpuSource(
                    out TerrainAnalysisGpuSource source
                )
        )
        {
            lastObservedHeightCacheInstanceId =
                source.SourceResourceIdentity;
        }
        else
        {
            lastObservedHeightCacheInstanceId =
                0;
        }

        TerrainAnalysisService
            .InvalidateAll();
    }

    private static void OnPreviewStateChanged()
    {
        if (
            !TerrainAuthoringPreviewService
                .CacheReady
        )
        {
            ResetDeferredAnalysisState();

            lastObservedHeightCacheInstanceId =
                0;

            TerrainAnalysisService.Clear();

            return;
        }

        int currentCacheInstanceId =
            TerrainAuthoringPreviewService
                .CacheTextureInstanceId;

        if (currentCacheInstanceId == 0)
        {
            ResetDeferredAnalysisState();

            lastObservedHeightCacheInstanceId =
                0;

            TerrainAnalysisService.Clear();

            return;
        }

        if (
            lastObservedHeightCacheInstanceId ==
                0
        )
        {
            ResetDeferredAnalysisState();

            lastObservedHeightCacheInstanceId =
                currentCacheInstanceId;

            TerrainAnalysisService
                .InvalidateAll();

            return;
        }

        if (
            currentCacheInstanceId !=
                lastObservedHeightCacheInstanceId
        )
        {
            ResetDeferredAnalysisState();

            lastObservedHeightCacheInstanceId =
                currentCacheInstanceId;

            TerrainAnalysisService
                .InvalidateAll();

            return;
        }

        /*
         * Same cache object means ordinary composite/metadata updates.
         * While a gesture is still active, a deferred analysis set must remain
         * stale even though the Height Preview itself has reached a valid
         * intermediate state.
         */
        if (
            TerrainAuthoringPreviewService
                .HasActiveInteractiveTerrainAuthoringEdit
        )
        {
            return;
        }

        /*
         * PreviewStateChanged is emitted after the successful height
         * recomposition/range transaction and after overall authoring identity
         * acknowledgement. It is therefore the safe release boundary for a
         * deferred interactive analysis update.
         */
        if (interactiveAnalysisDeferralPending)
        {
            FlushDeferredSourceTiles();
        }
    }

    private static void AccumulateDeferredSourceTiles(
        IReadOnlyList<Vector2Int> tileCoordinates
    )
    {
        if (tileCoordinates == null)
        {
            return;
        }

        for (
            int index = 0;
            index < tileCoordinates.Count;
            index++
        )
        {
            deferredSourceTiles.Add(
                tileCoordinates[
                    index
                ]
            );
        }
    }

    private static void FlushDeferredSourceTiles()
    {
        if (
            !interactiveAnalysisDeferralPending
            ||
            deferredSourceTiles.Count == 0
        )
        {
            ResetDeferredAnalysisState();

            return;
        }

        deferredSourceTileBuffer.Clear();

        foreach (
            Vector2Int tile
            in deferredSourceTiles
        )
        {
            deferredSourceTileBuffer.Add(
                tile
            );
        }

        /*
         * Submit source height tiles only. TerrainAnalysisService remains
         * runtime-safe and continues to perform the registered dependency
         * radius expansion independently for every ready persistent/transient
         * layer.
         */
        TerrainAnalysisService
            .NotifySourceTilesChanged(
                deferredSourceTileBuffer
            );

        ResetDeferredAnalysisState();
    }

    private static void ResetDeferredAnalysisState()
    {
        deferredSourceTiles.Clear();

        deferredSourceTileBuffer.Clear();

        interactiveAnalysisDeferralPending =
            false;
    }

    private static void Shutdown()
    {
        TerrainAuthoringPreviewService.CompositeTilesUpdated -=
            OnCompositeTilesUpdated;

        TerrainAuthoringPreviewService.PreviewStateChanged -=
            OnPreviewStateChanged;

        TerrainAuthoringPreviewService.TerrainAnalysisSourceChanged -=
            OnTerrainAnalysisSourceChanged;

        AssemblyReloadEvents.beforeAssemblyReload -=
            Shutdown;

        EditorApplication.quitting -=
            Shutdown;

        ResetDeferredAnalysisState();

        TerrainAnalysisService.Clear();

        TerrainAnalysisService
            .UnregisterGenerator(
                Instance
            );

        Instance.ResetShaderState();
    }

    private void ResetShaderState()
    {
        computeShader =
            null;

        kernelStates.Clear();

        lastObservedHeightCacheInstanceId =
            0;
    }

    private static bool VectorApproximately(
        Vector2 a,
        Vector2 b
    )
    {
        return
            Mathf.Approximately(
                a.x,
                b.x
            )
            &&
            Mathf.Approximately(
                a.y,
                b.y
            );
    }

    private static void DestroyCandidate(
        RenderTexture texture
    )
    {
        if (texture == null)
        {
            return;
        }

        if (texture.IsCreated())
        {
            texture.Release();
        }

        UnityEngine.Object
            .DestroyImmediate(
                texture
            );
    }
}
