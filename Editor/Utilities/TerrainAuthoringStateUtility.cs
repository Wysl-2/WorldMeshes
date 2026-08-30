using System.Collections.Generic;
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

        manifest.minimumCommittedHeight =
            0f;

        manifest.maximumCommittedHeight =
            0f;

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
        float minimumCommittedHeight,
        float maximumCommittedHeight,
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

        if (
            !IsFinite(
                minimumCommittedHeight
            )
            ||
            !IsFinite(
                maximumCommittedHeight
            )
            ||
            maximumCommittedHeight <
                minimumCommittedHeight
        )
        {
            errorMessage =
                "Committed authoring height range is invalid.";

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

        manifest.minimumCommittedHeight =
            minimumCommittedHeight;

        manifest.maximumCommittedHeight =
            maximumCommittedHeight;

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
    // COMMITTED HEIGHTFIELD SIGNATURE
    // =====================================================

    /*
     * Identity of the physical committed base heightfield only.
     *
     * This intentionally excludes TerrainAuthoringData.authoringRevision
     * and future modifier state. Non-destructive authoring edits can
     * therefore change the overall authoring signature without forcing
     * the committed GPU preview cache to be rebuilt.
     */
    public static string GetCommittedHeightfieldSignature(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
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
            "WorldMeshesCommittedHeightfieldStateV1"
        );

        AppendValue(
            builder,
            manifest.manifestVersion
        );

        AppendValue(
            builder,
            manifest.committedHeightRevision
        );

        /*
         * Heightfield layout is part of committed cache identity.
         * These values are also validated against the manifest above.
         */
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
            worldSettings.heightfieldResolutionPerChunk
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
    // LIGHTWEIGHT AUTHORING SIGNATURE
    // =====================================================

    /*
     * This is intentionally cheap enough for editor status
     * checks. The expensive physical tile-content verification
     * is performed during explicit validation/compilation.
     */
    public static string GetOverallAuthoringSignature(
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
            worldSettings.heightfieldResolutionPerChunk
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


        /*
         * Preserve the pre-Stage-11 overall signature exactly while
         * the modifier stack is empty. This avoids marking existing
         * generated runtime data stale merely because the modifier
         * data model was installed.
         *
         * Once modifiers exist, their ordered output-relevant state is
         * appended to the overall authoring signature.
         */
        if (
            authoringData.HeightModifierCount >
            0
        )
        {
            AppendModifierStackSignatureData(
                builder,
                authoringData
            );
        }

        return ComputeSHA256(
            builder.ToString()
        );
    }

    /*
     * Compatibility alias retained for existing integrations.
     *
     * New code should explicitly request the overall authoring
     * signature or the committed-heightfield signature depending on
     * which dependency it actually tracks.
     */
    public static string GetCurrentAuthoringSignature(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        return
            GetOverallAuthoringSignature(
                worldSettings,
                authoringData
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
                "create the current manifest.";

            return false;
        }

        if (!manifest.isComplete)
        {
            errorMessage =
                "The committed authoring heightfield is marked " +
                "incomplete.\n\n" +
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
                "The committed authoring heightfield does not " +
                "have a valid committed revision.";

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
                out float currentMinimumHeight,
                out float currentMaximumHeight,
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
                $"Manifest Hash:\n{manifest.committedContentHash}\n\n" +
                $"Current Hash:\n{currentContentHash}";

            return false;
        }

        if (
            !manifest.HasValidCommittedHeightRange
            ||
            !FloatMatches(
                manifest.minimumCommittedHeight,
                currentMinimumHeight
            )
            ||
            !FloatMatches(
                manifest.maximumCommittedHeight,
                currentMaximumHeight
            )
        )
        {
            errorMessage =
                "The committed authoring height range metadata does " +
                "not match the physical authoring tiles.\n\n" +
                $"Manifest Range: " +
                $"{manifest.minimumCommittedHeight:R} -> " +
                $"{manifest.maximumCommittedHeight:R}\n" +
                $"Current Range: " +
                $"{currentMinimumHeight:R} -> " +
                $"{currentMaximumHeight:R}\n\n" +
                "Reinitialize the authoring heightfield.";

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
        out float minimumHeight,
        out float maximumHeight,
        out string errorMessage
    )
    {
        contentHash =
            "";

        minimumHeight =
            float.PositiveInfinity;

        maximumHeight =
            float.NegativeInfinity;

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

        long totalSamples =
            0L;

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

                    if (!IsFinite(height))
                    {
                        errorMessage =
                            $"Authoring height tile ({tileX}, {tileZ}) " +
                            "contains an invalid height sample.\n\n" +
                            $"Sample Index: {index}\n" +
                            $"Value: {height}\n\n" +
                            $"Path:\n{path}";

                        return false;
                    }

                    minimumHeight =
                        Mathf.Min(
                            minimumHeight,
                            height
                        );

                    maximumHeight =
                        Mathf.Max(
                            maximumHeight,
                            height
                        );

                    totalSamples++;
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

        if (
            totalSamples <= 0
            ||
            !IsFinite(
                minimumHeight
            )
            ||
            !IsFinite(
                maximumHeight
            )
            ||
            maximumHeight <
                minimumHeight
        )
        {
            errorMessage =
                "No valid committed authoring height samples " +
                "were found.";

            return false;
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
            manifest.heightfieldResolutionPerChunk ==
                worldSettings.heightfieldResolutionPerChunk
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

        manifest.heightfieldResolutionPerChunk =
            worldSettings.heightfieldResolutionPerChunk;

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
    // MODIFIER SIGNATURE HELPERS
    // =====================================================

    private static void AppendModifierStackSignatureData(
        StringBuilder builder,
        TerrainAuthoringData authoringData
    )
    {
        if (
            builder == null
            ||
            authoringData == null
        )
        {
            return;
        }

        IReadOnlyList<TerrainHeightModifier> modifiers =
            authoringData.HeightModifiers;

        builder.Append(
            "|TerrainHeightModifiersV1"
        );

        AppendValue(
            builder,
            modifiers.Count
        );

        List<UnityEngine.Object> dependencies =
            new List<UnityEngine.Object>();

        for (
            int index = 0;
            index < modifiers.Count;
            index++
        )
        {
            AppendValue(
                builder,
                index
            );

            TerrainHeightModifier modifier =
                modifiers[
                    index
                ];

            if (modifier == null)
            {
                builder.Append(
                    "|NullModifier"
                );

                continue;
            }

            modifier.AppendDeterministicSignatureData(
                builder
            );

            dependencies.Clear();

            modifier.CollectSignatureDependencies(
                dependencies
            );

            AppendValue(
                builder,
                dependencies.Count
            );

            for (
                int dependencyIndex = 0;
                dependencyIndex < dependencies.Count;
                dependencyIndex++
            )
            {
                AppendModifierDependencySignature(
                    builder,
                    dependencies[
                        dependencyIndex
                    ]
                );
            }
        }
    }

    private static void AppendModifierDependencySignature(
        StringBuilder builder,
        UnityEngine.Object dependency
    )
    {
        if (builder == null)
        {
            return;
        }

        if (dependency == null)
        {
            builder.Append(
                "|NullDependency"
            );

            return;
        }

        string path =
            AssetDatabase.GetAssetPath(
                dependency
            );

        if (
            string.IsNullOrEmpty(
                path
            )
        )
        {
            /*
             * Unsaved transient references are not valid persistent
             * authoring dependencies. Keep the signature deterministic
             * without using runtime instance IDs.
             */
            builder.Append(
                "|UnsavedDependency"
            );

            builder.Append('|');

            builder.Append(
                dependency.GetType()
                    .FullName
            );

            return;
        }

        string guid =
            AssetDatabase.AssetPathToGUID(
                path
            );

        Hash128 dependencyHash =
            AssetDatabase.GetAssetDependencyHash(
                path
            );

        builder.Append(
            "|Dependency"
        );

        builder.Append('|');

        builder.Append(
            guid
        );

        builder.Append('|');

        builder.Append(
            dependencyHash.ToString()
        );
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

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
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
