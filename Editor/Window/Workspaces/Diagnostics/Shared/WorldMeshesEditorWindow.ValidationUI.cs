using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private bool DrawValidationAction(
        string title,
        string statusLabel,
        bool isRunning,
        string buttonLabel,
        string description,
        MessageType descriptionType
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            title,
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            statusLabel,
            isRunning
                ? "Running"
                : "Ready"
        );

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            isRunning
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        bool requested =
            GUILayout.Button(
                buttonLabel,
                GUILayout.ExpandWidth(true)
            );

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            description,
            descriptionType
        );

        GUILayout.EndVertical();

        return requested;
    }

    private bool DrawValidationResultAction(
        string title,
        bool isScheduled,
        bool isRunning,
        int passedCount,
        int failedCount,
        string buttonLabel,
        string summary,
        string description
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            title,
            EditorStyles.boldLabel
        );

        string status =
            isScheduled
                ? "Scheduled"
                :
                isRunning
                    ? "Running"
                    :
                    failedCount > 0
                        ? "Failed"
                        :
                        passedCount > 0
                            ? "Passed"
                            : "Ready";

        EditorGUILayout.LabelField(
            "Status",
            status
        );

        EditorGUILayout.LabelField(
            "Passed",
            passedCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Failed",
            failedCount.ToString()
        );

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            isScheduled
            ||
            isRunning
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        bool requested =
            GUILayout.Button(
                buttonLabel,
                GUILayout.ExpandWidth(true)
            );

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            summary,
            failedCount > 0
                ? MessageType.Error
                : MessageType.Info
        );

        if (!string.IsNullOrEmpty(description))
        {
            EditorGUILayout.HelpBox(
                description,
                MessageType.None
            );
        }

        GUILayout.EndVertical();

        return requested;
    }
}
