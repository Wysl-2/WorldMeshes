using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainAuthoringStateUtility
{
    // =====================================================
    // VERSION
    // =====================================================

    /*
     * Increment this whenever the meaning of the committed
     * authoring-state signature changes.
     */
    public const int AuthoringStateVersion =
        1;

    // =====================================================
    // AUTHORING TILE PATH / NAME
    // =====================================================

    public static string GetAuthoringHeightTilePath(
        int tileX,
        int tileZ
    )
    {
        return
            $"{WorldMeshesPaths.AuthoringHeightTiles}/" +
            $"{GetAuthoringHeightTileName(tileX, tileZ)}" +
            ".asset";
    }

    public static string GetAuthoringHeightTileName(
        int tileX,
        int tileZ
    )
    {
        return
            $"HeightTile_{tileX}_{tileZ}";
    }

    // =====================================================
    // LIGHTWEIGHT AUTHORING SIGNATURE
    // =====================================================

    /*
     * This signature is intentionally cheap to calculate.
     *
     * It is used by editor UI generation-state checks, which
     * may run frequently while the WorldMeshes window is open.
     *
     * Normal authoring tools must increment authoringRevision
     * whenever the authored terrain result changes.
     *
     * The compiler separately calculates a content hash across
     * the committed height-tile assets when compilation occurs.
     */
    public static string GetCurrentAuthoringSignature(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "WorldMeshesTerrainAuthoringState"
        );

        AppendValue(
            builder,
            AuthoringStateVersion
        );

        AppendValue(
            builder,
            Mathf.Max(
                0,
                authoringData.authoringRevision
            )
        );

        // -------------------------------------------------
        // World layout
        // -------------------------------------------------

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.gridWidth
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.gridHeight
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            )
        );

        // -------------------------------------------------
        // Heightfield layout
        // -------------------------------------------------

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.heightTileChunkSpan
            )
        );

        AppendValue(
            builder,
            worldSettings.HeightTileGridWidth
        );

        AppendValue(
            builder,
            worldSettings.HeightTileGridHeight
        );

        AppendValue(
            builder,
            worldSettings.HeightTileSamplesPerSide
        );

        return ComputeSHA256(
            builder.ToString()
        );
    }

    // =====================================================
    // VALIDATE COMMITTED AUTHORING HEIGHTFIELD
    // =====================================================

    public static bool TryValidateCommittedHeightfield(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";

            return false;
        }

        if (
            authoringData.authoringRevision <= 0
        )
        {
            errorMessage =
                "The authoring heightfield has not been " +
                "successfully initialized yet.";

            return false;
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.AuthoringHeightTiles
            )
        )
        {
            errorMessage =
                "The authoring height-tile folder does not exist:\n" +
                WorldMeshesPaths.AuthoringHeightTiles;

            return false;
        }

        int tileGridWidth =
            worldSettings.HeightTileGridWidth;

        int tileGridHeight =
            worldSettings.HeightTileGridHeight;

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        int expectedSampleCount =
            samplesPerSide *
            samplesPerSide;

        for (
            int tileZ = 0;
            tileZ < tileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < tileGridWidth;
                tileX++
            )
            {
                string path =
                    GetAuthoringHeightTilePath(
                        tileX,
                        tileZ
                    );

                Texture2D texture =
                    AssetDatabase
                        .LoadAssetAtPath<Texture2D>(
                            path
                        );

                if (texture == null)
                {
                    errorMessage =
                        "A required committed authoring height tile " +
                        "is missing:\n" +
                        path;

                    return false;
                }

                if (
                    texture.width !=
                        samplesPerSide
                    ||
                    texture.height !=
                        samplesPerSide
                )
                {
                    errorMessage =
                        $"Authoring height tile ({tileX}, {tileZ}) " +
                        "has the wrong dimensions.\n\n" +

                        $"Expected: {samplesPerSide} x " +
                        $"{samplesPerSide}\n" +

                        $"Actual: {texture.width} x " +
                        $"{texture.height}\n\n" +

                        $"Path:\n{path}";

                    return false;
                }

                if (
                    texture.format !=
                    TextureFormat.RFloat
                )
                {
                    errorMessage =
                        $"Authoring height tile ({tileX}, {tileZ}) " +
                        "does not use TextureFormat.RFloat.\n\n" +

                        $"Actual Format: {texture.format}\n\n" +

                        $"Path:\n{path}";

                    return false;
                }

                NativeArray<float> heightData;

                try
                {
                    heightData =
                        texture.GetPixelData<float>(
                            0
                        );
                }
                catch (
                    System.Exception exception
                )
                {
                    errorMessage =
                        $"Could not read authoring height tile " +
                        $"({tileX}, {tileZ}).\n\n" +
                        exception.Message +
                        "\n\nPath:\n" +
                        path;

                    return false;
                }

                if (
                    heightData.Length !=
                    expectedSampleCount
                )
                {
                    errorMessage =
                        $"Authoring height tile ({tileX}, {tileZ}) " +
                        "contains the wrong number of samples.\n\n" +

                        $"Expected: {expectedSampleCount:N0}\n" +
                        $"Actual: {heightData.Length:N0}\n\n" +

                        $"Path:\n{path}";

                    return false;
                }

                for (
                    int index = 0;
                    index < heightData.Length;
                    index++
                )
                {
                    float height =
                        heightData[index];

                    if (
                        float.IsNaN(height)
                        ||
                        float.IsInfinity(height)
                    )
                    {
                        errorMessage =
                            $"Authoring height tile ({tileX}, {tileZ}) " +
                            "contains an invalid height sample.\n\n" +

                            $"Sample Index: {index}\n" +
                            $"Value: {height}\n\n" +

                            $"Path:\n{path}";

                        return false;
                    }
                }
            }
        }

        return true;
    }

    // =====================================================
    // COMMITTED CONTENT HASH
    // =====================================================

    /*
     * This is intentionally more expensive than
     * GetCurrentAuthoringSignature().
     *
     * It is intended for explicit compile/bake operations,
     * not per-frame or per-OnGUI status checks.
     */
    public static bool TryGetCurrentAuthoringContentHash(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        out string contentHash,
        out string errorMessage
    )
    {
        contentHash =
            "";

        if (
            !TryValidateCommittedHeightfield(
                worldSettings,
                authoringData,
                out errorMessage
            )
        )
        {
            return false;
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            GetCurrentAuthoringSignature(
                worldSettings,
                authoringData
            )
        );

        int tileGridWidth =
            worldSettings.HeightTileGridWidth;

        int tileGridHeight =
            worldSettings.HeightTileGridHeight;

        for (
            int tileZ = 0;
            tileZ < tileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < tileGridWidth;
                tileX++
            )
            {
                string path =
                    GetAuthoringHeightTilePath(
                        tileX,
                        tileZ
                    );

                Hash128 dependencyHash =
                    AssetDatabase
                        .GetAssetDependencyHash(
                            path
                        );

                AppendValue(
                    builder,
                    tileX
                );

                AppendValue(
                    builder,
                    tileZ
                );

                builder.Append('|');

                builder.Append(
                    dependencyHash.ToString()
                );
            }
        }

        contentHash =
            ComputeSHA256(
                builder.ToString()
            );

        errorMessage =
            "";

        return true;
    }

    // =====================================================
    // SIGNATURE HELPERS
    // =====================================================

    private static void AppendValue(
        StringBuilder builder,
        int value
    )
    {
        builder.Append('|');

        builder.Append(
            value.ToString(
                CultureInfo.InvariantCulture
            )
        );
    }

    private static void AppendValue(
        StringBuilder builder,
        float value
    )
    {
        builder.Append('|');

        builder.Append(
            value.ToString(
                "R",
                CultureInfo.InvariantCulture
            )
        );
    }

    private static string ComputeSHA256(
        string value
    )
    {
        byte[] inputBytes =
            Encoding.UTF8.GetBytes(
                value
            );

        byte[] hashBytes;

        using (
            SHA256 sha256 =
                SHA256.Create()
        )
        {
            hashBytes =
                sha256.ComputeHash(
                    inputBytes
                );
        }

        StringBuilder result =
            new StringBuilder(
                hashBytes.Length * 2
            );

        for (
            int index = 0;
            index < hashBytes.Length;
            index++
        )
        {
            result.Append(
                hashBytes[index].ToString(
                    "x2"
                )
            );
        }

        return result.ToString();
    }
}
