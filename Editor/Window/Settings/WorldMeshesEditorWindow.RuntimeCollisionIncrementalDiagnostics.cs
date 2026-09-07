using System.Text;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRuntimeCollisionIncrementalDiagnostics()
    {
        TerrainRuntimeBakePlan plan =
            TerrainRuntimeBakePlanner
                .BuildPlan(
                    worldSettings,
                    terrainAuthoringData
                );

        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService
                .GetSnapshot();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Incremental Collision Baking",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Collision Plan Mode",
            plan.CollisionWorkMode.ToString()
        );

        EditorGUILayout.LabelField(
            "Planned Collision Chunks",
            plan.CollisionChunkCount
                .ToString("N0")
        );

        if (worldSettings != null)
        {
            EditorGUILayout.LabelField(
                "Collision Generation Revision",
                worldSettings
                    .collisionMeshGenerationRevision
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Collision Source Height Revision",
                worldSettings
                    .collisionSourceHeightmapGenerationRevision
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Current Height Generation Revision",
                worldSettings
                    .heightmapGenerationRevision
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Collision Settings Signature",
                TerrainGenerationStateUtility
                    .GetCurrentCollisionSettingsSignature(
                        worldSettings
                    )
            );
        }

        EditorGUILayout.LabelField(
            "Persistent Pending Collision Chunks",
            snapshot
                .PendingCollisionChunkCount
                .ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Full Collision Required",
            snapshot
                .FullCollisionRebuildRequired
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
                "Generate Planned Collision Work",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeBakePlan currentPlan =
                TerrainRuntimeBakePlanner
                    .BuildPlan(
                        worldSettings,
                        terrainAuthoringData
                    );

            TerrainCollisionGenerationResult result =
                TerrainCollisionMeshGenerator
                    .GeneratePlannedCollisionMeshes(
                        worldSettings,
                        currentPlan
                    );

            string report =
                result != null
                    ? result.BuildDiagnosticReport()
                    : "Collision generation returned no result.";

            if (
                result != null
                &&
                (
                    result.Outcome ==
                        TerrainCollisionGenerationOutcome
                            .Completed
                    ||
                    result.Outcome ==
                        TerrainCollisionGenerationOutcome
                            .NoWork
                )
            )
            {
                Debug.Log(
                    report
                );
            }
            else if (
                result != null
                &&
                result.Outcome ==
                    TerrainCollisionGenerationOutcome
                        .Cancelled
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

            Repaint();
        }

        if (
            GUILayout.Button(
                "Log Planned Collision Chunks",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            Debug.Log(
                BuildPlannedCollisionChunkReport(
                    plan
                )
            );
        }

        EditorGUILayout.HelpBox(
            "Package 05 diagnostics execute only the collision-mesh stage of the current bake plan. Collision Addressables preparation, bake-marker generation, surface masks, runtime scene synchronization, and the unified bake pipeline are intentionally not executed.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private static string BuildPlannedCollisionChunkReport(
        TerrainRuntimeBakePlan plan
    )
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Planned Collision Chunks"
        );

        builder.AppendLine(
            "Mode: " +
            plan.CollisionWorkMode
        );

        builder.AppendLine(
            "Count: " +
            plan.CollisionChunkCount
        );

        int displayCount =
            Mathf.Min(
                plan.CollisionChunkCount,
                128
            );

        for (
            int index = 0;
            index < displayCount;
            index++
        )
        {
            Vector2Int coordinate =
                plan.CollisionChunks[
                    index
                ];

            builder.AppendLine(
                "(" +
                coordinate.x +
                ", " +
                coordinate.y +
                ")"
            );
        }

        if (
            plan.CollisionChunkCount >
            displayCount
        )
        {
            builder.Append(
                "... +" +
                (
                    plan.CollisionChunkCount -
                    displayCount
                ) +
                " more"
            );
        }

        return
            builder.ToString();
    }
}
