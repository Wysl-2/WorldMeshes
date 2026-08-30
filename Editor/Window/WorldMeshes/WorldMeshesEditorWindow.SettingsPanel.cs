using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private Vector2 settingsScrollPosition =
        Vector2.zero;

    private void DrawSettingsPanel(
        Rect settingsArea
    )
    {
        GUILayout.BeginArea(
            settingsArea
        );

        settingsScrollPosition =
            EditorGUILayout.BeginScrollView(
                settingsScrollPosition,
                false,
                false,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );

        DrawWorldSettings();

        GUILayout.Space(10f);

        DrawGenerationStateSettings();

        GUILayout.Space(10f);

        DrawClipmapGenerationSettings();

        GUILayout.Space(10f);

        DrawHeightAuthoringSettings();

        GUILayout.Space(10f);

        DrawHeightPreviewSettings();

        GUILayout.Space(10f);

        DrawPreviewResponsivenessValidationSettings();

        GUILayout.Space(10f);

        DrawSceneViewFollowingSettings();

        GUILayout.Space(10f);

        DrawAuthoringVisualizationSettings();

        GUILayout.Space(10f);

        DrawAuthoringWireframeSettings();

        GUILayout.Space(10f);

        DrawCollisionGenerationSettings();

        GUILayout.Space(10f);

        DrawWorldHierarchySettings();

        GUILayout.Space(10f);

        DrawRuntimeValidationSettings();

        EditorGUILayout.EndScrollView();

        GUILayout.EndArea();
    }
}
