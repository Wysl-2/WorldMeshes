using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawBaseMeshSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Base Mesh",
            EditorStyles.boldLabel
        );

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "generating the base mesh.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        // -------------------------------------------------
        // Current generation settings
        // -------------------------------------------------

        EditorGUILayout.LabelField(
            "Chunk Size",
            worldSettings.chunkSize.ToString()
        );

        EditorGUILayout.LabelField(
            "LOD0 Resolution",
            worldSettings.lod0Resolution.ToString()
        );

        GUILayout.Space(5f);

        int vertexCount =
            (worldSettings.lod0Resolution + 1) *
            (worldSettings.lod0Resolution + 1);

        EditorGUILayout.HelpBox(
            $"Creates a {worldSettings.chunkSize} x " +
            $"{worldSettings.chunkSize} unit flat terrain mesh " +
            $"with {worldSettings.lod0Resolution} x " +
            $"{worldSettings.lod0Resolution} quads.\n\n" +
            $"Vertices: {vertexCount:N0}",
            MessageType.Info
        );

        GUILayout.Space(5f);

        // -------------------------------------------------
        // Generate
        // -------------------------------------------------

        if (
            GUILayout.Button(
                "Generate LOD0 Base Mesh",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainBaseMeshGenerator
                .GenerateBaseMesh(
                    worldSettings.chunkSize,
                    worldSettings.lod0Resolution
                );
        }

        GUILayout.EndVertical();
    }
}
