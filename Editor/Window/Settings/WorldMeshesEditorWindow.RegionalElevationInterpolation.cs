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

        bool cpuSupported =
            TerrainNodeElevationInterpolationModeUtility
                .SupportsCpuEvaluation(mode);

        bool gpuSupported =
            TerrainNodeElevationInterpolationModeUtility
                .SupportsGpuComposition(mode);

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
        else if (!cpuSupported)
        {
            EditorGUILayout.HelpBox(
                TerrainNodeElevationInterpolationModeUtility
                    .GetCpuNotImplementedMessage(mode) +
                " Use the selector to return the source to Inverse Distance Weighted.",
                MessageType.Warning);
        }
        else if (!gpuSupported)
        {
            EditorGUILayout.HelpBox(
                TerrainNodeElevationInterpolationModeUtility
                    .GetGpuNotImplementedMessage(mode) +
                " The live production selector keeps this mode disabled until CPU/GPU parity is completed.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Inverse Distance Weighted is available for both CPU evaluation and GPU terrain composition. " +
                "Triangulated Linear CPU evaluation is implemented, but live GPU composition is deferred to Package I4.",
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
            new GUIContent("Triangulated Linear (CPU Ready - GPU Pending I4)"),
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

        if (!TerrainNodeElevationInterpolationModeUtility
            .SupportsGpuComposition(mode))
        {
            Debug.LogError(
                "Regional elevation interpolation mode edit failed.\n\n" +
                TerrainNodeElevationInterpolationModeUtility
                    .GetGpuNotImplementedMessage(mode));
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
