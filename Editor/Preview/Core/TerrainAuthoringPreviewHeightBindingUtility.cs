using System.Collections.Generic;
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
            block.SetTexture(TextureId, null);
            block.SetFloat(ReadyId, 0);
            binding.Renderer.SetPropertyBlock(block);
        }
    }

    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
