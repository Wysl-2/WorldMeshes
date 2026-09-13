using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class TerrainRuntimeAddressablesUtility
{
    private sealed class GeneratedTargetIdentity
    {
        public int heightRevision;
        public string heightSignature;

        public int surfaceRevision;
        public int surfaceSourceHeightRevision;
        public string surfaceSignature;

        public int collisionRevision;
        public int collisionSourceHeightRevision;
        public string collisionSignature;

        public int gridWidth;
        public int gridHeight;
        public float chunkSize;
        public int heightfieldResolutionPerChunk;
        public int heightTileChunkSpan;
        public int collisionResolution;
    }

    // =====================================================
    // PACKAGE 09 ADVANCED MAINTENANCE
    // =====================================================

    /*
     * Explicit structural reconciliation for the Package 09 Advanced Runtime
     * Tools UI. This performs the same lower-level reconciliation used by
     * Package 07 without forcing an Addressables content build.
     *
     * Persistent state acknowledgement remains owned here rather than by UI
     * code. Any structural mutation marks player content dirty. Collision
     * preparation metadata changes mark runtime scene metadata dirty.
     */
    public static bool ReconfigureAllRuntimeAddressables(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot reconfigure runtime Addressables because WorldSettings is unavailable."
            );

            return false;
        }

        if (
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Runtime Addressables configuration cannot be changed while entering or running Play Mode."
            );

            return false;
        }

        if (
            !GeneratedDatasetsAreCurrent(
                worldSettings,
                out string generationError
            )
        )
        {
            Debug.LogError(
                "Cannot reconfigure runtime Addressables.\n\n" +
                generationError
            );

            return false;
        }

        TerrainHeightmapManifest heightManifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        if (
            heightManifest == null
            ||
            !heightManifest.isComplete
            ||
            surfaceManifest == null
            ||
            !surfaceManifest.isComplete
        )
        {
            Debug.LogError(
                "Cannot reconfigure runtime Addressables because current runtime height/surface manifests are missing or incomplete."
            );

            return false;
        }

        TerrainRuntimeBakeStateSnapshot startSnapshot =
            TerrainRuntimeBakeStateService.GetSnapshot();

        GeneratedTargetIdentity target =
            CaptureGeneratedTarget(
                worldSettings
            );

        TerrainAddressablesOperationStats stats =
            new TerrainAddressablesOperationStats();

        if (
            !TerrainHeightmapAddressablesUtility.ReconcileConfiguration(
                heightManifest,
                stats,
                out bool heightCancelled,
                out string heightError
            )
        )
        {
            MarkConfigurationRepairRequired();

            Debug.LogError(
                heightCancelled
                    ? "Runtime Addressables reconfiguration was cancelled during height configuration."
                    : heightError
            );

            return false;
        }

        if (
            !TerrainSurfaceMaskAddressablesUtility.ReconcileConfiguration(
                surfaceManifest,
                stats,
                out bool surfaceCancelled,
                out string surfaceError
            )
        )
        {
            MarkConfigurationRepairRequired();

            Debug.LogError(
                surfaceCancelled
                    ? "Runtime Addressables reconfiguration was cancelled during surface configuration."
                    : surfaceError
            );

            return false;
        }

        if (
            !TerrainCollisionAddressablesUtility.ReconcileConfiguration(
                worldSettings,
                stats,
                out bool collisionCancelled,
                out string collisionError
            )
        )
        {
            MarkConfigurationRepairRequired();

            Debug.LogError(
                collisionCancelled
                    ? "Runtime Addressables reconfiguration was cancelled during collision configuration."
                    : collisionError
            );

            return false;
        }

        if (
            !TerrainCollisionAddressablesUtility.RefreshRuntimeMetadata(
                worldSettings,
                out bool metadataChanged,
                out bool manifestCreated,
                out string metadataError
            )
        )
        {
            MarkConfigurationRepairRequired();

            Debug.LogError(
                metadataError
            );

            return false;
        }

        stats.collisionRuntimeMetadataUpdated |=
            metadataChanged;

        stats.collisionManifestCreated |=
            manifestCreated;

        if (
            !TargetStillCurrent(
                worldSettings,
                target
            )
            ||
            TerrainRuntimeBakeStateService
                .GetSnapshot()
                .StateRevision !=
                startSnapshot.StateRevision
        )
        {
            MarkConfigurationRepairRequired();

            Debug.LogError(
                "Runtime generated data or persistent bake state changed while Addressables structure was being reconciled. Configuration changes were preserved, but Configuration and Content remain dirty."
            );

            return false;
        }

        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation();

        bool hasMutation =
            false;

        if (stats.AnyConfigurationChanged)
        {
            mutation.DirtyAddressablesContent();
            hasMutation = true;
        }

        if (
            metadataChanged
            ||
            manifestCreated
        )
        {
            mutation.DirtyRuntimeSceneMetadata();
            hasMutation = true;
        }

        if (hasMutation)
        {
            TerrainRuntimeBakeStateService.ApplyMutation(
                mutation
            );
        }

        TerrainRuntimeBakeStateSnapshot beforeClear =
            TerrainRuntimeBakeStateService.GetSnapshot();

        if (beforeClear.AddressablesConfigurationDirty)
        {
            TerrainRuntimeBakeStateService
                .ClearAddressablesConfigurationDirty();
        }

        Debug.Log(
            "Runtime Addressables structural reconfiguration complete.\n\n" +
            "Height Configuration Changed: " +
            stats.heightConfigurationChanged +
            "\n" +
            "Surface Configuration Changed: " +
            stats.surfaceConfigurationChanged +
            "\n" +
            "Collision Configuration Changed: " +
            stats.collisionConfigurationChanged +
            "\n" +
            "Collision Markers Regenerated: " +
            stats.collisionMarkersRegenerated +
            "\n" +
            "Collision Markers Reused: " +
            stats.collisionMarkersReused +
            "\n" +
            "Entries Created: " +
            stats.entriesCreated +
            "\n" +
            "Entries Moved: " +
            stats.entriesMoved +
            "\n" +
            "Entries Removed: " +
            stats.entriesRemoved +
            "\n" +
            "Addressables Content Dirty: " +
            TerrainRuntimeBakeStateService
                .GetSnapshot()
                .AddressablesContentDirty
        );

        return true;
    }

    /*
     * Explicit player-content rebuild for Advanced Runtime Tools. Unlike the
     * legacy raw BuildAddressablesContent wrapper, this validates structural
     * readiness, preserves target identity, refreshes collision preparation
     * metadata, and acknowledges Addressables Content dirty only when the
     * completed build still matches the generated target.
     */
    public static bool RebuildAddressablesContentAndAcknowledge(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot rebuild Addressables content because WorldSettings is unavailable."
            );

            return false;
        }

        if (
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Addressables content cannot be rebuilt while entering or running Play Mode."
            );

            return false;
        }

        if (
            !GeneratedDatasetsAreCurrent(
                worldSettings,
                out string generationError
            )
        )
        {
            Debug.LogError(
                "Cannot rebuild Addressables content.\n\n" +
                generationError
            );

            return false;
        }

        TerrainRuntimeBakeStateSnapshot startSnapshot =
            TerrainRuntimeBakeStateService.GetSnapshot();

        if (startSnapshot.AddressablesConfigurationDirty)
        {
            Debug.LogError(
                "Addressables structural configuration is dirty. Run Reconfigure Addressables before rebuilding player content."
            );

            return false;
        }

        TerrainRuntimeAddressablesValidationResult validation =
            ValidateExistingRuntimeConfiguration(
                worldSettings
            );

        if (!validation.IsValid)
        {
            MarkConfigurationRepairRequired();

            Debug.LogError(
                validation.BuildDiagnosticReport()
            );

            return false;
        }

        GeneratedTargetIdentity target =
            CaptureGeneratedTarget(
                worldSettings
            );

        long expectedStateRevision =
            startSnapshot.StateRevision;

        if (
            !TerrainCollisionAddressablesUtility.RefreshRuntimeMetadata(
                worldSettings,
                out bool metadataChanged,
                out bool manifestCreated,
                out string metadataError
            )
        )
        {
            Debug.LogError(
                metadataError
            );

            TerrainRuntimeBakeStateService
                .MarkAddressablesContentDirty();

            return false;
        }

        if (manifestCreated)
        {
            MarkConfigurationRepairRequired();

            Debug.LogError(
                "Collision runtime metadata unexpectedly created a new prepared manifest while rebuilding Addressables content. Run Reconfigure Addressables before rebuilding player content."
            );

            return false;
        }

        if (metadataChanged)
        {
            TerrainRuntimeBakeStateMutation mutation =
                new TerrainRuntimeBakeStateMutation();

            mutation.DirtyRuntimeSceneMetadata();

            TerrainRuntimeBakeStateService.ApplyMutation(
                mutation
            );

            expectedStateRevision =
                TerrainRuntimeBakeStateService
                    .GetSnapshot()
                    .StateRevision;
        }

        if (
            !TargetStillCurrent(
                worldSettings,
                target
            )
            ||
            TerrainRuntimeBakeStateService
                .GetSnapshot()
                .StateRevision !=
                expectedStateRevision
        )
        {
            TerrainRuntimeBakeStateService
                .MarkAddressablesContentDirty();

            Debug.LogError(
                "Runtime generated data or bake state changed before the Addressables content rebuild could start. Content remains dirty."
            );

            return false;
        }

        if (
            !TryBuildAddressablesContent(
                out string outputPath,
                out double duration,
                out string buildError
            )
        )
        {
            TerrainRuntimeBakeStateService
                .MarkAddressablesContentDirty();

            Debug.LogError(
                buildError
            );

            return false;
        }

        if (
            !TargetStillCurrent(
                worldSettings,
                target
            )
            ||
            TerrainRuntimeBakeStateService
                .GetSnapshot()
                .StateRevision !=
                expectedStateRevision
        )
        {
            TerrainRuntimeBakeStateService
                .MarkAddressablesContentDirty();

            Debug.LogError(
                "Runtime generated data or bake state changed while Addressables player content was building. The completed build was not acknowledged and Content remains dirty."
            );

            return false;
        }

        TerrainRuntimeBakeStateSnapshot beforeClear =
            TerrainRuntimeBakeStateService.GetSnapshot();

        if (beforeClear.AddressablesContentDirty)
        {
            TerrainRuntimeBakeStateService
                .ClearAddressablesContentDirty();
        }

        Debug.Log(
            "Addressables player content rebuild complete.\n\n" +
            "Output Path:\n" +
            outputPath +
            "\n\n" +
            "Build Duration: " +
            duration.ToString("0.00") +
            " seconds"
        );

        return true;
    }

    // =====================================================
    // PLANNED PACKAGE 07 OPERATION
    // =====================================================

    public static TerrainRuntimeAddressablesResult
        ProcessPlannedRuntimeContent(
            WorldSettings worldSettings,
            TerrainRuntimeBakePlan plan
        )
    {
        TerrainAddressablesOperationStats stats =
            new TerrainAddressablesOperationStats();

        if (plan == null)
        {
            return CreateResult(
                TerrainRuntimeAddressablesOutcome.Failed,
                TerrainRuntimeAddressablesOperationMode.None,
                false,
                false,
                false,
                false,
                stats,
                "",
                0d,
                false,
                false,
                "Addressables bake plan is null.",
                ""
            );
        }

        TerrainRuntimeAddressablesOperationMode mode =
            GetOperationMode(
                plan
            );

        bool configurationRequired =
            plan.AddressablesConfigurationRequired;

        bool contentRequired =
            plan.AddressablesContentBuildRequired;

        if (plan.IsBlocked)
        {
            return CreateResult(
                TerrainRuntimeAddressablesOutcome.Blocked,
                mode,
                configurationRequired,
                false,
                contentRequired,
                false,
                stats,
                "",
                0d,
                false,
                false,
                plan.BlockReason,
                ""
            );
        }

        if (worldSettings == null)
        {
            return CreateResult(
                TerrainRuntimeAddressablesOutcome.Blocked,
                mode,
                configurationRequired,
                false,
                contentRequired,
                false,
                stats,
                "",
                0d,
                false,
                false,
                "WorldSettings is unavailable.",
                ""
            );
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return CreateResult(
                TerrainRuntimeAddressablesOutcome.Blocked,
                mode,
                configurationRequired,
                false,
                contentRequired,
                false,
                stats,
                "",
                0d,
                false,
                false,
                "Runtime Addressables work must run outside Play Mode.",
                ""
            );
        }

        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService.GetSnapshot();

        if (snapshot.StateRevision != plan.SourceStateRevision)
        {
            return CreateResult(
                TerrainRuntimeAddressablesOutcome.StalePlan,
                mode,
                configurationRequired,
                false,
                contentRequired,
                false,
                stats,
                "",
                0d,
                false,
                false,
                "Persistent runtime bake state changed after the Addressables plan was built.",
                ""
            );
        }

        if (
            mode ==
            TerrainRuntimeAddressablesOperationMode.None
        )
        {
            return CreateResult(
                TerrainRuntimeAddressablesOutcome.NoWork,
                mode,
                false,
                false,
                false,
                false,
                stats,
                "",
                0d,
                false,
                false,
                "",
                "No runtime Addressables work is required."
            );
        }

        if (
            !GeneratedDatasetsAreCurrent(
                worldSettings,
                out string generationError
            )
        )
        {
            return CreateResult(
                TerrainRuntimeAddressablesOutcome.Blocked,
                mode,
                configurationRequired,
                false,
                contentRequired,
                false,
                stats,
                "",
                0d,
                false,
                false,
                generationError,
                ""
            );
        }

        TerrainHeightmapManifest heightManifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        if (
            heightManifest == null
            ||
            !heightManifest.isComplete
            ||
            surfaceManifest == null
            ||
            !surfaceManifest.isComplete
        )
        {
            return CreateResult(
                TerrainRuntimeAddressablesOutcome.Blocked,
                mode,
                configurationRequired,
                false,
                contentRequired,
                false,
                stats,
                "",
                0d,
                false,
                false,
                "Current runtime height/surface generation requires complete manifests before Addressables work.",
                ""
            );
        }

        GeneratedTargetIdentity target =
            CaptureGeneratedTarget(
                worldSettings
            );

        long expectedStateRevision =
            plan.SourceStateRevision;

        bool configurationPerformed =
            false;

        bool contentBuildPerformed =
            false;

        bool configurationDirtyCleared =
            false;

        bool contentDirtyCleared =
            false;

        string buildOutputPath =
            "";

        double buildDuration =
            0d;

        // =================================================
        // CONTENT-ONLY READ-ONLY VALIDATION
        // =================================================

        if (
            mode ==
            TerrainRuntimeAddressablesOperationMode.ContentOnly
        )
        {
            TerrainRuntimeAddressablesValidationResult validation =
                ValidateExistingRuntimeConfiguration(
                    worldSettings
                );

            stats.collisionMarkersReused =
                validation.CollisionMarkerStructureValid
                    ? validation.CollisionMarkerCount
                    : 0;

            if (!validation.IsValid)
            {
                MarkConfigurationRepairRequired();

                return CreateResult(
                    TerrainRuntimeAddressablesOutcome.StalePlan,
                    mode,
                    false,
                    false,
                    true,
                    false,
                    stats,
                    "",
                    0d,
                    false,
                    false,
                    validation.BuildDiagnosticReport(),
                    "Read-only Addressables validation found structural configuration damage. Configuration and content were marked dirty for a repair pass."
                );
            }

            if (
                !TerrainCollisionAddressablesUtility.RefreshRuntimeMetadata(
                    worldSettings,
                    out bool metadataChanged,
                    out bool manifestCreated,
                    out string metadataError
                )
            )
            {
                MarkConfigurationRepairRequired();

                return CreateResult(
                    TerrainRuntimeAddressablesOutcome.Failed,
                    mode,
                    false,
                    false,
                    true,
                    false,
                    stats,
                    "",
                    0d,
                    false,
                    false,
                    metadataError,
                    "Collision runtime metadata could not be refreshed safely."
                );
            }

            /*
             * A structurally valid ContentOnly path should never create the
             * manifest because validation requires the previous complete
             * structural manifest. Treat unexpected creation conservatively.
             */
            if (manifestCreated)
            {
                MarkConfigurationRepairRequired();

                return CreateResult(
                    TerrainRuntimeAddressablesOutcome.StalePlan,
                    mode,
                    false,
                    false,
                    true,
                    false,
                    stats,
                    "",
                    0d,
                    false,
                    false,
                    "Collision runtime metadata unexpectedly created a new prepared manifest during ContentOnly processing.",
                    "Configuration was marked dirty so the new manifest can be reconciled explicitly."
                );
            }

            stats.collisionRuntimeMetadataUpdated =
                metadataChanged;
        }

        // =================================================
        // STRUCTURAL CONFIGURATION
        // =================================================

        if (
            mode ==
            TerrainRuntimeAddressablesOperationMode.ConfigureAndBuild
        )
        {
            if (
                !TerrainHeightmapAddressablesUtility.ReconcileConfiguration(
                    heightManifest,
                    stats,
                    out bool heightCancelled,
                    out string heightError
                )
            )
            {
                MarkConfigurationRepairRequired();

                return CreateResult(
                    heightCancelled
                        ? TerrainRuntimeAddressablesOutcome.Cancelled
                        : TerrainRuntimeAddressablesOutcome.Failed,
                    mode,
                    true,
                    true,
                    true,
                    false,
                    stats,
                    "",
                    0d,
                    false,
                    false,
                    heightError,
                    "Addressables structural configuration did not complete. Configuration and content remain dirty."
                );
            }

            if (
                !TerrainSurfaceMaskAddressablesUtility.ReconcileConfiguration(
                    surfaceManifest,
                    stats,
                    out bool surfaceCancelled,
                    out string surfaceError
                )
            )
            {
                MarkConfigurationRepairRequired();

                return CreateResult(
                    surfaceCancelled
                        ? TerrainRuntimeAddressablesOutcome.Cancelled
                        : TerrainRuntimeAddressablesOutcome.Failed,
                    mode,
                    true,
                    true,
                    true,
                    false,
                    stats,
                    "",
                    0d,
                    false,
                    false,
                    surfaceError,
                    "Addressables structural configuration did not complete. Configuration and content remain dirty."
                );
            }

            if (
                !TerrainCollisionAddressablesUtility.ReconcileConfiguration(
                    worldSettings,
                    stats,
                    out bool collisionCancelled,
                    out string collisionError
                )
            )
            {
                MarkConfigurationRepairRequired();

                return CreateResult(
                    collisionCancelled
                        ? TerrainRuntimeAddressablesOutcome.Cancelled
                        : TerrainRuntimeAddressablesOutcome.Failed,
                    mode,
                    true,
                    true,
                    true,
                    false,
                    stats,
                    "",
                    0d,
                    false,
                    false,
                    collisionError,
                    "Addressables structural configuration did not complete. Configuration and content remain dirty."
                );
            }

            configurationPerformed =
                true;

            if (
                !TerrainCollisionAddressablesUtility.RefreshRuntimeMetadata(
                    worldSettings,
                    out bool metadataChanged,
                    out bool manifestCreated,
                    out string metadataError
                )
            )
            {
                MarkConfigurationRepairRequired();

                return CreateResult(
                    TerrainRuntimeAddressablesOutcome.Failed,
                    mode,
                    true,
                    true,
                    true,
                    false,
                    stats,
                    "",
                    0d,
                    false,
                    false,
                    metadataError,
                    "Structural configuration completed partially, but collision runtime metadata could not be finalized."
                );
            }

            stats.collisionRuntimeMetadataUpdated |=
                metadataChanged;

            stats.collisionManifestCreated |=
                manifestCreated;

            if (
                !TargetStillCurrent(
                    worldSettings,
                    target
                )
                ||
                TerrainRuntimeBakeStateService
                    .GetSnapshot()
                    .StateRevision !=
                    expectedStateRevision
            )
            {
                MarkConfigurationRepairRequired();

                return CreateResult(
                    TerrainRuntimeAddressablesOutcome.StalePlan,
                    mode,
                    true,
                    true,
                    true,
                    false,
                    stats,
                    "",
                    0d,
                    false,
                    false,
                    "Generated runtime data or persistent bake state changed before Addressables configuration could be acknowledged.",
                    "Configuration changes were preserved, but dirty state was left conservative."
                );
            }

            TerrainRuntimeBakeStateSnapshot beforeConfigurationClear =
                TerrainRuntimeBakeStateService.GetSnapshot();

            if (beforeConfigurationClear.AddressablesConfigurationDirty)
            {
                configurationDirtyCleared =
                    TerrainRuntimeBakeStateService
                        .ClearAddressablesConfigurationDirty();

                expectedStateRevision =
                    TerrainRuntimeBakeStateService
                        .GetSnapshot()
                        .StateRevision;
            }

            if (stats.collisionManifestCreated)
            {
                TerrainRuntimeBakeStateMutation sceneMutation =
                    new TerrainRuntimeBakeStateMutation();

                sceneMutation.DirtyRuntimeSceneMetadata();

                TerrainRuntimeBakeStateService.ApplyMutation(
                    sceneMutation
                );

                expectedStateRevision =
                    TerrainRuntimeBakeStateService
                        .GetSnapshot()
                        .StateRevision;
            }
        }

        // =================================================
        // CONTENT BUILD
        // =================================================

        if (contentRequired)
        {
            if (
                !TargetStillCurrent(
                    worldSettings,
                    target
                )
                ||
                TerrainRuntimeBakeStateService
                    .GetSnapshot()
                    .StateRevision !=
                    expectedStateRevision
            )
            {
                return CreateResult(
                    TerrainRuntimeAddressablesOutcome.StalePlan,
                    mode,
                    configurationRequired,
                    configurationPerformed,
                    true,
                    false,
                    stats,
                    "",
                    0d,
                    configurationDirtyCleared,
                    false,
                    "Generated runtime data or persistent bake state changed before Addressables content build.",
                    "Content remains dirty."
                );
            }

            contentBuildPerformed =
                true;

            if (
                !TryBuildAddressablesContent(
                    out buildOutputPath,
                    out buildDuration,
                    out string buildError
                )
            )
            {
                TerrainRuntimeBakeStateService
                    .MarkAddressablesContentDirty();

                return CreateResult(
                    TerrainRuntimeAddressablesOutcome.Failed,
                    mode,
                    configurationRequired,
                    configurationPerformed,
                    true,
                    true,
                    stats,
                    buildOutputPath,
                    buildDuration,
                    configurationDirtyCleared,
                    false,
                    buildError,
                    configurationPerformed
                        ? "Addressables configuration is repaired, but player content is still dirty because the build failed."
                        : "Existing configuration remains valid, but player content is still dirty because the build failed."
                );
            }

            if (
                !TargetStillCurrent(
                    worldSettings,
                    target
                )
                ||
                TerrainRuntimeBakeStateService
                    .GetSnapshot()
                    .StateRevision !=
                    expectedStateRevision
            )
            {
                return CreateResult(
                    TerrainRuntimeAddressablesOutcome.StalePlan,
                    mode,
                    configurationRequired,
                    configurationPerformed,
                    true,
                    true,
                    stats,
                    buildOutputPath,
                    buildDuration,
                    configurationDirtyCleared,
                    false,
                    "Runtime generated data or bake state changed while Addressables player content was building.",
                    "The completed build was not acknowledged; Addressables Content remains dirty for the newer target."
                );
            }

            TerrainRuntimeBakeStateSnapshot beforeContentClear =
                TerrainRuntimeBakeStateService.GetSnapshot();

            if (beforeContentClear.AddressablesContentDirty)
            {
                contentDirtyCleared =
                    TerrainRuntimeBakeStateService
                        .ClearAddressablesContentDirty();

                expectedStateRevision =
                    TerrainRuntimeBakeStateService
                        .GetSnapshot()
                        .StateRevision;
            }
        }

        TerrainRuntimeIntegrityAuditUtility
            .InvalidateCachedAudit();

        return CreateResult(
            TerrainRuntimeAddressablesOutcome.Completed,
            mode,
            configurationRequired,
            configurationPerformed,
            contentRequired,
            contentBuildPerformed,
            stats,
            buildOutputPath,
            buildDuration,
            configurationDirtyCleared,
            contentDirtyCleared,
            "",
            mode == TerrainRuntimeAddressablesOperationMode.ContentOnly
                ? "Existing Addressables configuration was reused, collision runtime metadata was refreshed as required, and updated player content was built."
                : "Runtime Addressables structural configuration was reconciled and updated player content was built."
        );
    }

    // =====================================================
    // READ-ONLY VALIDATION API
    // =====================================================

    public static TerrainRuntimeAddressablesValidationResult
        ValidateExistingRuntimeConfiguration(
            WorldSettings worldSettings
        )
    {
        TerrainHeightmapManifest heightManifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        bool heightValid =
            TerrainHeightmapAddressablesUtility.ValidateExistingConfiguration(
                heightManifest,
                out string heightError
            );

        bool surfaceValid =
            TerrainSurfaceMaskAddressablesUtility.ValidateExistingConfiguration(
                surfaceManifest,
                out string surfaceError
            );

        bool markerValid =
            TerrainCollisionBakeMarkerUtility.ValidateExistingBakeMarkers(
                worldSettings,
                out List<
                    TerrainCollisionBakeMarkerUtility.BakeMarkerRecord
                > markerRecords,
                out string markerError
            );

        bool collisionValid =
            false;

        string collisionError =
            "";

        if (markerValid)
        {
            collisionValid =
                TerrainCollisionAddressablesUtility
                    .ValidateExistingConfiguration(
                        worldSettings,
                        markerRecords,
                        out collisionError
                    );
        }
        else
        {
            collisionError =
                "Collision Addressables validation was not completed because collision marker structure needs repair.";
        }

        bool collisionManifestValid =
            TerrainCollisionAddressablesUtility
                .ValidatePreparedManifestStructure(
                    worldSettings,
                    out string collisionManifestError
                );

        return
            new TerrainRuntimeAddressablesValidationResult(
                heightValid,
                surfaceValid,
                collisionValid,
                markerValid,
                collisionManifestValid,
                markerRecords != null
                    ? markerRecords.Count
                    : 0,
                heightError,
                surfaceError,
                collisionError,
                markerError,
                collisionManifestError
            );
    }

    // =====================================================
    // CENTRALIZED BUILDPLAYERCONTENT
    // =====================================================

    public static bool BuildAddressablesContent()
    {
        if (
            !TryBuildAddressablesContent(
                out string outputPath,
                out double duration,
                out string errorMessage
            )
        )
        {
            Debug.LogError(
                errorMessage
            );

            return false;
        }

        Debug.Log(
            "Addressables content build complete.\n\n" +
            "Output Path:\n" +
            outputPath +
            "\n\n" +
            "Build Duration: " +
            duration.ToString("0.00") +
            " seconds"
        );

        return true;
    }

    private static bool TryBuildAddressablesContent(
        out string outputPath,
        out double duration,
        out string errorMessage
    )
    {
        outputPath = "";
        duration = 0d;
        errorMessage = "";

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage =
                "Addressables content cannot be rebuilt while entering or running Play Mode.";

            return false;
        }

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(
                false
            );

        if (settings == null)
        {
            errorMessage =
                "AddressableAssetSettings could not be loaded.";

            return false;
        }

        /*
         * Do not force AssetDatabase.Refresh here. ContentOnly work intentionally
         * avoids broad reimport/refresh churn; SaveAssets is sufficient before
         * Unity's normal Addressables builder runs.
         */
        AssetDatabase.SaveAssets();

        Debug.Log(
            "Building Addressables player content..."
        );

        AddressableAssetSettings.BuildPlayerContent(
            out AddressablesPlayerBuildResult result
        );

        if (result == null)
        {
            errorMessage =
                "Addressables content build failed: no build result was returned.";

            return false;
        }

        outputPath =
            result.OutputPath ?? "";

        duration =
            result.Duration;

        if (!string.IsNullOrEmpty(result.Error))
        {
            errorMessage =
                "Addressables content build failed.\n\n" +
                result.Error;

            return false;
        }

        return true;
    }

    // =====================================================
    // OPERATION / TARGET
    // =====================================================

    public static TerrainRuntimeAddressablesOperationMode
        GetOperationMode(
            TerrainRuntimeBakePlan plan
        )
    {
        if (plan == null)
        {
            return
                TerrainRuntimeAddressablesOperationMode.None;
        }

        if (plan.AddressablesConfigurationRequired)
        {
            return
                TerrainRuntimeAddressablesOperationMode.ConfigureAndBuild;
        }

        if (plan.AddressablesContentBuildRequired)
        {
            return
                TerrainRuntimeAddressablesOperationMode.ContentOnly;
        }

        return
            TerrainRuntimeAddressablesOperationMode.None;
    }

    private static bool GeneratedDatasetsAreCurrent(
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            TerrainGenerationStateUtility.GetHeightmapStatus(
                worldSettings
            )
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            errorMessage =
                "Runtime Heightmaps are not current.";

            return false;
        }

        if (
            TerrainGenerationStateUtility.GetSurfaceMaskStatus(
                worldSettings
            )
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            errorMessage =
                "Runtime Surface Masks are not current.";

            return false;
        }

        if (
            TerrainGenerationStateUtility.GetCollisionMeshStatus(
                worldSettings
            )
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            errorMessage =
                "Runtime Collision Meshes are not current.";

            return false;
        }

        return true;
    }

    private static GeneratedTargetIdentity CaptureGeneratedTarget(
        WorldSettings worldSettings
    )
    {
        return
            new GeneratedTargetIdentity
            {
                heightRevision =
                    worldSettings.heightmapGenerationRevision,

                heightSignature =
                    worldSettings.lastGeneratedHeightSignature ?? "",

                surfaceRevision =
                    worldSettings.surfaceMaskGenerationRevision,

                surfaceSourceHeightRevision =
                    worldSettings.surfaceSourceHeightmapGenerationRevision,

                surfaceSignature =
                    worldSettings.lastGeneratedSurfaceSignature ?? "",

                collisionRevision =
                    worldSettings.collisionMeshGenerationRevision,

                collisionSourceHeightRevision =
                    worldSettings.collisionSourceHeightmapGenerationRevision,

                collisionSignature =
                    worldSettings.lastGeneratedCollisionSignature ?? "",

                gridWidth =
                    Mathf.Max(
                        1,
                        worldSettings.gridWidth
                    ),

                gridHeight =
                    Mathf.Max(
                        1,
                        worldSettings.gridHeight
                    ),

                chunkSize =
                    Mathf.Max(
                        0.01f,
                        worldSettings.chunkSize
                    ),

                heightfieldResolutionPerChunk =
                    Mathf.Max(
                        1,
                        worldSettings.heightfieldResolutionPerChunk
                    ),

                heightTileChunkSpan =
                    Mathf.Max(
                        1,
                        worldSettings.heightTileChunkSpan
                    ),

                collisionResolution =
                    Mathf.Max(
                        1,
                        worldSettings.collisionResolution
                    )
            };
    }

    private static bool TargetStillCurrent(
        WorldSettings worldSettings,
        GeneratedTargetIdentity target
    )
    {
        if (
            worldSettings == null
            ||
            target == null
        )
        {
            return false;
        }

        if (
            !GeneratedDatasetsAreCurrent(
                worldSettings,
                out _
            )
        )
        {
            return false;
        }

        return
            worldSettings.heightmapGenerationRevision
                == target.heightRevision
            &&
            string.Equals(
                worldSettings.lastGeneratedHeightSignature ?? "",
                target.heightSignature,
                StringComparison.Ordinal
            )
            &&
            worldSettings.surfaceMaskGenerationRevision
                == target.surfaceRevision
            &&
            worldSettings.surfaceSourceHeightmapGenerationRevision
                == target.surfaceSourceHeightRevision
            &&
            string.Equals(
                worldSettings.lastGeneratedSurfaceSignature ?? "",
                target.surfaceSignature,
                StringComparison.Ordinal
            )
            &&
            worldSettings.collisionMeshGenerationRevision
                == target.collisionRevision
            &&
            worldSettings.collisionSourceHeightmapGenerationRevision
                == target.collisionSourceHeightRevision
            &&
            string.Equals(
                worldSettings.lastGeneratedCollisionSignature ?? "",
                target.collisionSignature,
                StringComparison.Ordinal
            )
            &&
            Mathf.Max(1, worldSettings.gridWidth)
                == target.gridWidth
            &&
            Mathf.Max(1, worldSettings.gridHeight)
                == target.gridHeight
            &&
            Mathf.Approximately(
                Mathf.Max(0.01f, worldSettings.chunkSize),
                target.chunkSize
            )
            &&
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            )
                == target.heightfieldResolutionPerChunk
            &&
            Mathf.Max(
                1,
                worldSettings.heightTileChunkSpan
            )
                == target.heightTileChunkSpan
            &&
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            )
                == target.collisionResolution;
    }

    // =====================================================
    // DIRTY STATE POLICY
    // =====================================================

    private static void MarkConfigurationRepairRequired()
    {
        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation();

        mutation
            .DirtyAddressablesConfiguration()
            .DirtyAddressablesContent();

        TerrainRuntimeBakeStateService.ApplyMutation(
            mutation
        );
    }

    // =====================================================
    // RESULT
    // =====================================================

    private static TerrainRuntimeAddressablesResult CreateResult(
        TerrainRuntimeAddressablesOutcome outcome,
        TerrainRuntimeAddressablesOperationMode mode,
        bool configurationWasRequired,
        bool configurationPerformed,
        bool contentBuildWasRequired,
        bool contentBuildPerformed,
        TerrainAddressablesOperationStats stats,
        string buildOutputPath,
        double buildDuration,
        bool configurationDirtyCleared,
        bool contentDirtyCleared,
        string errorMessage,
        string summaryMessage
    )
    {
        return
            new TerrainRuntimeAddressablesResult(
                outcome,
                mode,
                configurationWasRequired,
                configurationPerformed,
                contentBuildWasRequired,
                contentBuildPerformed,
                stats,
                buildOutputPath,
                buildDuration,
                configurationDirtyCleared,
                contentDirtyCleared,
                errorMessage,
                summaryMessage
            );
    }
}
