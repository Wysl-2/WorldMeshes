using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    // =====================================================
    // COLLISION INPUTS
    // =====================================================

    [SerializeField]
    private int inputCollisionResolution = 64;

    /*
     * Collision configuration remains shared by the Runtime workspace.
     * Runtime generation itself is owned by TerrainRuntimeBakePipeline.
     */
    private void UpdateCollisionSettings()
    {
        if (worldSettings == null)
        {
            return;
        }

        int previousCollisionResolution =
            worldSettings.collisionResolution;

        int collisionResolution =
            Mathf.Max(
                1,
                inputCollisionResolution
            );

        int heightfieldResolutionPerChunk =
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            );

        if (
            collisionResolution >
            heightfieldResolutionPerChunk
        )
        {
            EditorUtility.DisplayDialog(
                "Invalid Collision Resolution",

                "Collision Resolution cannot be greater " +
                "than Heightfield Resolution / Chunk.",

                "OK"
            );

            return;
        }

        if (
            heightfieldResolutionPerChunk %
            collisionResolution != 0
        )
        {
            EditorUtility.DisplayDialog(
                "Invalid Collision Resolution",

                "Heightfield Resolution / Chunk must be evenly divisible " +
                "by Collision Resolution.\n\n" +

                $"Heightfield Resolution / Chunk: " +
                $"{heightfieldResolutionPerChunk}\n" +

                $"Collision Resolution: " +
                $"{collisionResolution}",

                "OK"
            );

            return;
        }

        Undo.RecordObject(
            worldSettings,
            "Update Collision Settings"
        );

        worldSettings.collisionResolution =
            collisionResolution;

        EditorUtility.SetDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );

        TerrainRuntimeInvalidationService
            .InvalidateCollisionSettingsChanged(
                worldSettings,
                previousCollisionResolution
            );

        Repaint();

        Debug.Log(
            "Collision settings updated.\n\n" +

            $"Collision Resolution: " +
            $"{worldSettings.collisionResolution}\n" +

            $"Height Sample Step: " +
            $"{worldSettings.heightfieldResolutionPerChunk / worldSettings.collisionResolution}"
        );
    }
}
