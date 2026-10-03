using System;
using System.IO;

public static class TerrainRuntimeSurfaceMaskAssetUtility
{
    public const string SurfaceMaskRootFolder =
        WorldMeshesPaths.GeneratedSurfaceMasks;

    public const string SurfaceMaskTileFolder =
        WorldMeshesPaths.SurfaceMaskTiles;

    public const string SurfaceMaskStreamingFolder =
        WorldMeshesPaths.SurfaceMaskStreaming;

    public const string SurfaceMaskManifestPath =
        WorldMeshesPaths.SurfaceMaskManifestAssetPath;

    // =====================================================
    // AUTHORITATIVE SURFACE TILES
    // =====================================================

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

    // =====================================================
    // DERIVED STREAMING SURFACE TILES
    // =====================================================

    public static string GetStreamingStrideFolder(
        int sampleStride
    )
    {
        ValidateDerivedStride(
            sampleStride
        );

        return
            $"{SurfaceMaskStreamingFolder}/" +
            $"Stride_{sampleStride}";
    }

    public static string GetStreamingSurfaceTilePath(
        int sampleStride,
        int tileX,
        int tileZ
    )
    {
        return
            $"{GetStreamingStrideFolder(sampleStride)}/" +
            $"{GetSurfaceTileName(tileX, tileZ)}.asset";
    }

    public static string GetSurfaceRepresentationPath(
        int sampleStride,
        int tileX,
        int tileZ
    )
    {
        if (sampleStride == 1)
        {
            return
                GetSurfaceTilePath(
                    tileX,
                    tileZ
                );
        }

        return
            GetStreamingSurfaceTilePath(
                sampleStride,
                tileX,
                tileZ
            );
    }

    public static bool TryGetStreamingSurfaceTileIdentity(
        string assetPath,
        out int sampleStride,
        out int tileX,
        out int tileZ
    )
    {
        sampleStride =
            0;

        tileX =
            0;

        tileZ =
            0;

        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        string normalizedPath =
            NormalizePath(
                assetPath
            );

        string directory =
            NormalizePath(
                Path.GetDirectoryName(
                    normalizedPath
                )
            );

        string strideFolder =
            Path.GetFileName(
                directory
            );

        string streamingRoot =
            NormalizePath(
                Path.GetDirectoryName(
                    directory
                )
            );

        if (
            !string.Equals(
                streamingRoot,
                SurfaceMaskStreamingFolder,
                StringComparison.Ordinal
            )
            ||
            string.IsNullOrEmpty(
                strideFolder
            )
            ||
            !strideFolder.StartsWith(
                "Stride_",
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        string strideText =
            strideFolder.Substring(
                "Stride_".Length
            );

        if (
            !int.TryParse(
                strideText,
                out sampleStride
            )
            ||
            sampleStride <= 1
            ||
            !TerrainSurfaceStreamingPyramidPolicy
                .IsPowerOfTwo(
                    sampleStride
                )
        )
        {
            sampleStride =
                0;

            return false;
        }

        if (
            !TryGetSurfaceTileCoordinates(
                normalizedPath,
                out tileX,
                out tileZ
            )
        )
        {
            sampleStride =
                0;

            return false;
        }

        return true;
    }

    public static bool TryGetSurfaceRepresentationIdentity(
        string assetPath,
        out int sampleStride,
        out int tileX,
        out int tileZ
    )
    {
        sampleStride =
            0;

        tileX =
            0;

        tileZ =
            0;

        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        string normalizedPath =
            NormalizePath(
                assetPath
            );

        string directory =
            NormalizePath(
                Path.GetDirectoryName(
                    normalizedPath
                )
            );

        if (
            string.Equals(
                directory,
                SurfaceMaskTileFolder,
                StringComparison.Ordinal
            )
            &&
            TryGetSurfaceTileCoordinates(
                normalizedPath,
                out tileX,
                out tileZ
            )
        )
        {
            sampleStride =
                1;

            return true;
        }

        return
            TryGetStreamingSurfaceTileIdentity(
                normalizedPath,
                out sampleStride,
                out tileX,
                out tileZ
            );
    }

    private static void ValidateDerivedStride(
        int sampleStride
    )
    {
        if (
            sampleStride <= 1
            ||
            !TerrainSurfaceStreamingPyramidPolicy
                .IsPowerOfTwo(
                    sampleStride
                )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride),
                sampleStride,
                "A derived streaming Surface stride must be a power of two greater than one."
            );
        }
    }

    private static string NormalizePath(
        string path
    )
    {
        return
            string.IsNullOrEmpty(path)
                ? ""
                : path.Replace(
                    '\\',
                    '/'
                );
    }
}
