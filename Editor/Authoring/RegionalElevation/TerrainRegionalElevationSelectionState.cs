using System;
using System.Collections.Generic;

/*
 * Package 7 editor-only regional node selection state.
 *
 * Selection is scoped to one TerrainAuthoringData instance and is identified
 * exclusively by node StableIds. The current selection is a transient editor
 * group; no selection/group data is persisted into terrain authoring data.
 */
public static class TerrainRegionalElevationSelectionState
{
    private static int selectedAuthoringDataInstanceId;
    private static readonly HashSet<string> selectedStableIds =
        new HashSet<string>(StringComparer.Ordinal);
    private static string primaryStableId = "";

    public static event Action SelectionChanged;

    public static int GetSelectedCount(TerrainAuthoringData authoringData)
    {
        EnsureContext(authoringData);
        ValidateSelection(authoringData);
        return IsCurrentContext(authoringData) ? selectedStableIds.Count : 0;
    }

    public static string GetPrimaryStableId(TerrainAuthoringData authoringData)
    {
        EnsureContext(authoringData);
        ValidateSelection(authoringData);
        return IsCurrentContext(authoringData) ? primaryStableId : "";
    }

    /* Package 6 compatibility: the former single selected StableId is now the primary. */
    public static string GetSelectedNodeStableId(TerrainAuthoringData authoringData)
    {
        return GetPrimaryStableId(authoringData);
    }

    public static bool IsSelected(
        TerrainAuthoringData authoringData,
        string stableId)
    {
        EnsureContext(authoringData);
        ValidateSelection(authoringData);
        return
            IsCurrentContext(authoringData) &&
            !string.IsNullOrEmpty(stableId) &&
            selectedStableIds.Contains(stableId);
    }

    public static void CopySelectedStableIds(
        TerrainAuthoringData authoringData,
        ICollection<string> output)
    {
        if (output == null)
        {
            return;
        }

        EnsureContext(authoringData);
        ValidateSelection(authoringData);

        if (!IsCurrentContext(authoringData) ||
            !(authoringData.RegionalElevationSource is TerrainNodeElevationSource source))
        {
            return;
        }

        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node != null && selectedStableIds.Contains(node.StableId))
            {
                output.Add(node.StableId);
            }
        }
    }

    public static bool SelectOnly(
        TerrainAuthoringData authoringData,
        string stableId,
        out string errorMessage)
    {
        errorMessage = "";
        if (!ValidateSelectionMutation(authoringData, out TerrainNodeElevationSource source, out errorMessage))
        {
            return false;
        }

        if (string.IsNullOrEmpty(stableId))
        {
            return ClearSelection(authoringData, out errorMessage);
        }

        if (!ContainsNode(source, stableId))
        {
            errorMessage = "The requested regional elevation node StableId was not found.";
            return false;
        }

        int instanceId = authoringData.GetInstanceID();
        bool unchanged =
            selectedAuthoringDataInstanceId == instanceId &&
            selectedStableIds.Count == 1 &&
            selectedStableIds.Contains(stableId) &&
            primaryStableId == stableId;

        if (unchanged)
        {
            return true;
        }

        selectedAuthoringDataInstanceId = instanceId;
        selectedStableIds.Clear();
        selectedStableIds.Add(stableId);
        primaryStableId = stableId;
        SelectionChanged?.Invoke();
        return true;
    }

    public static bool AddNode(
        TerrainAuthoringData authoringData,
        string stableId,
        out string errorMessage)
    {
        errorMessage = "";
        if (!ValidateSelectionMutation(authoringData, out TerrainNodeElevationSource source, out errorMessage))
        {
            return false;
        }

        if (string.IsNullOrEmpty(stableId) || !ContainsNode(source, stableId))
        {
            errorMessage = "The requested regional elevation node StableId was not found.";
            return false;
        }

        EnsureContext(authoringData);
        bool changed = selectedStableIds.Add(stableId);
        if (primaryStableId != stableId)
        {
            primaryStableId = stableId;
            changed = true;
        }

        if (changed)
        {
            SelectionChanged?.Invoke();
        }

        return true;
    }

    public static bool ToggleNode(
        TerrainAuthoringData authoringData,
        string stableId,
        out string errorMessage)
    {
        errorMessage = "";
        if (!ValidateSelectionMutation(authoringData, out TerrainNodeElevationSource source, out errorMessage))
        {
            return false;
        }

        if (string.IsNullOrEmpty(stableId) || !ContainsNode(source, stableId))
        {
            errorMessage = "The requested regional elevation node StableId was not found.";
            return false;
        }

        EnsureContext(authoringData);
        if (selectedStableIds.Contains(stableId))
        {
            selectedStableIds.Remove(stableId);
            if (primaryStableId == stableId)
            {
                primaryStableId = FindFirstSelectedInSourceOrder(source);
            }
        }
        else
        {
            selectedStableIds.Add(stableId);
            primaryStableId = stableId;
        }

        if (selectedStableIds.Count == 0)
        {
            primaryStableId = "";
        }

        SelectionChanged?.Invoke();
        return true;
    }

    public static bool RemoveNode(
        TerrainAuthoringData authoringData,
        string stableId,
        out string errorMessage)
    {
        errorMessage = "";
        if (!ValidateSelectionMutation(authoringData, out TerrainNodeElevationSource source, out errorMessage))
        {
            return false;
        }

        EnsureContext(authoringData);
        if (!selectedStableIds.Remove(stableId))
        {
            return true;
        }

        if (primaryStableId == stableId)
        {
            primaryStableId = FindFirstSelectedInSourceOrder(source);
        }

        if (selectedStableIds.Count == 0)
        {
            primaryStableId = "";
        }

        SelectionChanged?.Invoke();
        return true;
    }

    public static bool SetSelection(
        TerrainAuthoringData authoringData,
        IEnumerable<string> stableIds,
        string requestedPrimaryStableId,
        out string errorMessage)
    {
        errorMessage = "";
        if (!ValidateSelectionMutation(authoringData, out TerrainNodeElevationSource source, out errorMessage))
        {
            return false;
        }

        HashSet<string> requested = new HashSet<string>(StringComparer.Ordinal);
        if (stableIds != null)
        {
            foreach (string stableId in stableIds)
            {
                if (string.IsNullOrEmpty(stableId) || !ContainsNode(source, stableId))
                {
                    errorMessage = "Selection contains an invalid regional elevation StableId.";
                    return false;
                }

                requested.Add(stableId);
            }
        }

        string resolvedPrimary = "";
        if (requested.Count > 0)
        {
            if (!string.IsNullOrEmpty(requestedPrimaryStableId) && requested.Contains(requestedPrimaryStableId))
            {
                resolvedPrimary = requestedPrimaryStableId;
            }
            else
            {
                resolvedPrimary = FindFirstSelectedInSourceOrder(source, requested);
            }
        }

        EnsureContext(authoringData);
        bool changed =
            primaryStableId != resolvedPrimary ||
            selectedStableIds.Count != requested.Count ||
            !selectedStableIds.SetEquals(requested);

        if (!changed)
        {
            return true;
        }

        selectedStableIds.Clear();
        selectedStableIds.UnionWith(requested);
        primaryStableId = resolvedPrimary;
        SelectionChanged?.Invoke();
        return true;
    }

    /* Package 6 compatibility wrapper. */
    public static bool TrySelectNode(
        TerrainAuthoringData authoringData,
        string stableId,
        out string errorMessage)
    {
        return SelectOnly(authoringData, stableId, out errorMessage);
    }

    public static bool ClearSelection(
        TerrainAuthoringData authoringData,
        out string errorMessage)
    {
        errorMessage = "";

        if (TerrainRegionalElevationService.HasActiveInteractiveEdit && IsCurrentContext(authoringData))
        {
            errorMessage = "Regional node selection cannot change while an interactive edit is active.";
            return false;
        }

        if (authoringData != null && !IsCurrentContext(authoringData))
        {
            return true;
        }

        if (selectedAuthoringDataInstanceId == 0 &&
            selectedStableIds.Count == 0 &&
            string.IsNullOrEmpty(primaryStableId))
        {
            return true;
        }

        ClearInternal(true);
        return true;
    }

    internal static void ForceClearSelection(TerrainAuthoringData authoringData)
    {
        if (authoringData != null && !IsCurrentContext(authoringData))
        {
            return;
        }

        ClearInternal(true);
    }

    public static bool ValidateSelection(TerrainAuthoringData authoringData)
    {
        if (authoringData == null)
        {
            return false;
        }

        if (selectedAuthoringDataInstanceId == 0)
        {
            return false;
        }

        if (authoringData.GetInstanceID() != selectedAuthoringDataInstanceId)
        {
            ClearInternal(true);
            return false;
        }

        if (!(authoringData.RegionalElevationSource is TerrainNodeElevationSource source))
        {
            ClearInternal(true);
            return false;
        }

        bool changed = false;
        HashSet<string> valid = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node != null && selectedStableIds.Contains(node.StableId))
            {
                valid.Add(node.StableId);
            }
        }

        if (!selectedStableIds.SetEquals(valid))
        {
            selectedStableIds.Clear();
            selectedStableIds.UnionWith(valid);
            changed = true;
        }

        if (selectedStableIds.Count == 0)
        {
            if (!string.IsNullOrEmpty(primaryStableId))
            {
                primaryStableId = "";
                changed = true;
            }
        }
        else if (string.IsNullOrEmpty(primaryStableId) || !selectedStableIds.Contains(primaryStableId))
        {
            primaryStableId = FindFirstSelectedInSourceOrder(source);
            changed = true;
        }

        if (changed)
        {
            SelectionChanged?.Invoke();
        }

        return selectedStableIds.Count > 0;
    }

    private static bool ValidateSelectionMutation(
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

        if (TerrainRegionalElevationService.HasActiveInteractiveEdit)
        {
            errorMessage = "Regional node selection cannot change while an interactive edit is active.";
            return false;
        }

        if (!(authoringData.RegionalElevationSource is TerrainNodeElevationSource nodeSource))
        {
            errorMessage = "The active regional elevation source is not a node source.";
            return false;
        }

        source = nodeSource;
        EnsureContext(authoringData);
        ValidateSelection(authoringData);
        return true;
    }

    private static bool ContainsNode(TerrainNodeElevationSource source, string stableId)
    {
        if (source == null || string.IsNullOrEmpty(stableId))
        {
            return false;
        }

        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node != null && node.StableId == stableId)
            {
                return true;
            }
        }

        return false;
    }

    private static string FindFirstSelectedInSourceOrder(TerrainNodeElevationSource source)
    {
        return FindFirstSelectedInSourceOrder(source, selectedStableIds);
    }

    private static string FindFirstSelectedInSourceOrder(
        TerrainNodeElevationSource source,
        HashSet<string> selection)
    {
        if (source == null || selection == null || selection.Count == 0)
        {
            return "";
        }

        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node != null && selection.Contains(node.StableId))
            {
                return node.StableId;
            }
        }

        return "";
    }

    private static bool IsCurrentContext(TerrainAuthoringData authoringData)
    {
        return
            authoringData != null &&
            selectedAuthoringDataInstanceId != 0 &&
            authoringData.GetInstanceID() == selectedAuthoringDataInstanceId;
    }

    private static void EnsureContext(TerrainAuthoringData authoringData)
    {
        if (authoringData == null)
        {
            return;
        }

        int instanceId = authoringData.GetInstanceID();
        if (selectedAuthoringDataInstanceId == instanceId)
        {
            return;
        }

        bool hadSelection =
            selectedAuthoringDataInstanceId != 0 ||
            selectedStableIds.Count > 0 ||
            !string.IsNullOrEmpty(primaryStableId);

        selectedAuthoringDataInstanceId = instanceId;
        selectedStableIds.Clear();
        primaryStableId = "";

        if (hadSelection)
        {
            SelectionChanged?.Invoke();
        }
    }

    private static void ClearInternal(bool notify)
    {
        bool changed =
            selectedAuthoringDataInstanceId != 0 ||
            selectedStableIds.Count > 0 ||
            !string.IsNullOrEmpty(primaryStableId);

        selectedAuthoringDataInstanceId = 0;
        selectedStableIds.Clear();
        primaryStableId = "";

        if (changed && notify)
        {
            SelectionChanged?.Invoke();
        }
    }
}
