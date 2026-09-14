using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private TerrainRuntimeSceneSynchronizationResult
        lastRuntimeSceneSynchronizationResult;

    private void DrawRuntimeSceneSynchronizationDiagnostics()
    {
        TerrainRuntimeBakeStateSummary snapshot =
            GetRuntimeBakeDiagnosticsSummary();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Scene Synchronization",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Persistent Scene Metadata Dirty",
            snapshot.RuntimeSceneMetadataDirty
                ? "Yes"
                : "No"
        );

        DrawRuntimeSceneHierarchyStatus();

        GUILayout.Space(5f);

        TerrainGenerationStateEvaluationResult generationState =
            GetRuntimeBakeDiagnosticsGenerationState();

        TerrainGenerationStateUtility.GenerationStatus heightStatus =
            generationState.HeightmapStatus;

        TerrainGenerationStateUtility.GenerationStatus surfaceStatus =
            generationState.SurfaceMaskStatus;

        TerrainGenerationStateUtility.GenerationStatus collisionStatus =
            generationState.CollisionMeshStatus;

        EditorGUILayout.LabelField(
            "Runtime Height Manifest",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    heightStatus
                )
        );

        TerrainHeightmapManifest heightManifest =
            AssetDatabase
                .LoadAssetAtPath<
                    TerrainHeightmapManifest
                >(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        EditorGUILayout.LabelField(
            "Physical Height Range",
            heightManifest != null
                &&
                heightManifest.isComplete
                &&
                heightManifest.HasValidHeightRange
                    ?
                    heightManifest.minimumTerrainHeight
                        .ToString("R") +
                    " -> " +
                    heightManifest.maximumTerrainHeight
                        .ToString("R")
                    :
                    "Not Available"
        );

        EditorGUILayout.LabelField(
            "Surface Manifest",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    surfaceStatus
                )
        );

        EditorGUILayout.LabelField(
            "Collision Generation",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    collisionStatus
                )
        );

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            TerrainRuntimeBakePipeline.IsRunning
        );

        if (
            GUILayout.Button(
                "Synchronize Runtime Scene Metadata",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            lastRuntimeSceneSynchronizationResult =
                TerrainRuntimeSceneSynchronizer
                    .SynchronizeExistingHierarchy(
                        worldSettings
                    );

            Debug.Log(
                lastRuntimeSceneSynchronizationResult
                    .BuildDiagnosticReport()
            );

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (
            lastRuntimeSceneSynchronizationResult !=
            null
        )
        {
            MessageType messageType =
                GetRuntimeSceneSynchronizationMessageType(
                    lastRuntimeSceneSynchronizationResult
                        .Outcome
                );

            EditorGUILayout.HelpBox(
                lastRuntimeSceneSynchronizationResult
                    .Outcome +
                "\n" +
                (
                    !string.IsNullOrEmpty(
                        lastRuntimeSceneSynchronizationResult
                            .ErrorMessage
                    )
                        ?
                        lastRuntimeSceneSynchronizationResult
                            .ErrorMessage
                        :
                        lastRuntimeSceneSynchronizationResult
                            .SummaryMessage
                ),
                messageType
            );
        }

        EditorGUILayout.HelpBox(
            "This operation only synchronizes runtime-derived metadata on the existing generated hierarchy. Missing or structurally outdated hierarchy content is reported for Setup / Repair World Hierarchy and is never recreated automatically.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private static MessageType
        GetRuntimeSceneSynchronizationMessageType(
            TerrainRuntimeSceneSynchronizationOutcome outcome
        )
    {
        switch (outcome)
        {
            case TerrainRuntimeSceneSynchronizationOutcome
                .Completed:
            case TerrainRuntimeSceneSynchronizationOutcome
                .NoWork:
                return
                    MessageType.Info;

            case TerrainRuntimeSceneSynchronizationOutcome
                .CompletedWithWarnings:
            case TerrainRuntimeSceneSynchronizationOutcome
                .Blocked:
            case TerrainRuntimeSceneSynchronizationOutcome
                .RepairRequired:
                return
                    MessageType.Warning;

            default:
                return
                    MessageType.Error;
        }
    }

    private static void DrawRuntimeSceneHierarchyStatus()
    {
        if (
            !TerrainWorldSceneUtility
                .TryGetActiveScene(
                    out Scene scene,
                    out string sceneError
                )
        )
        {
            EditorGUILayout.LabelField(
                "Scene",
                sceneError
            );

            return;
        }

        bool worldLookup =
            TerrainWorldSceneUtility
                .TryFindWorldRoot(
                    scene,
                    out Transform worldRoot,
                    out string worldError
                );

        EditorGUILayout.LabelField(
            "WorldRoot",
            worldLookup
                ?
                (
                    worldRoot != null
                        ? "Found"
                        : "Missing"
                )
                :
                "Ambiguous / Invalid"
        );

        if (
            !worldLookup
            &&
            !string.IsNullOrEmpty(worldError)
        )
        {
            EditorGUILayout.HelpBox(
                worldError,
                MessageType.Warning
            );

            return;
        }

        bool clipmapLookup =
            TerrainWorldSceneUtility
                .TryFindClipmapRoot(
                    scene,
                    out Transform clipmapRoot,
                    out _
                );

        EditorGUILayout.LabelField(
            "Clipmap",
            clipmapLookup
                ?
                (
                    clipmapRoot != null
                        ? "Found"
                        : "Missing"
                )
                :
                "Ambiguous"
        );

        EditorGUILayout.LabelField(
            "Bounds Controller",
            clipmapRoot != null
                &&
                clipmapRoot.GetComponents<
                    TerrainClipmapBoundsController
                >().Length == 1
                    ? "Found"
                    : "Missing / Invalid"
        );

        EditorGUILayout.LabelField(
            "Height Streamer",
            clipmapRoot != null
                &&
                clipmapRoot.GetComponents<
                    TerrainHeightmapStreamer
                >().Length == 1
                    ? "Found"
                    : "Missing / Invalid"
        );

        bool collisionLookup =
            TerrainWorldSceneUtility
                .TryFindCollisionRoot(
                    scene,
                    out Transform collisionRoot,
                    out _
                );

        EditorGUILayout.LabelField(
            "Collision Root",
            collisionLookup
                ?
                (
                    collisionRoot != null
                        ? "Found"
                        : "Missing"
                )
                :
                "Ambiguous"
        );
    }
}
