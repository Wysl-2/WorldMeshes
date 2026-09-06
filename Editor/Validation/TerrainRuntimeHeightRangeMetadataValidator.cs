using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeHeightRangeMetadataValidator
{
    private const float Tolerance =
        0.0001f;

    public static bool Validate(
        WorldSettings worldSettings,
        bool comparePhysicalTiles = true
    )
    {
        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot validate runtime height range metadata: " +
                "WorldSettings is null."
            );

            return false;
        }

        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (manifest == null)
        {
            Debug.LogError(
                "Cannot validate runtime height range metadata because the " +
                "runtime heightmap manifest does not exist."
            );

            return false;
        }

        if (!manifest.isComplete)
        {
            Debug.LogError(
                "Runtime height range metadata validation failed because the " +
                "manifest is structurally incomplete."
            );

            return false;
        }

        if (
            manifest.compilerVersion !=
            TerrainGenerationStateUtility
                .RuntimeHeightCompilerVersion
        )
        {
            Debug.LogError(
                "Runtime height range metadata validation failed because the " +
                "manifest compiler version is not current."
            );

            return false;
        }

        if (
            manifest.heightTileGridWidth !=
                worldSettings.HeightTileGridWidth
            ||
            manifest.heightTileGridHeight !=
                worldSettings.HeightTileGridHeight
            ||
            manifest.heightTileSamplesPerSide !=
                worldSettings.HeightTileSamplesPerSide
        )
        {
            Debug.LogError(
                "Runtime height range metadata validation failed because the " +
                "manifest height-tile layout does not match WorldSettings."
            );

            return false;
        }

        int expectedCount =
            worldSettings.HeightTileGridWidth *
            worldSettings.HeightTileGridHeight;

        if (
            manifest.TileHeightRangeCount !=
                expectedCount
            ||
            !manifest.HasCompleteTileHeightRanges
        )
        {
            Debug.LogError(
                "Runtime height range metadata validation failed.\n\n" +
                $"Expected Range Records: {expectedCount}\n" +
                $"Stored Range Records: {manifest.TileHeightRangeCount}\n" +
                $"Valid Range Records: {manifest.ValidTileHeightRangeCount}"
            );

            return false;
        }

        if (
            !manifest.TryCalculateGlobalHeightRange(
                out float metadataMinimum,
                out float metadataMaximum
            )
        )
        {
            Debug.LogError(
                "Runtime height range metadata could not be reduced to a " +
                "valid global range."
            );

            return false;
        }

        if (
            Mathf.Abs(
                manifest.minimumTerrainHeight -
                metadataMinimum
            ) > Tolerance
            ||
            Mathf.Abs(
                manifest.maximumTerrainHeight -
                metadataMaximum
            ) > Tolerance
        )
        {
            Debug.LogError(
                "Runtime height range metadata validation failed.\n\n" +
                $"Manifest Global Range: " +
                $"{manifest.minimumTerrainHeight:R} -> " +
                $"{manifest.maximumTerrainHeight:R}\n" +
                $"Reduced Metadata Range: " +
                $"{metadataMinimum:R} -> {metadataMaximum:R}"
            );

            return false;
        }

        if (
            comparePhysicalTiles
            &&
            !ValidatePhysicalTiles(
                worldSettings,
                manifest,
                out string physicalError
            )
        )
        {
            Debug.LogError(
                "Runtime height range metadata validation failed.\n\n" +
                physicalError
            );

            return false;
        }

        Debug.Log(
            "Runtime height range metadata validation passed.\n\n" +
            $"Range Records: {manifest.TileHeightRangeCount}\n" +
            $"Global Range: " +
            $"{metadataMinimum:R} -> {metadataMaximum:R}\n" +
            $"Physical Tile Comparison: " +
            (comparePhysicalTiles ? "Passed" : "Skipped")
        );

        return true;
    }

    private static bool ValidatePhysicalTiles(
        WorldSettings worldSettings,
        TerrainHeightmapManifest manifest,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        int samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        int expectedSampleCount =
            samplesPerSide *
            samplesPerSide;

        for (
            int tileZ = 0;
            tileZ < worldSettings.HeightTileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < worldSettings.HeightTileGridWidth;
                tileX++
            )
            {
                string path =
                    TerrainRuntimeHeightAssetUtility
                        .GetHeightTilePath(
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
                        $"Runtime height tile ({tileX}, {tileZ}) is missing:\n" +
                        path;

                    return false;
                }

                if (
                    texture.width != samplesPerSide
                    ||
                    texture.height != samplesPerSide
                    ||
                    texture.format != TextureFormat.RFloat
                )
                {
                    errorMessage =
                        $"Runtime height tile ({tileX}, {tileZ}) has an " +
                        "unexpected format or dimensions.";

                    return false;
                }

                NativeArray<float> data;

                try
                {
                    data =
                        texture.GetPixelData<float>(
                            0
                        );
                }
                catch (
                    System.Exception exception
                )
                {
                    errorMessage =
                        $"Could not read runtime height tile " +
                        $"({tileX}, {tileZ}).\n\n" +
                        exception.Message;

                    return false;
                }

                if (
                    data.Length !=
                    expectedSampleCount
                )
                {
                    errorMessage =
                        $"Runtime height tile ({tileX}, {tileZ}) has an " +
                        "unexpected sample count.";

                    return false;
                }

                float actualMinimum =
                    float.PositiveInfinity;

                float actualMaximum =
                    float.NegativeInfinity;

                for (
                    int index = 0;
                    index < data.Length;
                    index++
                )
                {
                    float height =
                        data[index];

                    if (
                        float.IsNaN(height)
                        ||
                        float.IsInfinity(height)
                    )
                    {
                        errorMessage =
                            $"Runtime height tile ({tileX}, {tileZ}) contains " +
                            "a non-finite sample.";

                        return false;
                    }

                    actualMinimum =
                        Mathf.Min(
                            actualMinimum,
                            height
                        );

                    actualMaximum =
                        Mathf.Max(
                            actualMaximum,
                            height
                        );
                }

                if (
                    !manifest.TryGetTileHeightRange(
                        tileX,
                        tileZ,
                        out float storedMinimum,
                        out float storedMaximum
                    )
                    ||
                    Mathf.Abs(
                        storedMinimum -
                        actualMinimum
                    ) > Tolerance
                    ||
                    Mathf.Abs(
                        storedMaximum -
                        actualMaximum
                    ) > Tolerance
                )
                {
                    errorMessage =
                        $"Runtime height tile ({tileX}, {tileZ}) range " +
                        "metadata does not match the physical Texture2D.\n\n" +
                        $"Stored: {storedMinimum:R} -> {storedMaximum:R}\n" +
                        $"Actual: {actualMinimum:R} -> {actualMaximum:R}";

                    return false;
                }
            }
        }

        return true;
    }
}
