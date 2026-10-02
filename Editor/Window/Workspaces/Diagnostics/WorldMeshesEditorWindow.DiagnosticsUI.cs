using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawDiagnosticsToolbar()
    {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Expand All"))
        {
            showSystemHealth = true;
            showLiveDiagnostics = true;
            showCapacityDiagnostics = true;
            showValidationSuites = true;
            showAdvancedValidation = true;
        }

        if (GUILayout.Button("Collapse All"))
        {
            showSystemHealth = false;
            showLiveDiagnostics = false;
            showCapacityDiagnostics = false;
            showValidationSuites = false;
            showAdvancedValidation = false;
        }

        GUILayout.EndHorizontal();
    }

    private static void DrawDiagnosticsStatusRow(
        string label,
        string status
    )
    {
        EditorGUILayout.LabelField(
            label,
            string.IsNullOrEmpty(status)
                ? "Unavailable"
                : status
        );
    }

    private static string FormatDiagnosticsBytes(
        long bytes
    )
    {
        if (bytes <= 0L)
        {
            return "0 MiB";
        }

        double mib =
            bytes /
            (1024d * 1024d);

        return
            mib.ToString("N2") +
            " MiB";
    }
}
