using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeSurfaceStreamingCompiler
{
    private const int PersistenceBatchFamilyCount =
        4;

    private static bool isGenerating;

    public static bool IsGenerating =>
        isGenerating;

    public static TerrainRuntimeSurfaceStreamingCompileResult RebuildAllSurfaceStreaming(
        WorldSettings worldSettings
    )
    {
        if (isGenerating)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                0,
                0,
                "Surface Streaming generation is already in progress."
            );
        }

        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        int revisionBefore =
            manifest != null
                ? Mathf.Max(
                    0,
                    manifest.streamingGenerationRevision
                )
                : 0;

        if (worldSettings == null)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                0,
                revisionBefore,
                "WorldSettings is unavailable."
            );
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                0,
                revisionBefore,
                "Surface Streaming generation must run outside Play Mode."
            );
        }

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                0,
                revisionBefore,
                "Surface Streaming generation is unavailable while the unified Runtime Bake is running."
            );
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                0,
                revisionBefore,
                "Surface Streaming generation is unavailable while authoritative Surface generation is running."
            );
        }

        if (
            TerrainGenerationStateUtility.GetSurfaceMaskStatus(
                worldSettings
            )
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                0,
                revisionBefore,
                "Authoritative runtime Surface masks are not current. Rebuild Surface Masks first."
            );
        }

        if (
            manifest == null
            || !manifest.isComplete
            || manifest.compilerVersion != TerrainSurfaceMaskManifest.CurrentCompilerVersion
            || manifest.channelLayoutVersion != TerrainSurfaceMaskManifest.CurrentChannelLayoutVersion
            || manifest.surfaceMaskGenerationRevision <= 0
            || string.IsNullOrEmpty(manifest.surfaceGenerationSignature)
        )
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                0,
                revisionBefore,
                "The authoritative Surface manifest is missing, incomplete, or incompatible."
            );
        }

        List<int> targetStrides =
            new List<int>();

        if (
            !TerrainSurfaceStreamingPyramidPolicy.TryGetDerivedStrides(
                worldSettings,
                manifest,
                targetStrides,
                out string policyError
            )
        )
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                0,
                revisionBefore,
                policyError
            );
        }

        List<TerrainSurfaceStreamingLevelDescriptor> targetDescriptors =
            new List<TerrainSurfaceStreamingLevelDescriptor>(
                targetStrides.Count
            );

        for (
            int index = 0;
            index < targetStrides.Count;
            index++
        )
        {
            if (
                !TerrainSurfaceStreamingPyramidPolicy.TryBuildLevelDescriptor(
                    worldSettings,
                    manifest,
                    targetStrides[index],
                    out TerrainSurfaceStreamingLevelDescriptor descriptor,
                    out string descriptorError
                )
            )
            {
                return CreateTerminalResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                    0,
                    revisionBefore,
                    descriptorError
                );
            }

            targetDescriptors.Add(descriptor);
        }

        string targetSignature =
            TerrainSurfaceSignatureUtility.GetStreamingGenerationSignature(
                TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion,
                TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion,
                manifest.surfaceMaskGenerationRevision,
                manifest.surfaceGenerationSignature,
                manifest,
                targetDescriptors
            );

        if (string.IsNullOrEmpty(targetSignature))
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Blocked,
                0,
                revisionBefore,
                "The Surface Streaming generation signature could not be calculated."
            );
        }

        int sourceSurfaceRevision =
            manifest.surfaceMaskGenerationRevision;

        string sourceSurfaceSignature =
            manifest.surfaceGenerationSignature;

        int requestedFamilyCount =
            targetDescriptors.Count > 0
                ? manifest.TileCount
                : 0;

        if (
            !TerrainSurfaceStreamingAssetLifecycleUtility.EnsureTargetFolders(
                targetDescriptors,
                out string folderError
            )
        )
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                requestedFamilyCount,
                revisionBefore,
                folderError
            );
        }

        if (!manifest.TrySetStreamingLevelDescriptors(targetDescriptors))
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                requestedFamilyCount,
                revisionBefore,
                "The Surface manifest rejected the target streaming descriptor set."
            );
        }

        manifest.streamingPyramidIsComplete = false;
        manifest.streamingPyramidCompilerVersion =
            TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion;
        manifest.streamingPyramidPolicyVersion =
            TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion;
        manifest.streamingSourceSurfaceMaskGenerationRevision = -1;
        manifest.streamingSourceSurfaceGenerationSignature = "";
        manifest.streamingGenerationSignature = targetSignature;

        if (!TrySaveManifest(manifest, out string manifestPrepareError))
        {
            return CreateTerminalResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                requestedFamilyCount,
                revisionBefore,
                manifestPrepareError
            );
        }

        Dictionary<int, byte[]> reusableDerivedBuffers =
            new Dictionary<int, byte[]>();

        List<Texture2D> dirtyOutputs =
            new List<Texture2D>();

        int completedFamilies = 0;
        int createdAssetCount = 0;
        int updatedAssetCount = 0;
        int removedAssetCount = 0;
        int familiesInCurrentBatch = 0;
        bool cancelled = false;
        string errorMessage = "";

        isGenerating = true;

        try
        {
            if (targetDescriptors.Count > 0)
            {
                for (
                    int tileZ = 0;
                    tileZ < manifest.tileGridHeight;
                    tileZ++
                )
                {
                    for (
                        int tileX = 0;
                        tileX < manifest.tileGridWidth;
                        tileX++
                    )
                    {
                        if (
                            familiesInCurrentBatch == 0
                            && EditorUtility.DisplayCancelableProgressBar(
                                "Rebuild Surface Streaming",
                                $"Surface streaming family {completedFamilies + 1} / {requestedFamilyCount}",
                                requestedFamilyCount > 0
                                    ? (float)completedFamilies /
                                        requestedFamilyCount
                                    : 1f
                            )
                        )
                        {
                            cancelled = true;
                            break;
                        }

                        if (
                            !SourceStillCurrent(
                                worldSettings,
                                manifest,
                                sourceSurfaceRevision,
                                sourceSurfaceSignature
                            )
                        )
                        {
                            errorMessage =
                                "Authoritative Surface generation changed while Surface Streaming generation was running.";
                            break;
                        }

                        Vector2Int coordinate =
                            new Vector2Int(
                                tileX,
                                tileZ
                            );

                        Texture2D nativeTexture =
                            AssetDatabase.LoadAssetAtPath<Texture2D>(
                                TerrainRuntimeSurfaceMaskAssetUtility.GetSurfaceTilePath(
                                    tileX,
                                    tileZ
                                )
                            );

                        TerrainSurfaceStreamingFamilyWriteResult familyResult;

                        try
                        {
                            familyResult =
                                TerrainSurfaceStreamingPyramidGenerator
                                    .PrepareFamilyFromExistingNativeTexture(
                                        worldSettings,
                                        manifest,
                                        coordinate,
                                        targetDescriptors,
                                        nativeTexture,
                                        reusableDerivedBuffers,
                                        dirtyOutputs
                                    );
                        }
                        finally
                        {
                            if (nativeTexture != null)
                            {
                                Resources.UnloadAsset(nativeTexture);
                            }
                        }

                        createdAssetCount +=
                            familyResult != null
                                ? familyResult.CreatedAssetCount
                                : 0;

                        updatedAssetCount +=
                            familyResult != null
                                ? familyResult.UpdatedAssetCount
                                : 0;

                        if (
                            familyResult == null
                            || !familyResult.Attempted
                            || !familyResult.PreparedComplete
                        )
                        {
                            errorMessage =
                                familyResult != null
                                    ? familyResult.ErrorMessage
                                    : "Surface Streaming generation returned no family result.";

                            if (dirtyOutputs.Count > 0)
                            {
                                if (
                                    TryPersistPreparedBatch(
                                        out string persistenceError
                                    )
                                )
                                {
                                    ReleasePersistedOutputs(dirtyOutputs);
                                }
                                else if (string.IsNullOrEmpty(errorMessage))
                                {
                                    errorMessage = persistenceError;
                                }
                            }

                            break;
                        }

                        completedFamilies++;
                        familiesInCurrentBatch++;

                        bool batchBoundaryReached =
                            familiesInCurrentBatch >= PersistenceBatchFamilyCount
                            || completedFamilies == requestedFamilyCount;

                        if (batchBoundaryReached)
                        {
                            if (
                                !TryPersistPreparedBatch(
                                    out string persistenceError
                                )
                            )
                            {
                                errorMessage = persistenceError;
                                break;
                            }

                            ReleasePersistedOutputs(dirtyOutputs);
                            familiesInCurrentBatch = 0;
                        }
                    }

                    if (
                        cancelled
                        || !string.IsNullOrEmpty(errorMessage)
                    )
                    {
                        break;
                    }
                }
            }

            if (cancelled)
            {
                return new TerrainRuntimeSurfaceStreamingCompileResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Cancelled,
                    requestedFamilyCount,
                    completedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    "",
                    "Surface Streaming generation was cancelled at a durability-safe batch boundary. Authoritative Surface data remains valid."
                );
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                if (dirtyOutputs.Count > 0)
                {
                    if (TryPersistPreparedBatch(out string persistenceError))
                    {
                        ReleasePersistedOutputs(dirtyOutputs);
                    }
                    else
                    {
                        errorMessage +=
                            "\n\nAdditionally, the final partial Surface Streaming batch could not be persisted.\n\n" +
                            persistenceError;
                    }
                }

                return new TerrainRuntimeSurfaceStreamingCompileResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                    requestedFamilyCount,
                    completedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    errorMessage,
                    "Surface Streaming generation did not complete. Authoritative Surface data remains valid."
                );
            }

            if (
                !SourceStillCurrent(
                    worldSettings,
                    manifest,
                    sourceSurfaceRevision,
                    sourceSurfaceSignature
                )
            )
            {
                return new TerrainRuntimeSurfaceStreamingCompileResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.StaleSource,
                    requestedFamilyCount,
                    completedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    "Authoritative Surface generation changed before Surface Streaming finalization.",
                    "The derived data was not published as current."
                );
            }

            if (
                !TerrainSurfaceStreamingAssetLifecycleUtility.DeleteObsoleteOutputs(
                    manifest,
                    targetDescriptors,
                    out removedAssetCount,
                    out string cleanupError
                )
            )
            {
                return new TerrainRuntimeSurfaceStreamingCompileResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                    requestedFamilyCount,
                    completedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    manifest.streamingGenerationRevision,
                    cleanupError,
                    "Surface Streaming generation completed its target representations but obsolete-output cleanup failed."
                );
            }

            AssetDatabase.SaveAssets();

            int previousRevision =
                Mathf.Max(
                    0,
                    manifest.streamingGenerationRevision
                );

            manifest.streamingPyramidCompilerVersion =
                TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion;
            manifest.streamingPyramidPolicyVersion =
                TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion;
            manifest.streamingSourceSurfaceMaskGenerationRevision =
                sourceSurfaceRevision;
            manifest.streamingSourceSurfaceGenerationSignature =
                sourceSurfaceSignature;
            manifest.streamingGenerationSignature =
                targetSignature;
            manifest.streamingGenerationRevision =
                previousRevision + 1;
            manifest.streamingPyramidIsComplete =
                true;

            if (!TrySaveManifest(manifest, out string finalizationError))
            {
                manifest.streamingGenerationRevision =
                    previousRevision;
                manifest.streamingPyramidIsComplete =
                    false;
                manifest.streamingSourceSurfaceMaskGenerationRevision =
                    -1;
                manifest.streamingSourceSurfaceGenerationSignature =
                    "";

                TrySaveManifest(
                    manifest,
                    out _
                );

                return new TerrainRuntimeSurfaceStreamingCompileResult(
                    TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                    requestedFamilyCount,
                    completedFamilies,
                    createdAssetCount,
                    updatedAssetCount,
                    removedAssetCount,
                    false,
                    revisionBefore,
                    previousRevision,
                    finalizationError,
                    "Derived Surface assets were written, but Surface Streaming manifest finalization failed."
                );
            }

            return new TerrainRuntimeSurfaceStreamingCompileResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Completed,
                requestedFamilyCount,
                completedFamilies,
                createdAssetCount,
                updatedAssetCount,
                removedAssetCount,
                true,
                revisionBefore,
                manifest.streamingGenerationRevision,
                "",
                targetDescriptors.Count > 0
                    ? "The complete Surface Streaming pyramid was rebuilt and finalized."
                    : "The current clipmap configuration requires no derived Surface representations; streaming metadata was finalized with an empty derived pyramid."
            );
        }
        catch (Exception exception)
        {
            if (dirtyOutputs.Count > 0)
            {
                try
                {
                    AssetDatabase.SaveAssets();
                    ReleasePersistedOutputs(dirtyOutputs);
                }
                catch
                {
                    // Dirty Editor assets may remain resident when durability is uncertain.
                }
            }

            return new TerrainRuntimeSurfaceStreamingCompileResult(
                TerrainRuntimeSurfaceStreamingCompileOutcome.Failed,
                requestedFamilyCount,
                completedFamilies,
                createdAssetCount,
                updatedAssetCount,
                removedAssetCount,
                false,
                revisionBefore,
                manifest.streamingGenerationRevision,
                "Surface Streaming generation failed with an exception.\n\n" +
                exception.Message,
                "Authoritative Surface data remains valid."
            );
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            isGenerating = false;
        }
    }

    private static bool SourceStillCurrent(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        int sourceSurfaceRevision,
        string sourceSurfaceSignature
    )
    {
        return
            worldSettings != null
            && manifest != null
            && manifest.isComplete
            && TerrainGenerationStateUtility.GetSurfaceMaskStatus(worldSettings)
                == TerrainGenerationStateUtility.GenerationStatus.Current
            && manifest.surfaceMaskGenerationRevision == sourceSurfaceRevision
            && string.Equals(
                manifest.surfaceGenerationSignature ?? "",
                sourceSurfaceSignature ?? "",
                StringComparison.Ordinal
            );
    }

    private static bool TryPersistPreparedBatch(
        out string errorMessage
    )
    {
        errorMessage = "";

        try
        {
            AssetDatabase.SaveAssets();
            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not persist the current Surface Streaming output batch.\n\n" +
                exception.Message;

            return false;
        }
    }

    private static void ReleasePersistedOutputs(
        List<Texture2D> dirtyOutputs
    )
    {
        if (dirtyOutputs == null)
        {
            return;
        }

        for (
            int index = 0;
            index < dirtyOutputs.Count;
            index++
        )
        {
            Texture2D texture =
                dirtyOutputs[index];

            if (texture == null)
            {
                continue;
            }

            try
            {
                Resources.UnloadAsset(texture);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "WorldMeshes could not unload a persisted Surface streaming texture after a durability checkpoint.\n\n" +
                    exception.Message
                );
            }
        }

        dirtyOutputs.Clear();
    }

    private static bool TrySaveManifest(
        TerrainSurfaceMaskManifest manifest,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (manifest == null)
        {
            errorMessage =
                "The Surface manifest is unavailable.";

            return false;
        }

        try
        {
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssetIfDirty(manifest);
            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not persist Surface Streaming manifest metadata.\n\n" +
                exception.Message;

            return false;
        }
    }

    private static TerrainRuntimeSurfaceStreamingCompileResult CreateTerminalResult(
        TerrainRuntimeSurfaceStreamingCompileOutcome outcome,
        int requestedFamilyCount,
        int revision,
        string errorMessage
    )
    {
        return new TerrainRuntimeSurfaceStreamingCompileResult(
            outcome,
            requestedFamilyCount,
            0,
            0,
            0,
            0,
            false,
            revision,
            revision,
            errorMessage,
            ""
        );
    }
}
