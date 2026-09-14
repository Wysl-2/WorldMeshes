using System.Text;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRuntimeSurfaceIncrementalDiagnostics()
    {
        TerrainRuntimeBakePlan plan =
            GetRuntimeBakeDiagnosticsPlan();

        TerrainRuntimeBakeStateSummary snapshot =
            GetRuntimeBakeDiagnosticsSummary();

        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        TerrainSurfaceSettings surfaceSettings =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                WorldMeshesPaths.TerrainSurfaceSettingsAssetPath
            );

        TerrainGenerationStateUtility.GenerationStatus surfaceStatus =
            TerrainGenerationStateUtility.GetSurfaceMaskStatus(
                worldSettings
            );

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Incremental Runtime Surface Masks",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Surface Plan Mode",
            plan.SurfaceWorkMode.ToString()
        );

        EditorGUILayout.LabelField(
            "Planned Surface Tiles",
            plan.SurfaceTileCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Persistent Pending Surface Tiles",
            snapshot.PendingSurfaceTileCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Full Surface Required",
            snapshot.FullSurfaceRebuildRequired
                ? "Yes"
                : "No"
        );

        if (worldSettings != null)
        {
            EditorGUILayout.LabelField(
                "Current Height Generation Revision",
                worldSettings.heightmapGenerationRevision.ToString()
            );

            EditorGUILayout.LabelField(
                "Current Surface Generation Revision",
                worldSettings.surfaceMaskGenerationRevision.ToString()
            );

            EditorGUILayout.LabelField(
                "Surface Source Height Revision",
                worldSettings
                    .surfaceSourceHeightmapGenerationRevision
                    .ToString()
            );
        }

        EditorGUILayout.LabelField(
            "Surface Generation Status",
            TerrainGenerationStateUtility.GetStatusLabel(
                surfaceStatus
            )
        );

        EditorGUILayout.LabelField(
            "Surface Manifest",
            GetRuntimeSurfaceManifestLabel(
                manifest,
                surfaceStatus
            )
        );

        string settingsSignature =
            TerrainSurfaceSignatureUtility.GetSettingsSignature(
                surfaceSettings
            );

        string generationSignature =
            TerrainGenerationStateUtility.GetCurrentSurfaceMaskSignature(
                worldSettings,
                surfaceSettings
            );

        EditorGUILayout.LabelField(
            "Surface Settings Signature",
            string.IsNullOrEmpty(settingsSignature)
                ? "Unavailable"
                : settingsSignature
        );

        EditorGUILayout.LabelField(
            "Surface Generation Signature",
            string.IsNullOrEmpty(generationSignature)
                ? "Unavailable"
                : generationSignature
        );

        EditorGUILayout.LabelField(
            "Compiler Is Generating",
            TerrainSurfaceMaskCompiler.IsGenerating
                ? "Yes"
                : "No"
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
                "Log Planned Surface Tiles",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            Debug.Log(
                BuildPlannedSurfaceTileReport(
                    plan
                )
            );
        }

        EditorGUILayout.HelpBox(
            "Read-only Package 06 diagnostics for planned Surface work. " +
            "Use Runtime > Bake Runtime Changes for production generation.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private static string GetRuntimeSurfaceManifestLabel(
        TerrainSurfaceMaskManifest manifest,
        TerrainGenerationStateUtility.GenerationStatus status
    )
    {
        if (manifest == null)
        {
            return "Missing";
        }

        if (!manifest.isComplete)
        {
            return "Incomplete";
        }

        return
            status == TerrainGenerationStateUtility.GenerationStatus.Current
                ? "Current"
                : "Out Of Date";
    }

    private static string BuildPlannedSurfaceTileReport(
        TerrainRuntimeBakePlan plan
    )
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Planned Surface Tiles"
        );

        builder.AppendLine(
            "Mode: " +
            plan.SurfaceWorkMode
        );

        builder.AppendLine(
            "Count: " +
            plan.SurfaceTileCount
        );

        int displayCount =
            Mathf.Min(
                plan.SurfaceTileCount,
                128
            );

        for (
            int index = 0;
            index < displayCount;
            index++
        )
        {
            Vector2Int coordinate =
                plan.SurfaceTiles[index];

            builder.AppendLine(
                "(" +
                coordinate.x +
                ", " +
                coordinate.y +
                ")"
            );
        }

        if (plan.SurfaceTileCount > displayCount)
        {
            builder.Append(
                "... +" +
                (plan.SurfaceTileCount - displayCount) +
                " more"
            );
        }

        return builder.ToString();
    }
}
