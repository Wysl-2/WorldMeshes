using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private enum RegionalElevationSetupMethod
    {
        None,
        ElevationNodes
    }

    private enum NodeElevationInitialLayout
    {
        FourCorners,
        Grid
    }

    [SerializeField]
    private RegionalElevationSetupMethod
        inputRegionalElevationMethod =
            RegionalElevationSetupMethod.None;

    [SerializeField]
    private NodeElevationInitialLayout
        inputNodeElevationInitialLayout =
            NodeElevationInitialLayout.FourCorners;

    [SerializeField]
    private int inputNodeGridDivisionsX =
        3;

    [SerializeField]
    private int inputNodeGridDivisionsZ =
        3;

    [SerializeField]
    private float inputNodeInitialElevation =
        0f;

    [System.NonSerialized]
    private TerrainAuthoringData
        regionalElevationSetupBoundAuthoringData;

    private void DrawRegionalElevationSetupSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Regional Elevation",
            EditorStyles.boldLabel
        );

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before configuring " +
                "regional elevation.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        if (terrainAuthoringData == null)
        {
            EditorGUILayout.HelpBox(
                "Create or assign TerrainAuthoringData in Height Authoring " +
                "before configuring regional elevation.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        EnsureRegionalElevationSetupBinding();

        TerrainRegionalElevationSource existingSource =
            terrainAuthoringData
                .RegionalElevationSource;

        TerrainNodeElevationSource existingNodeSource =
            existingSource as
            TerrainNodeElevationSource;

        bool unsupportedExistingSource =
            existingSource != null
            &&
            existingNodeSource == null;

        EditorGUILayout.LabelField(
            "Current Source",
            existingSource != null
                ? existingSource.GetType().Name
                : "None"
        );

        if (existingNodeSource != null)
        {
            EditorGUILayout.LabelField(
                "Current Nodes",
                existingNodeSource
                    .NodeCount
                    .ToString("N0")
            );
        }

        if (unsupportedExistingSource)
        {
            EditorGUILayout.HelpBox(
                "The current regional elevation source is not a node source. " +
                "Package 2 will not replace an unsupported/future source type.",
                MessageType.Warning
            );
        }

        GUILayout.Space(6f);

        inputRegionalElevationMethod =
            (RegionalElevationSetupMethod)
            EditorGUILayout.EnumPopup(
                "Method",
                inputRegionalElevationMethod
            );

        if (
            inputRegionalElevationMethod ==
            RegionalElevationSetupMethod.None
        )
        {
            EditorGUILayout.HelpBox(
                existingSource == null
                    ? "No regional elevation source will be created."
                    : "Selecting None here is only a staged setup choice. " +
                        "It does not delete the existing authored regional " +
                        "elevation source.",
                MessageType.Info
            );

            GUILayout.EndVertical();

            return;
        }

        GUILayout.Space(6f);

        GUILayout.Label(
            "Initial Node Layout",
            EditorStyles.boldLabel
        );

        inputNodeElevationInitialLayout =
            (NodeElevationInitialLayout)
            EditorGUILayout.EnumPopup(
                "Layout",
                inputNodeElevationInitialLayout
            );

        int effectiveDivisionsX;
        int effectiveDivisionsZ;

        if (
            inputNodeElevationInitialLayout ==
            NodeElevationInitialLayout.FourCorners
        )
        {
            effectiveDivisionsX =
                1;

            effectiveDivisionsZ =
                1;

            EditorGUILayout.LabelField(
                "World Divisions",
                "1 x 1"
            );

            EditorGUILayout.HelpBox(
                "Four Corners is the 1 x 1 grid-division preset. It uses " +
                "the same layout generator as every other node grid.",
                MessageType.Info
            );
        }
        else
        {
            inputNodeGridDivisionsX =
                Mathf.Max(
                    1,
                    EditorGUILayout.IntField(
                        "Grid Divisions X",
                        inputNodeGridDivisionsX
                    )
                );

            inputNodeGridDivisionsZ =
                Mathf.Max(
                    1,
                    EditorGUILayout.IntField(
                        "Grid Divisions Z",
                        inputNodeGridDivisionsZ
                    )
                );

            effectiveDivisionsX =
                inputNodeGridDivisionsX;

            effectiveDivisionsZ =
                inputNodeGridDivisionsZ;
        }

        inputNodeInitialElevation =
            EditorGUILayout.FloatField(
                "Initial Node Elevation",
                inputNodeInitialElevation
            );

        bool layoutValid =
            TerrainNodeElevationLayoutUtility
                .TryGetGeneratedNodeCounts(
                    effectiveDivisionsX,
                    effectiveDivisionsZ,
                    out int nodeCountX,
                    out int nodeCountZ,
                    out int totalNodeCount,
                    out string layoutError
                );

        bool elevationValid =
            IsFiniteRegionalElevationInput(
                inputNodeInitialElevation
            );

        GUILayout.Space(5f);

        GUILayout.Label(
            "Generated Layout",
            EditorStyles.boldLabel
        );

        if (layoutValid)
        {
            EditorGUILayout.LabelField(
                "Generated Nodes",
                $"{nodeCountX} x {nodeCountZ}"
            );

            EditorGUILayout.LabelField(
                "Total Nodes",
                totalNodeCount.ToString("N0")
            );
        }
        else
        {
            EditorGUILayout.HelpBox(
                layoutError,
                MessageType.Error
            );
        }

        if (!elevationValid)
        {
            EditorGUILayout.HelpBox(
                "Initial Node Elevation must be finite.",
                MessageType.Error
            );
        }

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        EditorGUILayout.LabelField(
            "World Size XZ",
            $"{worldSizeXZ.x:R} x {worldSizeXZ.y:R}"
        );

        GUILayout.Space(7f);

        bool canInitialize =
            layoutValid
            &&
            elevationValid
            &&
            !unsupportedExistingSource
            &&
            !Application.isPlaying
            &&
            !EditorApplication
                .isPlayingOrWillChangePlaymode;

        EditorGUI.BeginDisabledGroup(
            !canInitialize
        );

        string actionLabel =
            existingNodeSource != null
                ? "Reinitialize Regional Elevation"
                : "Initialize Regional Elevation";

        if (
            GUILayout.Button(
                actionLabel,
                GUILayout.ExpandWidth(true)
            )
        )
        {
            InitializeNodeRegionalElevation(
                effectiveDivisionsX,
                effectiveDivisionsZ
            );
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Grid divisions are used only to place the initial nodes. " +
            "After creation, every node is an independent " +
            "TerrainElevationNode containing only StableId, PositionXZ, " +
            "and Elevation.\n\n" +

            "Reinitializing the committed authoring heightfield does not " +
            "regenerate or overwrite this node layout.\n\n" +

            "Package 2 stores node data only. Regional elevation still has " +
            "no effect on visible terrain.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }

    private void EnsureRegionalElevationSetupBinding()
    {
        if (
            regionalElevationSetupBoundAuthoringData ==
            terrainAuthoringData
        )
        {
            return;
        }

        regionalElevationSetupBoundAuthoringData =
            terrainAuthoringData;

        if (terrainAuthoringData == null)
        {
            inputRegionalElevationMethod =
                RegionalElevationSetupMethod.None;

            return;
        }

        inputRegionalElevationMethod =
            terrainAuthoringData.RegionalElevationSource is
                TerrainNodeElevationSource
                ? RegionalElevationSetupMethod.ElevationNodes
                : RegionalElevationSetupMethod.None;
    }

    private void InitializeNodeRegionalElevation(
        int divisionsX,
        int divisionsZ
    )
    {
        if (
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            return;
        }

        TerrainRegionalElevationSource existingSource =
            terrainAuthoringData
                .RegionalElevationSource;

        if (
            existingSource != null
            &&
            !(existingSource is TerrainNodeElevationSource)
        )
        {
            EditorUtility.DisplayDialog(
                "Regional Elevation",
                "The existing regional elevation source is not a node " +
                "source. Package 2 will not replace it.",
                "OK"
            );

            return;
        }

        if (
            existingSource is TerrainNodeElevationSource existingNodeSource
        )
        {
            bool confirmed =
                EditorUtility.DisplayDialog(
                    "Reinitialize Regional Elevation",
                    "This will replace the existing node layout containing " +
                    $"{existingNodeSource.NodeCount:N0} nodes.\n\n" +
                    "This does not modify the committed base heightfield, " +
                    "but any existing regional node layout data will be " +
                    "replaced.\n\nContinue?",
                    "Reinitialize",
                    "Cancel"
                );

            if (!confirmed)
            {
                return;
            }
        }

        if (
            !TerrainNodeElevationLayoutUtility
                .TryCreateGridSource(
                    worldSettings,
                    divisionsX,
                    divisionsZ,
                    inputNodeInitialElevation,
                    out TerrainNodeElevationSource generatedSource,
                    out string errorMessage
                )
        )
        {
            EditorUtility.DisplayDialog(
                "Regional Elevation Initialization Failed",
                errorMessage,
                "OK"
            );

            return;
        }

        bool hasCommittedHeightfield =
            !string.IsNullOrEmpty(
                TerrainAuthoringStateUtility
                    .GetCommittedHeightfieldSignature(
                        worldSettings
                    )
            );

        if (
            hasCommittedHeightfield
            &&
            terrainAuthoringData.authoringRevision ==
            int.MaxValue
        )
        {
            EditorUtility.DisplayDialog(
                "Regional Elevation Initialization Failed",
                "TerrainAuthoringData.authoringRevision has reached the " +
                "maximum Int32 value and cannot be advanced safely.",
                "OK"
            );

            return;
        }

        string undoLabel =
            existingSource == null
                ? "Initialize Regional Elevation"
                : "Reinitialize Regional Elevation";

        Undo.RecordObject(
            terrainAuthoringData,
            undoLabel
        );

        terrainAuthoringData
            .SetRegionalElevationSourceInternal(
                generatedSource
            );

        /*
         * authoringRevision is also used by the current Height Authoring UI
         * as an initialized-heightfield signal. Before the first committed
         * heightfield exists, keep revision at zero so creating regional setup
         * data does not make the heightfield button incorrectly say
         * "Reinitialize". Once a committed base exists, regional source
         * creation/replacement is a normal authoring change and advances the
         * overall authoring revision.
         */
        if (hasCommittedHeightfield)
        {
            terrainAuthoringData.authoringRevision =
                Mathf.Max(
                    0,
                    terrainAuthoringData.authoringRevision
                )
                +
                1;
        }

        EditorUtility.SetDirty(
            terrainAuthoringData
        );

        AssetDatabase.SaveAssetIfDirty(
            terrainAuthoringData
        );

        Undo.FlushUndoRecordObjects();

        Selection.activeObject =
            terrainAuthoringData;

        regionalElevationSetupBoundAuthoringData =
            terrainAuthoringData;

        inputRegionalElevationMethod =
            RegionalElevationSetupMethod.ElevationNodes;

        Repaint();

        Debug.Log(
            "Regional elevation node initialization complete.\n\n" +
            $"World Divisions: {divisionsX} x {divisionsZ}\n" +
            $"Generated Nodes: {generatedSource.NodeCount:N0}\n" +
            $"Initial Elevation: {inputNodeInitialElevation:R}\n" +
            $"Authoring Revision: " +
            $"{terrainAuthoringData.authoringRevision}\n\n" +
            "The grid is initialization-only; generated nodes are now " +
            "independent persistent authoring data.\n\n" +
            "Package 2 does not evaluate these nodes against terrain."
        );
    }

    private static bool IsFiniteRegionalElevationInput(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }
}
