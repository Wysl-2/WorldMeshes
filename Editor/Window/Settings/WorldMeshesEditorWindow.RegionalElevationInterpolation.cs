using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRegionalElevationInterpolationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true));

        GUILayout.Label(
            "Regional Elevation Interpolation",
            EditorStyles.boldLabel);

        if (worldSettings == null || terrainAuthoringData == null)
        {
            EditorGUILayout.HelpBox(
                "Assign WorldSettings and TerrainAuthoringData before editing regional interpolation.",
                MessageType.Warning);
            GUILayout.EndVertical();
            return;
        }

        TerrainRegionalElevationSource regionalSource =
            terrainAuthoringData.RegionalElevationSource;

        if (regionalSource == null)
        {
            EditorGUILayout.HelpBox(
                "No regional elevation source is active.",
                MessageType.Info);
            GUILayout.EndVertical();
            return;
        }

        if (!(regionalSource is TerrainNodeElevationSource nodeSource))
        {
            EditorGUILayout.LabelField(
                "Source",
                regionalSource.GetType().Name);
            EditorGUILayout.HelpBox(
                "Interpolation mode settings currently apply to TerrainNodeElevationSource only.",
                MessageType.Warning);
            GUILayout.EndVertical();
            return;
        }

        TerrainNodeElevationInterpolationMode mode =
            nodeSource.InterpolationMode;

        bool knownMode =
            TerrainNodeElevationInterpolationModeUtility.IsKnown(mode);

        bool implementedMode =
            TerrainNodeElevationInterpolationModeUtility.IsImplemented(mode);

        EditorGUILayout.LabelField(
            "Current Mode",
            TerrainNodeElevationInterpolationModeUtility.GetDisplayName(mode));

        bool interactiveEditActive =
            TerrainRegionalElevationService.HasActiveInteractiveEdit;

        if (interactiveEditActive)
        {
            EditorGUILayout.HelpBox(
                "A regional Scene edit is active. Interpolation mode is temporarily locked until the gesture commits or cancels.",
                MessageType.None);
        }

        EditorGUI.BeginDisabledGroup(
            interactiveEditActive || !knownMode);

        if (GUILayout.Button(
            TerrainNodeElevationInterpolationModeUtility.GetDisplayName(mode),
            EditorStyles.popup,
            GUILayout.ExpandWidth(true)))
        {
            ShowRegionalElevationInterpolationMenu(mode);
        }

        EditorGUI.EndDisabledGroup();

        if (!knownMode)
        {
            EditorGUILayout.HelpBox(
                "The serialized interpolation mode is invalid. Production evaluation and deterministic output signatures intentionally reject this source.",
                MessageType.Error);
        }
        else if (!implementedMode)
        {
            EditorGUILayout.HelpBox(
                TerrainNodeElevationInterpolationModeUtility.GetNotImplementedMessage(mode) +
                " Use the selector to return the source to Inverse Distance Weighted.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Inverse Distance Weighted is the currently implemented regional interpolation mode. " +
                "Triangulated Linear and Triangulated Smooth are reserved for later interpolation packages.",
                MessageType.Info);
        }

        GUILayout.EndVertical();
    }

    private void ShowRegionalElevationInterpolationMenu(
        TerrainNodeElevationInterpolationMode currentMode)
    {
        GenericMenu menu = new GenericMenu();

        menu.AddItem(
            new GUIContent("Inverse Distance Weighted"),
            currentMode ==
                TerrainNodeElevationInterpolationMode.InverseDistanceWeighted,
            () => ApplyRegionalElevationInterpolationMode(
                TerrainNodeElevationInterpolationMode.InverseDistanceWeighted));

        menu.AddDisabledItem(
            new GUIContent("Triangulated Linear (Not Implemented)"),
            currentMode ==
                TerrainNodeElevationInterpolationMode.TriangulatedLinear);

        menu.AddDisabledItem(
            new GUIContent("Triangulated Smooth (Not Implemented)"),
            currentMode ==
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        menu.ShowAsContext();
    }

    private void ApplyRegionalElevationInterpolationMode(
        TerrainNodeElevationInterpolationMode mode)
    {
        if (worldSettings == null || terrainAuthoringData == null)
        {
            return;
        }

        if (!TerrainRegionalElevationService.SetInterpolationMode(
            terrainAuthoringData,
            worldSettings,
            mode,
            out string errorMessage))
        {
            Debug.LogError(
                "Regional elevation interpolation mode edit failed.\n\n" +
                errorMessage);
            return;
        }

        Repaint();
        SceneView.RepaintAll();
    }
}
