using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class TerrainRegionalElevationMutationDiagnostics
{
    public string Operation { get; internal set; }
    public bool Changed { get; internal set; }
    public int RevisionBefore { get; internal set; }
    public int RevisionAfter { get; internal set; }
    public string CommittedSignatureBefore { get; internal set; }
    public string CommittedSignatureAfter { get; internal set; }
    public string OverallSignatureBefore { get; internal set; }
    public string OverallSignatureAfter { get; internal set; }
    public string PreviewNotificationMode { get; internal set; }
    public string RuntimeInvalidationMode { get; internal set; }
    public int NodeCountBefore { get; internal set; }
    public int NodeCountAfter { get; internal set; }

    private readonly List<Vector2Int> dirtyTiles =
        new List<Vector2Int>();

    public IReadOnlyList<Vector2Int> DirtyTiles => dirtyTiles;
    public int DirtyTileCount => dirtyTiles.Count;

    internal void SetDirtyTiles(IEnumerable<Vector2Int> source)
    {
        dirtyTiles.Clear();
        if (source != null)
        {
            dirtyTiles.AddRange(source);
        }
    }
}

/*
 * Package 5 production mutation boundary for node regional elevation.
 *
 * Normal editor UI and future Scene tools should author nodes through this
 * service rather than calling TerrainAuthoringData/TerrainNodeElevationSource/
 * TerrainElevationNode internal mutation APIs directly.
 */
public static partial class TerrainRegionalElevationService
{
    public static TerrainRegionalElevationMutationDiagnostics LastMutationDiagnostics
    {
        get;
        private set;
    }

    public static bool AddNode(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        Vector2 positionXZ,
        float elevation,
        out string stableId,
        out string errorMessage)
    {
        stableId = "";
        errorMessage = "";

        if (!ValidateFinite(positionXZ, elevation, out errorMessage))
        {
            return false;
        }

        if (!TryGetValidatedNodeSource(authoringData, out TerrainNodeElevationSource source, out errorMessage))
        {
            return false;
        }

        TerrainElevationNode node = new TerrainElevationNode();
        node.SetPositionXZInternal(positionXZ);
        node.SetElevationInternal(elevation);

        if (!ExecuteMutation(
            authoringData,
            worldSettings,
            "Add Regional Elevation Node",
            () =>
            {
                source.AddNodeInternal(node);
                source.RepairNodeStableIds();
                return true;
            },
            out errorMessage))
        {
            return false;
        }

        stableId = node.StableId;
        return !string.IsNullOrEmpty(stableId);
    }

    public static bool RemoveNode(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        out string errorMessage)
    {
        if (!TryFindNode(authoringData, stableId, out TerrainNodeElevationSource source, out _, out int index, out errorMessage))
        {
            return false;
        }

        if (source.NodeCount <= 1)
        {
            errorMessage =
                "The final regional elevation node cannot be removed while the node source remains active. " +
                "Use Remove Regional Elevation to remove the complete regional source.";
            return false;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Remove Regional Elevation Node",
            () => source.RemoveNodeAtInternal(index),
            out errorMessage);
    }

    public static bool SetNodePosition(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        Vector2 positionXZ,
        out string errorMessage)
    {
        if (!ValidateFinite(positionXZ, 0f, out errorMessage))
        {
            return false;
        }

        if (!TryFindNode(authoringData, stableId, out _, out TerrainElevationNode node, out _, out errorMessage))
        {
            return false;
        }

        if (node.PositionXZ == positionXZ)
        {
            SetNoChangeDiagnostics("Move Regional Elevation Node", authoringData, worldSettings);
            return true;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Move Regional Elevation Node",
            () =>
            {
                node.SetPositionXZInternal(positionXZ);
                return true;
            },
            out errorMessage);
    }

    public static bool SetNodeElevation(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        float elevation,
        out string errorMessage)
    {
        if (!IsFinite(elevation))
        {
            errorMessage = "Regional elevation node height must be finite.";
            return false;
        }

        if (!TryFindNode(authoringData, stableId, out _, out TerrainElevationNode node, out _, out errorMessage))
        {
            return false;
        }

        if (node.Elevation == elevation)
        {
            SetNoChangeDiagnostics("Set Regional Elevation Node Height", authoringData, worldSettings);
            return true;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Set Regional Elevation Node Height",
            () =>
            {
                node.SetElevationInternal(elevation);
                return true;
            },
            out errorMessage);
    }

    public static bool SetNode(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        Vector2 positionXZ,
        float elevation,
        out string errorMessage)
    {
        if (!ValidateFinite(positionXZ, elevation, out errorMessage))
        {
            return false;
        }

        if (!TryFindNode(authoringData, stableId, out _, out TerrainElevationNode node, out _, out errorMessage))
        {
            return false;
        }

        if (node.PositionXZ == positionXZ && node.Elevation == elevation)
        {
            SetNoChangeDiagnostics("Edit Regional Elevation Node", authoringData, worldSettings);
            return true;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Edit Regional Elevation Node",
            () =>
            {
                node.SetPositionXZInternal(positionXZ);
                node.SetElevationInternal(elevation);
                return true;
            },
            out errorMessage);
    }

    public static bool ReplaceNodeLayoutWithGrid(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        int divisionsX,
        int divisionsZ,
        float initialElevation,
        out string errorMessage)
    {
        errorMessage = "";

        if (authoringData == null || worldSettings == null)
        {
            errorMessage = "Regional elevation grid replacement received an invalid context.";
            return false;
        }

        TerrainRegionalElevationSource existing = authoringData.RegionalElevationSource;
        if (existing != null && !(existing is TerrainNodeElevationSource))
        {
            errorMessage =
                "The active regional elevation source is not a TerrainNodeElevationSource and will not be replaced.";
            return false;
        }

        if (!IsFinite(initialElevation))
        {
            errorMessage = "Initial regional elevation must be finite.";
            return false;
        }

        if (!TerrainNodeElevationLayoutUtility.TryCreateGridSource(
            worldSettings,
            divisionsX,
            divisionsZ,
            initialElevation,
            out TerrainNodeElevationSource generated,
            out errorMessage))
        {
            return false;
        }

        if (!generated.TryValidateOutputData(out errorMessage) ||
            !generated.TryValidateNodeStableIds(out errorMessage))
        {
            return false;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            existing == null ? "Initialize Regional Elevation" : "Replace Regional Elevation Grid",
            () =>
            {
                authoringData.SetRegionalElevationSourceInternal(generated);
                return true;
            },
            out errorMessage);
    }

    public static bool RemoveRegionalElevationSource(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        out string errorMessage)
    {
        errorMessage = "";

        if (authoringData == null || worldSettings == null)
        {
            errorMessage = "Regional elevation removal received an invalid context.";
            return false;
        }

        TerrainRegionalElevationSource source = authoringData.RegionalElevationSource;
        if (source == null)
        {
            SetNoChangeDiagnostics("Remove Regional Elevation", authoringData, worldSettings);
            return true;
        }

        if (!(source is TerrainNodeElevationSource))
        {
            errorMessage =
                "Package 5 manages TerrainNodeElevationSource only and will not remove an unsupported regional source type.";
            return false;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Remove Regional Elevation",
            () =>
            {
                authoringData.ClearRegionalElevationSourceInternal();
                return true;
            },
            out errorMessage);
    }

    internal static bool TryFindNode(
        TerrainAuthoringData authoringData,
        string stableId,
        out TerrainNodeElevationSource source,
        out TerrainElevationNode node,
        out int index,
        out string errorMessage)
    {
        source = null;
        node = null;
        index = -1;
        errorMessage = "";

        if (!TryGetValidatedNodeSource(authoringData, out source, out errorMessage))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(stableId))
        {
            errorMessage = "Regional elevation node StableId is empty.";
            return false;
        }

        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        for (int current = 0; current < nodes.Count; current++)
        {
            TerrainElevationNode candidate = nodes[current];
            if (candidate != null && candidate.StableId == stableId)
            {
                node = candidate;
                index = current;
                return true;
            }
        }

        errorMessage = $"Regional elevation node {stableId} was not found.";
        return false;
    }

    internal static bool IsLiveAuthoringContext(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings)
    {
        if (authoringData == null || worldSettings == null)
        {
            return false;
        }

        TerrainAuthoringData liveAuthoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath);

        WorldSettings liveWorldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath);

        return ReferenceEquals(authoringData, liveAuthoringData) &&
               ReferenceEquals(worldSettings, liveWorldSettings);
    }

    private static bool ExecuteMutation(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string undoLabel,
        Func<bool> mutation,
        out string errorMessage)
    {
        errorMessage = "";

        if (HasActiveInteractiveEdit)
        {
            errorMessage = "A regional elevation interactive edit is currently active.";
            return false;
        }

        if (authoringData == null || worldSettings == null || mutation == null)
        {
            errorMessage = "Regional elevation mutation received an invalid context.";
            return false;
        }

        if (!TerrainRegionalElevationSnapshot.TryCapture(authoringData, out TerrainRegionalElevationSnapshot before, out errorMessage))
        {
            return false;
        }

        if (!ValidateExistingNodeSource(authoringData, out errorMessage))
        {
            return false;
        }

        string committedBefore = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);
        string overallBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, authoringData);
        int revisionBefore = authoringData.authoringRevision;

        // Before the first committed heightfield exists, authoringRevision is
        // still used by the Height Authoring UI as its initialized-state signal.
        // Regional-only edits therefore keep revision unchanged until a committed
        // base exists; the overall signature still reflects their persistent data.
        bool revisionWillAdvance =
            !string.IsNullOrEmpty(committedBefore);

        if (revisionWillAdvance && revisionBefore == int.MaxValue)
        {
            errorMessage = "TerrainAuthoringData.authoringRevision reached Int32.MaxValue.";
            return false;
        }

        bool liveDerivedState =
            IsLiveAuthoringContext(authoringData, worldSettings) &&
            !string.IsNullOrEmpty(committedBefore);

        bool notifyPreview = liveDerivedState && TerrainAuthoringPreviewService.Enabled;
        bool notifyRuntime = liveDerivedState;

        TerrainRegionalElevationChangeTracker.EnsureTracked(
            authoringData,
            worldSettings,
            notifyPreview,
            notifyRuntime);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoLabel);
        Undo.RegisterCompleteObjectUndo(authoringData, undoLabel);

        bool mutationReportedChange;
        try
        {
            mutationReportedChange = mutation();
        }
        catch (Exception exception)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            errorMessage = "Regional elevation mutation failed.\n\n" + exception.Message;
            return false;
        }

        if (!mutationReportedChange)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            SetNoChangeDiagnostics(undoLabel, authoringData, worldSettings);
            return true;
        }

        if (!ValidateResultingNodeSource(authoringData, out errorMessage))
        {
            Undo.RevertAllDownToGroup(undoGroup);
            return false;
        }

        if (!TerrainRegionalElevationSnapshot.TryCapture(authoringData, out TerrainRegionalElevationSnapshot after, out errorMessage))
        {
            Undo.RevertAllDownToGroup(undoGroup);
            return false;
        }

        if (before.StateEquals(after))
        {
            Undo.RevertAllDownToGroup(undoGroup);
            SetNoChangeDiagnostics(undoLabel, authoringData, worldSettings);
            return true;
        }

        if (revisionWillAdvance)
        {
            authoringData.authoringRevision = Mathf.Max(0, revisionBefore) + 1;
        }

        EditorUtility.SetDirty(authoringData);

        HashSet<Vector2Int> dirtyTiles = new HashSet<Vector2Int>();
        if (!string.IsNullOrEmpty(committedBefore))
        {
            TerrainRegionalElevationCompositionUtility.CollectAllHeightTiles(worldSettings, dirtyTiles);
        }

        TerrainRegionalElevationChangeTracker.UpdateTrackedState(
            authoringData,
            worldSettings,
            after,
            notifyPreview,
            notifyRuntime);

        if (notifyRuntime && dirtyTiles.Count > 0)
        {
            TerrainRuntimeInvalidationService.InvalidateAuthoringHeightTiles(
                worldSettings,
                authoringData,
                dirtyTiles);
        }

        if (notifyPreview)
        {
            TerrainAuthoringPreviewService.NotifyCompositeAuthoringStateChanged(dirtyTiles);
        }

        string committedAfter = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);
        string overallAfter = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, authoringData);

        TerrainRegionalElevationMutationDiagnostics diagnostics =
            new TerrainRegionalElevationMutationDiagnostics
            {
                Operation = undoLabel,
                Changed = true,
                RevisionBefore = revisionBefore,
                RevisionAfter = authoringData.authoringRevision,
                CommittedSignatureBefore = committedBefore,
                CommittedSignatureAfter = committedAfter,
                OverallSignatureBefore = overallBefore,
                OverallSignatureAfter = overallAfter,
                PreviewNotificationMode = notifyPreview ? "WholeWorld" : "Suppressed",
                RuntimeInvalidationMode = notifyRuntime ? "WholeWorld" : "Suppressed",
                NodeCountBefore = before.NodeCount,
                NodeCountAfter = after.NodeCount
            };

        diagnostics.SetDirtyTiles(dirtyTiles);
        LastMutationDiagnostics = diagnostics;

        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(undoGroup);
        return true;
    }

    private static bool TryGetValidatedNodeSource(
        TerrainAuthoringData authoringData,
        out TerrainNodeElevationSource source,
        out string errorMessage)
    {
        source = null;
        errorMessage = "";

        if (authoringData == null)
        {
            errorMessage = "TerrainAuthoringData is null.";
            return false;
        }

        TerrainRegionalElevationSource regionalSource = authoringData.RegionalElevationSource;
        if (regionalSource == null)
        {
            errorMessage = "No regional elevation source is active.";
            return false;
        }

        source = regionalSource as TerrainNodeElevationSource;
        if (source == null)
        {
            errorMessage =
                "The active regional elevation source is not a TerrainNodeElevationSource.";
            return false;
        }

        if (!source.TryValidateOutputData(out errorMessage))
        {
            return false;
        }

        if (!source.TryValidateNodeStableIds(out errorMessage) && source.NodeCount > 0)
        {
            return false;
        }

        return true;
    }

    private static bool ValidateExistingNodeSource(
        TerrainAuthoringData authoringData,
        out string errorMessage)
    {
        errorMessage = "";
        if (authoringData == null)
        {
            errorMessage = "TerrainAuthoringData is null.";
            return false;
        }

        TerrainRegionalElevationSource source = authoringData.RegionalElevationSource;
        if (source == null)
        {
            return true;
        }

        if (!(source is TerrainNodeElevationSource nodes))
        {
            errorMessage =
                "Package 5 cannot mutate an unsupported regional elevation source type.";
            return false;
        }

        if (!nodes.TryValidateOutputData(out errorMessage))
        {
            return false;
        }

        if (nodes.NodeCount > 0 && !nodes.TryValidateNodeStableIds(out errorMessage))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateResultingNodeSource(
        TerrainAuthoringData authoringData,
        out string errorMessage)
    {
        errorMessage = "";
        TerrainRegionalElevationSource source = authoringData.RegionalElevationSource;
        if (source == null)
        {
            return true;
        }

        if (!(source is TerrainNodeElevationSource nodes))
        {
            errorMessage = "Package 5 produced an unsupported regional source type.";
            return false;
        }

        if (!nodes.TryValidateOutputData(out errorMessage))
        {
            return false;
        }

        if (nodes.NodeCount <= 0)
        {
            errorMessage =
                "An active TerrainNodeElevationSource must contain at least one node after a production mutation.";
            return false;
        }

        return nodes.TryValidateNodeStableIds(out errorMessage);
    }

    private static void SetNoChangeDiagnostics(
        string operation,
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings)
    {
        int revision = authoringData != null ? authoringData.authoringRevision : 0;
        string committed = worldSettings != null
            ? TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings)
            : "";
        string overall = authoringData != null && worldSettings != null
            ? TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, authoringData)
            : "";

        int nodeCount =
            authoringData != null && authoringData.RegionalElevationSource is TerrainNodeElevationSource source
                ? source.NodeCount
                : 0;

        LastMutationDiagnostics =
            new TerrainRegionalElevationMutationDiagnostics
            {
                Operation = operation,
                Changed = false,
                RevisionBefore = revision,
                RevisionAfter = revision,
                CommittedSignatureBefore = committed,
                CommittedSignatureAfter = committed,
                OverallSignatureBefore = overall,
                OverallSignatureAfter = overall,
                PreviewNotificationMode = "None",
                RuntimeInvalidationMode = "None",
                NodeCountBefore = nodeCount,
                NodeCountAfter = nodeCount
            };
    }

    private static bool ValidateFinite(
        Vector2 positionXZ,
        float elevation,
        out string errorMessage)
    {
        errorMessage = "";
        if (!IsFinite(positionXZ.x) || !IsFinite(positionXZ.y))
        {
            errorMessage = "Regional elevation node position must contain finite X/Z values.";
            return false;
        }

        if (!IsFinite(elevation))
        {
            errorMessage = "Regional elevation node height must be finite.";
            return false;
        }

        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
