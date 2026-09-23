using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public enum TerrainAuthoringHeightfieldValidationMode
{
    IntegrityVerified,
    Operational
}

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

            manifest.InitializeTileHeightRanges();
        }
        else
        {
            manifest.ClearTileHeightRanges();
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
            manifest.TileHeightRangeMetadataVersion !=
            TerrainAuthoringHeightManifest
                .CurrentTileHeightRangeMetadataVersion
        )
        {
            errorMessage =
                "Committed authoring per-tile height range metadata " +
                "is not using the current metadata format.";

            return false;
        }

        if (!manifest.HasCompleteTileHeightRanges)
        {
            errorMessage =
                "Committed authoring per-tile height range metadata " +
                "is incomplete or invalid.";

            return false;
        }

        if (
            !manifest.TryCalculateGlobalHeightRange(
                out float metadataMinimumHeight,
                out float metadataMaximumHeight
            )
            ||
            !FloatMatches(
                metadataMinimumHeight,
                minimumCommittedHeight
            )
            ||
            !FloatMatches(
                metadataMaximumHeight,
                maximumCommittedHeight
            )
        )
        {
            errorMessage =
                "Committed authoring per-tile height range metadata " +
                "does not reduce to the verified physical height range.\n\n" +
                $"Metadata Range: " +
                $"{metadataMinimumHeight:R} -> " +
                $"{metadataMaximumHeight:R}\n" +
                $"Physical Range: " +
                $"{minimumCommittedHeight:R} -> " +
                $"{maximumCommittedHeight:R}";

            return false;
        }

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
        using var profilerScope =
            WorldMeshesProfiler.AuthoringSignature.Auto();

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            return "";
        }

        string signatureInput;

        using (WorldMeshesProfiler.AuthoringSignatureCollectInputs.Auto())
        {
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
             * Preserve legacy overall-signature behavior while regional
             * elevation is absent. Merely installing the regional-elevation
             * data model must not make existing generated terrain stale.
             */
            if (
                authoringData.RegionalElevationSource !=
                null
            )
            {
                if (
                    !AppendRegionalElevationSignatureData(
                        builder,
                        authoringData.RegionalElevationSource
                    )
                )
                {
                    return "";
                }
            }

            /*
             * Preserve the pre-Stage-11 overall signature exactly while
             * the modifier stack is empty and no regional source exists.
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

            signatureInput =
                builder.ToString();
        }

        using (WorldMeshesProfiler.AuthoringSignatureHash.Auto())
        {
            return ComputeSHA256(
                signatureInput
            );
        }
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
        return
            TryValidateCommittedHeightfield(
                worldSettings,
                authoringData,
                TerrainAuthoringHeightfieldValidationMode
                    .IntegrityVerified,
                out manifest,
                out currentContentHash,
                out errorMessage
            );
    }

    public static bool TryValidateCommittedHeightfield(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainAuthoringHeightfieldValidationMode validationMode,
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
            string.IsNullOrEmpty(
                manifest.committedContentHash
            )
        )
        {
            errorMessage =
                "The committed authoring heightfield does not " +
                "contain committed content metadata.";

            return false;
        }

        if (!manifest.HasValidCommittedHeightRange)
        {
            errorMessage =
                "The committed authoring heightfield does not " +
                "contain a valid committed global height range.";

            return false;
        }

        if (
            manifest.heightTileGridWidth <= 0
            ||
            manifest.heightTileGridHeight <= 0
            ||
            manifest.heightTileSamplesPerSide <= 1
        )
        {
            errorMessage =
                "The committed authoring heightfield contains an " +
                "invalid height-tile layout.";

            return false;
        }

        if (
            validationMode ==
            TerrainAuthoringHeightfieldValidationMode
                .Operational
        )
        {
            if (
                manifest.TileHeightRangeMetadataVersion !=
                TerrainAuthoringHeightManifest
                    .CurrentTileHeightRangeMetadataVersion
            )
            {
                errorMessage =
                    "Operational authoring heightfield validation " +
                    "requires current per-tile height range metadata.\n\n" +
                    $"Expected Metadata Version: " +
                    $"{TerrainAuthoringHeightManifest.CurrentTileHeightRangeMetadataVersion}\n" +
                    $"Actual Metadata Version: " +
                    $"{manifest.TileHeightRangeMetadataVersion}\n\n" +
                    "Run an integrity-verified authoring validation or " +
                    "reinitialize the authoring heightfield before " +
                    "rebuilding the preview.";

                return false;
            }

            int expectedTileRangeCount =
                manifest.ExpectedTileHeightRangeCount;

            if (
                expectedTileRangeCount <= 0
                ||
                manifest.TileHeightRangeCount !=
                    expectedTileRangeCount
                ||
                !manifest.HasCompleteTileHeightRanges
            )
            {
                errorMessage =
                    "Operational authoring heightfield validation " +
                    "found incomplete or invalid per-tile range metadata.\n\n" +
                    $"Expected Range Records: " +
                    $"{expectedTileRangeCount}\n" +
                    $"Stored Range Records: " +
                    $"{manifest.TileHeightRangeCount}\n" +
                    $"Valid Range Records: " +
                    $"{manifest.ValidTileHeightRangeCount}";

                return false;
            }

            if (
                !manifest.TryCalculateGlobalHeightRange(
                    out float metadataMinimumHeight,
                    out float metadataMaximumHeight
                )
                ||
                !FloatMatches(
                    metadataMinimumHeight,
                    manifest.minimumCommittedHeight
                )
                ||
                !FloatMatches(
                    metadataMaximumHeight,
                    manifest.maximumCommittedHeight
                )
            )
            {
                errorMessage =
                    "Operational authoring heightfield validation " +
                    "found per-tile range metadata that does not " +
                    "reduce to the committed global height range.\n\n" +
                    $"Manifest Range: " +
                    $"{manifest.minimumCommittedHeight:R} -> " +
                    $"{manifest.maximumCommittedHeight:R}\n" +
                    $"Metadata Range: " +
                    $"{metadataMinimumHeight:R} -> " +
                    $"{metadataMaximumHeight:R}";

                return false;
            }

            currentContentHash =
                manifest.committedContentHash;

            return true;
        }

        if (
            validationMode !=
            TerrainAuthoringHeightfieldValidationMode
                .IntegrityVerified
        )
        {
            errorMessage =
                $"Unsupported authoring heightfield validation mode: " +
                $"{validationMode}.";

            return false;
        }

        if (
            !TryCalculateCommittedHeightContentHash(
                worldSettings,
                out currentContentHash,
                out float currentMinimumHeight,
                out float currentMaximumHeight,
                out List<TerrainHeightTileRange>
                    currentTileHeightRanges,
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

        int tileRangeMetadataVersion =
            manifest.TileHeightRangeMetadataVersion;

        if (
            tileRangeMetadataVersion < 0
            ||
            tileRangeMetadataVersion >
                TerrainAuthoringHeightManifest
                    .CurrentTileHeightRangeMetadataVersion
        )
        {
            errorMessage =
                "The authoring heightfield uses an unsupported " +
                "per-tile height range metadata version.\n\n" +
                $"Supported: 0 -> " +
                $"{TerrainAuthoringHeightManifest.CurrentTileHeightRangeMetadataVersion}\n" +
                $"Actual: {tileRangeMetadataVersion}";

            return false;
        }

        if (tileRangeMetadataVersion == 0)
        {
            if (
                !TryStoreVerifiedTileHeightRanges(
                    manifest,
                    worldSettings,
                    currentTileHeightRanges,
                    out errorMessage
                )
                ||
                !TryValidateStoredTileHeightRanges(
                    manifest,
                    worldSettings,
                    currentTileHeightRanges,
                    currentMinimumHeight,
                    currentMaximumHeight,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }
        else if (
            !TryValidateStoredTileHeightRanges(
                manifest,
                worldSettings,
                currentTileHeightRanges,
                currentMinimumHeight,
                currentMaximumHeight,
                out errorMessage
            )
        )
        {
            return false;
        }

        return true;
    }

    // =====================================================
    // PER-TILE HEIGHT RANGE METADATA
    // =====================================================

    public static bool TryStoreVerifiedTileHeightRanges(
        TerrainAuthoringHeightManifest manifest,
        WorldSettings worldSettings,
        IReadOnlyList<TerrainHeightTileRange> verifiedRanges,
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

        if (
            !ManifestMatchesWorldSettings(
                manifest,
                worldSettings
            )
        )
        {
            errorMessage =
                "Cannot store committed per-tile height ranges because " +
                "the authoring manifest layout does not match WorldSettings.";

            return false;
        }

        int expectedCount =
            worldSettings.HeightTileGridWidth *
            worldSettings.HeightTileGridHeight;

        if (
            verifiedRanges == null
            ||
            verifiedRanges.Count != expectedCount
        )
        {
            errorMessage =
                "Verified committed per-tile height range count is invalid.\n\n" +
                $"Expected: {expectedCount}\n" +
                $"Actual: " +
                $"{(verifiedRanges != null ? verifiedRanges.Count : 0)}";

            return false;
        }

        for (
            int index = 0;
            index < verifiedRanges.Count;
            index++
        )
        {
            if (verifiedRanges[index].IsValid)
            {
                continue;
            }

            int tileX =
                index %
                worldSettings.HeightTileGridWidth;

            int tileZ =
                index /
                worldSettings.HeightTileGridWidth;

            errorMessage =
                $"Verified committed height range for tile " +
                $"({tileX}, {tileZ}) is invalid.";

            return false;
        }

        if (
            !manifest.TryReplaceTileHeightRanges(
                verifiedRanges
            )
            ||
            !manifest.HasCompleteTileHeightRanges
            ||
            !manifest.TryCalculateGlobalHeightRange(
                out _,
                out _
            )
        )
        {
            errorMessage =
                "Could not store complete committed per-tile " +
                "height range metadata.";

            return false;
        }

        EditorUtility.SetDirty(
            manifest
        );

        AssetDatabase.SaveAssetIfDirty(
            manifest
        );

        return true;
    }

    private static bool TryValidateStoredTileHeightRanges(
        TerrainAuthoringHeightManifest manifest,
        WorldSettings worldSettings,
        IReadOnlyList<TerrainHeightTileRange> physicalRanges,
        float physicalMinimumHeight,
        float physicalMaximumHeight,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            manifest.TileHeightRangeMetadataVersion !=
            TerrainAuthoringHeightManifest
                .CurrentTileHeightRangeMetadataVersion
        )
        {
            errorMessage =
                "Committed per-tile height range metadata is not " +
                "using the current metadata format.";

            return false;
        }

        if (!manifest.HasCompleteTileHeightRanges)
        {
            errorMessage =
                "Committed per-tile height range metadata is " +
                "missing, incomplete, or invalid.";

            return false;
        }

        int expectedCount =
            worldSettings.HeightTileGridWidth *
            worldSettings.HeightTileGridHeight;

        if (
            physicalRanges == null
            ||
            physicalRanges.Count != expectedCount
        )
        {
            errorMessage =
                "Physical per-tile height range validation returned " +
                "an unexpected tile count.";

            return false;
        }

        for (
            int index = 0;
            index < physicalRanges.Count;
            index++
        )
        {
            int tileX =
                index %
                worldSettings.HeightTileGridWidth;

            int tileZ =
                index /
                worldSettings.HeightTileGridWidth;

            TerrainHeightTileRange physicalRange =
                physicalRanges[index];

            if (
                !physicalRange.IsValid
                ||
                !manifest.TryGetTileHeightRange(
                    tileX,
                    tileZ,
                    out float storedMinimumHeight,
                    out float storedMaximumHeight
                )
                ||
                !FloatMatches(
                    storedMinimumHeight,
                    physicalRange.MinimumHeight
                )
                ||
                !FloatMatches(
                    storedMaximumHeight,
                    physicalRange.MaximumHeight
                )
            )
            {
                errorMessage =
                    $"Committed per-tile height range metadata for tile " +
                    $"({tileX}, {tileZ}) does not match the physical " +
                    "authoring height tile.";

                return false;
            }
        }

        if (
            !manifest.TryCalculateGlobalHeightRange(
                out float metadataMinimumHeight,
                out float metadataMaximumHeight
            )
            ||
            !FloatMatches(
                metadataMinimumHeight,
                physicalMinimumHeight
            )
            ||
            !FloatMatches(
                metadataMaximumHeight,
                physicalMaximumHeight
            )
            ||
            !FloatMatches(
                metadataMinimumHeight,
                manifest.minimumCommittedHeight
            )
            ||
            !FloatMatches(
                metadataMaximumHeight,
                manifest.maximumCommittedHeight
            )
        )
        {
            errorMessage =
                "Committed per-tile height range metadata does not " +
                "reduce to the verified committed global height range.\n\n" +
                $"Metadata Range: " +
                $"{metadataMinimumHeight:R} -> " +
                $"{metadataMaximumHeight:R}\n" +
                $"Physical Range: " +
                $"{physicalMinimumHeight:R} -> " +
                $"{physicalMaximumHeight:R}\n" +
                $"Manifest Range: " +
                $"{manifest.minimumCommittedHeight:R} -> " +
                $"{manifest.maximumCommittedHeight:R}";

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
        return
            TryCalculateCommittedHeightContentHash(
                worldSettings,
                out contentHash,
                out minimumHeight,
                out maximumHeight,
                out _,
                out errorMessage
            );
    }

    public static bool TryCalculateCommittedHeightContentHash(
        WorldSettings worldSettings,
        out string contentHash,
        out float minimumHeight,
        out float maximumHeight,
        out List<TerrainHeightTileRange> tileHeightRanges,
        out string errorMessage
    )
    {
        contentHash =
            "";

        minimumHeight =
            float.PositiveInfinity;

        maximumHeight =
            float.NegativeInfinity;

        tileHeightRanges =
            new List<TerrainHeightTileRange>();

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

        int expectedTileCount =
            tileGridWidth *
            tileGridHeight;

        tileHeightRanges.Capacity =
            expectedTileCount;

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

                float tileMinimumHeight =
                    float.PositiveInfinity;

                float tileMaximumHeight =
                    float.NegativeInfinity;

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

                    tileMinimumHeight =
                        Mathf.Min(
                            tileMinimumHeight,
                            height
                        );

                    tileMaximumHeight =
                        Mathf.Max(
                            tileMaximumHeight,
                            height
                        );

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

                TerrainHeightTileRange tileRange =
                    TerrainHeightTileRange.Create(
                        tileMinimumHeight,
                        tileMaximumHeight
                    );

                if (!tileRange.IsValid)
                {
                    errorMessage =
                        $"Could not calculate a valid committed height " +
                        $"range for authoring tile ({tileX}, {tileZ}).";

                    return false;
                }

                tileHeightRanges.Add(
                    tileRange
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

                Resources.UnloadAsset(
                    texture
                );
            }
        }

        if (
            totalSamples <= 0
            ||
            tileHeightRanges.Count !=
                expectedTileCount
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



    /*
     * Stage 12 per-modifier snapshot signature.
     */
    internal static string GetModifierContentSignature(
        TerrainHeightModifier modifier
    )
    {
        if (modifier == null)
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "TerrainHeightModifierContentV1"
        );

        modifier.AppendDeterministicSignatureData(
            builder
        );

        List<UnityEngine.Object> dependencies =
            new List<UnityEngine.Object>();

        modifier.CollectSignatureDependencies(
            dependencies
        );

        AppendValue(
            builder,
            dependencies.Count
        );

        for (
            int index = 0;
            index < dependencies.Count;
            index++
        )
        {
            AppendModifierDependencySignature(
                builder,
                dependencies[index]
            );
        }

        return
            ComputeSHA256(
                builder.ToString()
            );
    }

    // =====================================================
    // REGIONAL ELEVATION SIGNATURE HELPERS
    // =====================================================

    private static bool AppendRegionalElevationSignatureData(
        StringBuilder builder,
        TerrainRegionalElevationSource regionalSource
    )
    {
        if (
            builder == null
            ||
            regionalSource == null
        )
        {
            return false;
        }

        builder.Append(
            "|TerrainRegionalElevationV1"
        );

        return
            regionalSource
                .TryAppendDeterministicSignatureData(
                    builder,
                    out _
                );
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
