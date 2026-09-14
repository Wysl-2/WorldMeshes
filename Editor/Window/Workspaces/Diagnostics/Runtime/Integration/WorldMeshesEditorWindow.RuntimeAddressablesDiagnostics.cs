using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private TerrainRuntimeAddressablesValidationResult
        lastRuntimeAddressablesValidationResult;

    private void DrawRuntimeAddressablesDiagnostics()
    {
        TerrainRuntimeBakePlan plan =
            GetRuntimeBakeDiagnosticsPlan();

        TerrainRuntimeBakeStateSummary snapshot =
            GetRuntimeBakeDiagnosticsSummary();

        TerrainRuntimeAddressablesOperationMode operationMode =
            TerrainRuntimeAddressablesUtility.GetOperationMode(
                plan
            );

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Addressables Pipeline",
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

        EditorGUILayout.HelpBox(
            "Read-only Package 07 structural diagnostics. " +
            "Use Runtime > Bake Runtime Changes for normal Addressables work. " +
            "Use Advanced Runtime Tools for explicit Addressables maintenance.",
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
}
