using System;
using UnityEngine;
using UnityEngine.Rendering;

internal enum TerrainAuthoringPreviewSliceReadiness
{
    Uninitialized,
    CommittedBaseReady,
    FinalCompositeReady
}

public sealed partial class TerrainAuthoringPreviewCache
{
    private TerrainAuthoringPreviewSliceReadiness[]
        sliceReadiness;

    internal bool IsCompleteForActivation
    {
        get
        {
            return
                TryValidateCompleteForActivation(
                    out _
                );
        }
    }

    internal bool TryInitializeStagingWindow(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainHeightCacheWindow targetWindow,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";

            return false;
        }

        TerrainAuthoringHeightManifest manifest;
        string currentContentHash;
        string validationError;
        bool validationSucceeded;

        using (WorldMeshesProfiler.PreviewValidateCommitted.Auto())
        {
            validationSucceeded =
                TerrainAuthoringStateUtility
                    .TryValidateCommittedHeightfield(
                        worldSettings,
                        authoringData,
                        TerrainAuthoringHeightfieldValidationMode
                            .Operational,
                        out manifest,
                        out currentContentHash,
                        out validationError
                    );
        }

        if (!validationSucceeded)
        {
            errorMessage =
                "The staging cache could not validate the committed " +
                "authoring heightfield.\n\n" +
                validationError;

            return false;
        }

        string committedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        if (
            string.IsNullOrEmpty(
                committedSignature
            )
        )
        {
            errorMessage =
                "The committed heightfield signature could not be " +
                "calculated for staging.";

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
                RenderTextureFormat.RFloat
            )
        )
        {
            errorMessage =
                "The current graphics device does not support RFloat render textures.";

            return false;
        }

        if (
            SystemInfo.copyTextureSupport ==
                CopyTextureSupport.None
        )
        {
            errorMessage =
                "The current graphics device does not support Graphics.CopyTexture.";

            return false;
        }

        int manifestWidth =
            manifest.heightTileGridWidth;

        int manifestHeight =
            manifest.heightTileGridHeight;

        int stagingSamplesPerSide =
            manifest.heightTileSamplesPerSide;

        if (
            manifestWidth <= 0
            ||
            manifestHeight <= 0
            ||
            stagingSamplesPerSide <= 1
        )
        {
            errorMessage =
                "The committed authoring manifest contains an invalid height-tile layout.";

            return false;
        }

        if (
            !targetWindow.IsValid
            ||
            targetWindow.OriginTile.x < 0
            ||
            targetWindow.OriginTile.y < 0
            ||
            targetWindow.MaximumExclusive.x >
                manifestWidth
            ||
            targetWindow.MaximumExclusive.y >
                manifestHeight
        )
        {
            errorMessage =
                "The staging cache window lies outside the committed " +
                "authoring height-tile grid.\n\n" +
                $"Target: {targetWindow}\n" +
                $"World Tile Grid: {manifestWidth} x {manifestHeight}";

            return false;
        }

        long stagingSliceCountLong =
            (long)targetWindow.Width *
            targetWindow.Height;

        if (
            stagingSliceCountLong <= 0L
            ||
            stagingSliceCountLong > int.MaxValue
        )
        {
            errorMessage =
                "The staging cache window contains an invalid slice count.";

            return false;
        }

        int stagingSliceCount =
            (int)stagingSliceCountLong;

        if (
            stagingSamplesPerSide >
                SystemInfo.maxTextureSize
        )
        {
            errorMessage =
                "The staging height tile size exceeds the graphics device maximum texture size.";

            return false;
        }

        if (
            SystemInfo.maxTextureArraySlices > 0
            &&
            stagingSliceCount >
                SystemInfo.maxTextureArraySlices
        )
        {
            errorMessage =
                "The staging cache requires more texture-array slices than " +
                "the current graphics device supports.\n\n" +
                $"Required: {stagingSliceCount}\n" +
                $"Maximum: {SystemInfo.maxTextureArraySlices}";

            return false;
        }

        if (
            manifest.TileHeightRangeMetadataVersion !=
                TerrainAuthoringHeightManifest
                    .CurrentTileHeightRangeMetadataVersion
            ||
            !manifest.HasCompleteTileHeightRanges
        )
        {
            errorMessage =
                "The committed authoring per-tile range metadata is incomplete or out of date.";

            return false;
        }

        float[] stagingCommittedMinimums =
            new float[
                stagingSliceCount
            ];

        float[] stagingCommittedMaximums =
            new float[
                stagingSliceCount
            ];

        for (
            int localZ = 0;
            localZ < targetWindow.Height;
            localZ++
        )
        {
            int worldZ =
                targetWindow.OriginTile.y +
                localZ;

            for (
                int localX = 0;
                localX < targetWindow.Width;
                localX++
            )
            {
                int worldX =
                    targetWindow.OriginTile.x +
                    localX;

                if (
                    !manifest.TryGetTileHeightRange(
                        worldX,
                        worldZ,
                        out float minimum,
                        out float maximum
                    )
                )
                {
                    errorMessage =
                        "Committed range metadata is unavailable for " +
                        $"staging tile ({worldX}, {worldZ}).";

                    return false;
                }

                int slice =
                    localX
                    +
                    localZ *
                    targetWindow.Width;

                stagingCommittedMinimums[
                    slice
                ] =
                    minimum;

                stagingCommittedMaximums[
                    slice
                ] =
                    maximum;
            }
        }

        RenderTexture candidateCache =
            CreateHeightCache(
                stagingSamplesPerSide,
                stagingSliceCount
            );

        if (
            candidateCache == null
            ||
            !candidateCache.IsCreated()
        )
        {
            DestroyRenderTexture(
                candidateCache
            );

            errorMessage =
                "The staging GPU height cache could not be created.";

            return false;
        }

        DestroyRenderTexture(
            heightCache
        );

        heightCache =
            candidateCache;

        cacheOriginTile =
            targetWindow.OriginTile;

        cacheWidth =
            targetWindow.Width;

        cacheHeight =
            targetWindow.Height;

        samplesPerSide =
            stagingSamplesPerSide;

        sampleSpacing =
            Mathf.Max(
                0.000001f,
                worldSettings.chunkSize
                /
                Mathf.Max(
                    1,
                    worldSettings
                        .heightfieldResolutionPerChunk
                )
            );

        worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        committedSliceMinimumHeights =
            stagingCommittedMinimums;

        committedSliceMaximumHeights =
            stagingCommittedMaximums;

        sliceMinimumHeights =
            new float[
                stagingSliceCount
            ];

        sliceMaximumHeights =
            new float[
                stagingSliceCount
            ];

        sliceRangeValid =
            new bool[
                stagingSliceCount
            ];

        InitializeSliceReadiness(
            stagingSliceCount,
            TerrainAuthoringPreviewSliceReadiness
                .Uninitialized
        );

        minimumHeight =
            0f;

        maximumHeight =
            0f;

        sourceCommittedHeightfieldSignature =
            committedSignature;

        sourceOverallAuthoringSignature =
            "";

        sourceContentHash =
            currentContentHash;

        lastIncrementalSliceCount =
            0;

        totalIncrementalSliceUpdates =
            0L;

        return true;
    }

    internal bool TryLoadCommittedBaseTile(
        Vector2Int worldTile,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        int slice =
            GetSliceIndex(
                worldTile.x,
                worldTile.y
            );

        if (slice < 0)
        {
            errorMessage =
                $"Tile ({worldTile.x}, {worldTile.y}) is outside the staging cache.";

            return false;
        }

        if (
            !TryLoadCommittedTileTexture(
                worldTile.x,
                worldTile.y,
                samplesPerSide,
                out Texture2D sourceTexture,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TryGetCommittedSliceRange(
                slice,
                out float committedMinimum,
                out float committedMaximum,
                out errorMessage
            )
        )
        {
            return false;
        }

        try
        {
            using (WorldMeshesProfiler.PreviewCopyTiles.Auto())
            {
                Graphics.CopyTexture(
                    sourceTexture,
                    0,
                    0,
                    heightCache,
                    slice,
                    0
                );
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Committed tile ({worldTile.x}, {worldTile.y}) could not " +
                "be copied into the staging cache.\n\n" +
                exception.Message;

            return false;
        }

        sliceMinimumHeights[
            slice
        ] =
            committedMinimum;

        sliceMaximumHeights[
            slice
        ] =
            committedMaximum;

        sliceRangeValid[
            slice
        ] =
            true;

        SetSliceReadinessBySlice(
            slice,
            TerrainAuthoringPreviewSliceReadiness
                .CommittedBaseReady
        );

        return true;
    }

    internal bool TryCopyFinalCompositeTileFrom(
        TerrainAuthoringPreviewCache sourceCache,
        Vector2Int worldTile,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (sourceCache == null)
        {
            errorMessage =
                "The retained staging copy source cache is null.";

            return false;
        }

        int sourceSlice =
            sourceCache.GetSliceIndex(
                worldTile.x,
                worldTile.y
            );

        int destinationSlice =
            GetSliceIndex(
                worldTile.x,
                worldTile.y
            );

        if (
            sourceSlice < 0
            ||
            destinationSlice < 0
        )
        {
            errorMessage =
                $"Retained tile ({worldTile.x}, {worldTile.y}) is not " +
                "resident in both source and destination caches.";

            return false;
        }

        if (
            !sourceCache.IsSliceFinalCompositeReady(
                worldTile
            )
        )
        {
            errorMessage =
                $"Retained source tile ({worldTile.x}, {worldTile.y}) is not final-composite ready.";

            return false;
        }

        if (
            sourceCache.SamplesPerSide !=
                SamplesPerSide
            ||
            !Mathf.Approximately(
                sourceCache.SampleSpacing,
                SampleSpacing
            )
            ||
            sourceCache.WorldSizeXZ !=
                WorldSizeXZ
        )
        {
            errorMessage =
                "Retained source and staging caches have incompatible height addressing.";

            return false;
        }

        if (
            !sourceCache.TryGetCommittedSliceRange(
                sourceSlice,
                out float committedMinimum,
                out float committedMaximum,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !sourceCache.TryGetCompositeSliceRange(
                worldTile.x,
                worldTile.y,
                out float compositeMinimum,
                out float compositeMaximum
            )
        )
        {
            errorMessage =
                $"Retained source tile ({worldTile.x}, {worldTile.y}) does not have a valid final range.";

            return false;
        }

        try
        {
            using (WorldMeshesProfiler.PreviewCopyTiles.Auto())
            {
                Graphics.CopyTexture(
                    sourceCache.HeightCache,
                    sourceSlice,
                    0,
                    HeightCache,
                    destinationSlice,
                    0
                );
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Retained tile ({worldTile.x}, {worldTile.y}) could not " +
                "be copied from the active cache into staging.\n\n" +
                exception.Message;

            return false;
        }

        committedSliceMinimumHeights[
            destinationSlice
        ] =
            committedMinimum;

        committedSliceMaximumHeights[
            destinationSlice
        ] =
            committedMaximum;

        sliceMinimumHeights[
            destinationSlice
        ] =
            compositeMinimum;

        sliceMaximumHeights[
            destinationSlice
        ] =
            compositeMaximum;

        sliceRangeValid[
            destinationSlice
        ] =
            true;

        SetSliceReadinessBySlice(
            destinationSlice,
            TerrainAuthoringPreviewSliceReadiness
                .FinalCompositeReady
        );

        return true;
    }

    internal bool TryGetCommittedRange(
        Vector2Int worldTile,
        out float minimum,
        out float maximum,
        out string errorMessage
    )
    {
        int slice =
            GetSliceIndex(
                worldTile.x,
                worldTile.y
            );

        return
            TryGetCommittedSliceRange(
                slice,
                out minimum,
                out maximum,
                out errorMessage
            );
    }

    internal bool TryCommitFinalCompositeTile(
        Vector2Int worldTile,
        float minimum,
        float maximum,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        int slice =
            GetSliceIndex(
                worldTile.x,
                worldTile.y
            );

        if (slice < 0)
        {
            errorMessage =
                $"Tile ({worldTile.x}, {worldTile.y}) is outside the staging cache.";

            return false;
        }

        if (
            !IsFinite(
                minimum
            )
            ||
            !IsFinite(
                maximum
            )
            ||
            maximum <
                minimum
        )
        {
            errorMessage =
                $"Final staging range is invalid for tile ({worldTile.x}, {worldTile.y}).";

            return false;
        }

        sliceMinimumHeights[
            slice
        ] =
            minimum;

        sliceMaximumHeights[
            slice
        ] =
            maximum;

        sliceRangeValid[
            slice
        ] =
            true;

        SetSliceReadinessBySlice(
            slice,
            TerrainAuthoringPreviewSliceReadiness
                .FinalCompositeReady
        );

        return true;
    }

    internal bool TryFinalizeStagingForActivation(
        string overallAuthoringSignature,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            string.IsNullOrEmpty(
                overallAuthoringSignature
            )
        )
        {
            errorMessage =
                "The staging cache cannot be finalized without an overall authoring signature.";

            return false;
        }

        sourceOverallAuthoringSignature =
            overallAuthoringSignature;

        if (
            !RecalculateGlobalHeightRange(
                out errorMessage
            )
        )
        {
            sourceOverallAuthoringSignature =
                "";

            return false;
        }

        if (
            !TryValidateCompleteForActivation(
                out errorMessage
            )
        )
        {
            sourceOverallAuthoringSignature =
                "";

            return false;
        }

        return true;
    }

    internal bool TryValidateCompleteForActivation(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (!IsReady)
        {
            errorMessage =
                "The staging cache is not structurally ready.";

            return false;
        }

        if (
            sliceReadiness == null
            ||
            sliceReadiness.Length !=
                SliceCount
        )
        {
            errorMessage =
                "The staging cache slice-readiness state is incomplete.";

            return false;
        }

        if (
            string.IsNullOrEmpty(
                sourceCommittedHeightfieldSignature
            )
            ||
            string.IsNullOrEmpty(
                sourceOverallAuthoringSignature
            )
        )
        {
            errorMessage =
                "The staging cache source signatures are incomplete.";

            return false;
        }

        for (
            int slice = 0;
            slice < SliceCount;
            slice++
        )
        {
            if (
                sliceReadiness[
                    slice
                ]
                !=
                TerrainAuthoringPreviewSliceReadiness
                    .FinalCompositeReady
            )
            {
                errorMessage =
                    $"Staging cache slice {slice} has not reached FinalCompositeReady.";

                return false;
            }

            if (
                sliceRangeValid == null
                ||
                slice >=
                    sliceRangeValid.Length
                ||
                !sliceRangeValid[
                    slice
                ]
                ||
                !IsFinite(
                    sliceMinimumHeights[
                        slice
                    ]
                )
                ||
                !IsFinite(
                    sliceMaximumHeights[
                        slice
                    ]
                )
                ||
                sliceMaximumHeights[
                    slice
                ]
                <
                sliceMinimumHeights[
                    slice
                ]
            )
            {
                errorMessage =
                    $"Staging cache slice {slice} does not have a valid final composite range.";

                return false;
            }
        }

        if (
            !IsFinite(
                minimumHeight
            )
            ||
            !IsFinite(
                maximumHeight
            )
            ||
            maximumHeight <
                minimumHeight
        )
        {
            errorMessage =
                "The staging cache global final range is invalid.";

            return false;
        }

        return true;
    }

    internal bool IsSliceFinalCompositeReady(
        Vector2Int worldTile
    )
    {
        return
            GetSliceReadiness(
                worldTile
            )
            ==
            TerrainAuthoringPreviewSliceReadiness
                .FinalCompositeReady;
    }

    internal TerrainAuthoringPreviewSliceReadiness GetSliceReadiness(
        Vector2Int worldTile
    )
    {
        int slice =
            GetSliceIndex(
                worldTile.x,
                worldTile.y
            );

        if (
            slice < 0
            ||
            sliceReadiness == null
            ||
            slice >=
                sliceReadiness.Length
        )
        {
            return
                TerrainAuthoringPreviewSliceReadiness
                    .Uninitialized;
        }

        return
            sliceReadiness[
                slice
            ];
    }

    private void InitializeSliceReadiness(
        int count,
        TerrainAuthoringPreviewSliceReadiness initialState
    )
    {
        sliceReadiness =
            new TerrainAuthoringPreviewSliceReadiness[
                Mathf.Max(
                    0,
                    count
                )
            ];

        for (
            int index = 0;
            index < sliceReadiness.Length;
            index++
        )
        {
            sliceReadiness[
                index
            ] =
                initialState;
        }
    }

    private void SetSliceReadinessBySlice(
        int slice,
        TerrainAuthoringPreviewSliceReadiness readiness
    )
    {
        if (
            sliceReadiness == null
            ||
            slice < 0
            ||
            slice >=
                sliceReadiness.Length
        )
        {
            return;
        }

        sliceReadiness[
            slice
        ] =
            readiness;
    }
}
