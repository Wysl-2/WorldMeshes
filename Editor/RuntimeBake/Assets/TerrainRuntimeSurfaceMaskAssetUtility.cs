using System.IO;

public static class TerrainRuntimeSurfaceMaskAssetUtility
{
    public const string SurfaceMaskRootFolder =
        WorldMeshesPaths.GeneratedSurfaceMasks;

    public const string SurfaceMaskTileFolder =
        WorldMeshesPaths.SurfaceMaskTiles;

    public const string SurfaceMaskManifestPath =
        WorldMeshesPaths.SurfaceMaskManifestAssetPath;

    public static string GetSurfaceTilePath(
        int tileX,
        int tileZ
    )
    {
        return
            $"{SurfaceMaskTileFolder}/" +
            $"{GetSurfaceTileName(tileX, tileZ)}.asset";
    }

    public static string GetSurfaceTileName(
        int tileX,
        int tileZ
    )
    {
        return
            $"SurfaceTile_{tileX}_{tileZ}";
    }

    public static bool TryGetSurfaceTileCoordinates(
        string assetPath,
        out int tileX,
        out int tileZ
    )
    {
        tileX =
            0;

        tileZ =
            0;

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
            parts[0] !=
                "SurfaceTile"
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
