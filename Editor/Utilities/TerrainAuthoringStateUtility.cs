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

    public const int AuthoringStateVersion =
        2;

    // =====================================================
    // AUTHORING MANIFEST
    // =====================================================

    public static TerrainAuthoringHeightManifest
        LoadAuthoringHeightManifest()
    {
        return
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringHeightManifest>(
                    WorldMeshesPaths
                        .AuthoringHeightManifestAssetPath
                );
    }

    public static TerrainAuthoringHeightManifest
        GetOrCreateAuthoringHeightManifest()
    {
        TerrainAuthoringHeightManifest manifest =
            LoadAuthoringHeightManifest();

        if (manifest != null)
        {
            return manifest;
        }

        EnsureAuthoringFoldersExist();

        manifest =
            ScriptableObject
                .CreateInstance<TerrainAuthoringHeightManifest>();

        manifest.name =
            "AuthoringHeightManifest";

        manifest.manifestVersion =
            TerrainAuthoringHeightManifest
                .CurrentVersion;

        manifest.isComplete =
            false;

        AssetDatabase.CreateAsset(
            manifest,
            WorldMeshesPaths
                .AuthoringHeightManifestAssetPath
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        return manifest;
    }

    public static void MarkAuthoringHeightfieldIncomplete(
        TerrainAuthoringHeightManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (manifest == null)
        {
            return;
        }

        manifest.manifestVersion =
            TerrainAuthoringHeightManifest
                .CurrentVersion;

        manifest.isComplete =
            false;

        manifest.committedContentHash =
            "";

        if (worldSettings != null)
        {
            CopyLayoutToManifest(
                manifest,
                worldSettings
            );
        }

        EditorUtility.SetDirty(
            manifest
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );
    }

    public static bool FinalizeAuthoringHeightfield(
        TerrainAuthoringHeightManifest manifest,
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        string committedContentHash,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (manifest == null)
        {
            errorMessage =
                "TerrainAuthoringHeightManifest is null.";

            return false;
        }

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
            string.IsNullOrEmpty(
                committedContentHash
            )
        )
        {
            errorMessage =
                "Committed authoring content hash is empty.";

            return false;
        }

        manifest.manifestVersion =
            TerrainAuthoringHeightManifest
                .CurrentVersion;

        CopyLayoutToManifest(
            manifest,
            worldSettings
        );

        if (
            manifest.committedHeightRevision < 0
        )
        {
            manifest.committedHeightRevision =
                0;
        }

        manifest.committedHeightRevision++;

        manifest.committedContentHash =
            committedContentHash;

        manifest.isComplete =
            true;

        EditorUtility.SetDirty(
            manifest
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        return true;
    }

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
     * This is intentionally cheap enough for editor status
     * checks. The expensive physical tile-content verification
     * is performed during explicit validation/compilation.
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

        TerrainAuthoringHeightManifest manifest =
            LoadAuthoringHeightManifest();

        if (
            manifest == null
            ||
            !manifest.isComplete
            ||
            manifest.manifestVersion !=
                TerrainAuthoringHeightManifest.CurrentVersion
            ||
            manifest.committedHeightRevision <= 0
            ||
            !ManifestMatchesWorldSettings(
                manifest,
                worldSettings
            )
            ||
            string.IsNullOrEmpty(
                manifest.committedContentHash
            )
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
            manifest.manifestVersion
        );

        AppendValue(
            builder,
            authoringData.authoringRevision
        );

        AppendValue(
            builder,
            manifest.committedHeightRevision
        );

        AppendValue(
            builder,
            worldSettings.gridWidth
        );

        AppendValue(
            builder,
            worldSettings.gridHeight
        );

        AppendValue(
            builder,
            worldSettings.chunkSize
        );

        AppendValue(
            builder,
            worldSettings.lod0Resolution
        );

        AppendValue(
            builder,
            worldSettings.heightTileChunkSpan
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

        builder.Append('|');

        builder.Append(
            manifest.committedContentHash
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
        out TerrainAuthoringHeightManifest manifest,
        out string currentContentHash,
        out string errorMessage
    )
    {
        manifest =
            null;

        currentContentHash =
            "";

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

        manifest =
            LoadAuthoringHeightManifest();

        if (manifest == null)
        {
            errorMessage =
                "The authoring heightfield manifest does not exist.\n\n" +
                "Reinitialize the authoring heightfield once to " +
                "create the new manifest.";

            return false;
        }

        if (!manifest.isComplete)
        {
            errorMessage =
                "The committed authoring heightfield is marked " +
                "incomplete.\n\n" +
                "This normally means initialization or a future " +
                "bake operation was cancelled or failed.\n\n" +
                "Reinitialize the authoring heightfield before compiling.";

            return false;
        }

        if (
            manifest.manifestVersion !=
            TerrainAuthoringHeightManifest.CurrentVersion
        )
        {
            errorMessage =
                "The authoring heightfield manifest version is " +
                "out of date.\n\n" +
                $"Expected: {TerrainAuthoringHeightManifest.CurrentVersion}\n" +
                $"Actual: {manifest.manifestVersion}\n\n" +
                "Reinitialize the authoring heightfield.";

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
            manifest.committedHeightRevision <= 0
        )
        {
            errorMessage =
                "The committed authoring heightfield does not have " +
                "a valid committed revision.";

            return false;
        }

        if (
            !ManifestMatchesWorldSettings(
                manifest,
                worldSettings
            )
        )
        {
            errorMessage =
                "The committed authoring heightfield layout does not " +
                "match the current WorldSettings.\n\n" +
                "Reinitialize the authoring heightfield for the " +
                "current world/height-tile layout.";

            return false;
        }

        if (
            !TryCalculateCommittedHeightContentHash(
                worldSettings,
                out currentContentHash,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            manifest.committedContentHash !=
            currentContentHash
        )
        {
            errorMessage =
                "The committed authoring tile assets have changed " +
                "without the authoring manifest being updated.\n\n" +
                "This prevents the compiler from treating an unknown " +
                "or partially modified tile set as valid.\n\n" +
                $"Manifest Hash:\n{manifest.committedContentHash}\n\n" +
                $"Current Hash:\n{currentContentHash}";

            return false;
        }

        return true;
    }

    // =====================================================
    // PHYSICAL TILE VALIDATION + CONTENT HASH
    // =====================================================

    /*
     * This does not require the manifest to be complete.
     * The initializer uses it after writing every tile but
     * before finalizing the authoring manifest.
     */
    public static bool TryCalculateCommittedHeightContentHash(
        WorldSettings worldSettings,
        out string contentHash,
        out string errorMessage
    )
    {
        contentHash =
            "";

        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

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

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "WorldMeshesCommittedHeightTiles"
        );

        AppendValue(
            builder,
            tileGridWidth
        );

        AppendValue(
            builder,
            tileGridHeight
        );

        AppendValue(
            builder,
            samplesPerSide
        );

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
                    texture.width != samplesPerSide
                    ||
                    texture.height != samplesPerSide
                )
                {
                    errorMessage =
                        $"Authoring height tile ({tileX}, {tileZ}) " +
                        "has the wrong dimensions.\n\n" +
                        $"Expected: {samplesPerSide} x {samplesPerSide}\n" +
                        $"Actual: {texture.width} x {texture.height}\n\n" +
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

        return true;
    }

    // =====================================================
    // MANIFEST LAYOUT
    // =====================================================

    public static bool ManifestMatchesWorldSettings(
        TerrainAuthoringHeightManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (
            manifest == null
            ||
            worldSettings == null
        )
        {
            return false;
        }

        return
            manifest.gridWidth ==
                worldSettings.gridWidth
            &&
            manifest.gridHeight ==
                worldSettings.gridHeight
            &&
            FloatMatches(
                manifest.chunkSize,
                worldSettings.chunkSize
            )
            &&
            manifest.lod0Resolution ==
                worldSettings.lod0Resolution
            &&
            manifest.heightTileChunkSpan ==
                worldSettings.heightTileChunkSpan
            &&
            manifest.heightTileGridWidth ==
                worldSettings.HeightTileGridWidth
            &&
            manifest.heightTileGridHeight ==
                worldSettings.HeightTileGridHeight
            &&
            FloatMatches(
                manifest.heightTileWorldSize,
                worldSettings.HeightTileWorldSize
            )
            &&
            manifest.heightTileSamplesPerSide ==
                worldSettings.HeightTileSamplesPerSide;
    }

    public static void CopyLayoutToManifest(
        TerrainAuthoringHeightManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (
            manifest == null
            ||
            worldSettings == null
        )
        {
            return;
        }

        manifest.gridWidth =
            worldSettings.gridWidth;

        manifest.gridHeight =
            worldSettings.gridHeight;

        manifest.chunkSize =
            worldSettings.chunkSize;

        manifest.lod0Resolution =
            worldSettings.lod0Resolution;

        manifest.heightTileChunkSpan =
            worldSettings.heightTileChunkSpan;

        manifest.heightTileGridWidth =
            worldSettings.HeightTileGridWidth;

        manifest.heightTileGridHeight =
            worldSettings.HeightTileGridHeight;

        manifest.heightTileWorldSize =
            worldSettings.HeightTileWorldSize;

        manifest.heightTileSamplesPerSide =
            worldSettings.HeightTileSamplesPerSide;
    }

    // =====================================================
    // FOLDERS
    // =====================================================

    private static void EnsureAuthoringFoldersExist()
    {
        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.Authoring
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Root,
                "Authoring"
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.AuthoringHeight
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Authoring,
                "Height"
            );
        }

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.AuthoringHeightTiles
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.AuthoringHeight,
                "Tiles"
            );
        }
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

    private static bool FloatMatches(
        float a,
        float b
    )
    {
        return
            Mathf.Abs(
                a - b
            )
            <=
            0.0001f;
    }
}
