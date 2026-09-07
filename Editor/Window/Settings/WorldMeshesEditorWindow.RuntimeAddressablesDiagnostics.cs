using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private TerrainRuntimeAddressablesResult
        lastRuntimeAddressablesResult;

    private TerrainRuntimeAddressablesValidationResult
        lastRuntimeAddressablesValidationResult;

    private void DrawRuntimeAddressablesDiagnostics()
    {
        TerrainRuntimeBakePlan plan =
            TerrainRuntimeBakePlanner.BuildPlan(
                worldSettings,
                terrainAuthoringData
            );

        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService.GetSnapshot();

        TerrainRuntimeAddressablesOperationMode operationMode =
            TerrainRuntimeAddressablesUtility.GetOperationMode(
                plan
            );

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Addressables Pipeline Optimization",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Addressables Configuration Dirty",
            snapshot.AddressablesConfigurationDirty
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Addressables Content Dirty",
            snapshot.AddressablesContentDirty
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Plan Configuration Required",
            plan.AddressablesConfigurationRequired
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Plan Content Required",
            plan.AddressablesContentBuildRequired
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Planned Operation",
            GetRuntimeAddressablesOperationLabel(
                operationMode
            )
        );

        GUILayout.Space(4f);

        EditorGUILayout.LabelField(
            "Height Addressables Configuration",
            GetRuntimeAddressablesValidationLabel(
                lastRuntimeAddressablesValidationResult,
                0
            )
        );

        EditorGUILayout.LabelField(
            "Surface Addressables Configuration",
            GetRuntimeAddressablesValidationLabel(
                lastRuntimeAddressablesValidationResult,
                1
            )
        );

        EditorGUILayout.LabelField(
            "Collision Addressables Configuration",
            GetRuntimeAddressablesValidationLabel(
                lastRuntimeAddressablesValidationResult,
                2
            )
        );

        EditorGUILayout.LabelField(
            "Collision Marker Structure",
            GetRuntimeAddressablesValidationLabel(
                lastRuntimeAddressablesValidationResult,
                3
            )
        );

        EditorGUILayout.LabelField(
            "Collision Prepared Manifest",
            GetCollisionPreparedManifestStatusLabel()
        );

        if (plan.IsBlocked)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                plan.BlockReason,
                MessageType.Warning
            );
        }

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Process Planned Addressables Work",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeBakePlan currentPlan =
                TerrainRuntimeBakePlanner.BuildPlan(
                    worldSettings,
                    terrainAuthoringData
                );

            TerrainRuntimeAddressablesResult result =
                TerrainRuntimeAddressablesUtility
                    .ProcessPlannedRuntimeContent(
                        worldSettings,
                        currentPlan
                    );

            lastRuntimeAddressablesResult =
                result;

            LogRuntimeAddressablesResult(
                result
            );

            lastRuntimeAddressablesValidationResult =
                null;

            Repaint();
        }

        if (
            GUILayout.Button(
                "Validate Existing Addressables Configuration",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            lastRuntimeAddressablesValidationResult =
                TerrainRuntimeAddressablesUtility
                    .ValidateExistingRuntimeConfiguration(
                        worldSettings
                    );

            string report =
                lastRuntimeAddressablesValidationResult
                    .BuildDiagnosticReport();

            if (
                lastRuntimeAddressablesValidationResult
                    .IsValid
            )
            {
                Debug.Log(
                    report
                );
            }
            else
            {
                Debug.LogWarning(
                    report
                );
            }

            Repaint();
        }

        if (lastRuntimeAddressablesResult != null)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                lastRuntimeAddressablesResult.Outcome +
                "\n" +
                (
                    !string.IsNullOrEmpty(
                        lastRuntimeAddressablesResult.ErrorMessage
                    )
                        ? lastRuntimeAddressablesResult.ErrorMessage
                        : lastRuntimeAddressablesResult.SummaryMessage
                ),
                GetRuntimeAddressablesMessageType(
                    lastRuntimeAddressablesResult.Outcome
                )
            );
        }

        EditorGUILayout.HelpBox(
            "Package 07 diagnostics execute only the Addressables stage. Content Only validation is read-only: it does not create/move entries or regenerate collision markers. Configure + Build performs structural reconciliation before Unity's normal BuildPlayerContent.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private static string GetRuntimeAddressablesOperationLabel(
        TerrainRuntimeAddressablesOperationMode mode
    )
    {
        switch (mode)
        {
            case TerrainRuntimeAddressablesOperationMode.ContentOnly:
                return "Content Only";

            case TerrainRuntimeAddressablesOperationMode.ConfigureAndBuild:
                return "Configure + Build";

            default:
                return "None";
        }
    }

    private static string GetRuntimeAddressablesValidationLabel(
        TerrainRuntimeAddressablesValidationResult validation,
        int channel
    )
    {
        if (validation == null)
        {
            return "Not Checked";
        }

        bool valid;

        switch (channel)
        {
            case 0:
                valid =
                    validation.HeightConfigurationValid;
                break;

            case 1:
                valid =
                    validation.SurfaceConfigurationValid;
                break;

            case 2:
                valid =
                    validation.CollisionConfigurationValid;
                break;

            default:
                valid =
                    validation.CollisionMarkerStructureValid;
                break;
        }

        return
            valid
                ? "Valid"
                : "Needs Repair";
    }

    private string GetCollisionPreparedManifestStatusLabel()
    {
        TerrainCollisionManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainCollisionManifest>(
                WorldMeshesPaths.CollisionManifestAssetPath
            );

        if (manifest == null)
        {
            return "Missing";
        }

        if (!manifest.isComplete)
        {
            return "Incomplete";
        }

        if (
            !TerrainCollisionAddressablesUtility
                .ValidatePreparedManifestStructure(
                    worldSettings,
                    out _
                )
        )
        {
            return "Needs Repair";
        }

        if (
            worldSettings != null
            &&
            manifest.collisionMeshGenerationRevision
                == worldSettings.collisionMeshGenerationRevision
            &&
            manifest.collisionSourceHeightmapGenerationRevision
                == worldSettings.collisionSourceHeightmapGenerationRevision
        )
        {
            return "Current";
        }

        return "Out Of Date";
    }

    private static void LogRuntimeAddressablesResult(
        TerrainRuntimeAddressablesResult result
    )
    {
        if (result == null)
        {
            Debug.LogError(
                "Runtime Addressables processing returned no result."
            );

            return;
        }

        string report =
            result.BuildDiagnosticReport();

        if (
            result.Outcome ==
                TerrainRuntimeAddressablesOutcome.Completed
            ||
            result.Outcome ==
                TerrainRuntimeAddressablesOutcome.NoWork
        )
        {
            Debug.Log(
                report
            );
        }
        else if (
            result.Outcome ==
                TerrainRuntimeAddressablesOutcome.Cancelled
            ||
            result.Outcome ==
                TerrainRuntimeAddressablesOutcome.StalePlan
            ||
            result.Outcome ==
                TerrainRuntimeAddressablesOutcome.Blocked
        )
        {
            Debug.LogWarning(
                report
            );
        }
        else
        {
            Debug.LogError(
                report
            );
        }
    }

    private static MessageType GetRuntimeAddressablesMessageType(
        TerrainRuntimeAddressablesOutcome outcome
    )
    {
        switch (outcome)
        {
            case TerrainRuntimeAddressablesOutcome.Completed:
            case TerrainRuntimeAddressablesOutcome.NoWork:
                return MessageType.Info;

            case TerrainRuntimeAddressablesOutcome.Cancelled:
            case TerrainRuntimeAddressablesOutcome.StalePlan:
            case TerrainRuntimeAddressablesOutcome.Blocked:
                return MessageType.Warning;

            default:
                return MessageType.Error;
        }
    }
}
