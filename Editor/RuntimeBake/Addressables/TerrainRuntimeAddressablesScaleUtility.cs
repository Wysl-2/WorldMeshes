using System;
using System.Text;
using UnityEngine;

internal static class TerrainRuntimeAddressablesScaleUtility
{
    internal static bool TryPopulateStats(
        WorldSettings worldSettings,
        TerrainHeightmapManifest heightManifest,
        TerrainSurfaceMaskManifest surfaceManifest,
        TerrainAddressablesOperationStats stats,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            worldSettings == null
            ||
            heightManifest == null
            ||
            surfaceManifest == null
            ||
            stats == null
        )
        {
            errorMessage =
                "Addressables scale diagnostics require WorldSettings and complete Height/Surface manifests.";

            return false;
        }

        long heightGeographicTileCount =
            (long)Mathf.Max(1, heightManifest.heightTileGridWidth) *
            Mathf.Max(1, heightManifest.heightTileGridHeight);

        long heightRepresentationLevelCount =
            Math.Max(
                1,
                heightManifest.StreamingLevelCount + 1
            );

        long heightEntryCount =
            heightGeographicTileCount *
            heightRepresentationLevelCount;

        long surfaceEntryCount =
            (long)Mathf.Max(1, surfaceManifest.tileGridWidth) *
            Mathf.Max(1, surfaceManifest.tileGridHeight);

        long collisionMeshEntryCount =
            (long)Mathf.Max(1, worldSettings.gridWidth) *
            Mathf.Max(1, worldSettings.gridHeight);

        int collisionRegionSpan =
            Mathf.Max(
                1,
                TerrainCollisionAddressablesUtility.CollisionRegionChunkSpan
            );

        long collisionRegionGridWidth =
            ((long)Mathf.Max(1, worldSettings.gridWidth) +
                collisionRegionSpan - 1L) /
            collisionRegionSpan;

        long collisionRegionGridHeight =
            ((long)Mathf.Max(1, worldSettings.gridHeight) +
                collisionRegionSpan - 1L) /
            collisionRegionSpan;

        long collisionMarkerEntryCount =
            collisionRegionGridWidth *
            collisionRegionGridHeight;

        if (
            heightEntryCount > int.MaxValue
            ||
            heightGeographicTileCount > int.MaxValue
            ||
            heightRepresentationLevelCount > int.MaxValue
        )
        {
            errorMessage =
                "Height Addressables scale exceeds the supported Int32 range.";

            return false;
        }

        int heightEntryCountInt =
            (int)heightEntryCount;

        int representationLevelCountInt =
            (int)heightRepresentationLevelCount;

        long heightBundleCount =
            TerrainHeightAddressablesPackingPolicy
                .EstimateHeightBundleCount(
                    heightEntryCountInt,
                    representationLevelCountInt
                );

        /*
         * ARH01 intentionally preserves existing packing topology:
         * Surface is PackSeparately and Collision is PackTogetherByLabel
         * using one region label per packaging region.
         */
        long surfaceBundleCount =
            surfaceEntryCount;

        long collisionBundleCount =
            collisionMarkerEntryCount;

        stats.expectedHeightEntryCount =
            heightEntryCount;

        stats.expectedHeightBundleCount =
            heightBundleCount;

        stats.expectedSurfaceEntryCount =
            surfaceEntryCount;

        stats.expectedSurfaceBundleCount =
            surfaceBundleCount;

        stats.expectedCollisionMeshEntryCount =
            collisionMeshEntryCount;

        stats.expectedCollisionMarkerEntryCount =
            collisionMarkerEntryCount;

        stats.expectedCollisionBundleCount =
            collisionBundleCount;

        stats.expectedTotalManagedEntryCount =
            heightEntryCount +
            surfaceEntryCount +
            collisionMeshEntryCount +
            collisionMarkerEntryCount;

        stats.expectedTotalBundleCount =
            heightBundleCount +
            surfaceBundleCount +
            collisionBundleCount;

        if (stats.heightGeographicTileCount <= 0)
        {
            stats.heightGeographicTileCount =
                (int)heightGeographicTileCount;
        }

        if (stats.heightRepresentationLevelCount <= 0)
        {
            stats.heightRepresentationLevelCount =
                representationLevelCountInt;
        }

        if (stats.heightExpectedEntryCount <= 0)
        {
            stats.heightExpectedEntryCount =
                heightEntryCountInt;

            stats.heightAuthoritativeAssetCount =
                (int)heightGeographicTileCount;

            stats.heightDerivedAssetCount =
                Math.Max(
                    0,
                    heightEntryCountInt -
                    (int)heightGeographicTileCount
                );

            stats.heightManagedStrideLabelCount =
                representationLevelCountInt;
        }

        return true;
    }

    internal static TerrainRuntimeAddressablesScaleReport CreateReport(
        TerrainAddressablesOperationStats stats
    )
    {
        return
            new TerrainRuntimeAddressablesScaleReport(
                stats != null ? stats.expectedHeightEntryCount : 0L,
                stats != null ? stats.expectedHeightBundleCount : 0L,
                stats != null ? stats.expectedSurfaceEntryCount : 0L,
                stats != null ? stats.expectedSurfaceBundleCount : 0L,
                stats != null ? stats.expectedCollisionMeshEntryCount : 0L,
                stats != null ? stats.expectedCollisionMarkerEntryCount : 0L,
                stats != null ? stats.expectedCollisionBundleCount : 0L,
                stats != null ? stats.expectedTotalManagedEntryCount : 0L,
                stats != null ? stats.expectedTotalBundleCount : 0L
            );
    }
}

public sealed class TerrainRuntimeAddressablesScaleReport
{
    public long ExpectedHeightEntryCount { get; private set; }
    public long ExpectedHeightBundleCount { get; private set; }
    public long ExpectedSurfaceEntryCount { get; private set; }
    public long ExpectedSurfaceBundleCount { get; private set; }
    public long ExpectedCollisionMeshEntryCount { get; private set; }
    public long ExpectedCollisionMarkerEntryCount { get; private set; }
    public long ExpectedCollisionBundleCount { get; private set; }
    public long ExpectedTotalManagedEntryCount { get; private set; }
    public long ExpectedTotalBundleCount { get; private set; }

    internal TerrainRuntimeAddressablesScaleReport(
        long expectedHeightEntryCount,
        long expectedHeightBundleCount,
        long expectedSurfaceEntryCount,
        long expectedSurfaceBundleCount,
        long expectedCollisionMeshEntryCount,
        long expectedCollisionMarkerEntryCount,
        long expectedCollisionBundleCount,
        long expectedTotalManagedEntryCount,
        long expectedTotalBundleCount
    )
    {
        ExpectedHeightEntryCount = expectedHeightEntryCount;
        ExpectedHeightBundleCount = expectedHeightBundleCount;
        ExpectedSurfaceEntryCount = expectedSurfaceEntryCount;
        ExpectedSurfaceBundleCount = expectedSurfaceBundleCount;
        ExpectedCollisionMeshEntryCount = expectedCollisionMeshEntryCount;
        ExpectedCollisionMarkerEntryCount = expectedCollisionMarkerEntryCount;
        ExpectedCollisionBundleCount = expectedCollisionBundleCount;
        ExpectedTotalManagedEntryCount = expectedTotalManagedEntryCount;
        ExpectedTotalBundleCount = expectedTotalBundleCount;
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("WorldMeshes Addressables Scale");
        builder.AppendLine("Expected Height Entries: " + ExpectedHeightEntryCount);
        builder.AppendLine("Expected Height Bundles: " + ExpectedHeightBundleCount);
        builder.AppendLine("Expected Surface Entries: " + ExpectedSurfaceEntryCount);
        builder.AppendLine("Expected Surface Bundles: " + ExpectedSurfaceBundleCount);
        builder.AppendLine("Expected Collision Mesh Entries: " + ExpectedCollisionMeshEntryCount);
        builder.AppendLine("Expected Collision Marker Entries: " + ExpectedCollisionMarkerEntryCount);
        builder.AppendLine("Expected Collision Bundles: " + ExpectedCollisionBundleCount);
        builder.AppendLine("Expected Total Managed Entries: " + ExpectedTotalManagedEntryCount);
        builder.AppendLine("Expected Total WorldMeshes Bundles: " + ExpectedTotalBundleCount);

        return builder.ToString();
    }
}
