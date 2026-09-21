using System;
using UnityEngine;

/*
 * Package 07 Terrain Analysis identity for the active resident height source.
 *
 * This partial keeps analysis-specific transient generations beside preview
 * ownership without changing the established Package 01-06 lifecycle code.
 * A small tracker subscribes before external analysis consumers: source
 * replacement is detected at the authoritative PreviewStateChanged boundary,
 * while successful in-place content changes are observed through the existing
 * CompositeTilesUpdated publication.
 */
public static partial class TerrainAuthoringPreviewService
{
    internal static event Action TerrainAnalysisSourceChanged;

    private static long analysisResidencyGeneration;
    private static long analysisCompositeGeneration;

    /*
     * Field initialization runs as part of PreviewService type initialization,
     * before TerrainAnalysisGpuGenerator can subscribe as an external consumer.
     */
    private static readonly AnalysisSourceTracker
        analysisSourceTracker =
            new AnalysisSourceTracker();

    internal static long AnalysisResidencyGeneration =>
        analysisResidencyGeneration;

    internal static long AnalysisCompositeGeneration =>
        analysisCompositeGeneration;

    internal static bool TryGetTerrainAnalysisGpuSource(
        out TerrainAnalysisGpuSource source
    )
    {
        source = default;

        /*
         * Force the tracker field to be considered used and make the ownership
         * relationship obvious to future maintainers.
         */
        _ = analysisSourceTracker;

        TerrainAuthoringPreviewCache cache =
            activeCache;

        if (
            cache == null
            ||
            !cache.IsReady
            ||
            cache.HeightCache == null
            ||
            !cache.HeightCache.IsCreated()
        )
        {
            return false;
        }

        TerrainHeightCacheWindow sourceWindow =
            new TerrainHeightCacheWindow(
                cache.CacheOriginTile,
                cache.CacheSize
            );

        if (!sourceWindow.IsValid)
        {
            return false;
        }

        int samplesPerSide =
            cache.SamplesPerSide;

        float sampleSpacing =
            cache.SampleSpacing;

        float tileWorldSize =
            Mathf.Max(
                0.000001f,
                (samplesPerSide - 1) *
                sampleSpacing
            );

        Vector2 worldSizeXZ =
            cache.WorldSizeXZ;

        Vector2Int worldTileGridSize =
            new Vector2Int(
                Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        worldSizeXZ.x /
                        tileWorldSize
                    )
                ),
                Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        worldSizeXZ.y /
                        tileWorldSize
                    )
                )
            );

        string authoringSignature =
            !string.IsNullOrEmpty(
                cache.SourceOverallAuthoringSignature
            )
                ? cache.SourceOverallAuthoringSignature
                : cache.SourceCommittedHeightfieldSignature;

        int instanceId =
            cache.HeightCache.GetInstanceID();

        string sourceSignature =
            (authoringSignature ?? "") +
            "|cache:" +
            instanceId +
            "|origin:" +
            cache.CacheOriginTile.x +
            "," +
            cache.CacheOriginTile.y +
            "|size:" +
            cache.CacheSize.x +
            "," +
            cache.CacheSize.y +
            "|residency:" +
            analysisResidencyGeneration +
            "|composite:" +
            analysisCompositeGeneration;

        source =
            new TerrainAnalysisGpuSource(
                cache.HeightCache,
                sourceWindow,
                worldTileGridSize,
                samplesPerSide,
                sampleSpacing,
                worldSizeXZ,
                sourceSignature,
                instanceId,
                analysisResidencyGeneration,
                analysisCompositeGeneration
            );

        return source.IsValid;
    }

    private static long NextAnalysisGeneration(
        long current
    )
    {
        long next = current + 1;

        if (next <= 0)
        {
            next = 1;
        }

        return next;
    }

    private sealed class AnalysisSourceTracker
    {
        private bool hasObservedSource;
        private int observedResourceIdentity;
        private Vector2Int observedOrigin;
        private Vector2Int observedSize;

        public AnalysisSourceTracker()
        {
            PreviewStateChanged +=
                OnPreviewStateChanged;

            CompositeTilesUpdated +=
                OnCompositeTilesUpdated;
        }

        private void OnPreviewStateChanged()
        {
            TerrainAuthoringPreviewCache cache =
                activeCache;

            if (
                cache == null
                ||
                !cache.IsReady
                ||
                cache.HeightCache == null
                ||
                !cache.HeightCache.IsCreated()
            )
            {
                hasObservedSource = false;
                observedResourceIdentity = 0;
                observedOrigin = Vector2Int.zero;
                observedSize = Vector2Int.zero;
                return;
            }

            int resourceIdentity =
                cache.HeightCache.GetInstanceID();

            bool replaced =
                !hasObservedSource
                ||
                resourceIdentity != observedResourceIdentity
                ||
                cache.CacheOriginTile != observedOrigin
                ||
                cache.CacheSize != observedSize;

            observedResourceIdentity = resourceIdentity;
            observedOrigin = cache.CacheOriginTile;
            observedSize = cache.CacheSize;
            hasObservedSource = true;

            if (!replaced)
            {
                return;
            }

            analysisResidencyGeneration =
                NextAnalysisGeneration(
                    analysisResidencyGeneration
                );

            TerrainAnalysisSourceChanged?.Invoke();
        }

        private void OnCompositeTilesUpdated(
            System.Collections.Generic.IReadOnlyList<Vector2Int> tiles
        )
        {
            if (tiles == null || tiles.Count == 0)
            {
                return;
            }

            analysisCompositeGeneration =
                NextAnalysisGeneration(
                    analysisCompositeGeneration
                );
        }
    }
}
