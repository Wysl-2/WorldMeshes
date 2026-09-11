using System;
using System.Collections.Generic;

/*
 * Package 6 editor-only single-node selection state.
 *
 * StableId remains the identity shared by the WorldMeshes management UI and
 * the SceneView tool. Selection never becomes persistent terrain data.
 */
public static class TerrainRegionalElevationSelectionState
{
    private static int selectedAuthoringDataInstanceId;
    private static string selectedStableId = "";

    public static event Action SelectionChanged;

    public static string GetSelectedNodeStableId(
        TerrainAuthoringData authoringData)
    {
        if (
            authoringData == null ||
            authoringData.GetInstanceID() != selectedAuthoringDataInstanceId)
        {
            return "";
        }

        ValidateSelection(authoringData);
        return selectedStableId;
    }

    public static bool TrySelectNode(
        TerrainAuthoringData authoringData,
        string stableId,
        out string errorMessage)
    {
        errorMessage = "";

        if (authoringData == null)
        {
            errorMessage = "TerrainAuthoringData is null.";
            return false;
        }

        if (string.IsNullOrEmpty(stableId))
        {
            return ClearSelection(authoringData, out errorMessage);
        }

        if (
            TerrainRegionalElevationService.HasActiveInteractiveEdit &&
            TerrainRegionalElevationService.ActiveInteractiveNodeStableId != stableId)
        {
            errorMessage =
                "Node selection cannot change while another regional node drag is active.";
            return false;
        }

        if (!(authoringData.RegionalElevationSource is TerrainNodeElevationSource source))
        {
            errorMessage = "The active regional elevation source is not a node source.";
            return false;
        }

        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        bool found = false;

        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node != null && node.StableId == stableId)
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            errorMessage = "The requested regional elevation node StableId was not found.";
            return false;
        }

        int instanceId = authoringData.GetInstanceID();
        if (
            selectedAuthoringDataInstanceId == instanceId &&
            selectedStableId == stableId)
        {
            return true;
        }

        selectedAuthoringDataInstanceId = instanceId;
        selectedStableId = stableId;
        SelectionChanged?.Invoke();
        return true;
    }

    public static bool ClearSelection(
        TerrainAuthoringData authoringData,
        out string errorMessage)
    {
        errorMessage = "";

        if (authoringData == null)
        {
            if (selectedAuthoringDataInstanceId == 0 && string.IsNullOrEmpty(selectedStableId))
            {
                return true;
            }

            selectedAuthoringDataInstanceId = 0;
            selectedStableId = "";
            SelectionChanged?.Invoke();
            return true;
        }

        if (
            TerrainRegionalElevationService.HasActiveInteractiveEdit &&
            authoringData.GetInstanceID() == selectedAuthoringDataInstanceId)
        {
            errorMessage = "Node selection cannot be cleared while a regional node drag is active.";
            return false;
        }

        if (authoringData.GetInstanceID() != selectedAuthoringDataInstanceId)
        {
            return true;
        }

        selectedAuthoringDataInstanceId = 0;
        selectedStableId = "";
        SelectionChanged?.Invoke();
        return true;
    }

    internal static void ForceClearSelection(
        TerrainAuthoringData authoringData)
    {
        if (
            authoringData != null &&
            selectedAuthoringDataInstanceId != authoringData.GetInstanceID())
        {
            return;
        }

        if (selectedAuthoringDataInstanceId == 0 && string.IsNullOrEmpty(selectedStableId))
        {
            return;
        }

        selectedAuthoringDataInstanceId = 0;
        selectedStableId = "";
        SelectionChanged?.Invoke();
    }

    public static bool ValidateSelection(
        TerrainAuthoringData authoringData)
    {
        if (
            authoringData == null ||
            authoringData.GetInstanceID() != selectedAuthoringDataInstanceId ||
            string.IsNullOrEmpty(selectedStableId))
        {
            return false;
        }

        if (!(authoringData.RegionalElevationSource is TerrainNodeElevationSource source))
        {
            ForceClearSelection(authoringData);
            return false;
        }

        IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            TerrainElevationNode node = nodes[index];
            if (node != null && node.StableId == selectedStableId)
            {
                return true;
            }
        }

        ForceClearSelection(authoringData);
        return false;
    }
}
