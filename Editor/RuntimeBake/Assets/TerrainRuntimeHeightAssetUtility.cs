using System.IO;

public static class TerrainRuntimeHeightAssetUtility
{
    public const string HeightmapRootFolder =
        WorldMeshesPaths.GeneratedHeightmaps;

    public const string HeightmapTileFolder =
        WorldMeshesPaths.HeightmapTiles;

    public const string HeightmapManifestPath =
        WorldMeshesPaths.HeightmapManifestAssetPath;

    public static string GetHeightTilePath(
        int tileX,
        int tileZ
    )
    {
        return
            $"{HeightmapTileFolder}/" +
            $"{GetHeightTileName(tileX, tileZ)}.asset";
    }

    public static string GetHeightTileName(
        int tileX,
        int tileZ
    )
    {
        return
            $"HeightTile_{tileX}_{tileZ}";
    }

    public static bool TryGetHeightTileCoordinates(
        string assetPath,
        out int tileX,
        out int tileZ
    )
    {
        tileX = 0;
        tileZ = 0;

        string fileName =
            Path.GetFileNameWithoutExtension(
                assetPath
            );

        string[] parts =
            fileName.Split(
                '_'
            );

        if (
            parts.Length != 3
            ||
            parts[0] != "HeightTile"
        )
        {
            return false;
        }

        return
            int.TryParse(
                parts[1],
                out tileX
            )
            &&
            int.TryParse(
                parts[2],
                out tileZ
            );
    }
}
