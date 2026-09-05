using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void ShowAddStampModifierMenu()
    {
        TerrainHeightStampLibraryScanResult scan =
            TerrainHeightStampLibraryUtility
                .ScanLibrary();

        if (
            scan == null
            ||
            scan.StampAssets == null
        )
        {
            SetModifierAuthoringError(
                "The terrain stamp library could not be scanned."
            );

            return;
        }

        List<TerrainHeightStampAsset> assets =
            new List<TerrainHeightStampAsset>();

        for (
            int index = 0;
            index < scan.StampAssets.Count;
            index++
        )
        {
            TerrainHeightStampAsset asset =
                scan.StampAssets[index];

            if (
                asset != null
                &&
                asset.HasLibraryId
            )
            {
                assets.Add(
                    asset
                );
            }
        }

        assets.Sort(
            CompareStampAssetsForCreationMenu
        );

        if (assets.Count <= 0)
        {
            SetModifierAuthoringError(
                "No valid terrain height stamps are available in the stamp library."
            );

            return;
        }

        ClearModifierAuthoringError();

        GenericMenu menu =
            new GenericMenu();

        for (
            int index = 0;
            index < assets.Count;
            index++
        )
        {
            TerrainHeightStampAsset stampAsset =
                assets[index];

            menu.AddItem(
                new GUIContent(
                    stampAsset.DisplayName
                ),
                false,
                () =>
                    AddStampModifierFromAssetDefaults(
                        stampAsset
                    )
            );
        }

        menu.ShowAsContext();
    }

    private void AddStampModifierFromAssetDefaults(
        TerrainHeightStampAsset stampAsset
    )
    {
        if (
            stampAsset == null
            ||
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            SetModifierAuthoringError(
                "A valid stamp asset and terrain authoring context are required."
            );

            return;
        }

        Vector2 positionXZ =
            GetDefaultNewStampPositionXZ();

        if (
            !TerrainAuthoringModifierService
                .AddStampModifierFromAssetDefaults(
                    terrainAuthoringData,
                    worldSettings,
                    stampAsset,
                    positionXZ,
                    out string stableId,
                    out string errorMessage
                )
        )
        {
            SetModifierAuthoringError(
                errorMessage
            );

            return;
        }

        TerrainAuthoringModifierSelection
            .Select(
                stableId
            );

        OnModifierMutationSucceeded();
    }

    private static int CompareStampAssetsForCreationMenu(
        TerrainHeightStampAsset left,
        TerrainHeightStampAsset right
    )
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int idComparison =
            left.LibraryId.CompareTo(
                right.LibraryId
            );

        if (idComparison != 0)
        {
            return idComparison;
        }

        return
            string.CompareOrdinal(
                left.DisplayName,
                right.DisplayName
            );
    }
}
