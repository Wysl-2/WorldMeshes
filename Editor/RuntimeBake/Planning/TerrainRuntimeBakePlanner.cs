using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Side-effect-free reconciliation of persistent pending work with the current
 * authored world and generated runtime state.
 */
public static class TerrainRuntimeBakePlanner
{
    public static TerrainRuntimeBakePlan BuildCurrentPlan()
    {
        WorldSettings worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        TerrainAuthoringData authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        return
            BuildPlan(
                worldSettings,
                authoringData
            );
    }

    public static TerrainRuntimeBakePlan BuildPlan(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.RuntimeBakeBuildPlan.Auto();

        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService
                .GetSnapshot();

        List<string> safetyReasons =
            new List<string>();

        HashSet<Vector2Int> heightTiles =
            new HashSet<Vector2Int>();

        HashSet<Vector2Int> surfaceTiles =
            new HashSet<Vector2Int>();

        HashSet<Vector2Int> collisionChunks =
            new HashSet<Vector2Int>();

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            string missingReason =
                worldSettings == null
                    ? "WorldSettings is unavailable."
                    : "TerrainAuthoringData is unavailable.";

            return
                CreatePlan(
                    snapshot,
                    TerrainRuntimeBakeWorkMode.None,
                    TerrainRuntimeBakeWorkMode.None,
                    TerrainRuntimeBakeWorkMode.None,
                    heightTiles,
                    surfaceTiles,
                    collisionChunks,
                    false,
                    false,
                    false,
                    false,
                    true,
                    missingReason,
                    safetyReasons,
                    "",
                    "",
                    ""
                );
        }

        TerrainGenerationStateEvaluationContext generationState =
            new TerrainGenerationStateEvaluationContext(
                worldSettings,
                authoringData,
                TerrainGenerationStateEvaluationMode.Operational
            );

        string currentAuthoringSignature =
            generationState.AuthoringSignature;

        TerrainSurfaceSettings surfaceSettings =
            generationState.SurfaceSettings;

        string currentSurfaceSettingsSignature =
            TerrainSurfaceSignatureUtility
                .GetSettingsSignature(
                    surfaceSettings
                );

        string currentCollisionSettingsSignature =
            TerrainGenerationStateUtility
                .GetCurrentCollisionSettingsSignature(
                    worldSettings
                );

        TerrainGenerationStateUtility.GenerationStatus
            authoringStatus =
                TerrainGenerationStateUtility
                    .GetAuthoringHeightfieldStatus(
                        generationState
                    );

        bool authoringReady =
            authoringStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current;

        string blockReason =
            authoringReady
                ? ""
                : GetAuthoringBlockReason(
                    worldSettings,
                    authoringData
                );

        // =================================================
        // HEIGHT
        // =================================================

        TerrainHeightmapManifest heightManifest =
            generationState.HeightmapManifest;

        TerrainGenerationStateUtility.GenerationStatus
            heightStatus =
                TerrainGenerationStateUtility
                    .GetHeightmapStatus(
                        generationState
                    );

        bool heightGenerated =
            worldSettings.heightmapGenerationRevision > 0
            &&
            !string.IsNullOrEmpty(
                worldSettings.lastGeneratedHeightSignature
            )
            &&
            heightManifest != null;

        bool isInitialBake =
            worldSettings.heightmapGenerationRevision <= 0
            &&
            heightManifest == null;

        bool heightTopologyCompatible =
            HeightTopologyMatchesWorld(
                heightManifest,
                worldSettings
            );

        bool heightLayoutCompatible =
            HeightLayoutMatchesWorld(
                heightManifest,
                worldSettings
            );

        bool heightCompilerCompatible =
            heightManifest != null
            &&
            heightManifest.compilerVersion ==
                TerrainGenerationStateUtility
                    .RuntimeHeightCompilerVersion;

        bool heightDatasetUsable =
            heightManifest != null
            &&
            heightManifest.isComplete
            &&
            heightManifest.HasCompleteTileHeightRanges
            &&
            heightManifest.HasValidHeightRange
            &&
            heightCompilerCompatible
            &&
            heightLayoutCompatible;

        TerrainRuntimeBakeWorkMode heightMode =
            TerrainRuntimeBakeWorkMode.None;

        if (
            heightStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            // Generated truth wins over stale persistent queue entries.
            heightMode =
                TerrainRuntimeBakeWorkMode.None;
        }
        else if (!authoringReady)
        {
            heightMode =
                TerrainRuntimeBakeWorkMode.Full;
        }
        else if (!heightGenerated)
        {
            heightMode =
                TerrainRuntimeBakeWorkMode.Full;
        }
        else if (!heightDatasetUsable)
        {
            heightMode =
                TerrainRuntimeBakeWorkMode.Full;

            AddHeightCompatibilityReason(
                heightManifest,
                worldSettings,
                safetyReasons
            );
        }
        else if (snapshot.FullHeightRebuildRequired)
        {
            heightMode =
                TerrainRuntimeBakeWorkMode.Full;
        }
        else
        {
            bool pendingHeightValid =
                TerrainRuntimeBakeDependencyUtility
                    .TryCopyValidHeightTiles(
                        worldSettings,
                        snapshot.PendingHeightTiles,
                        heightTiles,
                        out string heightCoordinateError
                    );

            bool observedAuthoringCurrent =
                IsObservedAuthoringCurrent(
                    snapshot,
                    currentAuthoringSignature
                );

            if (!pendingHeightValid)
            {
                heightMode =
                    TerrainRuntimeBakeWorkMode.Full;

                safetyReasons.Add(
                    "Persistent height dirty coordinates do not match the " +
                    "current height-tile layout. " +
                    heightCoordinateError
                );
            }
            else if (
                heightTiles.Count > 0
                &&
                observedAuthoringCurrent
            )
            {
                heightMode =
                    TerrainRuntimeBakeWorkMode.Incremental;
            }
            else
            {
                heightMode =
                    TerrainRuntimeBakeWorkMode.Full;

                safetyReasons.Add(
                    heightTiles.Count == 0
                        ? "Runtime heightmaps are stale but no complete local " +
                          "height dirty set is available."
                        : "The current authoring signature does not match the " +
                          "signature observed by the dirty-tile tracker."
                );
            }
        }

        if (
            heightMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            heightTiles.Clear();

            TerrainRuntimeBakeDependencyUtility
                .CollectAllHeightTiles(
                    worldSettings,
                    heightTiles
                );
        }

        // =================================================
        // SURFACE
        // =================================================

        TerrainSurfaceMaskManifest surfaceManifest =
            generationState.SurfaceMaskManifest;

        TerrainGenerationStateUtility.GenerationStatus
            surfaceStatus =
                TerrainGenerationStateUtility
                    .GetSurfaceMaskStatus(
                        generationState
                    );

        bool surfaceGenerated =
            worldSettings.surfaceMaskGenerationRevision > 0
            &&
            !string.IsNullOrEmpty(
                worldSettings.lastGeneratedSurfaceSignature
            )
            &&
            surfaceManifest != null;

        bool surfaceOwnCompatible =
            SurfaceOwnStateMatchesCurrent(
                surfaceManifest,
                surfaceSettings,
                currentSurfaceSettingsSignature,
                worldSettings
            );

        TerrainRuntimeBakeWorkMode surfaceMode =
            TerrainRuntimeBakeWorkMode.None;

        if (
            heightMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.Full;
        }
        else if (
            surfaceStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.None;
        }
        else if (!surfaceGenerated)
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.Full;
        }
        else if (!surfaceOwnCompatible)
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.Full;

            safetyReasons.Add(
                GetSurfaceCompatibilityReason(
                    surfaceManifest,
                    surfaceSettings,
                    currentSurfaceSettingsSignature,
                    worldSettings
                )
            );
        }
        else if (snapshot.FullSurfaceRebuildRequired)
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.Full;
        }
        else
        {
            bool pendingSurfaceValid =
                TerrainRuntimeBakeDependencyUtility
                    .TryCopyValidSurfaceTiles(
                        worldSettings,
                        snapshot.PendingSurfaceTiles,
                        surfaceTiles,
                        out string surfaceCoordinateError
                    );

            if (!pendingSurfaceValid)
            {
                surfaceMode =
                    TerrainRuntimeBakeWorkMode.Full;

                safetyReasons.Add(
                    "Persistent surface dirty coordinates do not match the " +
                    "current tile layout. " +
                    surfaceCoordinateError
                );
            }
            else
            {
                if (
                    heightMode ==
                    TerrainRuntimeBakeWorkMode.Incremental
                )
                {
                    string surfaceDependencyError =
                        "";

                    if (
                        surfaceSettings == null
                        ||
                        !TerrainRuntimeBakeDependencyUtility
                            .TryCollectDependentSurfaceTiles(
                                worldSettings,
                                surfaceSettings,
                                heightTiles,
                                surfaceTiles,
                                out surfaceDependencyError
                            )
                    )
                    {
                        surfaceMode =
                            TerrainRuntimeBakeWorkMode.Full;

                        if (
                            string.IsNullOrEmpty(
                                surfaceDependencyError
                            )
                        )
                        {
                            surfaceDependencyError =
                                "TerrainSurfaceSettings is unavailable.";
                        }

                        safetyReasons.Add(
                            "Surface dependency expansion could not be " +
                            "calculated safely. " +
                            surfaceDependencyError
                        );
                    }
                }

                if (
                    surfaceMode !=
                    TerrainRuntimeBakeWorkMode.Full
                )
                {
                    bool provenanceKnown =
                        heightMode ==
                            TerrainRuntimeBakeWorkMode.Incremental
                        ||
                        IsObservedAuthoringCurrent(
                            snapshot,
                            currentAuthoringSignature
                        );

                    if (
                        surfaceTiles.Count > 0
                        &&
                        provenanceKnown
                    )
                    {
                        surfaceMode =
                            TerrainRuntimeBakeWorkMode.Incremental;
                    }
                    else
                    {
                        surfaceMode =
                            TerrainRuntimeBakeWorkMode.Full;

                        safetyReasons.Add(
                            "Runtime surface masks are stale but no complete " +
                            "incremental surface dirty set can be proven."
                        );
                    }
                }
            }
        }

        if (
            surfaceMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            surfaceTiles.Clear();

            TerrainRuntimeBakeDependencyUtility
                .CollectAllHeightTiles(
                    worldSettings,
                    surfaceTiles
                );
        }

        // =================================================
        // COLLISION
        // =================================================

        TerrainGenerationStateUtility.GenerationStatus
            collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        generationState
                    );

        bool collisionFolderExists =
            AssetDatabase.IsValidFolder(
                TerrainCollisionMeshGenerator
                    .CollisionMeshFolder
            );

        bool collisionGenerated =
            worldSettings.collisionMeshGenerationRevision > 0
            &&
            !string.IsNullOrEmpty(
                worldSettings.lastGeneratedCollisionSignature
            )
            &&
            collisionFolderExists;

        bool collisionOwnCompatible =
            collisionGenerated
            &&
            worldSettings.lastGeneratedCollisionSignature ==
                currentCollisionSettingsSignature;

        TerrainRuntimeBakeWorkMode collisionMode =
            TerrainRuntimeBakeWorkMode.None;

        if (
            heightMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.Full;
        }
        else if (
            collisionStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.None;
        }
        else if (!collisionGenerated)
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.Full;
        }
        else if (!collisionOwnCompatible)
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.Full;

            safetyReasons.Add(
                "Collision generator/settings compatibility changed; " +
                "incremental collision coordinates cannot be reused."
            );
        }
        else if (snapshot.FullCollisionRebuildRequired)
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.Full;
        }
        else
        {
            bool pendingCollisionValid =
                TerrainRuntimeBakeDependencyUtility
                    .TryCopyValidCollisionChunks(
                        worldSettings,
                        snapshot.PendingCollisionChunks,
                        collisionChunks,
                        out string collisionCoordinateError
                    );

            if (!pendingCollisionValid)
            {
                collisionMode =
                    TerrainRuntimeBakeWorkMode.Full;

                safetyReasons.Add(
                    "Persistent collision dirty coordinates do not match the " +
                    "current world grid. " +
                    collisionCoordinateError
                );
            }
            else
            {
                if (
                    heightMode ==
                    TerrainRuntimeBakeWorkMode.Incremental
                )
                {
                    if (
                        !TerrainRuntimeBakeDependencyUtility
                            .TryCollectDependentCollisionChunks(
                                worldSettings,
                                heightTiles,
                                collisionChunks,
                                out string collisionDependencyError
                            )
                    )
                    {
                        collisionMode =
                            TerrainRuntimeBakeWorkMode.Full;

                        safetyReasons.Add(
                            "Collision dependency mapping could not be " +
                            "calculated safely. " +
                            collisionDependencyError
                        );
                    }
                }

                if (
                    collisionMode !=
                    TerrainRuntimeBakeWorkMode.Full
                )
                {
                    bool provenanceKnown =
                        heightMode ==
                            TerrainRuntimeBakeWorkMode.Incremental
                        ||
                        IsObservedAuthoringCurrent(
                            snapshot,
                            currentAuthoringSignature
                        );

                    if (
                        collisionChunks.Count > 0
                        &&
                        provenanceKnown
                    )
                    {
                        collisionMode =
                            TerrainRuntimeBakeWorkMode.Incremental;
                    }
                    else
                    {
                        collisionMode =
                            TerrainRuntimeBakeWorkMode.Full;

                        safetyReasons.Add(
                            "Collision meshes are stale but no complete " +
                            "incremental collision dirty set can be proven."
                        );
                    }
                }
            }
        }

        if (
            collisionMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            collisionChunks.Clear();

            TerrainRuntimeBakeDependencyUtility
                .CollectAllCollisionChunks(
                    worldSettings,
                    collisionChunks
                );
        }

        // =================================================
        // BLOCKING VALIDATION
        // =================================================

        if (
            surfaceMode !=
                TerrainRuntimeBakeWorkMode.None
            &&
            surfaceSettings == null
        )
        {
            blockReason =
                AppendBlockReason(
                    blockReason,
                    "TerrainSurfaceSettings is unavailable."
                );
        }

        if (
            collisionMode !=
                TerrainRuntimeBakeWorkMode.None
            &&
            !CollisionSettingsAreValid(
                worldSettings
            )
        )
        {
            blockReason =
                AppendBlockReason(
                    blockReason,
                    "Collision Resolution must be no greater than, and " +
                    "evenly divide, Heightfield Resolution / Chunk."
                );
        }

        // =================================================
        // GLOBAL WORK
        // =================================================

        bool topologyRequiresAddressablesConfiguration =
            !heightTopologyCompatible
            ||
            (
                heightMode !=
                    TerrainRuntimeBakeWorkMode.None
                &&
                !heightGenerated
            )
            ||
            (
                surfaceMode !=
                    TerrainRuntimeBakeWorkMode.None
                &&
                !surfaceGenerated
            )
            ||
            (
                collisionMode !=
                    TerrainRuntimeBakeWorkMode.None
                &&
                !collisionGenerated
            );

        bool generatedDataWork =
            heightMode !=
                TerrainRuntimeBakeWorkMode.None
            ||
            surfaceMode !=
                TerrainRuntimeBakeWorkMode.None
            ||
            collisionMode !=
                TerrainRuntimeBakeWorkMode.None;

        /*
         * Persistent dirty state records changes WorldMeshes observed.
         * External Addressables damage is folded into routine planning only
         * when a validation result for the current generated target is already
         * cached. Planning must not synchronously launch whole-dataset
         * validation merely to discover unobserved external damage.
         */
        bool externalAddressablesRepairRequired =
            false;

        if (
            !generatedDataWork
            &&
            authoringReady
            &&
            heightStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            surfaceStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            collisionStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
        )
        {
            if (
                TerrainRuntimeIntegrityAuditUtility
                    .TryGetCachedAddressablesValidation(
                        worldSettings,
                        out TerrainRuntimeAddressablesValidationResult
                            addressablesValidation
                    )
            )
            {
                externalAddressablesRepairRequired =
                    addressablesValidation == null
                    ||
                    !addressablesValidation.IsValid;

                if (externalAddressablesRepairRequired)
                {
                    safetyReasons.Add(
                        "Runtime Addressables structure is damaged or incomplete; Configuration and Content require reconciliation."
                    );
                }
            }
        }

        bool addressablesConfigurationRequired =
            snapshot.AddressablesConfigurationDirty
            ||
            topologyRequiresAddressablesConfiguration
            ||
            externalAddressablesRepairRequired;

        bool addressablesContentRequired =
            snapshot.AddressablesContentDirty
            ||
            generatedDataWork
            ||
            addressablesConfigurationRequired;

        bool runtimeSceneMetadataRequired =
            snapshot.RuntimeSceneMetadataDirty
            ||
            heightMode !=
                TerrainRuntimeBakeWorkMode.None
            ||
            topologyRequiresAddressablesConfiguration;

        return
            CreatePlan(
                snapshot,
                heightMode,
                surfaceMode,
                collisionMode,
                heightTiles,
                surfaceTiles,
                collisionChunks,
                addressablesConfigurationRequired,
                addressablesContentRequired,
                runtimeSceneMetadataRequired,
                isInitialBake,
                !string.IsNullOrEmpty(
                    blockReason
                ),
                blockReason,
                safetyReasons,
                currentAuthoringSignature,
                currentSurfaceSettingsSignature,
                currentCollisionSettingsSignature
            );
    }

    private static TerrainRuntimeBakePlan CreatePlan(
        TerrainRuntimeBakeStateSnapshot snapshot,
        TerrainRuntimeBakeWorkMode heightMode,
        TerrainRuntimeBakeWorkMode surfaceMode,
        TerrainRuntimeBakeWorkMode collisionMode,
        IEnumerable<Vector2Int> heightTiles,
        IEnumerable<Vector2Int> surfaceTiles,
        IEnumerable<Vector2Int> collisionChunks,
        bool addressablesConfigurationRequired,
        bool addressablesContentRequired,
        bool runtimeSceneMetadataRequired,
        bool isInitialBake,
        bool isBlocked,
        string blockReason,
        IEnumerable<string> safetyReasons,
        string currentAuthoringSignature,
        string currentSurfaceSettingsSignature,
        string currentCollisionSettingsSignature
    )
    {
        return
            new TerrainRuntimeBakePlan(
                heightMode,
                surfaceMode,
                collisionMode,
                heightTiles,
                surfaceTiles,
                collisionChunks,
                addressablesConfigurationRequired,
                addressablesContentRequired,
                runtimeSceneMetadataRequired,
                isInitialBake,
                isBlocked,
                blockReason,
                safetyReasons,
                snapshot.StateRevision,
                currentAuthoringSignature,
                snapshot.LastObservedAuthoringSignature,
                currentSurfaceSettingsSignature,
                currentCollisionSettingsSignature
            );
    }

    private static bool IsObservedAuthoringCurrent(
        TerrainRuntimeBakeStateSnapshot snapshot,
        string currentAuthoringSignature
    )
    {
        return
            snapshot != null
            &&
            !string.IsNullOrEmpty(
                currentAuthoringSignature
            )
            &&
            string.Equals(
                snapshot.LastObservedAuthoringSignature,
                currentAuthoringSignature,
                System.StringComparison.Ordinal
            );
    }

    private static bool HeightTopologyMatchesWorld(
        TerrainHeightmapManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (
            manifest == null
            ||
            worldSettings == null
        )
        {
            return false;
        }

        return
            manifest.gridWidth ==
                Mathf.Max(
                    1,
                    worldSettings.gridWidth
                )
            &&
            manifest.gridHeight ==
                Mathf.Max(
                    1,
                    worldSettings.gridHeight
                )
            &&
            manifest.heightTileChunkSpan ==
                Mathf.Max(
                    1,
                    worldSettings.heightTileChunkSpan
                )
            &&
            manifest.heightTileGridWidth ==
                Mathf.Max(
                    1,
                    worldSettings.HeightTileGridWidth
                )
            &&
            manifest.heightTileGridHeight ==
                Mathf.Max(
                    1,
                    worldSettings.HeightTileGridHeight
                );
    }

    private static bool HeightLayoutMatchesWorld(
        TerrainHeightmapManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (
            !HeightTopologyMatchesWorld(
                manifest,
                worldSettings
            )
        )
        {
            return false;
        }

        return
            Mathf.Approximately(
                manifest.chunkSize,
                Mathf.Max(
                    0.01f,
                    worldSettings.chunkSize
                )
            )
            &&
            manifest.heightfieldResolutionPerChunk ==
                Mathf.Max(
                    1,
                    worldSettings.heightfieldResolutionPerChunk
                )
            &&
            Mathf.Approximately(
                manifest.heightTileWorldSize,
                worldSettings.HeightTileWorldSize
            )
            &&
            manifest.heightTileSamplesPerSide ==
                worldSettings.HeightTileSamplesPerSide;
    }

    private static bool SurfaceOwnStateMatchesCurrent(
        TerrainSurfaceMaskManifest manifest,
        TerrainSurfaceSettings surfaceSettings,
        string currentSurfaceSettingsSignature,
        WorldSettings worldSettings
    )
    {
        if (
            manifest == null
            ||
            surfaceSettings == null
            ||
            worldSettings == null
            ||
            !manifest.isComplete
            ||
            manifest.compilerVersion !=
                TerrainGenerationStateUtility
                    .SurfaceMaskCompilerVersion
            ||
            manifest.channelLayoutVersion !=
                TerrainSurfaceMaskManifest
                    .CurrentChannelLayoutVersion
            ||
            string.IsNullOrEmpty(
                currentSurfaceSettingsSignature
            )
            ||
            manifest.surfaceSettingsSignature !=
                currentSurfaceSettingsSignature
        )
        {
            return false;
        }

        float sampleSpacing =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            )
            /
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            );

        Vector2 worldSize =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        return
            manifest.tileGridWidth ==
                worldSettings.HeightTileGridWidth
            &&
            manifest.tileGridHeight ==
                worldSettings.HeightTileGridHeight
            &&
            manifest.samplesPerSide ==
                worldSettings.HeightTileSamplesPerSide
            &&
            Mathf.Approximately(
                manifest.tileWorldSize,
                worldSettings.HeightTileWorldSize
            )
            &&
            Mathf.Approximately(
                manifest.sampleSpacing,
                sampleSpacing
            )
            &&
            Mathf.Approximately(
                manifest.worldSizeXZ.x,
                worldSize.x
            )
            &&
            Mathf.Approximately(
                manifest.worldSizeXZ.y,
                worldSize.y
            );
    }

    private static void AddHeightCompatibilityReason(
        TerrainHeightmapManifest manifest,
        WorldSettings worldSettings,
        ICollection<string> reasons
    )
    {
        if (reasons == null)
        {
            return;
        }

        if (manifest == null)
        {
            reasons.Add(
                "Runtime heightmap manifest is missing; a full height rebuild " +
                "is required."
            );
            return;
        }

        if (!manifest.isComplete)
        {
            reasons.Add(
                "Runtime heightmap manifest is incomplete; a full height " +
                "rebuild is required."
            );
            return;
        }

        if (
            manifest.compilerVersion !=
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion
        )
        {
            reasons.Add(
                "Runtime height compiler version changed; existing tiles " +
                "cannot be incrementally trusted."
            );
            return;
        }

        if (
            !HeightLayoutMatchesWorld(
                manifest,
                worldSettings
            )
        )
        {
            reasons.Add(
                "Runtime heightmap layout does not match current WorldSettings; " +
                "a full height rebuild is required."
            );
            return;
        }

        if (!manifest.HasCompleteTileHeightRanges)
        {
            reasons.Add(
                "Runtime per-tile height range metadata is incomplete; a " +
                "full height rebuild is required."
            );
            return;
        }

        if (!manifest.HasValidHeightRange)
        {
            reasons.Add(
                "Runtime height range metadata is invalid; a full height " +
                "rebuild is required."
            );
        }
    }

    private static string GetSurfaceCompatibilityReason(
        TerrainSurfaceMaskManifest manifest,
        TerrainSurfaceSettings surfaceSettings,
        string currentSettingsSignature,
        WorldSettings worldSettings
    )
    {
        if (manifest == null)
        {
            return
                "Runtime surface-mask manifest is missing; a full surface " +
                "rebuild is required.";
        }

        if (!manifest.isComplete)
        {
            return
                "Runtime surface-mask manifest is incomplete; a full surface " +
                "rebuild is required.";
        }

        if (
            manifest.compilerVersion !=
                TerrainGenerationStateUtility
                    .SurfaceMaskCompilerVersion
            ||
            manifest.channelLayoutVersion !=
                TerrainSurfaceMaskManifest
                    .CurrentChannelLayoutVersion
        )
        {
            return
                "Runtime surface compiler/channel format changed; a full " +
                "surface rebuild is required.";
        }

        if (surfaceSettings == null)
        {
            return
                "TerrainSurfaceSettings is unavailable; surface dependency " +
                "compatibility cannot be proven.";
        }

        if (
            manifest.surfaceSettingsSignature !=
            currentSettingsSignature
        )
        {
            return
                "TerrainSurfaceSettings changed; runtime surface masks require " +
                "a full rebuild.";
        }

        return
            "Runtime surface-mask layout does not match the current world " +
            "height-tile layout; a full rebuild is required.";
    }

    private static string GetAuthoringBlockReason(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        if (worldSettings == null)
        {
            return
                "WorldSettings is unavailable.";
        }

        if (authoringData == null)
        {
            return
                "TerrainAuthoringData is unavailable.";
        }

        TerrainAuthoringHeightManifest manifest =
            TerrainAuthoringStateUtility
                .LoadAuthoringHeightManifest();

        if (manifest == null)
        {
            return
                "The committed authoring heightfield does not exist. " +
                "Initialize the authoring heightfield before runtime baking.";
        }

        if (!manifest.isComplete)
        {
            return
                "The committed authoring heightfield is incomplete. " +
                "Reinitialize it before runtime baking.";
        }

        if (
            !TerrainAuthoringStateUtility
                .ManifestMatchesWorldSettings(
                    manifest,
                    worldSettings
                )
        )
        {
            return
                "The committed authoring heightfield does not match the current " +
                "world/height-tile layout. Reinitialize it before runtime baking.";
        }

        if (authoringData.authoringRevision <= 0)
        {
            return
                "The authoring heightfield has not been initialized yet.";
        }

        return
            "The committed authoring heightfield is not current. Reinitialize " +
            "or repair authoring data before runtime baking.";
    }

    private static bool CollisionSettingsAreValid(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return false;
        }

        int heightResolution =
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            );

        int collisionResolution =
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            );

        return
            collisionResolution <=
                heightResolution
            &&
            heightResolution %
                collisionResolution ==
                0;
    }

    private static string AppendBlockReason(
        string existing,
        string next
    )
    {
        if (string.IsNullOrEmpty(existing))
        {
            return
                next ??
                "";
        }

        if (string.IsNullOrEmpty(next))
        {
            return existing;
        }

        return
            existing +
            "\n" +
            next;
    }
}
