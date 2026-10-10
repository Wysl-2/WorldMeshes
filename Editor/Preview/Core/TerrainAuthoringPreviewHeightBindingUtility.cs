using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// The hierarchy owner supplies semantic roles; this utility never interprets names.
internal static class TerrainAuthoringPreviewHeightBindingUtility
{
    private static readonly int TextureId = Shader.PropertyToID("_HeightCache");
    private static readonly int OriginId = Shader.PropertyToID("_HeightCacheOriginTile");
    private static readonly int SizeId = Shader.PropertyToID("_HeightCacheSize");
    private static readonly int SamplesId = Shader.PropertyToID("_HeightTileSamplesPerSide");
    private static readonly int SpacingId = Shader.PropertyToID("_HeightSampleSpacing");
    private static readonly int FineId = Shader.PropertyToID("_HeightNormalSampleSpacingFine");
    private static readonly int CoarseId = Shader.PropertyToID("_HeightNormalSampleSpacingCoarse");
    private static readonly int ReadyId = Shader.PropertyToID("_HeightCacheReady");
    private static readonly int WorldId = Shader.PropertyToID("_WorldSizeXZ");
    private static readonly int WorldReadyId = Shader.PropertyToID("_WorldBoundsReady");
    private static readonly int[] RequiredProperties =
        { TextureId, OriginId, SizeId, SamplesId, SpacingId, FineId, CoarseId, ReadyId, WorldId, WorldReadyId };

    internal static bool TryPreflight(WorldSettings settings, TerrainAuthoringPreviewResidencyPlan plan,
        IReadOnlyList<TerrainClipmapRendererBinding> bindings,
        IReadOnlyList<TerrainAuthoringPreviewHeightCacheView> caches, out string error)
    {
        error = "";
        if (settings == null || plan == null || !plan.IsStructurallyValid || caches == null
            || caches.Count != plan.LevelCount || bindings == null
            || bindings.Count != 2 * plan.LevelCount - 1)
        { error = "The complete Height set and generated semantic renderer list are required."; return false; }
        var world = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
        if (!Finite(world.x) || !Finite(world.y) || world.x <= 0 || world.y <= 0)
        { error = "The Height world geometry is invalid."; return false; }
        for (int i = 0; i < caches.Count; i++)
        {
            var c = caches[i]; var p = plan.Levels[i]; var texture = c?.HeightCache;
            if (c == null || c.Level != i || c.SampleStride != p.SampleStride
                || c.SamplesPerSide != p.SamplesPerSide || !Mathf.Approximately(c.SampleSpacing, p.SampleSpacing)
                || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, c.SampleStride)
                || c.SamplesPerSide != TerrainHeightResolutionUtility.GetSamplesPerSide(settings, c.SampleStride)
                || !Mathf.Approximately(c.SampleSpacing, TerrainHeightResolutionUtility.GetSampleSpacing(settings, c.SampleStride))
                || !c.IsComplete || !c.ResidentWindow.Contains(p.RequiredWindow) || c.WorldSizeXZ != world
                || texture == null || !texture.IsCreated() || texture.format != RenderTextureFormat.RFloat
                || texture.dimension != TextureDimension.Tex2DArray || texture.width != c.SamplesPerSide
                || texture.height != c.SamplesPerSide || texture.volumeDepth != c.ResidentWindow.TileCount)
            { error = $"The Height resource, coverage or sample geometry at LOD {i} is invalid."; return false; }
        }
        var renderers = new HashSet<MeshRenderer>();
        var rings = new HashSet<int>(); var stitches = new HashSet<int>(); int centers = 0;
        foreach (var binding in bindings)
        {
            var role = binding.Role; int owner = role.HeightOwnerLevel;
            if (!binding.IsValid || !renderers.Add(binding.Renderer) || owner < 0 || owner >= caches.Count)
            { error = "A Height renderer role is invalid or duplicated."; return false; }
            if (role.Kind == TerrainClipmapRendererKind.Center) centers++;
            else if (role.Kind == TerrainClipmapRendererKind.Ring)
            {
                if (!rings.Add(owner)) { error = "A Height ring role is duplicated."; return false; }
            }
            else if (role.Kind == TerrainClipmapRendererKind.Stitch)
            {
                if (role.CoarseLevel >= caches.Count || !stitches.Add(role.FineLevel)
                    || !Mathf.Approximately(caches[role.CoarseLevel].SampleSpacing, caches[owner].SampleSpacing * 2f))
                { error = "A Height stitch lacks a valid adjacent coarse representation."; return false; }
            }
            else { error = "An unknown Height role was supplied."; return false; }
            var material = binding.Renderer.sharedMaterial;
            if (material == null) { error = "A Height renderer has no material."; return false; }
            foreach (int id in RequiredProperties)
                if (!material.HasProperty(id)) { error = "A terrain material lacks a required Height property."; return false; }
        }
        if (centers != 1 || rings.Count != caches.Count - 1 || stitches.Count != caches.Count - 1)
        { error = "The complete semantic Height hierarchy is required."; return false; }
        return true;
    }

    // Call only after preflight of every resource and renderer. GetPropertyBlock
    // preserves placement, visualization, analysis and unrelated material state.
    internal static void Bind(IReadOnlyList<TerrainClipmapRendererBinding> bindings,
        IReadOnlyList<TerrainAuthoringPreviewHeightCacheView> caches)
    {
        var surface = TerrainSurfaceSettings.LoadDefault();
        var block = new MaterialPropertyBlock();
        foreach (var binding in bindings)
        {
            var c = caches[binding.Role.HeightOwnerLevel];
            float coarse = binding.Role.Kind == TerrainClipmapRendererKind.Stitch
                ? caches[binding.Role.CoarseLevel].SampleSpacing : c.SampleSpacing;
            binding.Renderer.GetPropertyBlock(block);
            TerrainAuthoringPreviewSharedHeightBindingData.Clear(block);
            if (surface != null) TerrainSurfaceSettingsBindingUtility.TryApplyToPropertyBlock(block, surface, out _);
            block.SetTexture(TextureId, c.HeightCache);
            block.SetVector(OriginId, new Vector4(c.ResidentWindow.OriginTile.x, c.ResidentWindow.OriginTile.y, 0, 0));
            block.SetVector(SizeId, new Vector4(c.ResidentWindow.Size.x, c.ResidentWindow.Size.y, 0, 0));
            block.SetFloat(SamplesId, c.SamplesPerSide);
            block.SetFloat(SpacingId, c.SampleSpacing);
            block.SetFloat(FineId, c.SampleSpacing);
            block.SetFloat(CoarseId, coarse);
            block.SetFloat(ReadyId, 1);
            block.SetVector(WorldId, new Vector4(c.WorldSizeXZ.x, c.WorldSizeXZ.y, 0, 0));
            block.SetFloat(WorldReadyId, 1);
            binding.Renderer.SetPropertyBlock(block);
        }
    }

    internal static void Disable(IReadOnlyList<TerrainClipmapRendererBinding> bindings, int ownerLevel = -1)
    {
        if (bindings == null) return;
        var block = new MaterialPropertyBlock();
        foreach (var binding in bindings)
        {
            if (binding.Renderer == null || (ownerLevel >= 0 && binding.Role.HeightOwnerLevel != ownerLevel)) continue;
            binding.Renderer.GetPropertyBlock(block);
            TerrainAuthoringPreviewSharedHeightBindingData.Clear(block);
            block.SetTexture(TextureId, TerrainAuthoringPreviewSharedHeightBindingData.NeutralPoolTexture);
            block.SetFloat(ReadyId, 0);
            binding.Renderer.SetPropertyBlock(block);
        }
    }

    internal static bool TryPreflightShared(WorldSettings settings, TerrainAuthoringPreviewQualitySnapshot quality,
        TerrainClipmapLayout layout, TerrainAuthoringPreviewGeographicDemandPlan demand,
        TerrainAuthoringPreviewSharedHeightBindingData data, IReadOnlyList<TerrainClipmapRendererBinding> bindings,
        bool allowLastGood, out string error)
    {
        error = "";
        if (!TerrainAuthoringPreviewSharedHeightBindingData.TryValidateDevice(out error)) return false;
        if (settings == null || demand == null || !demand.TryValidate(settings, out error) || data == null
            || !data.Matches(settings, demand) || !data.Map.PolicyMatches(quality) || layout == null || !layout.IsValid
            || bindings == null || bindings.Count != 2 * layout.LevelCount - 1
            || layout.LevelCount != settings.clipmapLevelCount || demand.PolicyGeneration != quality?.Generation)
        { if (string.IsNullOrEmpty(error)) error = "Shared Height requires matching world, demand/map epoch, layout, quality and semantic renderers."; return false; }
        if (!allowLastGood && !data.Map.TargetsAreCurrent)
        { error = "The captured shared Height content/demand targets are older than their owner; explicitly allow last-good or upload a current snapshot."; return false; }
        if (data.RequiredMissingCount != 0 || data.DrawableCount == 0)
        { error = "Required shared Height pages are missing; a lookup allocation alone is not ready coverage."; return false; }
        foreach (var row in demand.Tiles)
        {
            if (!row.DisplayRequired) continue;
            if (!data.Map.TryGetEntry(row.Tile, out var page) || !page.IsValid || !allowLastGood && !page.IsCurrent)
            { error = "Required shared Height coverage is absent or stale at " + row.Tile + "."; return false; }
        }
        // Reuse the geographical planner for the supplied actual layout, including world
        // clamping, ring holes, stitch offsets and normal-sampling dependencies.
        Vector2 size = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
        Vector3 focusPosition = new Vector3(Mathf.Min(size.x, (demand.FocusTile.x + 0.5f) * settings.HeightTileWorldSize), 0,
            Mathf.Min(size.y, (demand.FocusTile.y + 0.5f) * settings.HeightTileWorldSize));
        var focus = new TerrainAuthoringPreviewFocus(settings, focusPosition, demand.FocusKind, demand.OwnershipGeneration);
        if (!TerrainAuthoringPreviewLodResidencyUtility.TryBuildGeographicDemand(settings, layout, quality, focus, null,
            demand.PlacementGeneration, demand.NativeWorkingGeneration, demand.Generation, out var coverage, out error)) return false;
        foreach (var row in coverage.Tiles)
            if (row.DisplayRequired && (!demand.TryGetTile(row.Tile, out var requested) || !requested.DisplayRequired
                || !data.Map.TryGetEntry(row.Tile, out var page) || !page.IsValid))
            { error = "Shared Height demand does not cover the actual centre/ring/stitch sampling layout at " + row.Tile + "."; return false; }
        // The original planner's normal halo uses geometry spacing. Coarse shared
        // pages also need their wider normal step and one boundary-profile band.
        // Check a conservative tile halo without adding demand or producing pages.
        float pageSpacing = 0, normalSpacing = 0;
        foreach (var entry in data.Map.Entries) if (entry.IsValid) pageSpacing = Mathf.Max(pageSpacing, entry.Page.SampleSpacing);
        for (int i = 0; i < layout.LevelCount; i++) normalSpacing = Mathf.Max(normalSpacing, layout.GetSpacing(i));
        int halo = Mathf.CeilToInt((Mathf.Max(pageSpacing, normalSpacing) + pageSpacing) / settings.HeightTileWorldSize);
        if (halo > 2) { error = "Shared Height sampling exceeds the bounded two-tile normal/boundary halo."; return false; }
        var checkedTiles = new HashSet<Vector2Int>();
        foreach (var row in coverage.Tiles)
        {
            if ((row.DisplayRequirement & TerrainAuthoringPreviewDisplayRequirement.Geometry) == 0) continue;
            for (int z = -halo; z <= halo; z++) for (int x = -halo; x <= halo; x++)
            {
                var tile = row.Tile + new Vector2Int(x, z);
                if (tile.x < 0 || tile.y < 0 || tile.x >= settings.HeightTileGridWidth || tile.y >= settings.HeightTileGridHeight
                    || !checkedTiles.Add(tile)) continue;
                if (!data.Map.TryGetEntry(tile, out var page) || !page.IsValid || !allowLastGood && !page.IsCurrent)
                { error = "Shared Height needs drawable/current coarse-normal and boundary halo coverage at " + tile + "."; return false; }
            }
        }
        var renderers = new HashSet<MeshRenderer>(); var rings = new HashSet<int>(); var stitches = new HashSet<int>(); int centers = 0;
        foreach (var binding in bindings)
        {
            var role = binding.Role; int owner = role.HeightOwnerLevel;
            if (!binding.IsValid || !renderers.Add(binding.Renderer) || owner < 0 || owner >= layout.LevelCount)
            { error = "A shared Height renderer role is invalid or duplicated."; return false; }
            if (role.Kind == TerrainClipmapRendererKind.Center) centers++;
            else if (role.Kind == TerrainClipmapRendererKind.Ring)
            { if (!rings.Add(owner)) { error = "A shared Height ring role is duplicated."; return false; } }
            else if (role.Kind == TerrainClipmapRendererKind.Stitch)
            {
                if (role.CoarseLevel >= layout.LevelCount || !stitches.Add(role.FineLevel)
                    || !Mathf.Approximately(layout.GetSpacing(role.CoarseLevel), layout.GetSpacing(owner) * 2f))
                { error = "A shared Height stitch lacks its adjacent coarse geometry."; return false; }
            }
            else { error = "An unknown shared Height renderer role was supplied."; return false; }
            var material = binding.Renderer.sharedMaterial;
            if (material == null || EditorUtility.IsPersistent(material)
                || !TerrainAuthoringPreviewSharedHeightBindingData.UsesDedicatedPreviewShader(material)
                || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader)
                || !TerrainAuthoringPreviewSharedHeightBindingData.MaterialHasProperties(material))
            { error = "Shared Height requires the supported temporary editor preview shader; shared assets must not be changed."; return false; }
            foreach (int id in RequiredProperties)
                if (!material.HasProperty(id)) { error = "The shared terrain material lacks the preserved legacy/world/normal properties."; return false; }
        }
        if (centers != 1 || rings.Count != layout.LevelCount - 1 || stitches.Count != layout.LevelCount - 1)
        { error = "The complete semantic shared Height hierarchy is required."; return false; }
        return true;
    }

    internal static bool TryBindShared(WorldSettings settings, TerrainAuthoringPreviewQualitySnapshot quality,
        TerrainClipmapLayout layout, TerrainAuthoringPreviewGeographicDemandPlan demand,
        TerrainAuthoringPreviewSharedHeightBindingData data, IReadOnlyList<TerrainClipmapRendererBinding> bindings,
        bool allowLastGood, out string error)
    {
        if (!TryPreflightShared(settings, quality, layout, demand, data, bindings, allowLastGood, out error)) return false;
        var original = new MaterialPropertyBlock[bindings.Count]; var prepared = new MaterialPropertyBlock[bindings.Count];
        try
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                original[i] = new MaterialPropertyBlock(); prepared[i] = new MaterialPropertyBlock();
                var binding = bindings[i]; binding.Renderer.GetPropertyBlock(original[i]); binding.Renderer.GetPropertyBlock(prepared[i]);
                data.Apply(prepared[i]);
                float fine = layout.GetSpacing(binding.Role.HeightOwnerLevel);
                float coarse = binding.Role.Kind == TerrainClipmapRendererKind.Stitch ? layout.GetSpacing(binding.Role.CoarseLevel) : fine;
                prepared[i].SetFloat(FineId, fine); prepared[i].SetFloat(CoarseId, coarse);
                prepared[i].SetVector(WorldId, new Vector4(data.WorldSize.x, data.WorldSize.y, 0, 0)); prepared[i].SetFloat(WorldReadyId, 1);
            }
            for (int i = 0; i < bindings.Count; i++) bindings[i].Renderer.SetPropertyBlock(prepared[i]);
            // Verify the installed renderer state, rather than trusting that a
            // staged MPB still contains the mapping after placement and binding.
            var installed = new MaterialPropertyBlock();
            for (int i = 0; i < bindings.Count; i++)
            {
                bindings[i].Renderer.GetPropertyBlock(installed);
                if (!data.TryVerifyBoundState(installed, out error))
                    throw new System.InvalidOperationException(error);
            }
            return true;
        }
        catch (System.Exception exception)
        {
            for (int i = 0; i < bindings.Count; i++) if (bindings[i].Renderer != null && original[i] != null) bindings[i].Renderer.SetPropertyBlock(original[i]);
            error = "Shared Height binding failed: " + exception.Message; return false;
        }
    }

    // Caller must stop/clear future draws before retiring the payload and its map.
    internal static void DisableShared(IReadOnlyList<TerrainClipmapRendererBinding> bindings)
    {
        if (bindings == null) return;
        var block = new MaterialPropertyBlock();
        foreach (var binding in bindings)
        {
            if (binding.Renderer == null) continue;
            binding.Renderer.GetPropertyBlock(block); TerrainAuthoringPreviewSharedHeightBindingData.Clear(block);
            binding.Renderer.SetPropertyBlock(block);
        }
    }

    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
