using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum TerrainRuntimeFaultRecoveryScenario
{
    None,

    MissingHeightTexture,
    MissingSurfaceTexture,
    MissingCollisionMesh,

    MissingHeightManifest,
    IncompleteHeightManifest,
    InvalidHeightMetadata,

    MissingSurfaceManifest,
    IncompleteSurfaceManifest,
    InvalidSurfaceMetadata,

    InvalidCollisionMetadata,

    MissingPreparedCollisionManifest,
    InvalidPreparedCollisionManifest,

    MissingHeightAddressableEntry,
    MissingSurfaceAddressableEntry,
    MissingCollisionAddressableEntry,
    IncorrectAddressableAddress,
    MissingAddressableLabel,
    IncorrectAddressableLabel,
    MissingAddressablesGroup,
    MissingOrInvalidAddressablesSchema,
    MissingCollisionMarker,
    CollisionMarkerWrongMesh,

    MissingWorldRoot,
    MissingClipmapRoot,
    DuplicateWorldRoot,
    DuplicateClipmapRoot,

    MissingTerrainClipmapBoundsController,
    DuplicateTerrainClipmapBoundsController,
    MissingTerrainHeightmapStreamer,
    DuplicateTerrainHeightmapStreamer,

    MissingCollisionRoot,
    MissingTerrainCollisionStreamer,
    DuplicateTerrainCollisionStreamer,
    MissingTerrainCollisionColliderPool,
    DuplicateTerrainCollisionColliderPool
}

public enum TerrainRuntimeFaultRecoveryAuthority
{
    None,
    UnifiedBake,
    AddressablesConfigureAndBuild,
    HierarchyRepair
}

public enum TerrainRuntimeFaultRecoveryValidationOutcome
{
    NotRun,
    FaultInjected,
    Passed,
    PassedWithWarnings,
    Failed,
    Blocked,
    CleanupFailed
}

public sealed class TerrainRuntimeFaultExpectation
{
    public TerrainRuntimeFaultRecoveryScenario Scenario { get; internal set; }
    public TerrainRuntimeBakeWorkMode HeightMode { get; internal set; }
    public TerrainRuntimeBakeWorkMode SurfaceMode { get; internal set; }
    public TerrainRuntimeBakeWorkMode CollisionMode { get; internal set; }
    public bool AddressablesConfigurationRequired { get; internal set; }
    public bool HierarchyReady { get; internal set; }
    public bool OverallReady { get; internal set; }
    public TerrainRuntimeFaultRecoveryAuthority RecoveryAuthority { get; internal set; }
    public string Description { get; internal set; }

    public static TerrainRuntimeFaultExpectation ForScenario(
        TerrainRuntimeFaultRecoveryScenario scenario
    )
    {
        TerrainRuntimeFaultExpectation e =
            new TerrainRuntimeFaultExpectation
            {
                Scenario = scenario,
                HeightMode = TerrainRuntimeBakeWorkMode.None,
                SurfaceMode = TerrainRuntimeBakeWorkMode.None,
                CollisionMode = TerrainRuntimeBakeWorkMode.None,
                AddressablesConfigurationRequired = false,
                HierarchyReady = true,
                OverallReady = false,
                RecoveryAuthority = TerrainRuntimeFaultRecoveryAuthority.None,
                Description = ""
            };

        switch (scenario)
        {
            case TerrainRuntimeFaultRecoveryScenario.MissingHeightTexture:
            case TerrainRuntimeFaultRecoveryScenario.IncompleteHeightManifest:
            case TerrainRuntimeFaultRecoveryScenario.InvalidHeightMetadata:
                e.HeightMode = TerrainRuntimeBakeWorkMode.Full;
                e.SurfaceMode = TerrainRuntimeBakeWorkMode.Full;
                e.CollisionMode = TerrainRuntimeBakeWorkMode.Full;
                e.RecoveryAuthority = TerrainRuntimeFaultRecoveryAuthority.UnifiedBake;
                e.Description =
                    "Height generation integrity is unproven; recover the complete Height dependency chain conservatively.";
                return e;

            case TerrainRuntimeFaultRecoveryScenario.MissingHeightManifest:
                e.HeightMode = TerrainRuntimeBakeWorkMode.Full;
                e.SurfaceMode = TerrainRuntimeBakeWorkMode.Full;
                e.CollisionMode = TerrainRuntimeBakeWorkMode.Full;
                e.AddressablesConfigurationRequired = true;
                e.RecoveryAuthority = TerrainRuntimeFaultRecoveryAuthority.UnifiedBake;
                e.Description =
                    "Missing Height topology/provenance requires Full Height dependency-chain recovery and structural Addressables reconciliation.";
                return e;

            case TerrainRuntimeFaultRecoveryScenario.MissingSurfaceTexture:
            case TerrainRuntimeFaultRecoveryScenario.IncompleteSurfaceManifest:
            case TerrainRuntimeFaultRecoveryScenario.InvalidSurfaceMetadata:
                e.SurfaceMode = TerrainRuntimeBakeWorkMode.Full;
                e.RecoveryAuthority = TerrainRuntimeFaultRecoveryAuthority.UnifiedBake;
                e.Description =
                    "Surface-only corruption must rebuild Surface without regenerating Height or Collision.";
                return e;

            case TerrainRuntimeFaultRecoveryScenario.MissingSurfaceManifest:
                e.SurfaceMode = TerrainRuntimeBakeWorkMode.Full;
                e.AddressablesConfigurationRequired = true;
                e.RecoveryAuthority = TerrainRuntimeFaultRecoveryAuthority.UnifiedBake;
                e.Description =
                    "Missing Surface topology requires Full Surface generation and Addressables reconciliation.";
                return e;

            case TerrainRuntimeFaultRecoveryScenario.MissingCollisionMesh:
            case TerrainRuntimeFaultRecoveryScenario.InvalidCollisionMetadata:
                e.CollisionMode = TerrainRuntimeBakeWorkMode.Full;
                e.RecoveryAuthority = TerrainRuntimeFaultRecoveryAuthority.UnifiedBake;
                e.Description =
                    "Collision-only corruption must rebuild Collision without regenerating Height or Surface.";
                return e;

            case TerrainRuntimeFaultRecoveryScenario.MissingPreparedCollisionManifest:
            case TerrainRuntimeFaultRecoveryScenario.InvalidPreparedCollisionManifest:
            case TerrainRuntimeFaultRecoveryScenario.MissingHeightAddressableEntry:
            case TerrainRuntimeFaultRecoveryScenario.MissingSurfaceAddressableEntry:
            case TerrainRuntimeFaultRecoveryScenario.MissingCollisionAddressableEntry:
            case TerrainRuntimeFaultRecoveryScenario.IncorrectAddressableAddress:
            case TerrainRuntimeFaultRecoveryScenario.MissingAddressableLabel:
            case TerrainRuntimeFaultRecoveryScenario.IncorrectAddressableLabel:
            case TerrainRuntimeFaultRecoveryScenario.MissingAddressablesGroup:
            case TerrainRuntimeFaultRecoveryScenario.MissingOrInvalidAddressablesSchema:
            case TerrainRuntimeFaultRecoveryScenario.MissingCollisionMarker:
            case TerrainRuntimeFaultRecoveryScenario.CollisionMarkerWrongMesh:
                e.AddressablesConfigurationRequired = true;
                e.RecoveryAuthority =
                    TerrainRuntimeFaultRecoveryAuthority.AddressablesConfigureAndBuild;
                e.Description =
                    "Addressables-only structural damage must require ConfigureAndBuild without regenerating valid terrain datasets.";
                return e;

            case TerrainRuntimeFaultRecoveryScenario.MissingWorldRoot:
            case TerrainRuntimeFaultRecoveryScenario.MissingClipmapRoot:
            case TerrainRuntimeFaultRecoveryScenario.DuplicateWorldRoot:
            case TerrainRuntimeFaultRecoveryScenario.DuplicateClipmapRoot:
            case TerrainRuntimeFaultRecoveryScenario.MissingTerrainClipmapBoundsController:
            case TerrainRuntimeFaultRecoveryScenario.DuplicateTerrainClipmapBoundsController:
            case TerrainRuntimeFaultRecoveryScenario.MissingTerrainHeightmapStreamer:
            case TerrainRuntimeFaultRecoveryScenario.DuplicateTerrainHeightmapStreamer:
            case TerrainRuntimeFaultRecoveryScenario.MissingCollisionRoot:
            case TerrainRuntimeFaultRecoveryScenario.MissingTerrainCollisionStreamer:
            case TerrainRuntimeFaultRecoveryScenario.DuplicateTerrainCollisionStreamer:
            case TerrainRuntimeFaultRecoveryScenario.MissingTerrainCollisionColliderPool:
            case TerrainRuntimeFaultRecoveryScenario.DuplicateTerrainCollisionColliderPool:
                e.HierarchyReady = false;
                e.RecoveryAuthority =
                    TerrainRuntimeFaultRecoveryAuthority.HierarchyRepair;
                e.Description =
                    "Structural hierarchy damage is Error / Incomplete and must be repaired only by Setup / Repair World Hierarchy.";
                return e;

            default:
                return null;
        }
    }
}

public sealed class TerrainRuntimeFaultRecoveryValidationResult
{
    public TerrainRuntimeFaultRecoveryScenario Scenario { get; internal set; }
    public TerrainRuntimeFaultRecoveryValidationOutcome Outcome { get; internal set; }
    public TerrainRuntimeFaultExpectation Expectation { get; internal set; }

    public TerrainRuntimeReadinessResult Baseline { get; internal set; }
    public TerrainRuntimeReadinessResult Damaged { get; internal set; }
    public TerrainRuntimeReadinessResult Final { get; internal set; }

    public string FaultDescription { get; internal set; }
    public string TargetPath { get; internal set; }
    public string TargetGuid { get; internal set; }

    public bool FaultDetected { get; internal set; }
    public bool ClassificationMatched { get; internal set; }

    public TerrainRuntimeSceneSynchronizationResult Package04Result { get; internal set; }
    public bool Package04RepairRequiredVerified { get; internal set; }
    public bool Package04StructuralMutationDetected { get; internal set; }

    public TerrainRuntimeBakeValidationResult BakeValidationResult { get; internal set; }

    public bool RecoveryStarted { get; internal set; }
    public bool RecoveryCompleted { get; internal set; }
    public bool CleanupAttempted { get; internal set; }
    public bool CleanupSucceeded { get; internal set; }

    public string ErrorMessage { get; internal set; }
    public string SummaryMessage { get; internal set; }

    public bool Passed =>
        Outcome == TerrainRuntimeFaultRecoveryValidationOutcome.Passed
        || Outcome == TerrainRuntimeFaultRecoveryValidationOutcome.PassedWithWarnings;

    public string BuildDiagnosticReport()
    {
        StringBuilder b = new StringBuilder();

        b.AppendLine("WorldMeshes Runtime Fault Recovery Validation");
        b.AppendLine("Scenario: " + Scenario);
        b.AppendLine("Outcome: " + Outcome);

        AppendReadiness(b, "Healthy Baseline", Baseline);

        if (!string.IsNullOrEmpty(FaultDescription))
        {
            b.AppendLine();
            b.AppendLine("Fault:");
            b.AppendLine("  " + FaultDescription);

            if (!string.IsNullOrEmpty(TargetPath))
            {
                b.AppendLine("  Asset: " + TargetPath);
            }

            if (!string.IsNullOrEmpty(TargetGuid))
            {
                b.AppendLine("  GUID: " + TargetGuid);
            }
        }

        AppendReadiness(b, "Damaged State", Damaged);

        if (Damaged != null)
        {
            b.AppendLine("  Fault Detected: " + FaultDetected);
            b.AppendLine("  Recovery Classification Matched: " + ClassificationMatched);
        }

        if (Package04Result != null)
        {
            b.AppendLine();
            b.AppendLine("Package 04:");
            b.AppendLine("  Outcome: " + Package04Result.Outcome);
            b.AppendLine("  RepairRequired Verified: " + Package04RepairRequiredVerified);
            b.AppendLine("  Structural Mutation Detected: " + Package04StructuralMutationDetected);
        }

        if (RecoveryStarted)
        {
            b.AppendLine();
            b.AppendLine("Recovery:");
            b.AppendLine("  Started: " + RecoveryStarted);
            b.AppendLine("  Completed: " + RecoveryCompleted);

            if (BakeValidationResult != null)
            {
                b.AppendLine("  Package 10.1 Validation: " + BakeValidationResult.Outcome);

                if (BakeValidationResult.AddressablesValidation != null)
                {
                    TerrainRuntimeAddressablesStageValidationResult a =
                        BakeValidationResult.AddressablesValidation;

                    b.AppendLine("  Addressables Mode: " + a.ActualOperationMode);
                    b.AppendLine("  Entries Created: " + a.EntriesCreated);
                    b.AppendLine("  Addresses Updated: " + a.AddressesUpdated);
                    b.AppendLine("  Labels Updated: " + a.LabelsUpdated);
                    b.AppendLine("  Groups Created: " + a.GroupsCreated);
                    b.AppendLine("  Schemas Created / Changed: " + a.SchemasCreatedOrChanged);
                    b.AppendLine("  Collision Markers Regenerated: " + a.CollisionMarkersRegenerated);
                }
            }
        }

        AppendReadiness(b, "Final", Final);

        if (CleanupAttempted)
        {
            b.AppendLine();
            b.AppendLine("Cleanup:");
            b.AppendLine("  Attempted: True");
            b.AppendLine("  Succeeded: " + CleanupSucceeded);
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            b.AppendLine();
            b.AppendLine("Error: " + ErrorMessage);
        }

        if (!string.IsNullOrEmpty(SummaryMessage))
        {
            b.AppendLine();
            b.AppendLine("Summary: " + SummaryMessage);
        }

        string resultLabel =
            Passed
                ? "PASS"
                : Outcome ==
                    TerrainRuntimeFaultRecoveryValidationOutcome.FaultInjected
                    ? "PENDING RECOVERY"
                    : "FAIL";

        b.AppendLine();
        b.AppendLine("Result: " + resultLabel);

        return b.ToString();
    }

    private static void AppendReadiness(
        StringBuilder b,
        string heading,
        TerrainRuntimeReadinessResult r
    )
    {
        if (r == null)
        {
            return;
        }

        b.AppendLine();
        b.AppendLine(heading + ":");
        b.AppendLine("  Height: " + r.HeightStatus);
        b.AppendLine("  Surface: " + r.SurfaceStatus);
        b.AppendLine("  Collision: " + r.CollisionStatus);
        b.AppendLine(
            "  Height Integrity: " +
            IntegrityLabel(r.IntegrityAudit != null ? r.IntegrityAudit.Height : null)
        );
        b.AppendLine(
            "  Surface Integrity: " +
            IntegrityLabel(r.IntegrityAudit != null ? r.IntegrityAudit.Surface : null)
        );
        b.AppendLine(
            "  Collision Integrity: " +
            IntegrityLabel(r.IntegrityAudit != null ? r.IntegrityAudit.Collision : null)
        );
        b.AppendLine(
            "  Addressables: " +
            (
                r.AddressablesValidation != null && r.AddressablesValidation.IsValid
                    ? "Valid"
                    : "Needs Repair"
            )
        );
        b.AppendLine(
            "  Hierarchy: " +
            (
                r.HierarchyReadiness != null && r.HierarchyReadiness.IsReady
                    ? "Valid"
                    : "Needs Repair"
            )
        );
        b.AppendLine(
            "  Plan: " +
            (
                r.Plan == null
                    ? "Unavailable"
                    : r.Plan.IsBlocked
                        ? "Blocked"
                        : r.Plan.HasWork
                            ? TerrainRuntimeBakePipelineResult.BuildPlanSummary(r.Plan)
                            : "No Work"
            )
        );
        b.AppendLine("  Runtime Ready: " + r.IsReady);
    }

    private static string IntegrityLabel(
        TerrainRuntimeGeneratedDataIntegrityResult r
    )
    {
        return r != null && r.IsValid
            ? "Valid"
            : "Invalid";
    }
}

[Serializable]
internal sealed class TerrainRuntimeFaultRecoveryCheckpoint
{
    public int scenario;
    public int authority;
    public string runId;
    public string quarantineDirectory;
    public string targetPath;
    public string backupAssetPath;
    public string backupMetaPath;
    public string renamedGroupOriginalName;
    public string renamedGroupFaultName;
    public string addressableGuid;
    public string originalGroupName;
    public string originalAddress;
    public string originalLabels;
    public bool registeredFaultLabel;
    public string faultLabel;
}

internal sealed class TerrainRuntimeFaultInjectionState
{
    public TerrainRuntimeFaultRecoveryScenario Scenario;
    public TerrainRuntimeFaultRecoveryAuthority Authority;
    public string RunId;
    public string QuarantineDirectory;

    public string FaultDescription;
    public string TargetPath;
    public string TargetGuid;
    public string BackupAssetPath;
    public string BackupMetaPath;

    public string AddressableGuid;
    public string OriginalGroupName;
    public string OriginalAddress;
    public List<string> OriginalLabels = new List<string>();

    public string RenamedGroupOriginalName;
    public string RenamedGroupFaultName;

    public string FaultLabel;
    public bool RegisteredFaultLabel;

    public BundledAssetGroupSchema.BundlePackingMode OriginalBundleMode;
    public bool OriginalIncludeAddressInCatalog;
}

public static class TerrainRuntimeFaultInjectionUtility
{
    public const string ValidationRoot =
        "Library/WorldMeshes/Validation/FaultRecovery";

    private const string SessionFileName =
        "session.json";

    private const string ValidationRootMarkerName =
        "__WorldMeshesFaultRecoveryValidationRoot";

    public static bool HasOrphanedValidationSession()
    {
        return TryFindCheckpoint(out _, out _);
    }

    internal static bool TryInject(
        TerrainRuntimeFaultRecoveryScenario scenario,
        out TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        state = null;
        error = "";

        TerrainRuntimeFaultExpectation expectation =
            TerrainRuntimeFaultExpectation.ForScenario(scenario);

        if (expectation == null)
        {
            error = "No safe fault expectation exists for the selected scenario.";
            return false;
        }

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        if (worldSettings == null)
        {
            error = "WorldSettings is unavailable.";
            return false;
        }

        state =
            new TerrainRuntimeFaultInjectionState
            {
                Scenario = scenario,
                Authority = expectation.RecoveryAuthority,
                RunId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_ffff")
            };

        state.QuarantineDirectory =
            AbsoluteProjectPath(
                ValidationRoot + "/" + state.RunId
            );

        Directory.CreateDirectory(state.QuarantineDirectory);

        bool ok;

        try
        {
            if (IsGeneratedFileFault(scenario))
            {
                ok = InjectGeneratedFileFault(state, out error);
            }
            else if (IsMetadataFault(scenario))
            {
                ok = InjectMetadataFault(state, out error);
            }
            else if (IsAddressablesFault(scenario))
            {
                ok = InjectAddressablesFault(worldSettings, state, out error);
            }
            else if (IsHierarchyFault(scenario))
            {
                ok = InjectHierarchyFault(state, out error);
            }
            else
            {
                ok = false;
                error = "Selected fault has no injector.";
            }
        }
        catch (Exception exception)
        {
            ok = false;
            error =
                "Fault injection failed unexpectedly.\n\n" +
                exception.Message;
        }

        if (!ok)
        {
            TryRestore(state, out _);
            DeleteQuarantine(state);
            state = null;
            return false;
        }

        WriteCheckpoint(state);

        TerrainRuntimeIntegrityAuditUtility
            .InvalidateCachedAudit();

        AssetDatabase.Refresh();

        return true;
    }

    internal static bool PrepareForCanonicalRecovery(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        if (
            state == null
            || string.IsNullOrEmpty(state.TargetPath)
            || string.IsNullOrEmpty(state.BackupMetaPath)
            || !File.Exists(state.BackupMetaPath)
        )
        {
            return true;
        }

        string targetAbsolute =
            AbsoluteProjectPath(state.TargetPath);

        /*
         * Metadata faults still have their asset bytes, so only missing-file
         * scenarios need orphan-meta restoration before regeneration.
         */
        if (File.Exists(targetAbsolute))
        {
            return true;
        }

        string targetMeta = targetAbsolute + ".meta";

        if (File.Exists(targetMeta))
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(targetAbsolute)
            );

            File.Copy(
                state.BackupMetaPath,
                targetMeta,
                true
            );

            return true;
        }
        catch (Exception exception)
        {
            error =
                "Could not restore the quarantined Unity .meta before canonical recovery.\n\n" +
                exception.Message;
            return false;
        }
    }

    internal static void CompleteSuccessfulRecovery(
        TerrainRuntimeFaultInjectionState state
    )
    {
        CleanupSuccessfulAddressablesArtifacts(state);
        DeleteQuarantine(state);

        TerrainRuntimeIntegrityAuditUtility
            .InvalidateCachedAudit();
    }

    internal static bool TryRestore(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        if (state == null)
        {
            return true;
        }

        bool ok = true;

        try
        {
            if (
                !string.IsNullOrEmpty(state.BackupAssetPath)
                && File.Exists(state.BackupAssetPath)
                && !string.IsNullOrEmpty(state.TargetPath)
            )
            {
                string targetAbsolute =
                    AbsoluteProjectPath(state.TargetPath);

                Directory.CreateDirectory(
                    Path.GetDirectoryName(targetAbsolute)
                );

                File.Copy(
                    state.BackupAssetPath,
                    targetAbsolute,
                    true
                );

                if (
                    !string.IsNullOrEmpty(state.BackupMetaPath)
                    && File.Exists(state.BackupMetaPath)
                )
                {
                    File.Copy(
                        state.BackupMetaPath,
                        targetAbsolute + ".meta",
                        true
                    );
                }
            }

            AssetDatabase.Refresh();

            if (
                state.Authority ==
                TerrainRuntimeFaultRecoveryAuthority.AddressablesConfigureAndBuild
            )
            {
                ok &=
                    RestoreAddressablesState(
                        state,
                        out string addressablesError
                    );

                if (!ok)
                {
                    error = addressablesError;
                }
            }
            else if (
                state.Authority ==
                TerrainRuntimeFaultRecoveryAuthority.HierarchyRepair
            )
            {
                WorldSettings worldSettings =
                    AssetDatabase.LoadAssetAtPath<WorldSettings>(
                        WorldMeshesPaths.WorldSettingsAssetPath
                    );

                if (worldSettings != null)
                {
                    TerrainWorldHierarchyGenerator
                        .SyncWorldHierarchy(worldSettings);
                }

                TerrainRuntimeHierarchyReadinessResult hierarchy =
                    TerrainRuntimeHierarchyReadinessUtility.Evaluate();

                if (hierarchy == null || !hierarchy.IsReady)
                {
                    ok = false;
                    error =
                        hierarchy != null
                            ? hierarchy.ErrorMessage
                            : "Hierarchy cleanup produced no readiness result.";
                }
            }

            AssetDatabase.SaveAssets();

            TerrainRuntimeIntegrityAuditUtility
                .InvalidateCachedAudit();
        }
        catch (Exception exception)
        {
            ok = false;
            error = exception.Message;
        }

        return ok;
    }

    public static bool RestoreOrphanedValidationSession(
        out string message
    )
    {
        message = "";

        if (!TryFindCheckpoint(out string checkpointPath, out TerrainRuntimeFaultRecoveryCheckpoint c))
        {
            message = "No incomplete Package 10.4 validation quarantine was found.";
            return true;
        }

        bool ok = true;

        try
        {
            if (
                !string.IsNullOrEmpty(c.backupAssetPath)
                && File.Exists(c.backupAssetPath)
                && !string.IsNullOrEmpty(c.targetPath)
            )
            {
                string targetAbsolute =
                    AbsoluteProjectPath(c.targetPath);

                Directory.CreateDirectory(
                    Path.GetDirectoryName(targetAbsolute)
                );

                File.Copy(c.backupAssetPath, targetAbsolute, true);

                if (
                    !string.IsNullOrEmpty(c.backupMetaPath)
                    && File.Exists(c.backupMetaPath)
                )
                {
                    File.Copy(
                        c.backupMetaPath,
                        targetAbsolute + ".meta",
                        true
                    );
                }
            }

            AssetDatabase.Refresh();

            WorldSettings worldSettings =
                AssetDatabase.LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths.WorldSettingsAssetPath
                );

            TerrainRuntimeFaultRecoveryAuthority authority =
                (TerrainRuntimeFaultRecoveryAuthority)c.authority;

            if (
                authority ==
                TerrainRuntimeFaultRecoveryAuthority.AddressablesConfigureAndBuild
            )
            {
                ok &=
                    TerrainRuntimeAddressablesUtility
                        .ReconfigureAllRuntimeAddressables(
                            worldSettings
                        );

                if (ok)
                {
                    ok &=
                        TerrainRuntimeAddressablesUtility
                            .RebuildAddressablesContentAndAcknowledge(
                                worldSettings
                            );
                }

                CleanupOrphanedAddressablesArtifacts(c);
            }
            else if (
                authority ==
                TerrainRuntimeFaultRecoveryAuthority.HierarchyRepair
            )
            {
                TerrainWorldHierarchyGenerator
                    .SyncWorldHierarchy(worldSettings);
            }

            TerrainRuntimeIntegrityAuditUtility
                .InvalidateCachedAudit();

            if (ok)
            {
                string directory =
                    Path.GetDirectoryName(checkpointPath);

                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }

                message =
                    "Incomplete Package 10.4 validation state was restored/reconciled.";
            }
            else
            {
                message =
                    "Package 10.4 orphan cleanup could not complete. Restore Runtime Ready before continuing.";
            }
        }
        catch (Exception exception)
        {
            ok = false;
            message =
                "Package 10.4 orphan cleanup failed.\n\n" +
                exception.Message;
        }

        return ok;
    }

    private static bool InjectGeneratedFileFault(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        switch (state.Scenario)
        {
            case TerrainRuntimeFaultRecoveryScenario.MissingHeightTexture:
                state.TargetPath =
                    TerrainRuntimeHeightAssetUtility.GetHeightTilePath(0, 0);
                state.FaultDescription =
                    "Removed generated Height Texture2D at coordinate (0, 0).";
                break;

            case TerrainRuntimeFaultRecoveryScenario.MissingSurfaceTexture:
                state.TargetPath =
                    TerrainRuntimeSurfaceMaskAssetUtility.GetSurfaceTilePath(0, 0);
                state.FaultDescription =
                    "Removed generated Surface Texture2D at coordinate (0, 0).";
                break;

            case TerrainRuntimeFaultRecoveryScenario.MissingCollisionMesh:
                state.TargetPath =
                    TerrainCollisionMeshGenerator.GetCollisionMeshPath(0, 0);
                state.FaultDescription =
                    "Removed generated Collision Mesh at coordinate (0, 0).";
                break;

            case TerrainRuntimeFaultRecoveryScenario.MissingHeightManifest:
                state.TargetPath =
                    TerrainRuntimeHeightAssetUtility.HeightmapManifestPath;
                state.FaultDescription =
                    "Removed runtime Height manifest.";
                break;

            case TerrainRuntimeFaultRecoveryScenario.MissingSurfaceManifest:
                state.TargetPath =
                    TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath;
                state.FaultDescription =
                    "Removed runtime Surface manifest.";
                break;

            case TerrainRuntimeFaultRecoveryScenario.MissingPreparedCollisionManifest:
                state.TargetPath =
                    WorldMeshesPaths.CollisionManifestAssetPath;
                state.FaultDescription =
                    "Removed prepared runtime Collision manifest.";
                break;

            case TerrainRuntimeFaultRecoveryScenario.MissingCollisionMarker:
                state.TargetPath =
                    TerrainCollisionBakeMarkerUtility.GetBakeMarkerAssetPath(0, 0);
                state.FaultDescription =
                    "Removed collision bake-marker prefab region (0, 0).";
                break;

            default:
                error = "Selected scenario is not a generated file-removal fault.";
                return false;
        }

        if (!BackupAsset(state, state.TargetPath, out error))
        {
            return false;
        }

        string absolute =
            AbsoluteProjectPath(state.TargetPath);

        if (!File.Exists(absolute))
        {
            error =
                "Fault target does not exist:\n" +
                state.TargetPath;
            return false;
        }

        /*
         * Remove only asset bytes. The .meta remains when Unity permits it;
         * PrepareForCanonicalRecovery restores the quarantined .meta if Unity
         * deletes that orphan during refresh.
         */
        File.Delete(absolute);

        return true;
    }

    private static bool InjectMetadataFault(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        switch (state.Scenario)
        {
            case TerrainRuntimeFaultRecoveryScenario.IncompleteHeightManifest:
            case TerrainRuntimeFaultRecoveryScenario.InvalidHeightMetadata:
            {
                state.TargetPath =
                    TerrainRuntimeHeightAssetUtility.HeightmapManifestPath;

                if (!BackupAsset(state, state.TargetPath, out error))
                {
                    return false;
                }

                TerrainHeightmapManifest manifest =
                    AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                        state.TargetPath
                    );

                if (manifest == null)
                {
                    error = "Runtime Height manifest is unavailable.";
                    return false;
                }

                if (
                    state.Scenario ==
                    TerrainRuntimeFaultRecoveryScenario.IncompleteHeightManifest
                )
                {
                    manifest.isComplete = false;
                    state.FaultDescription =
                        "Set runtime Height manifest isComplete=false.";
                }
                else
                {
                    manifest.compilerVersion =
                        TerrainGenerationStateUtility.RuntimeHeightCompilerVersion + 1000;
                    state.FaultDescription =
                        "Set runtime Height manifest compilerVersion to an incompatible validation value.";
                }

                EditorUtility.SetDirty(manifest);
                AssetDatabase.SaveAssetIfDirty(manifest);
                return true;
            }

            case TerrainRuntimeFaultRecoveryScenario.IncompleteSurfaceManifest:
            case TerrainRuntimeFaultRecoveryScenario.InvalidSurfaceMetadata:
            {
                state.TargetPath =
                    TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath;

                if (!BackupAsset(state, state.TargetPath, out error))
                {
                    return false;
                }

                TerrainSurfaceMaskManifest manifest =
                    AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                        state.TargetPath
                    );

                if (manifest == null)
                {
                    error = "Runtime Surface manifest is unavailable.";
                    return false;
                }

                if (
                    state.Scenario ==
                    TerrainRuntimeFaultRecoveryScenario.IncompleteSurfaceManifest
                )
                {
                    manifest.isComplete = false;
                    state.FaultDescription =
                        "Set runtime Surface manifest isComplete=false.";
                }
                else
                {
                    manifest.compilerVersion =
                        TerrainSurfaceMaskManifest.CurrentCompilerVersion + 1000;
                    state.FaultDescription =
                        "Set runtime Surface manifest compilerVersion to an incompatible validation value.";
                }

                EditorUtility.SetDirty(manifest);
                AssetDatabase.SaveAssetIfDirty(manifest);
                return true;
            }

            case TerrainRuntimeFaultRecoveryScenario.InvalidCollisionMetadata:
            {
                state.TargetPath =
                    WorldMeshesPaths.WorldSettingsAssetPath;

                if (!BackupAsset(state, state.TargetPath, out error))
                {
                    return false;
                }

                WorldSettings settings =
                    AssetDatabase.LoadAssetAtPath<WorldSettings>(
                        state.TargetPath
                    );

                if (settings == null)
                {
                    error =
                        "WorldSettings is unavailable for Collision metadata fault injection.";
                    return false;
                }

                settings.lastGeneratedCollisionSignature =
                    "__WorldMeshesFaultRecovery_InvalidCollisionSignature";

                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);

                state.FaultDescription =
                    "Replaced generated Collision settings signature with an invalid validation value.";
                return true;
            }

            case TerrainRuntimeFaultRecoveryScenario.InvalidPreparedCollisionManifest:
            {
                state.TargetPath =
                    WorldMeshesPaths.CollisionManifestAssetPath;

                if (!BackupAsset(state, state.TargetPath, out error))
                {
                    return false;
                }

                TerrainCollisionManifest manifest =
                    AssetDatabase.LoadAssetAtPath<TerrainCollisionManifest>(
                        state.TargetPath
                    );

                if (manifest == null)
                {
                    error =
                        "Prepared Collision manifest is unavailable.";
                    return false;
                }

                manifest.isComplete = false;
                EditorUtility.SetDirty(manifest);
                AssetDatabase.SaveAssetIfDirty(manifest);

                state.FaultDescription =
                    "Set prepared Collision manifest isComplete=false.";
                return true;
            }

            case TerrainRuntimeFaultRecoveryScenario.CollisionMarkerWrongMesh:
                return InjectWrongCollisionMarkerMesh(state, out error);

            default:
                error = "Selected scenario is not a metadata fault.";
                return false;
        }
    }

    private static bool InjectAddressablesFault(
        WorldSettings worldSettings,
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        if (
            state.Scenario ==
            TerrainRuntimeFaultRecoveryScenario.MissingPreparedCollisionManifest
            || state.Scenario ==
            TerrainRuntimeFaultRecoveryScenario.MissingCollisionMarker
        )
        {
            return InjectGeneratedFileFault(state, out error);
        }

        if (
            state.Scenario ==
            TerrainRuntimeFaultRecoveryScenario.InvalidPreparedCollisionManifest
        )
        {
            return InjectMetadataFault(state, out error);
        }

        if (
            state.Scenario ==
            TerrainRuntimeFaultRecoveryScenario.CollisionMarkerWrongMesh
        )
        {
            return InjectWrongCollisionMarkerMesh(state, out error);
        }

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(false);

        if (settings == null)
        {
            error = "AddressableAssetSettings is unavailable.";
            return false;
        }

        if (
            state.Scenario ==
            TerrainRuntimeFaultRecoveryScenario.MissingAddressablesGroup
        )
        {
            AddressableAssetGroup group =
                settings.FindGroup(
                    TerrainHeightmapAddressablesUtility
                        .HeightmapAddressablesGroupName
                );

            if (group == null)
            {
                error = "Height Addressables group is unavailable.";
                return false;
            }

            state.RenamedGroupOriginalName = group.name;
            state.RenamedGroupFaultName =
                group.name + "__FaultRecoveryValidation";

            group.name = state.RenamedGroupFaultName;
            EditorUtility.SetDirty(group);
            AssetDatabase.SaveAssets();

            state.FaultDescription =
                "Renamed the required Height Addressables group so the canonical group is missing.";
            return true;
        }

        if (
            state.Scenario ==
            TerrainRuntimeFaultRecoveryScenario.MissingOrInvalidAddressablesSchema
        )
        {
            AddressableAssetGroup group =
                settings.FindGroup(
                    TerrainHeightmapAddressablesUtility
                        .HeightmapAddressablesGroupName
                );

            if (group == null)
            {
                error = "Height Addressables group is unavailable.";
                return false;
            }

            BundledAssetGroupSchema schema =
                group.GetSchema<BundledAssetGroupSchema>();

            if (schema == null)
            {
                error =
                    "Height Addressables BundledAssetGroupSchema is already missing.";
                return false;
            }

            state.OriginalBundleMode = schema.BundleMode;
            state.OriginalIncludeAddressInCatalog =
                schema.IncludeAddressInCatalog;

            schema.BundleMode =
                BundledAssetGroupSchema.BundlePackingMode.PackTogether;

            schema.IncludeAddressInCatalog = false;

            EditorUtility.SetDirty(schema);
            AssetDatabase.SaveAssets();

            state.FaultDescription =
                "Changed required Height Addressables schema to incompatible packing/catalog settings.";
            return true;
        }

        if (
            !TryResolveAddressableTarget(
                state.Scenario,
                settings,
                out AddressableAssetGroup targetGroup,
                out AddressableAssetEntry entry,
                out string expectedLabel,
                out error
            )
        )
        {
            return false;
        }

        state.AddressableGuid = entry.guid;
        state.OriginalGroupName =
            entry.parentGroup != null
                ? entry.parentGroup.name
                : "";
        state.OriginalAddress = entry.address;
        state.OriginalLabels =
            new List<string>(entry.labels);
        state.TargetGuid = entry.guid;
        state.TargetPath =
            AssetDatabase.GUIDToAssetPath(entry.guid);

        switch (state.Scenario)
        {
            case TerrainRuntimeFaultRecoveryScenario.MissingHeightAddressableEntry:
            case TerrainRuntimeFaultRecoveryScenario.MissingSurfaceAddressableEntry:
            case TerrainRuntimeFaultRecoveryScenario.MissingCollisionAddressableEntry:
                targetGroup.RemoveAssetEntry(entry, true);
                state.FaultDescription =
                    "Removed required Addressables entry for " +
                    state.TargetPath + ".";
                break;

            case TerrainRuntimeFaultRecoveryScenario.IncorrectAddressableAddress:
                entry.SetAddress(
                    "__WorldMeshesFaultRecovery_InvalidAddress",
                    true
                );
                state.FaultDescription =
                    "Assigned an incorrect runtime address to " +
                    state.TargetPath + ".";
                break;

            case TerrainRuntimeFaultRecoveryScenario.MissingAddressableLabel:
                if (string.IsNullOrEmpty(expectedLabel))
                {
                    error =
                        "The selected Collision entry has no required region label.";
                    return false;
                }

                entry.SetLabel(
                    expectedLabel,
                    false,
                    false,
                    true
                );
                state.FaultDescription =
                    "Removed required Collision region label from " +
                    state.TargetPath + ".";
                break;

            case TerrainRuntimeFaultRecoveryScenario.IncorrectAddressableLabel:
                if (string.IsNullOrEmpty(expectedLabel))
                {
                    error =
                        "The selected Collision entry has no required region label.";
                    return false;
                }

                entry.SetLabel(
                    expectedLabel,
                    false,
                    false,
                    true
                );

                state.FaultLabel =
                    "WorldMeshesFaultRecovery_InvalidLabel";

                state.RegisteredFaultLabel =
                    !new HashSet<string>(
                        settings.GetLabels()
                    ).Contains(state.FaultLabel);

                settings.AddLabel(
                    state.FaultLabel,
                    true
                );

                entry.SetLabel(
                    state.FaultLabel,
                    true,
                    false,
                    true
                );

                state.FaultDescription =
                    "Replaced the required Collision region label with a validation-only incorrect label.";
                break;

            default:
                error = "Selected Addressables fault is unsupported.";
                return false;
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        return true;
    }

    private static bool TryResolveAddressableTarget(
        TerrainRuntimeFaultRecoveryScenario scenario,
        AddressableAssetSettings settings,
        out AddressableAssetGroup group,
        out AddressableAssetEntry entry,
        out string expectedLabel,
        out string error
    )
    {
        group = null;
        entry = null;
        expectedLabel = "";
        error = "";

        string path;

        if (
            scenario ==
            TerrainRuntimeFaultRecoveryScenario.MissingSurfaceAddressableEntry
        )
        {
            group =
                settings.FindGroup(
                    TerrainSurfaceMaskAddressablesUtility
                        .SurfaceMaskAddressablesGroupName
                );

            path =
                TerrainRuntimeSurfaceMaskAssetUtility
                    .GetSurfaceTilePath(0, 0);
        }
        else if (
            scenario ==
                TerrainRuntimeFaultRecoveryScenario.MissingCollisionAddressableEntry
            || scenario ==
                TerrainRuntimeFaultRecoveryScenario.MissingAddressableLabel
            || scenario ==
                TerrainRuntimeFaultRecoveryScenario.IncorrectAddressableLabel
        )
        {
            group =
                settings.FindGroup(
                    TerrainCollisionAddressablesUtility
                        .CollisionAddressablesGroupName
                );

            path =
                TerrainCollisionMeshGenerator
                    .GetCollisionMeshPath(0, 0);

            TerrainCollisionManifest manifest =
                AssetDatabase.LoadAssetAtPath<TerrainCollisionManifest>(
                    WorldMeshesPaths.CollisionManifestAssetPath
                );

            expectedLabel =
                manifest != null
                    ? manifest.GetCollisionRegionLabel(0, 0)
                    : "";
        }
        else
        {
            group =
                settings.FindGroup(
                    TerrainHeightmapAddressablesUtility
                        .HeightmapAddressablesGroupName
                );

            path =
                TerrainRuntimeHeightAssetUtility
                    .GetHeightTilePath(0, 0);
        }

        if (group == null)
        {
            error =
                "Required Addressables group is unavailable.";
            return false;
        }

        string guid =
            AssetDatabase.AssetPathToGUID(path);

        if (string.IsNullOrEmpty(guid))
        {
            error =
                "Could not resolve fault-target asset GUID:\n" +
                path;
            return false;
        }

        entry =
            settings.FindAssetEntry(guid);

        if (entry == null)
        {
            error =
                "Required Addressables target entry is already missing:\n" +
                path;
            return false;
        }

        return true;
    }

    private static bool InjectWrongCollisionMarkerMesh(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        if (
            worldSettings == null
            || Mathf.Max(1, worldSettings.gridWidth) *
               Mathf.Max(1, worldSettings.gridHeight) < 2
        )
        {
            error =
                "At least two Collision Meshes are required for CollisionMarkerWrongMesh.";
            return false;
        }

        state.TargetPath =
            TerrainCollisionBakeMarkerUtility
                .GetBakeMarkerAssetPath(0, 0);

        if (!BackupAsset(state, state.TargetPath, out error))
        {
            return false;
        }

        GameObject contents =
            PrefabUtility.LoadPrefabContents(
                state.TargetPath
            );

        if (contents == null)
        {
            error =
                "Could not load collision marker prefab contents.";
            return false;
        }

        try
        {
            MeshCollider[] colliders =
                contents.GetComponentsInChildren<MeshCollider>(
                    true
                );

            if (colliders.Length == 0)
            {
                error =
                    "Collision marker contains no MeshCollider.";
                return false;
            }

            int alternativeX =
                Mathf.Max(1, worldSettings.gridWidth) > 1
                    ? 1
                    : 0;

            int alternativeZ =
                alternativeX == 0
                    ? 1
                    : 0;

            Mesh wrongMesh =
                AssetDatabase.LoadAssetAtPath<Mesh>(
                    TerrainCollisionMeshGenerator
                        .GetCollisionMeshPath(
                            alternativeX,
                            alternativeZ
                        )
                );

            if (wrongMesh == null)
            {
                error =
                    "Alternative generated Collision Mesh could not be loaded.";
                return false;
            }

            colliders[0].sharedMesh = wrongMesh;

            GameObject saved =
                PrefabUtility.SaveAsPrefabAsset(
                    contents,
                    state.TargetPath
                );

            if (saved == null)
            {
                error =
                    "Could not save deliberately corrupted collision marker.";
                return false;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        AssetDatabase.SaveAssets();

        state.FaultDescription =
            "Changed one collision marker MeshCollider to reference the wrong generated Collision Mesh GUID.";

        return true;
    }

    private static bool InjectHierarchyFault(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        if (
            !TerrainWorldSceneUtility.TryGetActiveScene(
                out Scene scene,
                out error
            )
        )
        {
            return false;
        }

        if (
            !TerrainWorldSceneUtility.TryFindWorldRoot(
                scene,
                out Transform worldRoot,
                out error
            )
            || worldRoot == null
        )
        {
            if (string.IsNullOrEmpty(error))
            {
                error = "WorldRoot is unavailable.";
            }

            return false;
        }

        switch (state.Scenario)
        {
            case TerrainRuntimeFaultRecoveryScenario.MissingWorldRoot:
                UnityEngine.Object.DestroyImmediate(
                    worldRoot.gameObject
                );
                state.FaultDescription =
                    "Removed generated WorldRoot.";
                return true;

            case TerrainRuntimeFaultRecoveryScenario.DuplicateWorldRoot:
            {
                GameObject duplicate =
                    new GameObject(
                        TerrainWorldSceneUtility.WorldRootName
                    );

                new GameObject(
                    ValidationRootMarkerName
                ).transform.SetParent(
                    duplicate.transform,
                    false
                );

                state.FaultDescription =
                    "Created duplicate generated WorldRoot.";
                return true;
            }
        }

        if (
            !TerrainWorldSceneUtility.TryFindClipmapRoot(
                scene,
                out Transform clipmapRoot,
                out error
            )
            || clipmapRoot == null
        )
        {
            if (string.IsNullOrEmpty(error))
            {
                error = "Clipmap root is unavailable.";
            }

            return false;
        }

        if (
            !TerrainWorldSceneUtility.TryFindCollisionRoot(
                scene,
                out Transform collisionRoot,
                out error
            )
            || collisionRoot == null
        )
        {
            if (string.IsNullOrEmpty(error))
            {
                error = "Collision root is unavailable.";
            }

            return false;
        }

        switch (state.Scenario)
        {
            case TerrainRuntimeFaultRecoveryScenario.MissingClipmapRoot:
                UnityEngine.Object.DestroyImmediate(
                    clipmapRoot.gameObject
                );
                state.FaultDescription =
                    "Removed generated WorldRoot/Clipmap.";
                return true;

            case TerrainRuntimeFaultRecoveryScenario.DuplicateClipmapRoot:
                CreateDuplicateRoot(
                    worldRoot,
                    TerrainWorldSceneUtility.ClipmapRootName
                );
                state.FaultDescription =
                    "Created duplicate Clipmap root.";
                return true;

            case TerrainRuntimeFaultRecoveryScenario.MissingTerrainClipmapBoundsController:
                return DestroyOne<TerrainClipmapBoundsController>(
                    clipmapRoot.gameObject,
                    state,
                    "Removed TerrainClipmapBoundsController.",
                    out error
                );

            case TerrainRuntimeFaultRecoveryScenario.DuplicateTerrainClipmapBoundsController:
                clipmapRoot.gameObject
                    .AddComponent<TerrainClipmapBoundsController>();
                state.FaultDescription =
                    "Added duplicate TerrainClipmapBoundsController.";
                return true;

            case TerrainRuntimeFaultRecoveryScenario.MissingTerrainHeightmapStreamer:
                return DestroyOne<TerrainHeightmapStreamer>(
                    clipmapRoot.gameObject,
                    state,
                    "Removed TerrainHeightmapStreamer.",
                    out error
                );

            case TerrainRuntimeFaultRecoveryScenario.DuplicateTerrainHeightmapStreamer:
                clipmapRoot.gameObject
                    .AddComponent<TerrainHeightmapStreamer>();
                state.FaultDescription =
                    "Added duplicate TerrainHeightmapStreamer.";
                return true;

            case TerrainRuntimeFaultRecoveryScenario.MissingCollisionRoot:
                UnityEngine.Object.DestroyImmediate(
                    collisionRoot.gameObject
                );
                state.FaultDescription =
                    "Removed generated WorldRoot/Collision.";
                return true;

            case TerrainRuntimeFaultRecoveryScenario.MissingTerrainCollisionStreamer:
                return DestroyOne<TerrainCollisionStreamer>(
                    collisionRoot.gameObject,
                    state,
                    "Removed TerrainCollisionStreamer.",
                    out error
                );

            case TerrainRuntimeFaultRecoveryScenario.DuplicateTerrainCollisionStreamer:
                collisionRoot.gameObject
                    .AddComponent<TerrainCollisionStreamer>();
                state.FaultDescription =
                    "Added duplicate TerrainCollisionStreamer.";
                return true;

            case TerrainRuntimeFaultRecoveryScenario.MissingTerrainCollisionColliderPool:
                return DestroyOne<TerrainCollisionColliderPool>(
                    collisionRoot.gameObject,
                    state,
                    "Removed TerrainCollisionColliderPool.",
                    out error
                );

            case TerrainRuntimeFaultRecoveryScenario.DuplicateTerrainCollisionColliderPool:
                collisionRoot.gameObject
                    .AddComponent<TerrainCollisionColliderPool>();
                state.FaultDescription =
                    "Added duplicate TerrainCollisionColliderPool.";
                return true;

            default:
                error =
                    "Selected hierarchy fault is unsupported.";
                return false;
        }
    }

    private static void CreateDuplicateRoot(
        Transform parent,
        string name
    )
    {
        GameObject duplicate =
            new GameObject(name);

        duplicate.transform.SetParent(
            parent,
            false
        );

        new GameObject(
            ValidationRootMarkerName
        ).transform.SetParent(
            duplicate.transform,
            false
        );
    }

    private static bool DestroyOne<T>(
        GameObject gameObject,
        TerrainRuntimeFaultInjectionState state,
        string description,
        out string error
    )
        where T : Component
    {
        error = "";

        T component =
            gameObject.GetComponent<T>();

        if (component == null)
        {
            error =
                typeof(T).Name +
                " is already missing.";
            return false;
        }

        UnityEngine.Object.DestroyImmediate(
            component
        );

        state.FaultDescription =
            description;

        return true;
    }

    private static bool BackupAsset(
        TerrainRuntimeFaultInjectionState state,
        string assetPath,
        out string error
    )
    {
        error = "";

        string absolute =
            AbsoluteProjectPath(assetPath);

        if (!File.Exists(absolute))
        {
            error =
                "Fault target is already missing:\n" +
                assetPath;
            return false;
        }

        state.TargetGuid =
            AssetDatabase.AssetPathToGUID(assetPath);

        state.BackupAssetPath =
            Path.Combine(
                state.QuarantineDirectory,
                Path.GetFileName(absolute)
            );

        state.BackupMetaPath =
            state.BackupAssetPath + ".meta";

        File.Copy(
            absolute,
            state.BackupAssetPath,
            true
        );

        if (File.Exists(absolute + ".meta"))
        {
            File.Copy(
                absolute + ".meta",
                state.BackupMetaPath,
                true
            );
        }
        else
        {
            state.BackupMetaPath = "";
        }

        return true;
    }

    private static bool RestoreAddressablesState(
        TerrainRuntimeFaultInjectionState state,
        out string error
    )
    {
        error = "";

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(true);

        if (settings == null)
        {
            error =
                "AddressableAssetSettings is unavailable during cleanup.";
            return false;
        }

        if (
            !string.IsNullOrEmpty(
                state.RenamedGroupOriginalName
            )
        )
        {
            AddressableAssetGroup renamed =
                settings.FindGroup(
                    state.RenamedGroupFaultName
                );

            if (renamed != null)
            {
                renamed.name =
                    state.RenamedGroupOriginalName;
                EditorUtility.SetDirty(renamed);
            }
        }

        if (
            state.Scenario ==
            TerrainRuntimeFaultRecoveryScenario.MissingOrInvalidAddressablesSchema
        )
        {
            AddressableAssetGroup group =
                settings.FindGroup(
                    TerrainHeightmapAddressablesUtility
                        .HeightmapAddressablesGroupName
                );

            BundledAssetGroupSchema schema =
                group != null
                    ? group.GetSchema<BundledAssetGroupSchema>()
                    : null;

            if (schema != null)
            {
                schema.BundleMode =
                    state.OriginalBundleMode;

                schema.IncludeAddressInCatalog =
                    state.OriginalIncludeAddressInCatalog;

                EditorUtility.SetDirty(schema);
            }
        }

        if (!string.IsNullOrEmpty(state.AddressableGuid))
        {
            AddressableAssetGroup group =
                settings.FindGroup(
                    state.OriginalGroupName
                );

            if (group == null)
            {
                error =
                    "Original Addressables group is unavailable during cleanup: " +
                    state.OriginalGroupName;
                return false;
            }

            AddressableAssetEntry entry =
                settings.FindAssetEntry(
                    state.AddressableGuid
                );

            if (entry == null || entry.parentGroup != group)
            {
                entry =
                    settings.CreateOrMoveEntry(
                        state.AddressableGuid,
                        group,
                        false,
                        true
                    );
            }

            if (entry == null)
            {
                error =
                    "Could not restore Addressables entry " +
                    state.AddressableGuid + ".";
                return false;
            }

            entry.SetAddress(
                state.OriginalAddress,
                true
            );

            foreach (string label in new List<string>(entry.labels))
            {
                entry.SetLabel(
                    label,
                    false,
                    false,
                    true
                );
            }

            foreach (string label in state.OriginalLabels)
            {
                settings.AddLabel(label, true);
                entry.SetLabel(
                    label,
                    true,
                    false,
                    true
                );
            }
        }

        if (
            state.RegisteredFaultLabel
            && !string.IsNullOrEmpty(state.FaultLabel)
            && new HashSet<string>(
                settings.GetLabels()
            ).Contains(state.FaultLabel)
        )
        {
            settings.RemoveLabel(
                state.FaultLabel,
                true
            );
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        return true;
    }

    private static void CleanupSuccessfulAddressablesArtifacts(
        TerrainRuntimeFaultInjectionState state
    )
    {
        if (state == null)
        {
            return;
        }

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(false);

        if (settings == null)
        {
            return;
        }

        bool changed = false;

        if (
            !string.IsNullOrEmpty(
                state.RenamedGroupFaultName
            )
        )
        {
            AddressableAssetGroup group =
                settings.FindGroup(
                    state.RenamedGroupFaultName
                );

            if (group != null && group.entries.Count == 0)
            {
                settings.RemoveGroup(group);
                changed = true;
            }
        }

        if (
            state.RegisteredFaultLabel
            && !string.IsNullOrEmpty(state.FaultLabel)
            && new HashSet<string>(
                settings.GetLabels()
            ).Contains(state.FaultLabel)
        )
        {
            settings.RemoveLabel(
                state.FaultLabel,
                true
            );
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }

    private static void CleanupOrphanedAddressablesArtifacts(
        TerrainRuntimeFaultRecoveryCheckpoint c
    )
    {
        if (c == null)
        {
            return;
        }

        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(false);

        if (settings == null)
        {
            return;
        }

        bool changed = false;

        if (!string.IsNullOrEmpty(c.renamedGroupFaultName))
        {
            AddressableAssetGroup group =
                settings.FindGroup(
                    c.renamedGroupFaultName
                );

            if (group != null && group.entries.Count == 0)
            {
                settings.RemoveGroup(group);
                changed = true;
            }
        }

        if (
            c.registeredFaultLabel
            && !string.IsNullOrEmpty(c.faultLabel)
            && new HashSet<string>(
                settings.GetLabels()
            ).Contains(c.faultLabel)
        )
        {
            settings.RemoveLabel(
                c.faultLabel,
                true
            );
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }

    private static void WriteCheckpoint(
        TerrainRuntimeFaultInjectionState state
    )
    {
        TerrainRuntimeFaultRecoveryCheckpoint c =
            new TerrainRuntimeFaultRecoveryCheckpoint
            {
                scenario = (int)state.Scenario,
                authority = (int)state.Authority,
                runId = state.RunId,
                quarantineDirectory = state.QuarantineDirectory,
                targetPath = state.TargetPath ?? "",
                backupAssetPath = state.BackupAssetPath ?? "",
                backupMetaPath = state.BackupMetaPath ?? "",
                renamedGroupOriginalName =
                    state.RenamedGroupOriginalName ?? "",
                renamedGroupFaultName =
                    state.RenamedGroupFaultName ?? "",
                addressableGuid =
                    state.AddressableGuid ?? "",
                originalGroupName =
                    state.OriginalGroupName ?? "",
                originalAddress =
                    state.OriginalAddress ?? "",
                originalLabels =
                    string.Join("\n", state.OriginalLabels.ToArray()),
                registeredFaultLabel =
                    state.RegisteredFaultLabel,
                faultLabel =
                    state.FaultLabel ?? ""
            };

        File.WriteAllText(
            Path.Combine(
                state.QuarantineDirectory,
                SessionFileName
            ),
            JsonUtility.ToJson(c, true)
        );
    }

    private static bool TryFindCheckpoint(
        out string checkpointPath,
        out TerrainRuntimeFaultRecoveryCheckpoint checkpoint
    )
    {
        checkpointPath = "";
        checkpoint = null;

        string root =
            AbsoluteProjectPath(ValidationRoot);

        if (!Directory.Exists(root))
        {
            return false;
        }

        string[] files =
            Directory.GetFiles(
                root,
                SessionFileName,
                SearchOption.AllDirectories
            );

        if (files.Length == 0)
        {
            return false;
        }

        Array.Sort(files, StringComparer.Ordinal);
        checkpointPath = files[files.Length - 1];

        try
        {
            checkpoint =
                JsonUtility.FromJson<TerrainRuntimeFaultRecoveryCheckpoint>(
                    File.ReadAllText(checkpointPath)
                );
        }
        catch
        {
            checkpoint = null;
        }

        return checkpoint != null;
    }

    private static void DeleteQuarantine(
        TerrainRuntimeFaultInjectionState state
    )
    {
        if (
            state == null
            || string.IsNullOrEmpty(state.QuarantineDirectory)
        )
        {
            return;
        }

        try
        {
            if (Directory.Exists(state.QuarantineDirectory))
            {
                Directory.Delete(
                    state.QuarantineDirectory,
                    true
                );
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Could not delete Package 10.4 validation quarantine:\n" +
                exception.Message
            );
        }
    }

    private static string AbsoluteProjectPath(
        string projectRelativePath
    )
    {
        string projectRoot =
            Directory.GetParent(
                Application.dataPath
            ).FullName;

        return
            Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    projectRelativePath
                )
            );
    }

    private static bool IsGeneratedFileFault(
        TerrainRuntimeFaultRecoveryScenario s
    )
    {
        return
            s == TerrainRuntimeFaultRecoveryScenario.MissingHeightTexture
            || s == TerrainRuntimeFaultRecoveryScenario.MissingSurfaceTexture
            || s == TerrainRuntimeFaultRecoveryScenario.MissingCollisionMesh
            || s == TerrainRuntimeFaultRecoveryScenario.MissingHeightManifest
            || s == TerrainRuntimeFaultRecoveryScenario.MissingSurfaceManifest
            || s == TerrainRuntimeFaultRecoveryScenario.MissingPreparedCollisionManifest
            || s == TerrainRuntimeFaultRecoveryScenario.MissingCollisionMarker;
    }

    private static bool IsMetadataFault(
        TerrainRuntimeFaultRecoveryScenario s
    )
    {
        return
            s == TerrainRuntimeFaultRecoveryScenario.IncompleteHeightManifest
            || s == TerrainRuntimeFaultRecoveryScenario.InvalidHeightMetadata
            || s == TerrainRuntimeFaultRecoveryScenario.IncompleteSurfaceManifest
            || s == TerrainRuntimeFaultRecoveryScenario.InvalidSurfaceMetadata
            || s == TerrainRuntimeFaultRecoveryScenario.InvalidCollisionMetadata
            || s == TerrainRuntimeFaultRecoveryScenario.InvalidPreparedCollisionManifest
            || s == TerrainRuntimeFaultRecoveryScenario.CollisionMarkerWrongMesh;
    }

    private static bool IsAddressablesFault(
        TerrainRuntimeFaultRecoveryScenario s
    )
    {
        return
            s == TerrainRuntimeFaultRecoveryScenario.MissingPreparedCollisionManifest
            || s == TerrainRuntimeFaultRecoveryScenario.InvalidPreparedCollisionManifest
            || s == TerrainRuntimeFaultRecoveryScenario.MissingHeightAddressableEntry
            || s == TerrainRuntimeFaultRecoveryScenario.MissingSurfaceAddressableEntry
            || s == TerrainRuntimeFaultRecoveryScenario.MissingCollisionAddressableEntry
            || s == TerrainRuntimeFaultRecoveryScenario.IncorrectAddressableAddress
            || s == TerrainRuntimeFaultRecoveryScenario.MissingAddressableLabel
            || s == TerrainRuntimeFaultRecoveryScenario.IncorrectAddressableLabel
            || s == TerrainRuntimeFaultRecoveryScenario.MissingAddressablesGroup
            || s == TerrainRuntimeFaultRecoveryScenario.MissingOrInvalidAddressablesSchema
            || s == TerrainRuntimeFaultRecoveryScenario.MissingCollisionMarker
            || s == TerrainRuntimeFaultRecoveryScenario.CollisionMarkerWrongMesh;
    }

    private static bool IsHierarchyFault(
        TerrainRuntimeFaultRecoveryScenario s
    )
    {
        return s >= TerrainRuntimeFaultRecoveryScenario.MissingWorldRoot;
    }
}

public static class TerrainRuntimeFaultRecoveryScenarioRunner
{
    private static TerrainRuntimeFaultInjectionState activeState;
    private static TerrainRuntimeFaultRecoveryValidationResult lastResult;
    private static bool recoveryRunning;

    public static TerrainRuntimeFaultRecoveryValidationResult LastResult =>
        lastResult;

    public static bool HasActiveFault =>
        activeState != null;

    public static bool IsRunning =>
        HasActiveFault || recoveryRunning;

    public static bool IsRecoveryRunning =>
        recoveryRunning;

    public static TerrainRuntimeFaultRecoveryScenario ActiveScenario =>
        activeState != null
            ? activeState.Scenario
            : TerrainRuntimeFaultRecoveryScenario.None;

    public static TerrainRuntimeFaultRecoveryValidationResult InjectAndValidate(
        TerrainRuntimeFaultRecoveryScenario scenario
    )
    {
        if (IsRunning)
        {
            return Blocked(
                scenario,
                "A Package 10.4 fault/recovery scenario is already active."
            );
        }

        if (!CanStart(out string error))
        {
            lastResult =
                Blocked(
                    scenario,
                    error
                );
            return lastResult;
        }

        lastResult =
            new TerrainRuntimeFaultRecoveryValidationResult
            {
                Scenario = scenario,
                Outcome = TerrainRuntimeFaultRecoveryValidationOutcome.NotRun,
                Expectation =
                    TerrainRuntimeFaultExpectation.ForScenario(scenario),
                Baseline =
                    TerrainRuntimeReadinessUtility.Evaluate(true),
                ErrorMessage = "",
                SummaryMessage = ""
            };

        if (
            !TerrainRuntimeFaultInjectionUtility.TryInject(
                scenario,
                out activeState,
                out string injectionError
            )
        )
        {
            lastResult.Outcome =
                TerrainRuntimeFaultRecoveryValidationOutcome.Failed;
            lastResult.ErrorMessage =
                injectionError;
            lastResult.SummaryMessage =
                "The controlled fault could not be injected safely.";
            return lastResult;
        }

        lastResult.FaultDescription =
            activeState.FaultDescription;
        lastResult.TargetPath =
            activeState.TargetPath ?? "";
        lastResult.TargetGuid =
            activeState.TargetGuid ?? "";

        lastResult.Damaged =
            TerrainRuntimeReadinessUtility.Evaluate(true);

        lastResult.FaultDetected =
            lastResult.Damaged != null
            && !lastResult.Damaged.IsReady;

        lastResult.ClassificationMatched =
            ClassificationMatches(
                lastResult.Expectation,
                lastResult.Damaged
            );

        if (
            lastResult.Expectation.RecoveryAuthority ==
            TerrainRuntimeFaultRecoveryAuthority.HierarchyRepair
        )
        {
            string before =
                StructuralFingerprint(
                    lastResult.Damaged.HierarchyReadiness
                );

            WorldSettings worldSettings =
                AssetDatabase.LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths.WorldSettingsAssetPath
                );

            lastResult.Package04Result =
                TerrainRuntimeSceneSynchronizer
                    .SynchronizeExistingHierarchy(
                        worldSettings
                    );

            TerrainRuntimeHierarchyReadinessResult afterResult =
                TerrainRuntimeHierarchyReadinessUtility.Evaluate();

            string after =
                StructuralFingerprint(afterResult);

            lastResult.Package04RepairRequiredVerified =
                lastResult.Package04Result != null
                && lastResult.Package04Result.Outcome ==
                    TerrainRuntimeSceneSynchronizationOutcome.RepairRequired;

            lastResult.Package04StructuralMutationDetected =
                before != after;

            lastResult.ClassificationMatched &=
                lastResult.Package04RepairRequiredVerified
                && !lastResult.Package04StructuralMutationDetected;
        }

        if (
            lastResult.FaultDetected
            && lastResult.ClassificationMatched
        )
        {
            lastResult.Outcome =
                TerrainRuntimeFaultRecoveryValidationOutcome.FaultInjected;

            lastResult.SummaryMessage =
                "The fault was detected and classified correctly. Run Canonical Recovery to complete certification.";
        }
        else
        {
            lastResult.Outcome =
                TerrainRuntimeFaultRecoveryValidationOutcome.Failed;

            lastResult.ErrorMessage =
                !lastResult.FaultDetected
                    ? "The injected fault was not detected; Runtime still appears Ready."
                    : "The damaged state did not match the expected safe recovery classification.";

            lastResult.CleanupAttempted = true;

            lastResult.CleanupSucceeded =
                TerrainRuntimeFaultInjectionUtility
                    .TryRestore(
                        activeState,
                        out string cleanupError
                    );

            if (lastResult.CleanupSucceeded)
            {
                activeState = null;

                lastResult.Final =
                    TerrainRuntimeReadinessUtility
                        .Evaluate(true);

                lastResult.SummaryMessage =
                    "Fault detection/classification failed; validation cleanup restored the project.";
            }
            else
            {
                lastResult.Outcome =
                    TerrainRuntimeFaultRecoveryValidationOutcome.CleanupFailed;

                lastResult.ErrorMessage +=
                    "\nCleanup failed: " +
                    cleanupError;

                lastResult.SummaryMessage =
                    "Fault detection/classification failed and automatic cleanup also failed.";
            }
        }

        return lastResult;
    }

    public static bool RunCanonicalRecovery(
        Action<TerrainRuntimeFaultRecoveryValidationResult> onCompleted = null
    )
    {
        if (
            activeState == null
            || lastResult == null
            || recoveryRunning
            || lastResult.Outcome !=
                TerrainRuntimeFaultRecoveryValidationOutcome.FaultInjected
        )
        {
            return false;
        }

        recoveryRunning = true;
        lastResult.RecoveryStarted = true;

        if (
            !TerrainRuntimeFaultInjectionUtility
                .PrepareForCanonicalRecovery(
                    activeState,
                    out string preparationError
                )
        )
        {
            lastResult.ErrorMessage =
                preparationError;

            FinishRecovery(
                false,
                onCompleted
            );
            return false;
        }

        switch (activeState.Authority)
        {
            case TerrainRuntimeFaultRecoveryAuthority.UnifiedBake:
            case TerrainRuntimeFaultRecoveryAuthority.AddressablesConfigureAndBuild:
            {
                bool started =
                    TerrainRuntimeBakeValidationUtility
                        .BakePendingChanges(
                            validation =>
                            {
                                lastResult.BakeValidationResult =
                                    validation;

                                bool ok =
                                    validation != null
                                    && (
                                        validation.Outcome ==
                                            TerrainRuntimeBakeValidationOutcome.Passed
                                        || validation.Outcome ==
                                            TerrainRuntimeBakeValidationOutcome.PassedWithWarnings
                                    );

                                FinishRecovery(
                                    ok,
                                    onCompleted
                                );
                            }
                        );

                if (!started && recoveryRunning)
                {
                    TerrainRuntimeBakeValidationResult existing =
                        TerrainRuntimeBakeValidationUtility.LastResult;

                    lastResult.BakeValidationResult =
                        existing;

                    bool ok =
                        existing != null
                        && (
                            existing.Outcome ==
                                TerrainRuntimeBakeValidationOutcome.Passed
                            || existing.Outcome ==
                                TerrainRuntimeBakeValidationOutcome.PassedWithWarnings
                        );

                    FinishRecovery(
                        ok,
                        onCompleted
                    );
                }

                return started;
            }

            case TerrainRuntimeFaultRecoveryAuthority.HierarchyRepair:
            {
                WorldSettings worldSettings =
                    AssetDatabase.LoadAssetAtPath<WorldSettings>(
                        WorldMeshesPaths.WorldSettingsAssetPath
                    );

                if (worldSettings != null)
                {
                    TerrainWorldHierarchyGenerator
                        .SyncWorldHierarchy(worldSettings);
                }

                bool ok =
                    TerrainRuntimeHierarchyReadinessUtility
                        .Evaluate()
                        .IsReady;

                FinishRecovery(
                    ok,
                    onCompleted
                );

                return ok;
            }

            default:
                lastResult.ErrorMessage =
                    "No canonical recovery authority exists for this scenario.";

                FinishRecovery(
                    false,
                    onCompleted
                );
                return false;
        }
    }

    public static bool RestoreActiveFault(
        out string message
    )
    {
        message = "";

        if (recoveryRunning)
        {
            message =
                "Cannot restore while canonical recovery is running.";
            return false;
        }

        if (activeState == null)
        {
            if (
                TerrainRuntimeFaultInjectionUtility
                    .HasOrphanedValidationSession()
            )
            {
                return
                    TerrainRuntimeFaultInjectionUtility
                        .RestoreOrphanedValidationSession(
                            out message
                        );
            }

            message =
                "No active Package 10.4 fault exists.";
            return true;
        }

        bool ok =
            TerrainRuntimeFaultInjectionUtility
                .TryRestore(
                    activeState,
                    out string restoreError
                );

        lastResult.CleanupAttempted = true;
        lastResult.CleanupSucceeded = ok;

        if (ok)
        {
            lastResult.Final =
                TerrainRuntimeReadinessUtility.Evaluate(true);

            lastResult.SummaryMessage =
                "Active validation fault was restored without canonical recovery certification.";

            activeState = null;
            message =
                "Active Package 10.4 fault restored.";
        }
        else
        {
            lastResult.Outcome =
                TerrainRuntimeFaultRecoveryValidationOutcome.CleanupFailed;
            lastResult.ErrorMessage =
                restoreError;
            message = restoreError;
        }

        return ok;
    }

    private static void FinishRecovery(
        bool recoverySucceeded,
        Action<TerrainRuntimeFaultRecoveryValidationResult> onCompleted
    )
    {
        lastResult.RecoveryCompleted =
            recoverySucceeded;

        TerrainRuntimeIntegrityAuditUtility
            .InvalidateCachedAudit();

        lastResult.Final =
            TerrainRuntimeReadinessUtility.Evaluate(true);

        bool finalHealthy =
            recoverySucceeded
            && lastResult.Final != null
            && lastResult.Final.IsReady
            && lastResult.Final.Plan != null
            && !lastResult.Final.Plan.IsBlocked
            && !lastResult.Final.Plan.HasWork
            && lastResult.Final.IntegrityAudit != null
            && lastResult.Final.IntegrityAudit.GeneratedDataValid
            && lastResult.Final.AddressablesValidation != null
            && lastResult.Final.AddressablesValidation.IsValid
            && lastResult.Final.HierarchyReadiness != null
            && lastResult.Final.HierarchyReadiness.IsReady;

        if (finalHealthy)
        {
            TerrainRuntimeFaultInjectionUtility
                .CompleteSuccessfulRecovery(activeState);

            lastResult.Outcome =
                lastResult.BakeValidationResult != null
                && lastResult.BakeValidationResult.Outcome ==
                    TerrainRuntimeBakeValidationOutcome.PassedWithWarnings
                    ? TerrainRuntimeFaultRecoveryValidationOutcome.PassedWithWarnings
                    : TerrainRuntimeFaultRecoveryValidationOutcome.Passed;

            lastResult.SummaryMessage =
                "Fault detection and canonical recovery passed. Generated data, Addressables, hierarchy, final planner state, and Runtime Ready are valid.";

            activeState = null;
        }
        else
        {
            lastResult.ErrorMessage =
                string.IsNullOrEmpty(lastResult.ErrorMessage)
                    ? "Canonical recovery did not return WorldMeshes to a fully healthy Runtime Ready state."
                    : lastResult.ErrorMessage +
                      "\nCanonical recovery did not return WorldMeshes to a fully healthy Runtime Ready state.";

            lastResult.CleanupAttempted = true;

            lastResult.CleanupSucceeded =
                TerrainRuntimeFaultInjectionUtility
                    .TryRestore(
                        activeState,
                        out string cleanupError
                    );

            if (lastResult.CleanupSucceeded)
            {
                lastResult.Outcome =
                    TerrainRuntimeFaultRecoveryValidationOutcome.Failed;
                activeState = null;
            }
            else
            {
                lastResult.Outcome =
                    TerrainRuntimeFaultRecoveryValidationOutcome.CleanupFailed;
                lastResult.ErrorMessage +=
                    "\nCleanup failed: " +
                    cleanupError;
            }

            lastResult.SummaryMessage =
                "Fault recovery failed; validation cleanup was attempted.";
        }

        recoveryRunning = false;

        onCompleted?.Invoke(lastResult);
    }

    private static bool CanStart(
        out string error
    )
    {
        error = "";

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            error =
                "Package 10.4 fault validation must run outside Play Mode.";
            return false;
        }

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            error =
                "The unified runtime bake pipeline is already running.";
            return false;
        }

        if (TerrainRuntimeBakeValidationUtility.IsValidationBakeRunning)
        {
            error =
                "A Package 10.1 validation bake is already running.";
            return false;
        }

        if (TerrainRuntimeBakeResumeValidationUtility.IsRunning)
        {
            error =
                "A Package 10.2 persistence/resume scenario is already running.";
            return false;
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            error =
                "Independent Surface generation is already running.";
            return false;
        }

        if (
            TerrainRuntimeFaultInjectionUtility
                .HasOrphanedValidationSession()
        )
        {
            error =
                "An incomplete Package 10.4 validation quarantine exists. Restore it before starting another fault.";
            return false;
        }

        TerrainRuntimeReadinessResult baseline =
            TerrainRuntimeReadinessUtility.Evaluate(true);

        if (!baseline.IsReady)
        {
            error =
                "Package 10.4 destructive tests require Runtime Ready.\n\n" +
                baseline.ErrorMessage;
            return false;
        }

        return true;
    }

    private static bool ClassificationMatches(
        TerrainRuntimeFaultExpectation e,
        TerrainRuntimeReadinessResult damaged
    )
    {
        if (
            e == null
            || damaged == null
            || damaged.Plan == null
        )
        {
            return false;
        }

        bool modes =
            damaged.Plan.HeightWorkMode == e.HeightMode
            && damaged.Plan.SurfaceWorkMode == e.SurfaceMode
            && damaged.Plan.CollisionWorkMode == e.CollisionMode;

        bool config =
            damaged.Plan.AddressablesConfigurationRequired ==
                e.AddressablesConfigurationRequired;

        bool hierarchy =
            damaged.HierarchyReadiness != null
            && damaged.HierarchyReadiness.IsReady ==
                e.HierarchyReady;

        bool overall =
            damaged.IsReady == e.OverallReady;

        if (
            e.RecoveryAuthority ==
            TerrainRuntimeFaultRecoveryAuthority.HierarchyRepair
        )
        {
            return
                modes
                && !damaged.Plan.HasWork
                && config
                && hierarchy
                && overall
                && damaged.HeightStatus ==
                    TerrainGenerationStateUtility.GenerationStatus.Current
                && damaged.SurfaceStatus ==
                    TerrainGenerationStateUtility.GenerationStatus.Current
                && damaged.CollisionStatus ==
                    TerrainGenerationStateUtility.GenerationStatus.Current;
        }

        if (
            e.RecoveryAuthority ==
            TerrainRuntimeFaultRecoveryAuthority.AddressablesConfigureAndBuild
        )
        {
            return
                modes
                && config
                && hierarchy
                && overall
                && damaged.HeightStatus ==
                    TerrainGenerationStateUtility.GenerationStatus.Current
                && damaged.SurfaceStatus ==
                    TerrainGenerationStateUtility.GenerationStatus.Current
                && damaged.CollisionStatus ==
                    TerrainGenerationStateUtility.GenerationStatus.Current
                && damaged.AddressablesValidation != null
                && !damaged.AddressablesValidation.IsValid;
        }

        return
            modes
            && config
            && hierarchy
            && overall
            && (
                e.HeightMode == TerrainRuntimeBakeWorkMode.None
                    ? damaged.HeightStatus ==
                        TerrainGenerationStateUtility.GenerationStatus.Current
                    : damaged.HeightStatus !=
                        TerrainGenerationStateUtility.GenerationStatus.Current
            )
            && (
                e.SurfaceMode == TerrainRuntimeBakeWorkMode.None
                    ? damaged.SurfaceStatus ==
                        TerrainGenerationStateUtility.GenerationStatus.Current
                    : damaged.SurfaceStatus !=
                        TerrainGenerationStateUtility.GenerationStatus.Current
            )
            && (
                e.CollisionMode == TerrainRuntimeBakeWorkMode.None
                    ? damaged.CollisionStatus ==
                        TerrainGenerationStateUtility.GenerationStatus.Current
                    : damaged.CollisionStatus !=
                        TerrainGenerationStateUtility.GenerationStatus.Current
            );
    }

    private static string StructuralFingerprint(
        TerrainRuntimeHierarchyReadinessResult r
    )
    {
        if (r == null)
        {
            return "<null>";
        }

        return
            r.WorldRootCount + "|" +
            r.ClipmapRootCount + "|" +
            r.CollisionRootCount + "|" +
            r.BoundsControllerCount + "|" +
            r.HeightStreamerCount + "|" +
            r.CollisionStreamerCount + "|" +
            r.CollisionColliderPoolCount;
    }

    private static TerrainRuntimeFaultRecoveryValidationResult Blocked(
        TerrainRuntimeFaultRecoveryScenario scenario,
        string error
    )
    {
        return
            new TerrainRuntimeFaultRecoveryValidationResult
            {
                Scenario = scenario,
                Outcome =
                    TerrainRuntimeFaultRecoveryValidationOutcome.Blocked,
                Expectation =
                    TerrainRuntimeFaultExpectation.ForScenario(scenario),
                ErrorMessage = error ?? "",
                SummaryMessage =
                    "Fault validation did not run."
            };
    }
}
