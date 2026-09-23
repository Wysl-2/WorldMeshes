using System;
using System.IO;

public static class TerrainRuntimeHeightAssetUtility
{
    public const string HeightmapRootFolder =
        WorldMeshesPaths.GeneratedHeightmaps;

    public const string HeightmapTileFolder =
        WorldMeshesPaths.HeightmapTiles;

    public const string HeightmapStreamingFolder =
        WorldMeshesPaths.HeightmapStreaming;

    public const string HeightmapManifestPath =
        WorldMeshesPaths.HeightmapManifestAssetPath;

    // =====================================================
    // AUTHORITATIVE HEIGHT TILES
    // =====================================================

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

    // =====================================================
    // DERIVED STREAMING HEIGHT TILES
    // =====================================================

    public static string GetStreamingStrideFolder(
        int sampleStride
    )
    {
        ValidateDerivedStride(
            sampleStride
        );

        return
            $"{HeightmapStreamingFolder}/" +
            $"Stride_{sampleStride}";
    }

    public static string GetStreamingHeightTilePath(
        int sampleStride,
        int tileX,
        int tileZ
    )
    {
        return
            $"{GetStreamingStrideFolder(sampleStride)}/" +
            $"{GetHeightTileName(tileX, tileZ)}.asset";
    }

    public static string GetHeightRepresentationPath(
        int sampleStride,
        int tileX,
        int tileZ
    )
    {
        if (sampleStride == 1)
        {
            return
                GetHeightTilePath(
                    tileX,
                    tileZ
                );
        }

        return
            GetStreamingHeightTilePath(
                sampleStride,
                tileX,
                tileZ
            );
    }

    public static bool TryGetStreamingHeightTileIdentity(
        string assetPath,
        out int sampleStride,
        out int tileX,
        out int tileZ
    )
    {
        sampleStride = 0;
        tileX = 0;
        tileZ = 0;

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
                HeightmapStreamingFolder,
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
            !IsPowerOfTwo(
                sampleStride
            )
        )
        {
            sampleStride = 0;
            return false;
        }

        if (
            !TryGetHeightTileCoordinates(
                normalizedPath,
                out tileX,
                out tileZ
            )
        )
        {
            sampleStride = 0;
            return false;
        }

        return true;
    }

    public static bool TryGetHeightRepresentationIdentity(
        string assetPath,
        out int sampleStride,
        out int tileX,
        out int tileZ
    )
    {
        sampleStride = 0;
        tileX = 0;
        tileZ = 0;

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
                HeightmapTileFolder,
                StringComparison.Ordinal
            )
            &&
            TryGetHeightTileCoordinates(
                normalizedPath,
                out tileX,
                out tileZ
            )
        )
        {
            sampleStride = 1;
            return true;
        }

        return
            TryGetStreamingHeightTileIdentity(
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
            !IsPowerOfTwo(
                sampleStride
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride),
                sampleStride,
                "A derived streaming height stride must be a power of two greater than one."
            );
        }
    }

    private static bool IsPowerOfTwo(
        int value
    )
    {
        return
            value > 0
            &&
            (
                value &
                (value - 1)
            ) == 0;
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
