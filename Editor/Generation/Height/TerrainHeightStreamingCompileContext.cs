using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class TerrainHeightStreamingCompileContext
{
    private readonly WorldSettings
        worldSettings;

    private readonly TerrainHeightmapManifest
        manifest;

    private readonly TerrainRuntimeBakeWorkMode
        workMode;

    private readonly int
        requestedFamilyCount;

    private readonly List<int>
        targetStrides =
            new List<int>();

    private readonly List<TerrainHeightStreamingLevelDescriptor>
        targetDescriptors =
            new List<TerrainHeightStreamingLevelDescriptor>();

    private readonly string
        targetSignature;

    private readonly bool
        baselineWasCurrent;

    private bool generationEnabled;

    private bool manifestInvalidationAttempted;

    private bool manifestPrepared;

    private int completeFamilyCount;

    private int incompleteFamilyCount;

    private int createdAssetCount;

    private int updatedAssetCount;

    private int removedAssetCount;

    private bool datasetFinalized;

    private string firstError =
        "";

    private TerrainHeightStreamingCompileContext(
        WorldSettings worldSettings,
        TerrainHeightmapManifest manifest,
        TerrainRuntimeBakeWorkMode workMode,
        int requestedFamilyCount,
        bool generationEnabled,
        string targetSignature,
        bool baselineWasCurrent
    )
    {
        this.worldSettings =
            worldSettings;

        this.manifest =
            manifest;

        this.workMode =
            workMode;

        this.requestedFamilyCount =
            Mathf.Max(
                0,
                requestedFamilyCount
            );

        this.generationEnabled =
            generationEnabled;

        this.targetSignature =
            targetSignature ??
            "";

        this.baselineWasCurrent =
            baselineWasCurrent;
    }

    public static TerrainHeightStreamingCompileContext Create(
        WorldSettings worldSettings,
        TerrainHeightmapManifest manifest,
        TerrainRuntimeBakeWorkMode workMode,
        int requestedFamilyCount
    )
    {
        string targetSignature =
            TerrainGenerationStateUtility
                .GetCurrentHeightStreamingGenerationSignature(
                    worldSettings
                );

        bool baselineWasCurrent =
            TerrainGenerationStateUtility
                .IsHeightStreamingManifestCurrent(
                    manifest,
                    worldSettings,
                    worldSettings != null
                        ? worldSettings.heightmapGenerationRevision
                        : -1
                );

        TerrainHeightStreamingCompileContext context =
            new TerrainHeightStreamingCompileContext(
                worldSettings,
                manifest,
                workMode,
                requestedFamilyCount,
                true,
                targetSignature,
                baselineWasCurrent
            );

        if (
            worldSettings == null
            ||
            manifest == null
        )
        {
            context.generationEnabled =
                false;

            context.RecordError(
                worldSettings == null
                    ? "WorldSettings is unavailable for height-streaming generation."
                    : "The runtime height manifest is unavailable for height-streaming generation."
            );

            return context;
        }

        if (
            !TerrainHeightStreamingPyramidPolicy
                .TryGetDerivedStrides(
                    worldSettings,
                    context.targetStrides,
                    out string strideError
                )
        )
        {
            context.generationEnabled =
                false;

            context.RecordError(
                strideError
            );

            return context;
        }

        if (
            string.IsNullOrEmpty(
                targetSignature
            )
        )
        {
            context.generationEnabled =
                false;

            context.RecordError(
                "The height-streaming generation signature could not be calculated."
            );

            return context;
        }

        for (
            int index = 0;
            index < context.targetStrides.Count;
            index++
        )
        {
            int stride =
                context.targetStrides[index];

            if (
                !TerrainHeightStreamingPyramidPolicy
                    .TryBuildLevelDescriptor(
                        worldSettings,
                        stride,
                        out TerrainHeightStreamingLevelDescriptor descriptor,
                        out string descriptorError
                    )
            )
            {
                context.generationEnabled =
                    false;

                context.RecordError(
                    descriptorError
                );

                context.targetDescriptors.Clear();

                break;
            }

            context.targetDescriptors.Add(
                descriptor
            );
        }

        return context;
    }

    public TerrainHeightStreamingFamilyWriteResult PrepareFamily(
        Vector2Int coordinate,
        float[] authoritativeHeightData
    )
    {
        EnsureManifestInvalidated();

        if (!generationEnabled)
        {
            return
                TerrainHeightStreamingFamilyWriteResult
                    .NotAttempted(
                        coordinate,
                        targetDescriptors.Count,
                        firstError
                    );
        }

        if (!manifestPrepared)
        {
            return
                TerrainHeightStreamingFamilyWriteResult
                    .NotAttempted(
                        coordinate,
                        targetDescriptors.Count,
                        string.IsNullOrEmpty(firstError)
                            ? "The height-streaming manifest target could not be prepared."
                            : firstError
                    );
        }

        TerrainHeightStreamingFamilyWriteResult result =
            TerrainHeightStreamingPyramidGenerator
                .PrepareFamilyFromNativeBuffer(
                    worldSettings,
                    coordinate,
                    targetDescriptors,
                    authoritativeHeightData
                );

        if (
            result.Attempted
            &&
            !result.PreparedComplete
        )
        {
            RecordError(
                result.ErrorMessage
            );
        }

        return result;
    }

    public void RecordDurableBatch(
        IReadOnlyList<TerrainHeightStreamingFamilyWriteResult> results
    )
    {
        if (results == null)
        {
            return;
        }

        for (
            int index = 0;
            index < results.Count;
            index++
        )
        {
            TerrainHeightStreamingFamilyWriteResult result =
                results[index];

            if (
                result == null
                ||
                !result.Attempted
            )
            {
                continue;
            }

            createdAssetCount +=
                result.CreatedAssetCount;

            updatedAssetCount +=
                result.UpdatedAssetCount;

            if (result.PreparedComplete)
            {
                completeFamilyCount++;
            }
            else
            {
                incompleteFamilyCount++;

                RecordError(
                    result.ErrorMessage
                );
            }
        }
    }

    public void FinalizeAfterNativeSuccess(
        WorldSettings currentWorldSettings
    )
    {
        EnsureManifestInvalidated();

        if (
            currentWorldSettings == null
            ||
            manifest == null
        )
        {
            RecordError(
                "Height Streaming could not finalize because its native generation context is unavailable."
            );

            return;
        }

        bool familyCoverageComplete =
            generationEnabled
            &&
            manifestPrepared
            &&
            incompleteFamilyCount == 0
            &&
            completeFamilyCount ==
                requestedFamilyCount;

        bool canFinalize =
            familyCoverageComplete
            &&
            (
                workMode ==
                    TerrainRuntimeBakeWorkMode.Full
                ||
                (
                    workMode ==
                        TerrainRuntimeBakeWorkMode.Incremental
                    &&
                    baselineWasCurrent
                )
            );

        if (
            canFinalize
            &&
            workMode ==
                TerrainRuntimeBakeWorkMode.Full
        )
        {
            if (
                !TerrainHeightStreamingAssetLifecycleUtility
                    .DeleteObsoleteOutputs(
                        currentWorldSettings,
                        targetDescriptors,
                        out int removed,
                        out string cleanupError
                    )
            )
            {
                canFinalize =
                    false;

                RecordError(
                    cleanupError
                );
            }
            else
            {
                removedAssetCount +=
                    removed;
            }
        }

        if (!canFinalize)
        {
            manifest.streamingPyramidIsComplete =
                false;

            manifest.streamingSourceHeightmapGenerationRevision =
                -1;

            TrySaveManifestBestEffort();

            return;
        }

        int previousStreamingRevision =
            Mathf.Max(
                0,
                manifest.streamingGenerationRevision
            );

        manifest.streamingPyramidCompilerVersion =
            TerrainGenerationStateUtility
                .RuntimeHeightStreamingCompilerVersion;

        manifest.streamingGenerationSignature =
            targetSignature;

        manifest.streamingSourceHeightmapGenerationRevision =
            currentWorldSettings.heightmapGenerationRevision;

        manifest.streamingGenerationRevision =
            previousStreamingRevision +
            1;

        manifest.streamingPyramidIsComplete =
            true;

        if (TrySaveManifest(out string finalizationError))
        {
            datasetFinalized =
                true;

            return;
        }

        datasetFinalized =
            false;

        manifest.streamingGenerationRevision =
            previousStreamingRevision;

        manifest.streamingPyramidIsComplete =
            false;

        manifest.streamingSourceHeightmapGenerationRevision =
            -1;

        RecordError(
            finalizationError
        );

        TrySaveManifestBestEffort();
    }

    public TerrainHeightStreamingCompileSummary CreateSummary()
    {
        return
            new TerrainHeightStreamingCompileSummary(
                true,
                generationEnabled,
                targetStrides,
                requestedFamilyCount,
                completeFamilyCount,
                incompleteFamilyCount,
                createdAssetCount,
                updatedAssetCount,
                removedAssetCount,
                datasetFinalized,
                firstError
            );
    }

    private void EnsureManifestInvalidated()
    {
        if (manifestInvalidationAttempted)
        {
            return;
        }

        manifestInvalidationAttempted =
            true;

        if (manifest == null)
        {
            generationEnabled =
                false;

            RecordError(
                "The runtime height manifest is unavailable for Height Streaming invalidation."
            );

            return;
        }

        manifest.streamingPyramidIsComplete =
            false;

        manifest.streamingSourceHeightmapGenerationRevision =
            -1;

        if (generationEnabled)
        {
            manifest.streamingPyramidCompilerVersion =
                TerrainGenerationStateUtility
                    .RuntimeHeightStreamingCompilerVersion;

            manifest.streamingGenerationSignature =
                targetSignature;

            if (
                !manifest.TrySetStreamingLevelDescriptors(
                    targetDescriptors
                )
            )
            {
                generationEnabled =
                    false;

                RecordError(
                    "The runtime height manifest rejected the target Height Streaming descriptor set."
                );
            }
        }

        EditorUtility.SetDirty(
            manifest
        );

        if (
            !TrySaveManifest(
                out string invalidationError
            )
        )
        {
            generationEnabled =
                false;

            RecordError(
                invalidationError
            );

            return;
        }

        if (!generationEnabled)
        {
            return;
        }

        if (
            !TerrainHeightStreamingAssetLifecycleUtility
                .EnsureTargetFolders(
                    targetDescriptors,
                    out string folderError
                )
        )
        {
            generationEnabled =
                false;

            RecordError(
                folderError
            );

            return;
        }

        manifestPrepared =
            true;
    }

    private bool TrySaveManifest(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        try
        {
            EditorUtility.SetDirty(
                manifest
            );

            AssetDatabase.SaveAssetIfDirty(
                manifest
            );

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not persist Height Streaming manifest metadata.\n\n" +
                exception.Message;

            return false;
        }
    }

    private void TrySaveManifestBestEffort()
    {
        TrySaveManifest(
            out _
        );
    }

    private void RecordError(
        string errorMessage
    )
    {
        if (
            string.IsNullOrEmpty(firstError)
            &&
            !string.IsNullOrEmpty(errorMessage)
        )
        {
            firstError =
                errorMessage;
        }
    }
}
