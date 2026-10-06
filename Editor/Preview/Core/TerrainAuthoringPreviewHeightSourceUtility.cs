using System;
using UnityEditor;
using UnityEngine;

/*
 * Authoritative committed Height acquisition, independent of the destination
 * representation. The caller owns the transient asset reference; this utility
 * retains no loaded tiles and never creates derived authoring assets.
 */
internal static class TerrainAuthoringPreviewHeightSourceUtility
{
    internal static bool TryLoadCommittedNativeTile(
        WorldSettings worldSettings,
        Vector2Int worldTile,
        out Texture2D source,
        out string errorMessage
    )
    {
        source = null;
        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage = "WorldSettings is null.";
            return false;
        }

        if (
            worldTile.x < 0
            || worldTile.y < 0
            || worldTile.x >= worldSettings.HeightTileGridWidth
            || worldTile.y >= worldSettings.HeightTileGridHeight
        )
        {
            errorMessage =
                $"Committed Height tile {worldTile} is outside the world tile grid.";
            return false;
        }

        return TryLoadCommittedNativeTile(
            worldSettings.HeightTileSamplesPerSide,
            worldTile,
            out source,
            out errorMessage
        );
    }

    // Existing caches use the native topology captured at initialization.
    internal static bool TryLoadCommittedNativeTile(
        int nativeSamplesPerSide,
        Vector2Int worldTile,
        out Texture2D source,
        out string errorMessage
    )
    {
        source = null;
        errorMessage = "";

        if (
            nativeSamplesPerSide <= 1
            || worldTile.x < 0
            || worldTile.y < 0
        )
        {
            errorMessage = "The committed Height source topology or tile is invalid.";
            return false;
        }

        string sourcePath =
            TerrainAuthoringStateUtility.GetAuthoringHeightTilePath(
                worldTile.x,
                worldTile.y
            );

        try
        {
            using (WorldMeshesProfiler.PreviewLoadTiles.Auto())
            {
                source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Committed Height tile {worldTile} could not be loaded.\n\n" +
                sourcePath + "\n\n" + exception.Message;
            return false;
        }

        if (
            !TryValidateNativeSource(
                source,
                nativeSamplesPerSide,
                out errorMessage
            )
        )
        {
            errorMessage =
                $"Committed Height tile {worldTile}: " +
                errorMessage + "\n\n" + sourcePath;
            source = null;
            return false;
        }

        return true;
    }

    internal static bool TryValidateNativeSource(
        Texture2D source,
        int nativeSamplesPerSide,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (source == null)
        {
            errorMessage = "The native committed Height source is missing.";
            return false;
        }

        if (
            nativeSamplesPerSide <= 1
            || source.width != nativeSamplesPerSide
            || source.height != nativeSamplesPerSide
        )
        {
            errorMessage =
                "The committed Height source has unexpected native dimensions.\n\n" +
                $"Expected: {nativeSamplesPerSide} x {nativeSamplesPerSide}\n" +
                $"Actual: {source.width} x {source.height}";
            return false;
        }

        if (source.format != TextureFormat.RFloat)
        {
            errorMessage =
                "The committed Height source must use TextureFormat.RFloat.\n\n" +
                $"Actual: {source.format}";
            return false;
        }

        return true;
    }
}
