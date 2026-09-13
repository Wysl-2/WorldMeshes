using UnityEditor;
using UnityEngine;

/*
 * Editor-only visualization preferences for TerrainStampEditorTool.
 *
 * These values intentionally live in EditorPrefs rather than
 * TerrainAuthoringData. Changing Scene View overlays must never dirty
 * terrain authoring data, change authoring signatures, or invalidate
 * generated runtime terrain.
 */
public static class TerrainStampEditorToolPreferences
{
    private const string ShowFalloffVisualizationKey =
        "WorldMeshes.TerrainStampTool.ShowFalloffVisualization";

    private const string ShowAffectedTileOverlayKey =
        "WorldMeshes.TerrainStampTool.ShowAffectedTileOverlay";

    public static bool ShowFalloffVisualization
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    ShowFalloffVisualizationKey,
                    true
                );
        }

        set
        {
            SetPreference(
                ShowFalloffVisualizationKey,
                value,
                ShowFalloffVisualization
            );
        }
    }

    public static bool ShowAffectedTileOverlay
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    ShowAffectedTileOverlayKey,
                    false
                );
        }

        set
        {
            SetPreference(
                ShowAffectedTileOverlayKey,
                value,
                ShowAffectedTileOverlay
            );
        }
    }

    private static void SetPreference(
        string key,
        bool value,
        bool currentValue
    )
    {
        if (value == currentValue)
        {
            return;
        }

        EditorPrefs.SetBool(
            key,
            value
        );

        SceneView.RepaintAll();
    }
}
