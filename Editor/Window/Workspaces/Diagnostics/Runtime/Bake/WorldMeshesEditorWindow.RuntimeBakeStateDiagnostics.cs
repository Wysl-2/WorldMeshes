using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRuntimeBakeStateDiagnostics()
    {
        TerrainRuntimeBakeStateSummary summary =
            GetRuntimeBakeDiagnosticsSummary();

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
            summary
                .SerializedVersion
                .ToString()
        );

        EditorGUILayout.LabelField(
            "State Revision",
            summary
                .StateRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Observed Authoring Signature",
            string.IsNullOrEmpty(
                summary.LastObservedAuthoringSignature
            )
                ? "Not Recorded"
                : summary.LastObservedAuthoringSignature
        );

        EditorGUILayout.LabelField(
            "Has Pending Work",
            summary.HasPendingWork
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
            summary
                .PendingHeightTileCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Height Streaming Tiles",
            summary
                .PendingHeightStreamingTileCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Surface Tiles",
            summary
                .PendingSurfaceTileCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Collision Chunks",
            summary
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
                summary
                    .FullHeightRebuildRequired
            )
        );

        EditorGUILayout.LabelField(
            "Height Streaming",
            GetRuntimeBakeStateYesNo(
                summary
                    .FullHeightStreamingRebuildRequired
            )
        );

        EditorGUILayout.LabelField(
            "Surface Masks",
            GetRuntimeBakeStateYesNo(
                summary
                    .FullSurfaceRebuildRequired
            )
        );

        EditorGUILayout.LabelField(
            "Collision",
            GetRuntimeBakeStateYesNo(
                summary
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
                summary
                    .AddressablesConfigurationDirty
            )
        );

        EditorGUILayout.LabelField(
            "Addressables Content",
            GetRuntimeBakeStateCleanDirty(
                summary
                    .AddressablesContentDirty
            )
        );

        EditorGUILayout.LabelField(
            "Runtime Scene",
            GetRuntimeBakeStateCleanDirty(
                summary
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
            TerrainRuntimeBakeStateSnapshot detailedSnapshot =
                TerrainRuntimeBakeStateService
                    .GetSnapshot();

            Debug.Log(
                BuildRuntimeBakeStateDiagnosticReport(
                    detailedSnapshot
                )
            );
        }

        EditorGUI.BeginDisabledGroup(
            !summary.HasPendingWork
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
            "Persistent runtime bake state is independent from transient " +
            "terrain preview dirtiness and from the existing successful-" +
            "generation revision/signature state. Package 02 authoring and " +
            "settings invalidation now populate this queue. Existing legacy " +
            "bake commands still do not consume or clear it.",
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
            "Observed Authoring Signature: " +
            (
                string.IsNullOrEmpty(
                    snapshot.LastObservedAuthoringSignature
                )
                    ? "Not Recorded"
                    : snapshot.LastObservedAuthoringSignature
            )
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
            "Height Streaming Tiles",
            snapshot.PendingHeightStreamingTiles
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
            "Full Height Streaming Rebuild: " +
            snapshot.FullHeightStreamingRebuildRequired
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
