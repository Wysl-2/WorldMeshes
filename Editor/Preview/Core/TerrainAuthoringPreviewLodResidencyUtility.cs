using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Pure edit-mode planner that converts one exact clipmap layout into bounded
 * per-LOD Height residency intent.
 *
 * The planner allocates no GPU resources and performs no AssetDatabase work.
 */
internal static class TerrainAuthoringPreviewLodResidencyUtility
{
    internal static bool TryBuildPlan(
        WorldSettings worldSettings,
        TerrainClipmapLayout layout,
        int generation,
        out TerrainAuthoringPreviewResidencyPlan plan,
        out string errorMessage,
        bool requireStreamingPolicy = true
    )
    {
        plan =
            null;

        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (
            layout == null
            ||
            !layout.IsValid
        )
        {
            errorMessage =
                "TerrainClipmapLayout is unavailable or invalid.";

            return false;
        }

        int levelCount =
            layout.LevelCount;

        if (
            levelCount <
                TerrainClipmapTopologyUtility.MinimumLevelCount
            ||
            levelCount >
                TerrainClipmapTopologyUtility.MaximumLevelCount
        )
        {
            errorMessage =
                "The requested clipmap layout has an unsupported LOD count.";

            return false;
        }

        TerrainAuthoringPreviewResidencyPlan result =
            new TerrainAuthoringPreviewResidencyPlan
            {
                Generation =
                    generation,

                LevelCount =
                    levelCount,

                MinimumXZ =
                    layout.MinimumXZ,

                MaximumXZ =
                    layout.MaximumXZ,

                CoverageCenter =
                    layout.CoverageCenter,

                Levels =
                    new TerrainAuthoringPreviewLodResidencyPlan[
                        levelCount
                    ]
            };

        for (
            int level = 0;
            level < levelCount;
            level++
        )
        {
            if (
                !TerrainHeightResolutionUtility
                    .TryGetRequiredStrideForClipmapLevel(
                        worldSettings,
                        level,
                        out int sampleStride,
                        out string strideError
                    )
            )
            {
                errorMessage =
                    $"Could not resolve the Height representation for LOD{level}.\n\n" +
                    strideError;

                return false;
            }

            if (
                requireStreamingPolicy && !TerrainHeightStreamingPyramidPolicy
                    .IsHeightRepresentationStrideSupported(
                        worldSettings,
                        sampleStride
                    )
            )
            {
                errorMessage =
                    $"LOD{level} requires Height sample stride {sampleStride}, " +
                    "which is outside the configured Height representation policy.";

                return false;
            }

            int samplesPerSide =
                TerrainHeightResolutionUtility
                    .GetSamplesPerSide(
                        worldSettings,
                        sampleStride
                    );

            float sampleSpacing =
                TerrainHeightResolutionUtility
                    .GetSampleSpacing(
                        worldSettings,
                        sampleStride
                    );

            float layoutSpacing =
                layout.GetSpacing(
                    level
                );

            if (
                !Mathf.Approximately(
                    sampleSpacing,
                    layoutSpacing
                )
            )
            {
                errorMessage =
                    $"LOD{level} clipmap spacing does not match its Height " +
                    "representation.\n\n" +
                    $"Clipmap Spacing: {layoutSpacing}\n" +
                    $"Height Spacing: {sampleSpacing}\n" +
                    $"Sample Stride: {sampleStride}";

                return false;
            }

            float coarseSampleSpacing =
                sampleSpacing;

            if (level < levelCount - 1)
            {
                if (
                    !TerrainHeightResolutionUtility
                        .TryGetRequiredStrideForClipmapLevel(
                            worldSettings,
                            level + 1,
                            out int coarseSampleStride,
                            out string coarseStrideError
                        )
                )
                {
                    errorMessage =
                        $"Could not resolve the adjacent coarse Height " +
                        $"representation for LOD{level}.\n\n" +
                        coarseStrideError;

                    return false;
                }

                if (
                    requireStreamingPolicy && !TerrainHeightStreamingPyramidPolicy
                        .IsHeightRepresentationStrideSupported(
                            worldSettings,
                            coarseSampleStride
                        )
                )
                {
                    errorMessage =
                        $"LOD{level + 1} requires Height sample stride " +
                        $"{coarseSampleStride}, which is outside the " +
                        "configured Height representation policy.";

                    return false;
                }

                coarseSampleSpacing =
                    TerrainHeightResolutionUtility
                        .GetSampleSpacing(
                            worldSettings,
                            coarseSampleStride
                        );
            }

            if (
                !TerrainHeightClipmapCoverageUtility
                    .TryCalculateRequiredWorldBounds(
                        worldSettings,
                        layout,
                        level,
                        sampleSpacing,
                        coarseSampleSpacing,
                        out Vector2 requiredMinimumXZ,
                        out Vector2 requiredMaximumXZ,
                        out string coverageError
                    )
            )
            {
                errorMessage =
                    $"Could not calculate required Height coverage for " +
                    $"LOD{level}.\n\n" +
                    coverageError;

                return false;
            }

            if (
                !TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateRequiredWindowForRepresentation(
                        worldSettings,
                        requiredMinimumXZ,
                        requiredMaximumXZ,
                        sampleStride,
                        out TerrainHeightCacheWindow requiredWindow,
                        out string requiredWindowError
                    )
            )
            {
                errorMessage =
                    $"Could not calculate the required Height tile window " +
                    $"for LOD{level}.\n\n" +
                    requiredWindowError;

                return false;
            }

            if (
                !TerrainAuthoringPreviewResidencyUtility
                    .TryCalculateDesiredWindowForRepresentation(
                        worldSettings,
                        requiredWindow,
                        TerrainAuthoringPreviewResidencyUtility.DefaultGuardTileCount,
                        out TerrainHeightCacheWindow desiredWindow,
                        out string desiredWindowError
                    )
            )
            {
                errorMessage =
                    $"Could not calculate the desired Height tile window " +
                    $"for LOD{level}.\n\n" +
                    desiredWindowError;

                return false;
            }

            result.Levels[level] =
                new TerrainAuthoringPreviewLodResidencyPlan
                {
                    Level =
                        level,

                    SampleStride =
                        sampleStride,

                    SamplesPerSide =
                        samplesPerSide,

                    SampleSpacing =
                        sampleSpacing,

                    Anchor =
                        layout.GetAnchor(
                            level
                        ),

                    RequiredMinimumXZ =
                        requiredMinimumXZ,

                    RequiredMaximumXZ =
                        requiredMaximumXZ,

                    RequiredWindow =
                        requiredWindow,

                    DesiredWindow =
                        desiredWindow
                };
        }

        if (!result.IsStructurallyValid)
        {
            errorMessage =
                "The calculated multiresolution Height residency plan is structurally invalid.";

            return false;
        }

        plan =
            result;

        return true;
    }
    internal static bool TryCalculateEditableWindow(WorldSettings settings, TerrainAuthoringPreviewQualitySnapshot policy,
        Vector2 previewFocus, out Vector2Int focusTile, out TerrainHeightCacheWindow window, out string error)
    {
        focusTile = default; window = default; error = "";
        if (settings == null || policy == null || settings.gridWidth < 1 || settings.gridHeight < 1
            || settings.heightTileChunkSpan < 1 || !GeographicFinite(settings.HeightTileWorldSize)
            || settings.HeightTileWorldSize <= 0 || policy.EditableWindowSizeTiles < 1
            || (policy.EditableWindowSizeTiles & 1) == 0)
        { error = "Editable focus requires valid world tile bounds and an odd window size."; return false; }
        Vector2 focus = policy.FocusMode == TerrainAuthoringPreviewEditFocusMode.PinnedFocus && policy.HasPinnedFocus
            ? policy.PinnedFocusXZ : previewFocus;
        Vector2 size = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
        if (!GeographicFinite(focus.x) || !GeographicFinite(focus.y)
            || !GeographicFinite(size.x) || !GeographicFinite(size.y))
        { error = "Editable focus is not finite."; return false; }
        focusTile = new Vector2Int(
            GeographicTileCoordinate(Mathf.Clamp(focus.x, 0, size.x), settings.HeightTileWorldSize, settings.HeightTileGridWidth),
            GeographicTileCoordinate(Mathf.Clamp(focus.y, 0, size.y), settings.HeightTileWorldSize, settings.HeightTileGridHeight));
        int half = policy.EditableWindowSizeTiles / 2;
        int minX = Mathf.Max(0, focusTile.x - half), minZ = Mathf.Max(0, focusTile.y - half);
        int maxX = (int)Math.Min(settings.HeightTileGridWidth, (long)focusTile.x + half + 1);
        int maxZ = (int)Math.Min(settings.HeightTileGridHeight, (long)focusTile.y + half + 1);
        window = new TerrainHeightCacheWindow(new Vector2Int(minX, minZ), new Vector2Int(maxX - minX, maxZ - minZ));
        return true;
    }

    // Companion planning path. The existing live per-LOD planner is deliberately unchanged.
    internal static bool TryBuildGeographicDemand(WorldSettings settings, TerrainClipmapLayout layout,
        TerrainAuthoringPreviewQualitySnapshot policy, TerrainAuthoringPreviewFocus focus,
        IReadOnlyList<TerrainAuthoringPreviewNativeWorkingDemand> nativeWorking, long placementGeneration,
        long nativeWorkingGeneration, long generation, out TerrainAuthoringPreviewGeographicDemandPlan plan, out string error)
    {
        plan = null; error = "";
        if (settings == null || settings.gridWidth < 1 || settings.gridHeight < 1 || settings.heightTileChunkSpan < 1
            || settings.heightfieldResolutionPerChunk < 1 || (long)settings.heightTileChunkSpan * settings.heightfieldResolutionPerChunk >= int.MaxValue
            || !GeographicFinite(settings.chunkSize) || settings.chunkSize < 0.01f
            || settings.clipmapLevelCount < TerrainClipmapTopologyUtility.MinimumLevelCount
            || settings.clipmapLevelCount > TerrainClipmapTopologyUtility.MaximumLevelCount
            || layout == null || !layout.IsValid || layout.LevelCount != settings.clipmapLevelCount)
        { error = "Geographical Height demand requires valid matching world and clipmap topology."; return false; }
        if (!TerrainClipmapTopologyUtility.TryValidateSettings(settings, out error)
            || !TerrainAuthoringPreviewQualityPolicy.TryValidate(settings, policy, out var validated, out error)) return false;
        if (!validated.HasSameValues(policy)) { error = "Geographical Height demand requires a validated quality snapshot. " + error; return false; }
        if (focus.WorldIdentity != settings.GetInstanceID() || focus.OwnershipGeneration < 0
            || !Enum.IsDefined(typeof(TerrainAuthoringPreviewFocusKind), focus.Kind))
        { error = "Geographical Height focus does not belong to the current world/owner."; return false; }
        if (!TryCalculateEditableWindow(settings, policy, focus.PositionXZ, out var focusTile, out var editable, out error)) return false;
        Vector2 worldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
        if (!GeographicFinite(worldSize.x) || !GeographicFinite(worldSize.y)
            || !GeographicFinite(settings.HeightTileWorldSize))
        { error = "Geographical Height world bounds overflow."; return false; }
        var rows = new Dictionary<Vector2Int, GeographicTileAccumulator>();
        for (int level = 0; level < layout.LevelCount; level++)
        {
            if (!layout.TryGetLOD(level, out Vector3 anchor, out float spacing)
                || !GeographicFinite(anchor.x) || !GeographicFinite(anchor.z) || !GeographicFinite(anchor.y)
                || !TerrainHeightResolutionUtility.TryGetRequiredStrideForClipmapLevel(settings, level, out int stride, out error)
                || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride)
                || !GeographicFinite(spacing) || !Mathf.Approximately(spacing, TerrainHeightResolutionUtility.GetSampleSpacing(settings, stride)))
            { error = "Clipmap geometry and Height sampling disagree at LOD " + level + ". " + error; return false; }
            if (anchor.x < -spacing || anchor.z < -spacing || anchor.x > worldSize.x + spacing || anchor.z > worldSize.y + spacing)
            { error = "Clipmap anchors are outside the bounded world focus."; return false; }
            TerrainClipmapRendererRole geometryRole = TerrainClipmapRendererRole.CreateCenter();
            if (level > 0) TerrainClipmapRendererRole.TryCreateRing(level, out geometryRole);
            bool isRing = geometryRole.Kind == TerrainClipmapRendererKind.Ring;
            float coarseSpacing = level + 1 < layout.LevelCount ? layout.GetSpacing(level + 1) : spacing;
            if (!TerrainHeightClipmapCoverageUtility.TryCalculateRequiredWorldBounds(settings, layout, level,
                spacing, coarseSpacing, out _, out _, out error)) return false;
            float half = TerrainClipmapTopologyUtility.GetLODHalfExtent(settings, level);
            Vector2 center = new Vector2(anchor.x, anchor.z);
            Vector2 outerMin = center - Vector2.one * half, outerMax = center + Vector2.one * half;
            float holeHalf = level == 0 ? 0 : TerrainClipmapTopologyUtility.GetRingInnerHalfResolution(settings, level) * spacing;
            Vector2 holeMin = center - Vector2.one * holeHalf, holeMax = center + Vector2.one * holeHalf;
            AddGeographicFootprint(settings, rows, outerMin, outerMax, holeMin, holeMax, isRing, stride,
                TerrainAuthoringPreviewDisplayRequirement.Geometry);
            AddGeographicFootprint(settings, rows, outerMin - Vector2.one * spacing, outerMax + Vector2.one * spacing,
                holeMin + Vector2.one * spacing, holeMax - Vector2.one * spacing, isRing, stride,
                TerrainAuthoringPreviewDisplayRequirement.SamplingDependency);
            if (level + 1 >= layout.LevelCount) continue;
            TerrainClipmapRendererRole.TryCreateStitch(level, level + 1, out var stitchRole);
            Vector3 coarseAnchor = layout.GetAnchor(stitchRole.CoarseLevel);
            if (!GeographicFinite(coarseAnchor.x) || !GeographicFinite(coarseAnchor.z)
                || Mathf.Abs(anchor.x - coarseAnchor.x) > spacing * 1.01f
                || Mathf.Abs(anchor.z - coarseAnchor.z) > spacing * 1.01f)
            { error = "Adjacent clipmap anchors exceed the supported stitch offset."; return false; }
            // Fine boundary follows the fine anchor; outer boundary follows the coarse anchor.
            // Their bounding union minus the shared interior conservatively covers every stitch triangle.
            Vector2 coarseCenter = new Vector2(coarseAnchor.x, coarseAnchor.z);
            Vector2 fineMin = center - Vector2.one * half, fineMax = center + Vector2.one * half;
            Vector2 coarseInnerMin = coarseCenter - Vector2.one * half, coarseInnerMax = coarseCenter + Vector2.one * half;
            outerMin = Vector2.Min(fineMin, coarseCenter - Vector2.one * (half + 2 * spacing));
            outerMax = Vector2.Max(fineMax, coarseCenter + Vector2.one * (half + 2 * spacing));
            holeMin = Vector2.Max(fineMin, coarseInnerMin); holeMax = Vector2.Min(fineMax, coarseInnerMax);
            AddGeographicFootprint(settings, rows, outerMin, outerMax, holeMin, holeMax, true, stride,
                TerrainAuthoringPreviewDisplayRequirement.Geometry);
            float margin = Mathf.Max(spacing, coarseSpacing);
            AddGeographicFootprint(settings, rows, outerMin - Vector2.one * margin, outerMax + Vector2.one * margin,
                holeMin + Vector2.one * margin, holeMax - Vector2.one * margin, true, stride,
                TerrainAuthoringPreviewDisplayRequirement.SamplingDependency);
        }
        var geometryTiles = new List<Vector2Int>();
        foreach (var pair in rows) if ((pair.Value.Display & TerrainAuthoringPreviewDisplayRequirement.Geometry) != 0) geometryTiles.Add(pair.Key);
        int coarsest = 1; while (coarsest <= int.MaxValue / 2 && TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, coarsest * 2)) coarsest *= 2;
        int halo = Mathf.Min(2, Mathf.Max(1, Mathf.CeilToInt(2 * TerrainHeightResolutionUtility.GetSampleSpacing(settings, coarsest) / settings.HeightTileWorldSize)));
        foreach (var tile in geometryTiles) for (int z = -halo; z <= halo; z++) for (int x = -halo; x <= halo; x++)
        {
            var neighbour = tile + new Vector2Int(x, z);
            if (neighbour.x < 0 || neighbour.y < 0 || neighbour.x >= settings.HeightTileGridWidth || neighbour.y >= settings.HeightTileGridHeight) continue;
            if (!rows.TryGetValue(neighbour, out var row)) row = new GeographicTileAccumulator { Stride = coarsest };
            row.Display |= TerrainAuthoringPreviewDisplayRequirement.SamplingDependency; rows[neighbour] = row;
        }
        if (nativeWorking != null)
            foreach (var working in nativeWorking)
            {
                var window = working.Window;
                if (!window.IsValid || working.Reason == TerrainAuthoringPreviewNativeWorkingReason.None
                    || (working.Reason & ~(TerrainAuthoringPreviewNativeWorkingReason.Analysis | TerrainAuthoringPreviewNativeWorkingReason.InteractiveAuthoring)) != 0
                    || window.OriginTile.x < 0 || window.OriginTile.y < 0
                    || (long)window.OriginTile.x + window.Width > settings.HeightTileGridWidth
                    || (long)window.OriginTile.y + window.Height > settings.HeightTileGridHeight)
                { error = "Native working demand has an invalid world window or reason."; return false; }
                for (int z = window.OriginTile.y; z < window.MaximumExclusive.y; z++)
                    for (int x = window.OriginTile.x; x < window.MaximumExclusive.x; x++)
                    {
                        var tile = new Vector2Int(x, z);
                        if (!rows.TryGetValue(tile, out var row)) row = new GeographicTileAccumulator();
                        row.Native |= working.Reason; rows[tile] = row;
                    }
            }
        var coordinates = new List<Vector2Int>(rows.Keys);
        coordinates.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        var records = new TerrainAuthoringPreviewGeographicTileDemand[coordinates.Count];
        for (int i = 0; i < coordinates.Count; i++)
        {
            var tile = coordinates[i]; var row = rows[tile]; bool isEditable = editable.Contains(tile);
            int displayStride = row.Stride == 0 ? 0 : isEditable ? row.Stride : Mathf.Max(row.Stride, policy.ContextualMinimumStride);
            records[i] = new TerrainAuthoringPreviewGeographicTileDemand(tile, row.Stride, displayStride, isEditable, row.Display, row.Native);
        }
        var result = new TerrainAuthoringPreviewGeographicDemandPlan(settings, policy, focus, focusTile,
            editable, placementGeneration, nativeWorkingGeneration, generation, records);
        if (!result.TryValidate(settings, out error)) return false;
        plan = result; return true;
    }

    private struct GeographicTileAccumulator
    {
        internal int Stride;
        internal TerrainAuthoringPreviewDisplayRequirement Display;
        internal TerrainAuthoringPreviewNativeWorkingReason Native;
    }

    private static bool GeographicFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static int GeographicTileCoordinate(float position, float tileSize, int count) =>
        Mathf.Clamp((int)Math.Min(count - 1, Math.Floor((double)position / tileSize)), 0, count - 1);

    private static void AddGeographicFootprint(WorldSettings settings, Dictionary<Vector2Int, GeographicTileAccumulator> rows,
        Vector2 minimum, Vector2 maximum, Vector2 holeMinimum, Vector2 holeMaximum, bool hasHole,
        int stride, TerrainAuthoringPreviewDisplayRequirement requirement)
    {
        Vector2 world = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
        minimum = Vector2.Max(minimum, Vector2.zero); maximum = Vector2.Min(maximum, world);
        if (minimum.x > maximum.x || minimum.y > maximum.y) return;
        float size = settings.HeightTileWorldSize;
        // Include both sides of an exact shared edge. Canonical focus mapping remains half-open.
        int minX = Mathf.Max(0, Mathf.CeilToInt(minimum.x / size) - 1);
        int minZ = Mathf.Max(0, Mathf.CeilToInt(minimum.y / size) - 1);
        int maxX = GeographicTileCoordinate(maximum.x, size, settings.HeightTileGridWidth);
        int maxZ = GeographicTileCoordinate(maximum.y, size, settings.HeightTileGridHeight);
        for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                // Only tiles strictly inside the ring hole are absent. Shared edges retain finer obligations.
                if (hasHole && x * (double)size > holeMinimum.x && z * (double)size > holeMinimum.y
                    && (x + 1L) * size < holeMaximum.x && (z + 1L) * size < holeMaximum.y) continue;
                var tile = new Vector2Int(x, z);
                if (!rows.TryGetValue(tile, out var row)) row = new GeographicTileAccumulator();
                row.Stride = row.Stride == 0 ? stride : Mathf.Min(row.Stride, stride);
                row.Display |= requirement; rows[tile] = row;
            }
    }

}

