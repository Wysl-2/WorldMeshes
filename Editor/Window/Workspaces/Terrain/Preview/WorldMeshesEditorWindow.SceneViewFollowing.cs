using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawSceneViewFollowingSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Scene View Following",
            EditorStyles.boldLabel
        );

        bool followEnabled =
            TerrainAuthoringSceneViewController
                .FollowSceneView;

        EditorGUI.BeginChangeCheck();

        bool newFollowEnabled =
            EditorGUILayout.Toggle(
                "Follow Scene View",
                followEnabled
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringSceneViewController
                .FollowSceneView =
                    newFollowEnabled;

            followEnabled =
                newFollowEnabled;
        }

        EditorGUI.BeginDisabledGroup(
            !followEnabled
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        TerrainAuthoringSceneViewFollowSource
            followSource =
                TerrainAuthoringSceneViewController
                    .FollowSource;

        EditorGUI.BeginChangeCheck();

        TerrainAuthoringSceneViewFollowSource
            newFollowSource =
                (TerrainAuthoringSceneViewFollowSource)
                EditorGUILayout.EnumPopup(
                    "Follow Source",
                    followSource
                );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringSceneViewController
                .FollowSource =
                    newFollowSource;
        }

        bool freezePreview =
            TerrainAuthoringSceneViewController
                .FreezePreview;

        EditorGUI.BeginChangeCheck();

        bool newFreezePreview =
            EditorGUILayout.Toggle(
                "Freeze Preview",
                freezePreview
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringSceneViewController
                .FreezePreview =
                    newFreezePreview;
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.LabelField(
            "Status",
            TerrainAuthoringSceneViewController
                .StatusLabel
        );

        string statusMessage =
            TerrainAuthoringSceneViewController
                .StatusMessage;

        if (
            !string.IsNullOrEmpty(
                statusMessage
            )
        )
        {
            MessageType messageType =
                TerrainAuthoringSceneViewController.Status ==
                    TerrainAuthoringSceneViewStatus.Error
                    ? MessageType.Error
                    :
                    TerrainAuthoringSceneViewController.Status ==
                        TerrainAuthoringSceneViewStatus.Following
                    ||
                    TerrainAuthoringSceneViewController.Status ==
                        TerrainAuthoringSceneViewStatus.Frozen
                    ||
                    TerrainAuthoringSceneViewController.Status ==
                        TerrainAuthoringSceneViewStatus.WaitingForHeightCache
                        ? MessageType.Info
                        : MessageType.Warning;

            EditorGUILayout.HelpBox(
                statusMessage,
                messageType
            );
        }

        if (
            TerrainAuthoringSceneViewController
                .HasFollowTarget
        )
        {
            Vector2 targetXZ =
                TerrainAuthoringSceneViewController
                    .FollowTargetXZ;

            EditorGUILayout.LabelField(
                "Follow Target XZ",
                $"{targetXZ.x:R}, {targetXZ.y:R}"
            );
        }

        if (
            TerrainAuthoringSceneViewController
                .HasAppliedLOD0Anchor
        )
        {
            Vector2 lod0AnchorXZ =
                TerrainAuthoringSceneViewController
                    .AppliedLOD0AnchorXZ;

            EditorGUILayout.LabelField(
                "LOD0 Anchor XZ",
                $"{lod0AnchorXZ.x:R}, {lod0AnchorXZ.y:R}"
            );
        }

        GUILayout.Space(
            5f
        );

        EditorGUI.BeginDisabledGroup(
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Center On World",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainAuthoringSceneViewController
                .CenterOnWorld();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Scene View movement calculates the exact candidate clipmap " +
            "layout and always lets Height Preview evaluate residency. If the " +
            "active cache lacks required sample-safe coverage, the layout " +
            "still waits for a staged transition.\n\n" +

            "If coverage is already safe but the active cache is materially " +
            "over- or undersized, Package 03A requests a staged size recovery " +
            "without blocking Scene View placement. Origin differences alone " +
            "do not force a transition while guard coverage remains safe.\n\n" +

            "The previous active terrain remains bound throughout Package 03 " +
            "staging. Work is still synchronous in Package 03A; Package 04 " +
            "introduces incremental multi-update streaming.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
