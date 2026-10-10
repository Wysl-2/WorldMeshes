using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// An immutable GPU projection of one retained map. Successful construction transfers
// that map's lifetime to this payload; failed construction leaves the caller's map alive.
internal sealed class TerrainAuthoringPreviewSharedHeightBindingData : IDisposable
{
    internal const int MaximumPoolCount = 11;
    internal const int MaximumMapCells = 262144;
    internal const string ShaderKeyword = "WORLDMESHES_EDITOR_SHARED_HEIGHT";
    private const int MaximumExactInteger = 16777216;
    internal static readonly int EnabledId = Shader.PropertyToID("_EditorSharedHeightEnabled");
    internal static readonly int MapId = Shader.PropertyToID("_EditorSharedHeightMap");
    internal static readonly int WindowId = Shader.PropertyToID("_EditorSharedHeightMapWindow");
    internal static readonly int TopologyId = Shader.PropertyToID("_EditorSharedHeightTopology");
    private static readonly int[] PoolIds = CreatePropertyIds("_EditorSharedHeightPool");
    private static readonly int[] PoolInfoIds = CreatePropertyIds("_EditorSharedHeightPoolInfo");
    private static RenderTexture neutralPool;
    private readonly int threadId;
    private readonly RenderTexture[] textures;
    private readonly Vector4[] poolInfo;
    private IDisposable allocation;
    private Texture2D lookup;
    private bool ownsMap, disposed, fenceCaptured, readbackIssued, readbackComplete;
    private GraphicsFence releaseFence;
    private AsyncGPUReadbackRequest releaseReadback;
    internal TerrainAuthoringPreviewHeightPageMap Map { get; }
    internal TerrainHeightCacheWindow Window { get; }
    internal Vector4 Topology { get; }
    internal Vector2 WorldSize { get; }
    internal int DrawableCount { get; }
    // Captured currentness; shared preflight also checks the owner's current targets.
    internal int CurrentCount { get; }
    internal int RequiredMissingCount { get; }
    internal int OptionalMissingCount { get; }
    internal float MinimumHeight { get; }
    internal float MaximumHeight { get; }
    internal long AllocatedBytes { get; private set; }
    internal bool IsAlive => !disposed && lookup != null && Map.IsAlive;
    internal bool ReleaseComplete => disposed && lookup == null;
    private Texture2D visibility;
    private HashSet<Vector2Int> visible;
    internal static readonly int VisibilityId = Shader.PropertyToID("_EditorSharedHeightVisibility");
    internal static readonly int PartialId = Shader.PropertyToID("_EditorSharedHeightPartialCoverage");
    internal bool IsPartial => visible != null;
    internal int VisibilityMargin { get; private set; }
    internal int VisibleCount => IsPartial ? visible.Count : DrawableCount;
    internal bool IsVisible(Vector2Int tile) => !IsPartial || visible.Contains(tile);
    internal bool VisibilityEquals(HashSet<Vector2Int> other) => visible != null && visible.SetEquals(other);
    internal Texture2D Lookup => IsAlive ? lookup : null;

    private static int[] CreatePropertyIds(string prefix)
    {
        var ids = new int[MaximumPoolCount]; for (int i = 0; i < ids.Length; i++) ids[i] = Shader.PropertyToID(prefix + i); return ids;
    }
    static TerrainAuthoringPreviewSharedHeightBindingData()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ReleaseNeutralPool;
        EditorApplication.quitting += ReleaseNeutralPool;
    }
    internal static Texture NeutralPoolTexture
    {
        get
        {
            if (neutralPool == null)
            {
                var candidate = new RenderTexture(2, 2, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
                {
                    name = "Terrain Height Inactive Array",
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = 1,
                    antiAliasing = 1,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!candidate.Create())
                {
                    UnityEngine.Object.DestroyImmediate(candidate);
                    throw new InvalidOperationException("Could not allocate a neutral shared Height texture array.");
                }
                neutralPool = candidate;
            }
            return neutralPool;
        }
    }
    private static void ReleaseNeutralPool()
    {
        if (neutralPool == null) return;
        UnityEngine.Object.DestroyImmediate(neutralPool);
        neutralPool = null;
    }
    private TerrainAuthoringPreviewSharedHeightBindingData(WorldSettings settings, TerrainAuthoringPreviewHeightPageMap map,
        TerrainHeightCacheWindow window, RenderTexture[] textures, Vector4[] info, int drawable, int current,
        int requiredMissing, int optionalMissing, float minimum, float maximum, long bytes, IDisposable allocation)
    {
        threadId = Thread.CurrentThread.ManagedThreadId; Map = map; Window = window;
        Topology = new Vector4(settings.HeightTileWorldSize, settings.HeightTileIntervalsPerSide,
            settings.HeightTileGridWidth, settings.HeightTileGridHeight);
        WorldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
        this.textures = textures; poolInfo = info; DrawableCount = drawable; CurrentCount = current;
        RequiredMissingCount = requiredMissing; OptionalMissingCount = optionalMissing;
        MinimumHeight = minimum; MaximumHeight = maximum; AllocatedBytes = bytes; this.allocation = allocation;
    }
    internal static bool TryValidateDevice(out string error)
    {
        error = "";
        var api = SystemInfo.graphicsDeviceType;
        if (!SystemInfo.supports2DArrayTextures || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat)
            || !SystemInfo.supportsGraphicsFence || !SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsShaderLevel < 35
            || (api != GraphicsDeviceType.Direct3D11 && api != GraphicsDeviceType.Direct3D12
                && api != GraphicsDeviceType.Vulkan && api != GraphicsDeviceType.Metal && api != GraphicsDeviceType.OpenGLCore))
        { error = "Shared Height sampling requires desktop array/float-texture shaders, graphics fences and GPU readback; this graphics backend is unsupported."; return false; }
        return true;
    }
    // This shader deliberately has no runtime PBR or terrain-analysis variants.
    // Keep the edit-mode page display isolated from compilation errors in the
    // ordinary runtime terrain shader. The transient material retains matching
    // surface values; no asset material is modified.
    private const string SharedPreviewShaderPath =
        "Assets/WorldMeshes/Shaders/Terrain/TerrainAuthoringSharedHeightPreview.shader";

    // The dedicated shader unconditionally compiles its shared height sampler.
    // Validate the shader identity rather than an optional material keyword.
    internal static bool UsesDedicatedPreviewShader(Material material)
    {
        Shader expected = AssetDatabase.LoadAssetAtPath<Shader>(SharedPreviewShaderPath);
        return material != null && expected != null && material.shader == expected;
    }

    internal static bool TryCreateMaterial(Material source, out Material material, out string error)
    {
        material = null;
        if (!TryValidateDevice(out error)) return false;
        if (source == null || source.shader == null || !source.HasProperty(EnabledId))
        { error = "The source terrain material lacks shared Height binding properties."; return false; }
        Shader previewShader = AssetDatabase.LoadAssetAtPath<Shader>(SharedPreviewShaderPath);
        if (previewShader == null || !previewShader.isSupported)
        { error = "The dedicated shared Height preview shader is missing or unsupported."; return false; }
        Material candidate = null;
        try
        {
            candidate = new Material(previewShader)
            { hideFlags = HideFlags.HideAndDontSave, name = "Terrain Shared Height Material" };
            candidate.CopyMatchingPropertiesFromMaterial(source);
            // The draw-time MaterialPropertyBlock supplies the published page
            // map and pools. Keep this transient material explicitly enabled.
            candidate.SetFloat(EnabledId, 1f);
            if (!candidate.SetPass(0))
                throw new InvalidOperationException("The dedicated shared Height preview pass could not be activated. Check the Unity shader compiler log and graphics backend.");
            if (ShaderUtil.ShaderHasError(previewShader))
                throw new InvalidOperationException("The dedicated shared Height preview shader reports compilation errors. Check the Unity Console and Editor log.");
            material = candidate; return true;
        }
        catch (Exception exception)
        { if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate); error = exception.Message; return false; }
    }
    internal static bool TryCreate(WorldSettings settings, TerrainAuthoringPreviewHeightPageMap map,
        out TerrainAuthoringPreviewSharedHeightBindingData data, out string error,
        IReadOnlyCollection<Vector2Int> safeVisible = null, int visibilityMargin = 0)
    {
        data = null;
        if (!TryValidateDevice(out error)) return false;
        if (map == null || !map.ConfigurationMatches(settings) || !map.Demand.TryValidate(settings, out error)
            || map.Pools.Count > MaximumPoolCount || settings.HeightTileGridWidth > MaximumExactInteger
            || settings.HeightTileGridHeight > MaximumExactInteger || settings.HeightTileIntervalsPerSide > MaximumExactInteger)
        { if (string.IsNullOrEmpty(error)) error = "The shared Height map/world is stale or exceeds the eleven-pool shader/exact metadata limits."; return false; }
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = -1, maxZ = -1;
        int drawable = 0, current = 0, requiredMissing = 0, optionalMissing = 0;
        float minimum = float.MaxValue, maximum = float.MinValue;
        var borrowed = new RenderTexture[MaximumPoolCount]; var info = new Vector4[MaximumPoolCount];
        foreach (var entry in map.Entries)
        {
            if (!map.Demand.TryGetTile(entry.Tile, out var row) || !row.HasDisplay || row.SelectedDisplayStride != entry.RequestedStride)
            { error = "A shared Height render entry is not display demand in its captured epoch."; return false; }
            minX = Math.Min(minX, entry.Tile.x); minZ = Math.Min(minZ, entry.Tile.y);
            maxX = Math.Max(maxX, entry.Tile.x); maxZ = Math.Max(maxZ, entry.Tile.y);
            if (!entry.IsValid) { if (row.DisplayRequired) requiredMissing++; else optionalMissing++; continue; }
            var page = entry.Page; var h = page.Handle;
            if (entry.PoolIndex < 0 || entry.PoolIndex >= map.Pools.Count || h.OwnerId != map.OwnerId
                || h.ResourceGeneration != map.ResourceGeneration || h.Tile != entry.Tile
                || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, h.Stride)
                || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, page.SourceStride) || page.SourceStride > h.Stride
                || float.IsNaN(page.MinimumHeight) || float.IsInfinity(page.MinimumHeight)
                || float.IsNaN(page.MaximumHeight) || float.IsInfinity(page.MaximumHeight) || page.MaximumHeight < page.MinimumHeight)
            { error = "A shared Height page handle belongs to an incompatible owner/pool/stride."; return false; }
            var pool = map.Pools[entry.PoolIndex];
            if (!pool.IsAllocated || pool.AllocationGeneration != h.AllocationGeneration || pool.PoolIndex != entry.PoolIndex
                || pool.Stride != h.Stride || h.Stride != 1 << entry.PoolIndex || entry.Slice < 0 || entry.Slice >= pool.Capacity
                || pool.Capacity > MaximumExactInteger || page.Readiness != TerrainAuthoringPreviewHeightPageReadiness.FinalComposite
                || page.SamplesPerSide != pool.SamplesPerSide || !Mathf.Approximately(page.SampleSpacing, pool.SampleSpacing)
                || pool.SamplesPerSide != TerrainHeightResolutionUtility.GetSamplesPerSide(settings, h.Stride)
                || !Mathf.Approximately(pool.SampleSpacing, TerrainHeightResolutionUtility.GetSampleSpacing(settings, h.Stride)))
            { error = "Shared Height page metadata does not match its captured physical allocation."; return false; }
            if (borrowed[entry.PoolIndex] == null)
            {
                if (!map.TryGetPoolTexture(entry.PoolIndex, out var texture, out error)) return false;
                if (!texture.IsCreated() || texture.format != RenderTextureFormat.RFloat || texture.dimension != TextureDimension.Tex2DArray
                    || texture.width != pool.SamplesPerSide || texture.height != pool.SamplesPerSide || texture.volumeDepth != pool.Capacity)
                { error = "A shared Height physical array has incompatible dimensions/format."; return false; }
                borrowed[entry.PoolIndex] = texture;
                info[entry.PoolIndex] = new Vector4(pool.SamplesPerSide, pool.SampleSpacing, pool.Capacity, 1);
            }
            drawable++; if (entry.IsCurrent) current++;
            minimum = Math.Min(minimum, page.MinimumHeight); maximum = Math.Max(maximum, page.MaximumHeight);
        }
        int width = maxX < 0 ? 1 : maxX - minX + 1, height = maxZ < 0 ? 1 : maxZ - minZ + 1;
        long cells = (long)width * height;
        if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize || cells > MaximumMapCells
            || cells > Math.Max(64L, (long)map.Entries.Count * 4))
        { error = "Shared Height sparse coverage expands beyond the bounded local lookup texture/density limits."; return false; }
        var window = new TerrainHeightCacheWindow(maxX < 0 ? Vector2Int.zero : new Vector2Int(minX, minZ), new Vector2Int(width, height));
        bool r8 = SystemInfo.SupportsTextureFormat(TextureFormat.R8);
        if (safeVisible != null && !r8 && !SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32))
        { error = "Neither R8 nor RGBA32 supports the safe Height visibility mask."; return false; }
        if (safeVisible != null && (safeVisible.Count == 0 || visibilityMargin < 1 || visibilityMargin > 32))
        { error = "Partial visibility requires a nonempty bounded triangle/halo proof."; return false; }
        long bytes = checked(cells * (4 * sizeof(float) + (safeVisible == null ? 0 : r8 ? 1 : 4)));
        if (!map.TryClaimBinding(out error)) return false;
        if (!map.TryReserveLookupBytes(bytes, out var allocation, out error)) { map.ReleaseFailedBindingClaim(); return false; }
        var candidate = new TerrainAuthoringPreviewSharedHeightBindingData(settings, map, window, borrowed, info,
            drawable, current, requiredMissing, optionalMissing, drawable == 0 ? 0 : minimum, drawable == 0 ? 0 : maximum, bytes, allocation);
        try
        {
            var pixels = new Color[(int)cells];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0, -1, -1, 0);
            foreach (var entry in map.Entries)
                if (entry.IsValid)
                    pixels[entry.Tile.x - window.OriginTile.x + (entry.Tile.y - window.OriginTile.y) * width]
                        = new Color(1, entry.PoolIndex, entry.Slice, entry.Page.Handle.Stride);
            candidate.lookup = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
            { name = "Terrain Shared Height Geographical Lookup", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            candidate.lookup.SetPixels(pixels); candidate.lookup.Apply(false, true);
            if (safeVisible != null)
            {
                candidate.visible = new HashSet<Vector2Int>(safeVisible); candidate.VisibilityMargin = visibilityMargin;
                candidate.visibility = new Texture2D(width, height, r8 ? TextureFormat.R8 : TextureFormat.RGBA32, false, true)
                { name = "Terrain Shared Height Safe Visibility", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
                var mask = new byte[checked((int)cells * (r8 ? 1 : 4))];
                foreach (var tile in candidate.visible)
                {
                    if (!window.Contains(tile) || !map.Demand.TryGetTile(tile, out var row)
                        || (row.DisplayRequirement & TerrainAuthoringPreviewDisplayRequirement.Geometry) == 0
                        || !TerrainAuthoringPreviewHeightBindingUtility.IsSafeVisibilityCell(settings, map, tile, visibilityMargin))
                        throw new InvalidOperationException("A visible Height cell lacks ready geographical triangle/halo dependencies.");
                    int index = (tile.x - window.OriginTile.x + (tile.y - window.OriginTile.y) * width) * (r8 ? 1 : 4);
                    mask[index] = 255;
                }
                candidate.visibility.SetPixelData(mask, 0); candidate.visibility.Apply(false, true);
            }
            candidate.ownsMap = true; data = candidate; return true;
        }
        catch (Exception exception) { candidate.Dispose(); map.ReleaseFailedBindingClaim(); error = "Shared Height lookup upload failed: " + exception.Message; return false; }
    }
    internal bool Matches(WorldSettings settings, TerrainAuthoringPreviewGeographicDemandPlan demand)
    {
        RequireThread();
        if (!IsAlive || !Map.ConfigurationMatches(settings) || demand == null || demand.Generation != Map.DemandGeneration
            || demand.OwnershipGeneration != Map.OwnershipGeneration || !demand.IsEquivalentTo(Map.Demand)) return false;
        for (int i = 0; i < textures.Length; i++)
            if (textures[i] != null && (!textures[i].IsCreated() || textures[i].format != RenderTextureFormat.RFloat
                || textures[i].dimension != TextureDimension.Tex2DArray || textures[i].width != (int)poolInfo[i].x
                || textures[i].height != (int)poolInfo[i].x || textures[i].volumeDepth != (int)poolInfo[i].z)) return false;
        return true;
    }
    internal static bool MaterialHasProperties(Material material)
    {
        if (material == null || !material.HasProperty(EnabledId) || !material.HasProperty(MapId)
            || !material.HasProperty(WindowId) || !material.HasProperty(TopologyId)
            || !material.HasProperty(PartialId) || !material.HasProperty(VisibilityId)) return false;
        for (int i = 0; i < MaximumPoolCount; i++) if (!material.HasProperty(PoolIds[i]) || !material.HasProperty(PoolInfoIds[i])) return false;
        return true;
    }
    internal void Apply(MaterialPropertyBlock block)
    {
        RequireThread();
        if (!IsAlive) throw new InvalidOperationException("The shared Height binding payload has been retired.");
        block.SetTexture(VisibilityId, visibility != null ? visibility : Texture2D.whiteTexture);
        block.SetFloat(PartialId, IsPartial ? 1 : 0);
        block.SetTexture(MapId, lookup); block.SetVector(WindowId, new Vector4(Window.OriginTile.x, Window.OriginTile.y, Window.Width, Window.Height));
        block.SetVector(TopologyId, Topology);
        // for (int i = 0; i < MaximumPoolCount; i++) { block.SetTexture(PoolIds[i], textures[i]); block.SetVector(PoolInfoIds[i], poolInfo[i]); }
        // Bind a valid 2D array in every slot. Missing physical pools have
        // zero metadata, so they cannot be addressed by a valid lookup entry.
        var emptyPool = NeutralPoolTexture;
        for (int i = 0; i < MaximumPoolCount; i++)
        {
            block.SetTexture(PoolIds[i], textures[i] != null ? textures[i] : emptyPool);
            block.SetVector(PoolInfoIds[i], poolInfo[i]);
        }
        block.SetFloat(EnabledId, 1);
    }
    internal bool TryVerifyBoundState(MaterialPropertyBlock block, out string error)
    {
        RequireThread();
        error = "";
        if (!IsAlive || block == null || block.GetFloat(EnabledId) < 0.5f || block.GetTexture(MapId) != lookup)
        { error = "Shared Height active flag or geographical lookup was not preserved in the renderer property block."; return false; }
        if (block.GetFloat(PartialId) != (IsPartial ? 1 : 0)
            || block.GetTexture(VisibilityId) != (visibility != null ? visibility : Texture2D.whiteTexture)
            || block.GetVector(WindowId) != new Vector4(Window.OriginTile.x, Window.OriginTile.y, Window.Width, Window.Height)
            || block.GetVector(TopologyId) != Topology)
        { error = "Shared Height geographic window/topology property block differs from the published map."; return false; }
        for (int i = 0; i < MaximumPoolCount; i++)
        {
            var expected = textures[i] != null ? (Texture)textures[i] : NeutralPoolTexture;
            if (block.GetTexture(PoolIds[i]) != expected || block.GetVector(PoolInfoIds[i]) != poolInfo[i])
            { error = "Shared Height pool " + i + " has an incorrect texture or page geometry binding."; return false; }
        }
        return true;
    }
    internal static void Clear(MaterialPropertyBlock block)
    {
        // Replace previous GPU references with inert resources without
        // clearing unrelated renderer/authoring properties in this block.
        block.SetFloat(PartialId, 0); block.SetTexture(VisibilityId, Texture2D.whiteTexture);
        block.SetFloat(EnabledId, 0);
        block.SetTexture(MapId, Texture2D.blackTexture);
        block.SetVector(WindowId, Vector4.zero);
        block.SetVector(TopologyId, Vector4.zero);
        var emptyPool = NeutralPoolTexture;
        for (int i = 0; i < MaximumPoolCount; i++)
        {
            block.SetTexture(PoolIds[i], emptyPool);
            block.SetVector(PoolInfoIds[i], Vector4.zero);
        }
    }
    private void RequireThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("Shared Height binding lifetime requires its creating Unity main thread.");
    }
    // Clear all renderer/command-buffer references first; no more draws may be submitted
    // with this payload after Dispose. A fence protects both lookup and retained map reads.
    public void Dispose()
    {
        RequireThread();
        if (!disposed) { disposed = true; if (ownsMap) Map.Dispose(); }
        BeginRelease();
    }
    private void BeginRelease()
    {
        if (lookup == null) { FinishRelease(); return; }
        try
        {
            if (!fenceCaptured) { releaseFence = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations); fenceCaptured = true; }
            if (TryFinishRelease() || readbackIssued) return;
            readbackIssued = true;
            try { releaseReadback = AsyncGPUReadback.Request(lookup, 0, 0, 1, 0, 1, 0, 1, _ => { readbackComplete = true; TryFinishRelease(); }); }
            catch { readbackIssued = false; throw; }
        }
        catch { /* Remain charged/owned; explicit release can retry after a device error. */ }
    }
    private bool TryFinishRelease()
    {
        if (lookup == null) return true;
        if (!disposed || !fenceCaptured) return false;
        try { if (!releaseFence.passed) return false; } catch { return false; }
        if (readbackIssued && !readbackComplete && !releaseReadback.done) return false;
        FinishRelease(); return true;
    }
    private void FinishRelease()
    {
        if (lookup != null) UnityEngine.Object.DestroyImmediate(lookup);
        if (visibility != null) UnityEngine.Object.DestroyImmediate(visibility);
        visibility = null; lookup = null; allocation?.Dispose(); allocation = null; AllocatedBytes = 0;
    }
    internal bool WaitForRelease(out string error)
    {
        RequireThread(); error = "";
        if (!disposed) { error = "Retire shared Height renderer bindings before waiting for release."; return false; }
        BeginRelease();
        if (lookup != null && readbackIssued && !readbackComplete && !releaseReadback.done) releaseReadback.WaitForCompletion();
        bool complete = TryFinishRelease(); if (!complete) error = "Shared Height lookup release is still pending on its GPU boundary.";
        return complete;
    }
}
