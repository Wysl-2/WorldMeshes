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
            "Scene View following moves only the generated clipmap " +
            "hierarchy. The full-world editor height cache remains " +
            "fixed and is not rebuilt when the Scene View moves.\n\n" +

            "Follow is clamped to the logical world rectangle. " +
            "LOD levels keep their independent snapping and shared " +
            "adaptive stitch offsets.\n\n" +

            "Transient editor placement is restored to the canonical " +
            "world-centered hierarchy before scene save, Play Mode, " +
            "assembly reload, and editor shutdown.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
