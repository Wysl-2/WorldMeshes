using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRuntimeHeightIncrementalDiagnostics()
    {
        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Incremental Runtime Heightmaps",
            EditorStyles.boldLabel
        );

        if (manifest == null)
        {
            EditorGUILayout.LabelField(
                "Manifest",
                "Not Generated"
            );
        }
        else
        {
            EditorGUILayout.LabelField(
                "Manifest Complete",
                manifest.isComplete
                    ? "Yes"
                    : "No"
            );

            EditorGUILayout.LabelField(
                "Compiler Version",
                manifest.compilerVersion.ToString()
            );

            EditorGUILayout.LabelField(
                "Expected Range Records",
                manifest.ExpectedTileHeightRangeCount
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Stored Range Records",
                manifest.TileHeightRangeCount
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Valid Range Records",
                manifest.ValidTileHeightRangeCount
                    .ToString("N0")
            );

            EditorGUILayout.LabelField(
                "Global Minimum",
                manifest.minimumTerrainHeight
                    .ToString("R")
            );

            EditorGUILayout.LabelField(
                "Global Maximum",
                manifest.maximumTerrainHeight
                    .ToString("R")
            );
        }

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Compile Planned Height Work",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeBakePlan plan =
                TerrainRuntimeBakePlanner
                    .BuildPlan(
                        worldSettings,
                        terrainAuthoringData
                    );

            TerrainRuntimeHeightCompileResult result =
                TerrainRuntimeHeightCompiler
                    .CompilePlannedHeightWork(
                        worldSettings,
                        terrainAuthoringData,
                        plan
                    );

            string report =
                result != null
                    ? result.BuildDiagnosticReport()
                    : "Runtime height compilation returned no result.";

            if (
                result != null
                &&
                (
                    result.Outcome ==
                        TerrainRuntimeHeightCompileOutcome.Completed
                    ||
                    result.Outcome ==
                        TerrainRuntimeHeightCompileOutcome.NoWork
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
                    TerrainRuntimeHeightCompileOutcome.Cancelled
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
                "Validate Runtime Height Range Metadata",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeHeightRangeMetadataValidator
                .Validate(
                    worldSettings,
                    true
                );
        }

        EditorGUILayout.HelpBox(
            "Package 03 diagnostics can execute only the height stage of the " +
            "current bake plan. Surface masks, collision, Addressables build, " +
            "and runtime scene synchronization are intentionally not executed.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }
}
