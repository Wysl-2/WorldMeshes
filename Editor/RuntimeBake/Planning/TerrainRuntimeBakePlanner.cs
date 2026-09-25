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
        return
            BuildPlan(
                worldSettings,
                authoringData,
                null
            );
    }

    internal static TerrainRuntimeBakePlan BuildPlan(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainRuntimeBakePlanningDiagnosticsContext diagnostics
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.RuntimeBakeBuildPlan.Auto();

        TerrainRuntimeBakeTraceScope plannerTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.BuildPlan"
            );

        try
        {
            TerrainRuntimeBakePlan plan =
                BuildPlanCore(
                    worldSettings,
                    authoringData,
                    diagnostics
                );

            plannerTrace?.Complete();

            return plan;
        }
        catch (System.Exception exception)
        {
            plannerTrace?.Fail(
                exception.Message
            );

            throw;
        }
        finally
        {
            plannerTrace?.Dispose();
        }
    }

    private static TerrainRuntimeBakePlan BuildPlanCore(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainRuntimeBakePlanningDiagnosticsContext diagnostics
    )
    {
        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService
                .GetSnapshot();

        List<string> safetyReasons =
            new List<string>();

        HashSet<Vector2Int> heightTiles =
            new HashSet<Vector2Int>();

        HashSet<Vector2Int> heightStreamingTiles =
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

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.MissingRequiredAsset,
                TerrainRuntimeBakeReasonTarget.Blocking,
                missingReason
            );

            return
                CreatePlan(
                    snapshot,
                    TerrainRuntimeBakeWorkMode.None,
                    TerrainRuntimeBakeWorkMode.None,
                    TerrainRuntimeBakeWorkMode.None,
                    TerrainRuntimeBakeWorkMode.None,
                    heightTiles,
                    heightStreamingTiles,
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

        if (!authoringReady)
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.AuthoringNotReady,
                TerrainRuntimeBakeReasonTarget.Blocking,
                blockReason
            );
        }

        TerrainRuntimeBakeTraceScope heightTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.EvaluateHeight"
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

        TerrainRuntimeBakeTraceScope heightCompatibilityTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.EvaluateHeightCompatibility"
            );

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

        heightCompatibilityTrace?.Complete();

        if (
            heightGenerated
            &&
            heightStatus !=
                TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.OutdatedGeneratedData,
                TerrainRuntimeBakeReasonTarget.Height,
                "Runtime heightmaps are not current."
            );
        }

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

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.GeneratedDataCurrent,
                TerrainRuntimeBakeReasonTarget.Height,
                "Runtime heightmaps are current; no height work is required."
            );
        }
        else if (!authoringReady)
        {
            heightMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.AuthoringNotReady,
                TerrainRuntimeBakeReasonTarget.Height,
                "Height work cannot be incrementally trusted while committed authoring data is not current."
            );
        }
        else if (!heightGenerated)
        {
            heightMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                isInitialBake
                    ? TerrainRuntimeBakeReasonCode.InitialBake
                    : TerrainRuntimeBakeReasonCode.MissingGeneratedData,
                TerrainRuntimeBakeReasonTarget.Height,
                isInitialBake
                    ? "No runtime height dataset exists yet; the initial runtime height bake requires full work."
                    : "Runtime height generation state is missing; a full height rebuild is required."
            );
        }
        else if (!heightDatasetUsable)
        {
            heightMode =
                TerrainRuntimeBakeWorkMode.Full;

            AddHeightCompatibilityReason(
                heightManifest,
                worldSettings,
                safetyReasons,
                diagnostics
            );
        }
        else if (snapshot.FullHeightRebuildRequired)
        {
            heightMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.FullRebuildFlag,
                TerrainRuntimeBakeReasonTarget.Height,
                "Persistent runtime bake state requires a full height rebuild."
            );
        }
        else
        {
            using var heightDirtyTrace =
                diagnostics?.BeginTrace(
                    "TerrainRuntimeBakePlanner.ReconcileHeightDirtyState"
                );

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

                string reason =
                    "Persistent height dirty coordinates do not match the " +
                    "current height-tile layout. " +
                    heightCoordinateError;

                safetyReasons.Add(
                    reason
                );

                diagnostics?.AddPlannerReason(
                    TerrainRuntimeBakeReasonCode.InvalidPersistentDirtyState,
                    TerrainRuntimeBakeReasonTarget.Height,
                    reason,
                    true,
                    snapshot.PendingHeightTileCount
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

                diagnostics?.AddPlannerReason(
                    TerrainRuntimeBakeReasonCode.PendingAuthoringChange,
                    TerrainRuntimeBakeReasonTarget.Height,
                    "Pending authoring changes map to a valid incremental height dirty set.",
                    false,
                    heightTiles.Count
                );
            }
            else
            {
                heightMode =
                    TerrainRuntimeBakeWorkMode.Full;

                string reason =
                    heightTiles.Count == 0
                        ? "Runtime heightmaps are stale but no complete local " +
                          "height dirty set is available."
                        : "The current authoring signature does not match the " +
                          "signature observed by the dirty-tile tracker.";

                safetyReasons.Add(
                    reason
                );

                diagnostics?.AddPlannerReason(
                    heightTiles.Count == 0
                        ? TerrainRuntimeBakeReasonCode.NoProvableDirtySet
                        : TerrainRuntimeBakeReasonCode.AuthoringSignatureChanged,
                    TerrainRuntimeBakeReasonTarget.Height,
                    reason,
                    true,
                    heightTiles.Count
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

        heightTrace?.Complete();

        TerrainRuntimeBakeTraceScope heightStreamingTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.EvaluateHeightStreaming"
            );

        // =================================================
        // HEIGHT STREAMING
        // =================================================

        TerrainGenerationStateUtility.GenerationStatus
            heightStreamingStatus =
                TerrainGenerationStateUtility
                    .GetHeightStreamingStatus(
                        generationState
                    );

        if (
            TerrainRuntimeIntegrityAuditUtility
                .TryGetCachedGeneratedDataAudit(
                    worldSettings,
                    out TerrainRuntimeIntegrityAuditResult cachedIntegrity
                )
            && cachedIntegrity != null
            && cachedIntegrity.HeightStreaming != null
            && !cachedIntegrity.HeightStreaming.IsValid
        )
        {
            heightStreamingStatus =
                TerrainGenerationStateUtility.GenerationStatus.OutOfDate;
        }

        bool streamingBaselineCurrent =
            TerrainGenerationStateUtility
                .IsHeightStreamingManifestCurrent(
                    heightManifest,
                    worldSettings,
                    worldSettings.heightmapGenerationRevision
                );

        string currentHeightStreamingSignature =
            TerrainGenerationStateUtility
                .GetCurrentHeightStreamingGenerationSignature(
                    worldSettings
                );

        bool streamingTargetMetadataCompatible =
            heightManifest != null
            &&
            heightManifest.streamingPyramidCompilerVersion ==
                TerrainGenerationStateUtility
                    .RuntimeHeightStreamingCompilerVersion
            &&
            !string.IsNullOrEmpty(
                currentHeightStreamingSignature
            )
            &&
            heightManifest.streamingGenerationSignature ==
                currentHeightStreamingSignature
            &&
            heightManifest.StreamingLevelCount > 0;

        TerrainRuntimeBakeWorkMode heightStreamingMode =
            TerrainRuntimeBakeWorkMode.None;

        bool pendingHeightStreamingValid =
            TerrainRuntimeBakeDependencyUtility
                .TryCopyValidHeightTiles(
                    worldSettings,
                    snapshot.PendingHeightStreamingTiles,
                    heightStreamingTiles,
                    out string heightStreamingCoordinateError
                );

        if (
            heightMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            heightStreamingMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.DependencyPropagation,
                TerrainRuntimeBakeReasonTarget.HeightStreaming,
                "A full authoritative Height rebuild requires a full Height Streaming rebuild.",
                false,
                0,
                TerrainRuntimeBakeReasonTarget.Height
            );
        }
        else if (!pendingHeightStreamingValid)
        {
            heightStreamingMode =
                TerrainRuntimeBakeWorkMode.Full;

            string reason =
                "Persistent Height Streaming dirty coordinates do not match the current height-tile layout. " +
                heightStreamingCoordinateError;

            safetyReasons.Add(
                reason
            );

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.InvalidPersistentDirtyState,
                TerrainRuntimeBakeReasonTarget.HeightStreaming,
                reason,
                true,
                snapshot.PendingHeightStreamingTileCount
            );
        }
        else if (snapshot.FullHeightStreamingRebuildRequired)
        {
            heightStreamingMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.FullRebuildFlag,
                TerrainRuntimeBakeReasonTarget.HeightStreaming,
                "Persistent runtime bake state requires a full Height Streaming rebuild."
            );
        }
        else
        {
            if (
                heightMode ==
                TerrainRuntimeBakeWorkMode.Incremental
            )
            {
                int beforeDependencyCount =
                    heightStreamingTiles.Count;

                heightStreamingTiles.UnionWith(
                    heightTiles
                );

                int addedCount =
                    Mathf.Max(
                        0,
                        heightStreamingTiles.Count -
                        beforeDependencyCount
                    );

                if (addedCount > 0)
                {
                    diagnostics?.AddPlannerReason(
                        TerrainRuntimeBakeReasonCode.DependencyPropagation,
                        TerrainRuntimeBakeReasonTarget.HeightStreaming,
                        "Incremental authoritative Height work invalidates the corresponding Height Streaming families.",
                        false,
                        addedCount,
                        TerrainRuntimeBakeReasonTarget.Height
                    );
                }
            }

            if (heightStreamingTiles.Count > 0)
            {
                if (
                    streamingBaselineCurrent
                    ||
                    streamingTargetMetadataCompatible
                )
                {
                    heightStreamingMode =
                        TerrainRuntimeBakeWorkMode.Incremental;

                    diagnostics?.AddPlannerReason(
                        TerrainRuntimeBakeReasonCode.PendingHeightStreamingChange,
                        TerrainRuntimeBakeReasonTarget.HeightStreaming,
                        "Known Height Streaming dirty coordinates can be repaired incrementally.",
                        false,
                        heightStreamingTiles.Count
                    );
                }
                else
                {
                    heightStreamingMode =
                        TerrainRuntimeBakeWorkMode.Full;

                    string reason =
                        "Height Streaming is incomplete and no compatible local baseline can prove that the pending coordinate set is sufficient.";

                    safetyReasons.Add(
                        reason
                    );

                    diagnostics?.AddPlannerReason(
                        TerrainRuntimeBakeReasonCode.NoProvableDirtySet,
                        TerrainRuntimeBakeReasonTarget.HeightStreaming,
                        reason,
                        true,
                        heightStreamingTiles.Count
                    );
                }
            }
            else if (
                heightStreamingStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            )
            {
                heightStreamingMode =
                    TerrainRuntimeBakeWorkMode.None;

                diagnostics?.AddPlannerReason(
                    TerrainRuntimeBakeReasonCode.GeneratedDataCurrent,
                    TerrainRuntimeBakeReasonTarget.HeightStreaming,
                    "Height Streaming is current; no work is required."
                );
            }
            else
            {
                heightStreamingMode =
                    TerrainRuntimeBakeWorkMode.Full;

                diagnostics?.AddPlannerReason(
                    heightStreamingStatus ==
                        TerrainGenerationStateUtility.GenerationStatus.NotGenerated
                        ? TerrainRuntimeBakeReasonCode.MissingGeneratedData
                        : TerrainRuntimeBakeReasonCode.OutdatedGeneratedData,
                    TerrainRuntimeBakeReasonTarget.HeightStreaming,
                    "Height Streaming is missing or incompatible; a full derived pyramid rebuild is required.",
                    true
                );
            }
        }

        if (
            heightStreamingMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            heightStreamingTiles.Clear();

            TerrainRuntimeBakeDependencyUtility
                .CollectAllHeightTiles(
                    worldSettings,
                    heightStreamingTiles
                );
        }

        heightStreamingTrace?.Complete();

        TerrainRuntimeBakeTraceScope surfaceTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.EvaluateSurface"
            );

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

        TerrainRuntimeBakeTraceScope surfaceCompatibilityTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.EvaluateSurfaceCompatibility"
            );

        bool surfaceOwnCompatible =
            SurfaceOwnStateMatchesCurrent(
                surfaceManifest,
                surfaceSettings,
                currentSurfaceSettingsSignature,
                worldSettings
            );

        surfaceCompatibilityTrace?.Complete();

        if (
            surfaceGenerated
            &&
            surfaceStatus !=
                TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.OutdatedGeneratedData,
                TerrainRuntimeBakeReasonTarget.Surface,
                "Runtime surface masks are not current."
            );
        }

        TerrainRuntimeBakeWorkMode surfaceMode =
            TerrainRuntimeBakeWorkMode.None;

        if (
            heightMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.DependencyPropagation,
                TerrainRuntimeBakeReasonTarget.Surface,
                "A full height rebuild requires a full surface rebuild.",
                false,
                0,
                TerrainRuntimeBakeReasonTarget.Height
            );
        }
        else if (
            surfaceStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.None;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.GeneratedDataCurrent,
                TerrainRuntimeBakeReasonTarget.Surface,
                "Runtime surface masks are current; no surface work is required."
            );
        }
        else if (!surfaceGenerated)
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.MissingGeneratedData,
                TerrainRuntimeBakeReasonTarget.Surface,
                "Runtime surface-mask generation state is missing; a full surface rebuild is required."
            );
        }
        else if (!surfaceOwnCompatible)
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.Full;

            string surfaceCompatibilityReason =
                GetSurfaceCompatibilityReason(
                    surfaceManifest,
                    surfaceSettings,
                    currentSurfaceSettingsSignature,
                    worldSettings,
                    diagnostics
                );

            if (
                surfaceManifest != null
                &&
                surfaceSettings != null
                &&
                surfaceManifest.isComplete
                &&
                surfaceManifest.compilerVersion ==
                    TerrainGenerationStateUtility
                        .SurfaceMaskCompilerVersion
                &&
                surfaceManifest.channelLayoutVersion ==
                    TerrainSurfaceMaskManifest
                        .CurrentChannelLayoutVersion
                &&
                !string.IsNullOrEmpty(
                    currentSurfaceSettingsSignature
                )
            )
            {
                if (
                    surfaceManifest.surfaceSettingsSignature !=
                        currentSurfaceSettingsSignature
                )
                {
                    diagnostics?.AddPlannerReason(
                        TerrainRuntimeBakeReasonCode.SurfaceSettingsChanged,
                        TerrainRuntimeBakeReasonTarget.Surface,
                        surfaceCompatibilityReason,
                        true
                    );
                }
                else
                {
                    diagnostics?.AddPlannerReason(
                        TerrainRuntimeBakeReasonCode.LayoutChanged,
                        TerrainRuntimeBakeReasonTarget.Surface,
                        surfaceCompatibilityReason,
                        true
                    );
                }
            }

            safetyReasons.Add(
                surfaceCompatibilityReason
            );
        }
        else if (snapshot.FullSurfaceRebuildRequired)
        {
            surfaceMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.FullRebuildFlag,
                TerrainRuntimeBakeReasonTarget.Surface,
                "Persistent runtime bake state requires a full surface rebuild."
            );
        }
        else
        {
            using var surfaceDirtyTrace =
                diagnostics?.BeginTrace(
                    "TerrainRuntimeBakePlanner.ReconcileSurfaceDirtyState"
                );

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

                string reason =
                    "Persistent surface dirty coordinates do not match the " +
                    "current tile layout. " +
                    surfaceCoordinateError;

                safetyReasons.Add(
                    reason
                );

                diagnostics?.AddPlannerReason(
                    TerrainRuntimeBakeReasonCode.InvalidPersistentDirtyState,
                    TerrainRuntimeBakeReasonTarget.Surface,
                    reason,
                    true,
                    snapshot.PendingSurfaceTileCount
                );
            }
            else
            {
                int directSurfaceTileCount =
                    surfaceTiles.Count;

                if (directSurfaceTileCount > 0)
                {
                    diagnostics?.AddPlannerReason(
                        TerrainRuntimeBakeReasonCode.PendingSurfaceChange,
                        TerrainRuntimeBakeReasonTarget.Surface,
                        "Persistent surface dirty state contributes direct incremental surface work.",
                        false,
                        directSurfaceTileCount
                    );
                }

                if (
                    heightMode ==
                    TerrainRuntimeBakeWorkMode.Incremental
                )
                {
                    using var surfaceDependencyTrace =
                        diagnostics?.BeginTrace(
                            "TerrainRuntimeBakePlanner.ExpandSurfaceDependencies"
                        );

                    int beforeDependencyCount =
                        surfaceTiles.Count;

                    string surfaceDependencyError =
                        "";

                    bool dependencyMapped =
                        surfaceSettings != null
                        &&
                        TerrainRuntimeBakeDependencyUtility
                            .TryCollectDependentSurfaceTiles(
                                worldSettings,
                                surfaceSettings,
                                heightTiles,
                                surfaceTiles,
                                out surfaceDependencyError
                            );

                    if (!dependencyMapped)
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

                        string reason =
                            "Surface dependency expansion could not be " +
                            "calculated safely. " +
                            surfaceDependencyError;

                        safetyReasons.Add(
                            reason
                        );

                        diagnostics?.AddPlannerReason(
                            TerrainRuntimeBakeReasonCode.DependencyPropagationFailed,
                            TerrainRuntimeBakeReasonTarget.Surface,
                            reason,
                            true,
                            heightTiles.Count,
                            TerrainRuntimeBakeReasonTarget.Height
                        );
                    }
                    else
                    {
                        int addedDependencyCount =
                            Mathf.Max(
                                0,
                                surfaceTiles.Count -
                                beforeDependencyCount
                            );

                        if (addedDependencyCount > 0)
                        {
                            diagnostics?.AddPlannerReason(
                                TerrainRuntimeBakeReasonCode.DependencyPropagation,
                                TerrainRuntimeBakeReasonTarget.Surface,
                                "Incremental height work expanded the dependent surface dirty set.",
                                false,
                                addedDependencyCount,
                                TerrainRuntimeBakeReasonTarget.Height
                            );
                        }
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

                        string reason =
                            "Runtime surface masks are stale but no complete " +
                            "incremental surface dirty set can be proven.";

                        safetyReasons.Add(
                            reason
                        );

                        diagnostics?.AddPlannerReason(
                            TerrainRuntimeBakeReasonCode.NoProvableDirtySet,
                            TerrainRuntimeBakeReasonTarget.Surface,
                            reason,
                            true,
                            surfaceTiles.Count
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

        surfaceTrace?.Complete();

        TerrainRuntimeBakeTraceScope collisionTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.EvaluateCollision"
            );

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

        TerrainRuntimeBakeTraceScope collisionCompatibilityTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.EvaluateCollisionCompatibility"
            );

        bool collisionOwnCompatible =
            collisionGenerated
            &&
            worldSettings.lastGeneratedCollisionSignature ==
                currentCollisionSettingsSignature;

        collisionCompatibilityTrace?.Complete();

        if (
            collisionGenerated
            &&
            collisionStatus !=
                TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.OutdatedGeneratedData,
                TerrainRuntimeBakeReasonTarget.Collision,
                "Runtime collision meshes are not current."
            );
        }

        TerrainRuntimeBakeWorkMode collisionMode =
            TerrainRuntimeBakeWorkMode.None;

        if (
            heightMode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.DependencyPropagation,
                TerrainRuntimeBakeReasonTarget.Collision,
                "A full height rebuild requires a full collision rebuild.",
                false,
                0,
                TerrainRuntimeBakeReasonTarget.Height
            );
        }
        else if (
            collisionStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.None;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.GeneratedDataCurrent,
                TerrainRuntimeBakeReasonTarget.Collision,
                "Runtime collision meshes are current; no collision work is required."
            );
        }
        else if (!collisionGenerated)
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.MissingGeneratedData,
                TerrainRuntimeBakeReasonTarget.Collision,
                "Runtime collision generation state is missing; a full collision rebuild is required."
            );
        }
        else if (!collisionOwnCompatible)
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.Full;

            string reason =
                "Collision generator/settings compatibility changed; " +
                "incremental collision coordinates cannot be reused.";

            safetyReasons.Add(
                reason
            );

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.CollisionSettingsChanged,
                TerrainRuntimeBakeReasonTarget.Collision,
                reason,
                true
            );
        }
        else if (snapshot.FullCollisionRebuildRequired)
        {
            collisionMode =
                TerrainRuntimeBakeWorkMode.Full;

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.FullRebuildFlag,
                TerrainRuntimeBakeReasonTarget.Collision,
                "Persistent runtime bake state requires a full collision rebuild."
            );
        }
        else
        {
            using var collisionDirtyTrace =
                diagnostics?.BeginTrace(
                    "TerrainRuntimeBakePlanner.ReconcileCollisionDirtyState"
                );

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

                string reason =
                    "Persistent collision dirty coordinates do not match the " +
                    "current world grid. " +
                    collisionCoordinateError;

                safetyReasons.Add(
                    reason
                );

                diagnostics?.AddPlannerReason(
                    TerrainRuntimeBakeReasonCode.InvalidPersistentDirtyState,
                    TerrainRuntimeBakeReasonTarget.Collision,
                    reason,
                    true,
                    snapshot.PendingCollisionChunkCount
                );
            }
            else
            {
                int directCollisionChunkCount =
                    collisionChunks.Count;

                if (directCollisionChunkCount > 0)
                {
                    diagnostics?.AddPlannerReason(
                        TerrainRuntimeBakeReasonCode.PendingCollisionChange,
                        TerrainRuntimeBakeReasonTarget.Collision,
                        "Persistent collision dirty state contributes direct incremental collision work.",
                        false,
                        directCollisionChunkCount
                    );
                }

                if (
                    heightMode ==
                    TerrainRuntimeBakeWorkMode.Incremental
                )
                {
                    using var collisionDependencyTrace =
                        diagnostics?.BeginTrace(
                            "TerrainRuntimeBakePlanner.ExpandCollisionDependencies"
                        );

                    int beforeDependencyCount =
                        collisionChunks.Count;

                    bool dependencyMapped =
                        TerrainRuntimeBakeDependencyUtility
                            .TryCollectDependentCollisionChunks(
                                worldSettings,
                                heightTiles,
                                collisionChunks,
                                out string collisionDependencyError
                            );

                    if (!dependencyMapped)
                    {
                        collisionMode =
                            TerrainRuntimeBakeWorkMode.Full;

                        string reason =
                            "Collision dependency mapping could not be " +
                            "calculated safely. " +
                            collisionDependencyError;

                        safetyReasons.Add(
                            reason
                        );

                        diagnostics?.AddPlannerReason(
                            TerrainRuntimeBakeReasonCode.DependencyPropagationFailed,
                            TerrainRuntimeBakeReasonTarget.Collision,
                            reason,
                            true,
                            heightTiles.Count,
                            TerrainRuntimeBakeReasonTarget.Height
                        );
                    }
                    else
                    {
                        int addedDependencyCount =
                            Mathf.Max(
                                0,
                                collisionChunks.Count -
                                beforeDependencyCount
                            );

                        if (addedDependencyCount > 0)
                        {
                            diagnostics?.AddPlannerReason(
                                TerrainRuntimeBakeReasonCode.DependencyPropagation,
                                TerrainRuntimeBakeReasonTarget.Collision,
                                "Incremental height work expanded the dependent collision dirty set.",
                                false,
                                addedDependencyCount,
                                TerrainRuntimeBakeReasonTarget.Height
                            );
                        }
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

                        string reason =
                            "Collision meshes are stale but no complete " +
                            "incremental collision dirty set can be proven.";

                        safetyReasons.Add(
                            reason
                        );

                        diagnostics?.AddPlannerReason(
                            TerrainRuntimeBakeReasonCode.NoProvableDirtySet,
                            TerrainRuntimeBakeReasonTarget.Collision,
                            reason,
                            true,
                            collisionChunks.Count
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

        collisionTrace?.Complete();

        TerrainRuntimeBakeTraceScope blockingTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.ValidateBlocking"
            );

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
            const string reason =
                "TerrainSurfaceSettings is unavailable.";

            blockReason =
                AppendBlockReason(
                    blockReason,
                    reason
                );

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.BlockingValidation,
                TerrainRuntimeBakeReasonTarget.Blocking,
                reason
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
            const string reason =
                "Collision Resolution must be no greater than, and " +
                "evenly divide, Heightfield Resolution / Chunk.";

            blockReason =
                AppendBlockReason(
                    blockReason,
                    reason
                );

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.BlockingValidation,
                TerrainRuntimeBakeReasonTarget.Blocking,
                reason
            );
        }

        blockingTrace?.Complete();

        TerrainRuntimeBakeTraceScope globalTrace =
            diagnostics?.BeginTrace(
                "TerrainRuntimeBakePlanner.EvaluateGlobalWork"
            );

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
            heightStreamingMode !=
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
            heightStreamingStatus ==
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

        if (snapshot.AddressablesConfigurationDirty)
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.AddressablesConfigurationDirty,
                TerrainRuntimeBakeReasonTarget.Addressables,
                "Persistent runtime bake state marks Addressables configuration dirty."
            );
        }

        if (snapshot.AddressablesContentDirty)
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.AddressablesContentDirty,
                TerrainRuntimeBakeReasonTarget.Addressables,
                "Persistent runtime bake state marks Addressables content dirty."
            );
        }

        if (!heightTopologyCompatible)
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.TopologyChanged,
                TerrainRuntimeBakeReasonTarget.Addressables,
                "Runtime height topology does not match the current world; Addressables configuration requires reconciliation.",
                false,
                0,
                TerrainRuntimeBakeReasonTarget.Height
            );
        }

        if (generatedDataWork)
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.GeneratedDataChanged,
                TerrainRuntimeBakeReasonTarget.Addressables,
                "Generated runtime data work requires Addressables content reconciliation."
            );
        }

        if (externalAddressablesRepairRequired)
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.AddressablesRepairRequired,
                TerrainRuntimeBakeReasonTarget.Addressables,
                "Cached validation reports damaged or incomplete runtime Addressables structure.",
                true
            );
        }

        if (snapshot.RuntimeSceneMetadataDirty)
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.RuntimeSceneMetadataDirty,
                TerrainRuntimeBakeReasonTarget.SceneSync,
                "Persistent runtime bake state marks runtime scene metadata dirty."
            );
        }

        if (heightMode != TerrainRuntimeBakeWorkMode.None)
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.GeneratedDataChanged,
                TerrainRuntimeBakeReasonTarget.SceneSync,
                "Height runtime data work requires runtime scene metadata reconciliation.",
                false,
                0,
                TerrainRuntimeBakeReasonTarget.Height
            );
        }

        if (topologyRequiresAddressablesConfiguration)
        {
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.DependencyPropagation,
                TerrainRuntimeBakeReasonTarget.SceneSync,
                "Runtime topology/configuration changes require runtime scene metadata reconciliation.",
                false,
                0,
                TerrainRuntimeBakeReasonTarget.Addressables
            );
        }

        globalTrace?.Complete();

        return
            CreatePlan(
                snapshot,
                heightMode,
                heightStreamingMode,
                surfaceMode,
                collisionMode,
                heightTiles,
                heightStreamingTiles,
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
        TerrainRuntimeBakeWorkMode heightStreamingMode,
        TerrainRuntimeBakeWorkMode surfaceMode,
        TerrainRuntimeBakeWorkMode collisionMode,
        IEnumerable<Vector2Int> heightTiles,
        IEnumerable<Vector2Int> heightStreamingTiles,
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
                heightStreamingMode,
                surfaceMode,
                collisionMode,
                heightTiles,
                heightStreamingTiles,
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
        ICollection<string> reasons,
        TerrainRuntimeBakePlanningDiagnosticsContext diagnostics
    )
    {
        if (reasons == null)
        {
            return;
        }

        if (manifest == null)
        {
            const string reason =
                "Runtime heightmap manifest is missing; a full height rebuild is required.";

            reasons.Add(reason);
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.MissingGeneratedMetadata,
                TerrainRuntimeBakeReasonTarget.Height,
                reason,
                true
            );
            return;
        }

        if (!manifest.isComplete)
        {
            const string reason =
                "Runtime heightmap manifest is incomplete; a full height rebuild is required.";

            reasons.Add(reason);
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.InvalidGeneratedMetadata,
                TerrainRuntimeBakeReasonTarget.Height,
                reason,
                true
            );
            return;
        }

        if (
            manifest.compilerVersion !=
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion
        )
        {
            const string reason =
                "Runtime height compiler version changed; existing tiles cannot be incrementally trusted.";

            reasons.Add(reason);
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.CompilerVersionChanged,
                TerrainRuntimeBakeReasonTarget.Height,
                reason,
                true
            );
            return;
        }

        if (
            !HeightTopologyMatchesWorld(
                manifest,
                worldSettings
            )
        )
        {
            const string reason =
                "Runtime heightmap topology does not match current WorldSettings; a full height rebuild is required.";

            reasons.Add(reason);
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.TopologyChanged,
                TerrainRuntimeBakeReasonTarget.Height,
                reason,
                true
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
            const string reason =
                "Runtime heightmap layout does not match current WorldSettings; a full height rebuild is required.";

            reasons.Add(reason);
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.LayoutChanged,
                TerrainRuntimeBakeReasonTarget.Height,
                reason,
                true
            );
            return;
        }

        if (!manifest.HasCompleteTileHeightRanges)
        {
            const string reason =
                "Runtime per-tile height range metadata is incomplete; a full height rebuild is required.";

            reasons.Add(reason);
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.InvalidGeneratedMetadata,
                TerrainRuntimeBakeReasonTarget.Height,
                reason,
                true
            );
            return;
        }

        if (!manifest.HasValidHeightRange)
        {
            const string reason =
                "Runtime height range metadata is invalid; a full height rebuild is required.";

            reasons.Add(reason);
            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.InvalidGeneratedMetadata,
                TerrainRuntimeBakeReasonTarget.Height,
                reason,
                true
            );
        }
    }

    private static string GetSurfaceCompatibilityReason(
        TerrainSurfaceMaskManifest manifest,
        TerrainSurfaceSettings surfaceSettings,
        string currentSettingsSignature,
        WorldSettings worldSettings,
        TerrainRuntimeBakePlanningDiagnosticsContext diagnostics
    )
    {
        if (manifest == null)
        {
            const string reason =
                "Runtime surface-mask manifest is missing; a full surface " +
                "rebuild is required.";

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.MissingGeneratedMetadata,
                TerrainRuntimeBakeReasonTarget.Surface,
                reason,
                true
            );

            return reason;
        }

        if (!manifest.isComplete)
        {
            const string reason =
                "Runtime surface-mask manifest is incomplete; a full surface " +
                "rebuild is required.";

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.InvalidGeneratedMetadata,
                TerrainRuntimeBakeReasonTarget.Surface,
                reason,
                true
            );

            return reason;
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
            const string reason =
                "Runtime surface compiler/channel format changed; a full " +
                "surface rebuild is required.";

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.CompilerVersionChanged,
                TerrainRuntimeBakeReasonTarget.Surface,
                reason,
                true
            );

            return reason;
        }

        if (surfaceSettings == null)
        {
            const string reason =
                "TerrainSurfaceSettings is unavailable; surface dependency " +
                "compatibility cannot be proven.";

            diagnostics?.AddPlannerReason(
                TerrainRuntimeBakeReasonCode.MissingRequiredAsset,
                TerrainRuntimeBakeReasonTarget.Surface,
                reason,
                true
            );

            return reason;
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
