using System.Text;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private TerrainSurfaceMaskGenerationResult
        lastRuntimeSurfaceGenerationResult;

    private void DrawRuntimeSurfaceIncrementalDiagnostics()
    {
        TerrainRuntimeBakePlan plan =
            TerrainRuntimeBakePlanner.BuildPlan(
                worldSettings,
                terrainAuthoringData
            );

        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService.GetSnapshot();

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

        EditorGUI.BeginDisabledGroup(
            TerrainSurfaceMaskCompiler.IsGenerating
        );

        if (
            GUILayout.Button(
                "Generate Planned Surface Work",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeBakePlan currentPlan =
                TerrainRuntimeBakePlanner.BuildPlan(
                    worldSettings,
                    terrainAuthoringData
                );

            TerrainSurfaceMaskCompiler.GeneratePlannedSurfaceMasks(
                worldSettings,
                currentPlan,
                result =>
                {
                    lastRuntimeSurfaceGenerationResult =
                        result;

                    LogRuntimeSurfaceGenerationResult(
                        result
                    );

                    Repaint();
                }
            );

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

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

        if (lastRuntimeSurfaceGenerationResult != null)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                lastRuntimeSurfaceGenerationResult.Outcome +
                "\n" +
                (
                    !string.IsNullOrEmpty(
                        lastRuntimeSurfaceGenerationResult.ErrorMessage
                    )
                        ? lastRuntimeSurfaceGenerationResult.ErrorMessage
                        : lastRuntimeSurfaceGenerationResult.SummaryMessage
                ),
                GetRuntimeSurfaceGenerationMessageType(
                    lastRuntimeSurfaceGenerationResult.Outcome
                )
            );
        }

        EditorGUILayout.HelpBox(
            "Package 06 diagnostics execute only the surface-mask stage of the current bake plan. Terrain Analysis is reused, and only planned surface tiles are read back/written for Incremental work. Addressables build, runtime scene synchronization, and unified bake orchestration are intentionally not executed.",
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

    private static void LogRuntimeSurfaceGenerationResult(
        TerrainSurfaceMaskGenerationResult result
    )
    {
        if (result == null)
        {
            Debug.LogError(
                "Runtime surface-mask generation returned no result."
            );

            return;
        }

        string report =
            result.BuildDiagnosticReport();

        if (
            result.Outcome == TerrainSurfaceMaskGenerationOutcome.Completed
            ||
            result.Outcome == TerrainSurfaceMaskGenerationOutcome.NoWork
        )
        {
            Debug.Log(report);
        }
        else if (
            result.Outcome == TerrainSurfaceMaskGenerationOutcome.Cancelled
        )
        {
            Debug.LogWarning(report);
        }
        else
        {
            Debug.LogError(report);
        }
    }

    private static MessageType GetRuntimeSurfaceGenerationMessageType(
        TerrainSurfaceMaskGenerationOutcome outcome
    )
    {
        switch (outcome)
        {
            case TerrainSurfaceMaskGenerationOutcome.Completed:
            case TerrainSurfaceMaskGenerationOutcome.NoWork:
                return MessageType.Info;

            case TerrainSurfaceMaskGenerationOutcome.Cancelled:
            case TerrainSurfaceMaskGenerationOutcome.Blocked:
            case TerrainSurfaceMaskGenerationOutcome.StalePlan:
                return MessageType.Warning;

            default:
                return MessageType.Error;
        }
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
