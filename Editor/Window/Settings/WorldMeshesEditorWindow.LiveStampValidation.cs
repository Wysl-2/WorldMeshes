using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawLiveStampValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Live Stamp Integration Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Status",
            TerrainLiveStampValidationUtility.StatusLabel
        );

        EditorGUILayout.HelpBox(
            TerrainLiveStampValidationUtility.StatusMessage,
            TerrainLiveStampValidationUtility.Status ==
                TerrainLiveStampValidationStatus.Failed
                ? MessageType.Error
                : TerrainLiveStampValidationUtility.Status ==
                    TerrainLiveStampValidationStatus.Passed
                    ? MessageType.Info
                    : MessageType.None
        );

        bool busy =
            TerrainLiveStampValidationUtility.IsBusy;

        GUILayout.Space(4f);

        GUILayout.Label(
            "Controlled Test Stamp",
            EditorStyles.miniBoldLabel
        );

        int controlledCount =
            TerrainLiveStampValidationUtility.ControlledStampCount;

        if (controlledCount > 0)
        {
            string[] labels =
                new string[controlledCount];

            for (
                int index = 0;
                index < controlledCount;
                index++
            )
            {
                labels[index] =
                    TerrainLiveStampValidationUtility
                        .GetControlledStampLabel(index);
            }

            int selected =
                TerrainLiveStampValidationUtility
                    .SelectedControlledIndex;

            int newSelected =
                EditorGUILayout.Popup(
                    "Selected",
                    selected,
                    labels
                );

            if (newSelected != selected)
            {
                TerrainLiveStampValidationUtility
                    .SelectedControlledIndex =
                    newSelected;
            }
        }
        else
        {
            EditorGUILayout.LabelField(
                "Selected",
                "None"
            );
        }

        TerrainHeightStampAsset stampAsset =
            (TerrainHeightStampAsset)
            EditorGUILayout.ObjectField(
                "Test Stamp Asset",
                TerrainLiveStampValidationUtility
                    .PendingStampAsset,
                typeof(TerrainHeightStampAsset),
                false
            );

        if (
            stampAsset !=
            TerrainLiveStampValidationUtility
                .PendingStampAsset
        )
        {
            TerrainLiveStampValidationUtility
                .PendingStampAsset =
                stampAsset;
        }

        Vector2 position =
            TerrainLiveStampValidationUtility
                .PendingPositionXZ;

        Vector2 newPosition =
            new Vector2(
                EditorGUILayout.FloatField(
                    "Position X",
                    position.x
                ),
                EditorGUILayout.FloatField(
                    "Position Z",
                    position.y
                )
            );

        if (newPosition != position)
        {
            TerrainLiveStampValidationUtility
                .PendingPositionXZ =
                newPosition;
        }

        Vector2 size =
            TerrainLiveStampValidationUtility
                .PendingSizeXZ;

        Vector2 newSize =
            new Vector2(
                EditorGUILayout.FloatField(
                    "Size X",
                    size.x
                ),
                EditorGUILayout.FloatField(
                    "Size Z",
                    size.y
                )
            );

        if (newSize != size)
        {
            TerrainLiveStampValidationUtility
                .PendingSizeXZ =
                newSize;
        }

        float heightDelta =
            EditorGUILayout.FloatField(
                "Height Delta",
                TerrainLiveStampValidationUtility
                    .PendingHeightDelta
            );

        if (
            !Mathf.Approximately(
                heightDelta,
                TerrainLiveStampValidationUtility
                    .PendingHeightDelta
            )
        )
        {
            TerrainLiveStampValidationUtility
                .PendingHeightDelta =
                heightDelta;
        }

        float falloff =
            EditorGUILayout.Slider(
                "Falloff",
                TerrainLiveStampValidationUtility
                    .PendingFalloff,
                0f,
                1f
            );

        if (
            !Mathf.Approximately(
                falloff,
                TerrainLiveStampValidationUtility
                    .PendingFalloff
            )
        )
        {
            TerrainLiveStampValidationUtility
                .PendingFalloff =
                falloff;
        }

        bool enabled =
            EditorGUILayout.Toggle(
                "Enabled",
                TerrainLiveStampValidationUtility
                    .PendingEnabled
            );

        if (
            enabled !=
            TerrainLiveStampValidationUtility
                .PendingEnabled
        )
        {
            TerrainLiveStampValidationUtility
                .PendingEnabled =
                enabled;
        }

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            busy
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Add Test Stamp",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestAddTestStamp();
        }

        EditorGUI.BeginDisabledGroup(
            controlledCount <= 0
        );

        if (
            GUILayout.Button(
                "Move Test Stamp",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestMoveTestStamp();
        }

        if (
            GUILayout.Button(
                "Apply Test Parameters",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestApplyParameters();
        }

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Move Earlier"))
        {
            TerrainLiveStampValidationUtility
                .RequestReorderEarlier();
        }

        if (GUILayout.Button("Move Later"))
        {
            TerrainLiveStampValidationUtility
                .RequestReorderLater();
        }

        GUILayout.EndHorizontal();

        if (
            GUILayout.Button(
                "Remove Test Stamp",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestRemoveTestStamp();
        }

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Undo Test Edit"))
        {
            TerrainLiveStampValidationUtility
                .RequestUndo();
        }

        if (GUILayout.Button("Redo Test Edit"))
        {
            TerrainLiveStampValidationUtility
                .RequestRedo();
        }

        GUILayout.EndHorizontal();

        if (
            GUILayout.Button(
                "Validate Selected Shared Borders",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestValidateSelectedBorders();
        }

        EditorGUI.EndDisabledGroup();

        if (
            GUILayout.Button(
                "Center Pending Position In World",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .ResetPendingPositionToWorldCenter();
        }

        if (
            GUILayout.Button(
                "Reset Test State",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainLiveStampValidationUtility
                .RequestResetTestState();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(6f);

        TerrainLiveStampValidationBaseline baseline =
            TerrainLiveStampValidationUtility.LastBaseline;

        if (baseline != null)
        {
            GUILayout.Label(
                "Last Baseline",
                EditorStyles.miniBoldLabel
            );

            EditorGUILayout.LabelField(
                "Cache Texture ID",
                baseline.CacheTextureId.ToString()
            );

            EditorGUILayout.LabelField(
                "Full Cache Builds",
                baseline.FullCacheBuilds.ToString()
            );

            EditorGUILayout.LabelField(
                "Incremental Slice Updates",
                baseline
                    .TotalIncrementalSliceUpdates
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Composite Tiles",
                baseline
                    .TotalCompositeTileCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Renderer Bindings",
                baseline
                    .RendererBindingCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Authoring Revision",
                baseline
                    .AuthoringRevision
                    .ToString()
            );
        }

        TerrainLiveStampValidationReport report =
            TerrainLiveStampValidationUtility.LastReport;

        if (report != null)
        {
            GUILayout.Space(5f);

            GUILayout.Label(
                "Last Transaction",
                EditorStyles.miniBoldLabel
            );

            EditorGUILayout.LabelField(
                "Operation",
                report.Operation
            );

            EditorGUILayout.LabelField(
                "Dirty Tiles",
                report
                    .ExpectedDirtyTileCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Incremental Slices",
                report
                    .ActualIncrementalSliceCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Composited Tiles",
                report
                    .ActualCompositeTileCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Modifiers Considered",
                report
                    .ModifierConsideredCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Modifiers Dispatched",
                report
                    .ModifierDispatchCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Compute Dispatches",
                report
                    .ComputeDispatchCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Dirty Tile Set"
            );

            EditorGUILayout.TextArea(
                report.DirtyTileSummary,
                GUILayout.MinHeight(34f)
            );

            EditorGUILayout.HelpBox(
                report.Details,
                report.Passed
                    ? MessageType.Info
                    : MessageType.Error
            );
        }

        GUILayout.Space(5f);

        GUILayout.Label(
            "Shared Border Readback",
            EditorStyles.miniBoldLabel
        );

        EditorGUILayout.HelpBox(
            TerrainLiveStampValidationUtility
                .BorderValidationSummary,
            TerrainLiveStampValidationUtility
                .BorderValidationPassed
                ? MessageType.Info
                : MessageType.None
        );

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "This is a controlled integration-validation workflow, not "
            +
            "the production modifier authoring UI. Persistent modifier "
            +
            "changes are routed through TerrainAuthoringModifierService. "
            +
            "Each requested edit automatically captures cache/signature/"
            +
            "revision diagnostics and checks the settled incremental "
            +
            "preview transaction.\n\n"
            +
            "Visual deformation, visualization-mode agreement, and crack "
            +
            "inspection still require Scene View inspection.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
