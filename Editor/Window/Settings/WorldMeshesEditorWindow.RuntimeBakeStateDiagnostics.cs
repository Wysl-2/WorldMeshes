using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRuntimeBakeStateDiagnostics()
    {
        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService
                .GetSnapshot();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Bake State",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Storage",
            TerrainRuntimeBakeStateService
                .PersistencePath
        );

        EditorGUILayout.LabelField(
            "State Version",
            snapshot
                .SerializedVersion
                .ToString()
        );

        EditorGUILayout.LabelField(
            "State Revision",
            snapshot
                .StateRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Has Pending Work",
            snapshot.HasPendingWork
                ? "Yes"
                : "No"
        );

        GUILayout.Space(
            5f
        );

        GUILayout.Label(
            "Pending Work",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Height Tiles",
            snapshot
                .PendingHeightTileCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Surface Tiles",
            snapshot
                .PendingSurfaceTileCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Collision Chunks",
            snapshot
                .PendingCollisionChunkCount
                .ToString()
        );

        GUILayout.Space(
            5f
        );

        GUILayout.Label(
            "Full Rebuilds",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Height",
            GetRuntimeBakeStateYesNo(
                snapshot
                    .FullHeightRebuildRequired
            )
        );

        EditorGUILayout.LabelField(
            "Surface Masks",
            GetRuntimeBakeStateYesNo(
                snapshot
                    .FullSurfaceRebuildRequired
            )
        );

        EditorGUILayout.LabelField(
            "Collision",
            GetRuntimeBakeStateYesNo(
                snapshot
                    .FullCollisionRebuildRequired
            )
        );

        GUILayout.Space(
            5f
        );

        GUILayout.Label(
            "Global State",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Addressables Config",
            GetRuntimeBakeStateCleanDirty(
                snapshot
                    .AddressablesConfigurationDirty
            )
        );

        EditorGUILayout.LabelField(
            "Addressables Content",
            GetRuntimeBakeStateCleanDirty(
                snapshot
                    .AddressablesContentDirty
            )
        );

        EditorGUILayout.LabelField(
            "Runtime Scene",
            GetRuntimeBakeStateCleanDirty(
                snapshot
                    .RuntimeSceneMetadataDirty
            )
        );

        GUILayout.Space(
            5f
        );

        if (
            GUILayout.Button(
                "Log Detailed State",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            Debug.Log(
                BuildRuntimeBakeStateDiagnosticReport(
                    snapshot
                )
            );
        }

        EditorGUI.BeginDisabledGroup(
            !snapshot.HasPendingWork
        );

        if (
            GUILayout.Button(
                "Clear Pending Bake State",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool confirmed =
                EditorUtility.DisplayDialog(
                    "Clear Pending Runtime Bake State",
                    "Clear all explicitly queued runtime bake-state " +
                    "entries and flags?\n\n" +
                    "This is a diagnostic/recovery action. It does not " +
                    "change generated runtime assets, generation " +
                    "revisions, or authoring data.",
                    "Clear Pending State",
                    "Cancel"
                );

            if (confirmed)
            {
                TerrainRuntimeBakeStateService
                    .ClearAllPendingState();

                Repaint();
            }
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Package 01 persistence foundation only. This state is " +
            "independent from transient terrain preview dirtiness and " +
            "from the existing successful-generation revision/signature " +
            "state. Terrain edits do not populate it yet, and existing " +
            "runtime bake commands do not consume or clear it yet.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }

    private static string GetRuntimeBakeStateYesNo(
        bool value
    )
    {
        return
            value
                ? "Yes"
                : "No";
    }

    private static string GetRuntimeBakeStateCleanDirty(
        bool value
    )
    {
        return
            value
                ? "Dirty"
                : "Clean";
    }

    private static string BuildRuntimeBakeStateDiagnosticReport(
        TerrainRuntimeBakeStateSnapshot snapshot
    )
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Runtime Bake State"
        );

        builder.AppendLine(
            "Storage: " +
            TerrainRuntimeBakeStateService
                .PersistencePath
        );

        builder.AppendLine(
            "State Version: " +
            snapshot.SerializedVersion
        );

        builder.AppendLine(
            "State Revision: " +
            snapshot.StateRevision
        );

        builder.AppendLine(
            "Has Pending Work: " +
            snapshot.HasPendingWork
        );

        AppendRuntimeBakeCoordinates(
            builder,
            "Height Tiles",
            snapshot.PendingHeightTiles
        );

        AppendRuntimeBakeCoordinates(
            builder,
            "Surface Tiles",
            snapshot.PendingSurfaceTiles
        );

        AppendRuntimeBakeCoordinates(
            builder,
            "Collision Chunks",
            snapshot.PendingCollisionChunks
        );

        builder.AppendLine(
            "Full Height Rebuild: " +
            snapshot.FullHeightRebuildRequired
        );

        builder.AppendLine(
            "Full Surface Rebuild: " +
            snapshot.FullSurfaceRebuildRequired
        );

        builder.AppendLine(
            "Full Collision Rebuild: " +
            snapshot.FullCollisionRebuildRequired
        );

        builder.AppendLine(
            "Addressables Configuration Dirty: " +
            snapshot.AddressablesConfigurationDirty
        );

        builder.AppendLine(
            "Addressables Content Dirty: " +
            snapshot.AddressablesContentDirty
        );

        builder.Append(
            "Runtime Scene Metadata Dirty: " +
            snapshot.RuntimeSceneMetadataDirty
        );

        return
            builder.ToString();
    }

    private static void AppendRuntimeBakeCoordinates(
        StringBuilder builder,
        string label,
        IReadOnlyList<Vector2Int> coordinates
    )
    {
        builder.Append(
            label
        );

        builder.Append(
            " ("
        );

        builder.Append(
            coordinates.Count
        );

        builder.Append(
            "): "
        );

        if (coordinates.Count == 0)
        {
            builder.AppendLine(
                "None"
            );

            return;
        }

        for (
            int index = 0;
            index < coordinates.Count;
            index++
        )
        {
            if (index > 0)
            {
                builder.Append(
                    ", "
                );
            }

            Vector2Int coordinate =
                coordinates[index];

            builder.Append(
                '('
            );

            builder.Append(
                coordinate.x
            );

            builder.Append(
                ", "
            );

            builder.Append(
                coordinate.y
            );

            builder.Append(
                ')'
            );
        }

        builder.AppendLine();
    }
}
