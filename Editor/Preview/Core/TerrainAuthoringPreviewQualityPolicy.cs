using System;
using UnityEditor;
using UnityEngine;

internal enum TerrainAuthoringPreviewEditFocusMode { FollowPreviewFocus, PinnedFocus }
internal enum TerrainAuthoringPreviewContextualPresentation { CurrentSurface, SimplifiedSurface }
internal enum TerrainAuthoringPreviewFocusKind { Canonical, Following, Frozen }

internal readonly struct TerrainAuthoringPreviewFocus
{
    internal int WorldIdentity { get; }
    internal Vector2 PositionXZ { get; }
    internal TerrainAuthoringPreviewFocusKind Kind { get; }
    internal long OwnershipGeneration { get; }

    internal TerrainAuthoringPreviewFocus(WorldSettings settings, Vector3 position,
        TerrainAuthoringPreviewFocusKind kind, long ownership)
    {
        WorldIdentity = settings != null ? settings.GetInstanceID() : 0;
        PositionXZ = new Vector2(position.x, position.z);
        Kind = kind;
        OwnershipGeneration = ownership;
    }
}

// Configuration intent only. No field changes active renderer quality or allocates resources.
internal sealed class TerrainAuthoringPreviewQualitySnapshot
{
    internal int EditableWindowSizeTiles { get; }
    internal TerrainAuthoringPreviewEditFocusMode FocusMode { get; }
    internal bool HasPinnedFocus { get; }
    internal Vector2 PinnedFocusXZ { get; }
    internal int ContextualMinimumStride { get; }
    internal int SourceMemoryBudgetMiB { get; }
    internal int GpuMemoryBudgetMiB { get; }
    internal TerrainAuthoringPreviewContextualPresentation ContextualPresentationPreference { get; }
    internal long Generation { get; }

    internal TerrainAuthoringPreviewQualitySnapshot(int windowSize, int contextualStride,
        TerrainAuthoringPreviewEditFocusMode focusMode = TerrainAuthoringPreviewEditFocusMode.FollowPreviewFocus,
        bool hasPin = false, Vector2 pin = default, int sourceBudgetMiB = 256, int gpuBudgetMiB = 512,
        TerrainAuthoringPreviewContextualPresentation presentation = TerrainAuthoringPreviewContextualPresentation.CurrentSurface,
        long generation = 0)
    {
        EditableWindowSizeTiles = windowSize; ContextualMinimumStride = contextualStride;
        FocusMode = focusMode; HasPinnedFocus = hasPin; PinnedFocusXZ = pin;
        SourceMemoryBudgetMiB = sourceBudgetMiB; GpuMemoryBudgetMiB = gpuBudgetMiB;
        ContextualPresentationPreference = presentation; Generation = generation;
    }

    internal bool HasSameValues(TerrainAuthoringPreviewQualitySnapshot other) => other != null
        && EditableWindowSizeTiles == other.EditableWindowSizeTiles && FocusMode == other.FocusMode
        && HasPinnedFocus == other.HasPinnedFocus && PinnedFocusXZ == other.PinnedFocusXZ
        && ContextualMinimumStride == other.ContextualMinimumStride
        && SourceMemoryBudgetMiB == other.SourceMemoryBudgetMiB && GpuMemoryBudgetMiB == other.GpuMemoryBudgetMiB
        && ContextualPresentationPreference == other.ContextualPresentationPreference;

    internal TerrainAuthoringPreviewQualitySnapshot WithGeneration(long generation) =>
        new TerrainAuthoringPreviewQualitySnapshot(EditableWindowSizeTiles, ContextualMinimumStride,
            FocusMode, HasPinnedFocus, PinnedFocusXZ, SourceMemoryBudgetMiB, GpuMemoryBudgetMiB,
            ContextualPresentationPreference, generation);
}

internal static class TerrainAuthoringPreviewQualityPolicy
{
    private const int SchemaVersion = 1;
    private const int MaximumWindowSize = 4095;
    private const int MaximumBudgetMiB = 16384;
    private static WorldSettings cachedWorld;
    private static int cachedIntervals;
    private static Vector2 cachedWorldSize;
    private static string cachedKey;
    private static TerrainAuthoringPreviewQualitySnapshot cachedSnapshot;
    private static long generation;
    internal static string LastValidationMessage { get; private set; } = "";

    internal static TerrainAuthoringPreviewQualitySnapshot GetSnapshot(WorldSettings settings)
    {
        if (settings == null) return null;
        Vector2 size = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
        if (cachedSnapshot != null && cachedWorld == settings
            && cachedIntervals == settings.HeightTileIntervalsPerSide && cachedWorldSize == size)
            return cachedSnapshot;
        string key = PreferenceKey(settings);
        var defaults = CreateDefaults(settings);
        var requested = defaults;
        string schemaMessage = "";
        if (EditorPrefs.HasKey(key + ".Schema"))
        {
            if (EditorPrefs.GetInt(key + ".Schema", 0) == SchemaVersion)
                requested = new TerrainAuthoringPreviewQualitySnapshot(
                    EditorPrefs.GetInt(key + ".Window", defaults.EditableWindowSizeTiles),
                    EditorPrefs.GetInt(key + ".Stride", defaults.ContextualMinimumStride),
                    (TerrainAuthoringPreviewEditFocusMode)EditorPrefs.GetInt(key + ".Focus", 0),
                    EditorPrefs.GetBool(key + ".HasPin", false),
                    new Vector2(EditorPrefs.GetFloat(key + ".PinX", 0), EditorPrefs.GetFloat(key + ".PinZ", 0)),
                    EditorPrefs.GetInt(key + ".SourceMiB", defaults.SourceMemoryBudgetMiB),
                    EditorPrefs.GetInt(key + ".GpuMiB", defaults.GpuMemoryBudgetMiB),
                    (TerrainAuthoringPreviewContextualPresentation)EditorPrefs.GetInt(key + ".Presentation", 0));
            else schemaMessage = "Unsupported preview quality preference schema; defaults are used. ";
        }
        if (!TryValidate(settings, requested, out var validated, out string message))
        { LastValidationMessage = message; return null; }
        LastValidationMessage = schemaMessage + message;
        if (cachedWorld != settings || cachedKey != key || !validated.HasSameValues(cachedSnapshot)) generation++;
        cachedWorld = settings; cachedKey = key; cachedIntervals = settings.HeightTileIntervalsPerSide;
        cachedWorldSize = size; cachedSnapshot = validated.WithGeneration(generation);
        return cachedSnapshot;
    }

    internal static string PreferenceKey(WorldSettings settings)
    {
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(settings));
        return "WorldMeshes.TerrainAuthoringPreview.Quality." + Application.dataPath + "."
            + (string.IsNullOrEmpty(guid) ? "DefaultWorld" : guid);
    }

    internal static TerrainAuthoringPreviewQualitySnapshot CreateDefaults(WorldSettings settings) =>
        new TerrainAuthoringPreviewQualitySnapshot(5, DefaultContextualStride(settings));

    private static int DefaultContextualStride(WorldSettings settings)
    {
        for (int stride = 8; stride >= 1; stride /= 2)
            if (TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride)) return stride;
        return 1;
    }

    internal static bool TryValidate(WorldSettings settings, TerrainAuthoringPreviewQualitySnapshot requested,
        out TerrainAuthoringPreviewQualitySnapshot validated, out string explanation)
    {
        validated = null; explanation = "";
        if (settings == null || requested == null || settings.gridWidth < 1 || settings.gridHeight < 1
            || settings.heightTileChunkSpan < 1 || settings.heightfieldResolutionPerChunk < 1
            || (long)settings.heightTileChunkSpan * settings.heightfieldResolutionPerChunk >= int.MaxValue
            || !IsFinite(settings.chunkSize) || settings.chunkSize < 0.01f)
        { explanation = "Preview quality requires valid world topology and settings."; return false; }
        Vector2 size = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings);
        if (!IsFinite(size.x) || !IsFinite(size.y) || size.x <= 0 || size.y <= 0)
        { explanation = "Preview quality requires finite positive world bounds."; return false; }
        int window = requested.EditableWindowSizeTiles;
        if (window < 1 || window > MaximumWindowSize || (window & 1) == 0)
        { window = 5; explanation += "Editable window must be an odd size from 1 to 4095; using 5. "; }
        int stride = requested.ContextualMinimumStride;
        if (!TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride))
        { stride = DefaultContextualStride(settings); explanation += "Contextual stride must be a compatible power of two; using " + stride + ". "; }
        var mode = requested.FocusMode;
        if (mode != TerrainAuthoringPreviewEditFocusMode.FollowPreviewFocus && mode != TerrainAuthoringPreviewEditFocusMode.PinnedFocus)
        { mode = TerrainAuthoringPreviewEditFocusMode.FollowPreviewFocus; explanation += "Invalid focus mode; following preview focus. "; }
        bool pinValid = requested.HasPinnedFocus && IsFinite(requested.PinnedFocusXZ.x) && IsFinite(requested.PinnedFocusXZ.y);
        if (requested.HasPinnedFocus && !pinValid) explanation += "Invalid pin; current preview focus will be used. ";
        Vector2 pin = pinValid ? new Vector2(Mathf.Clamp(requested.PinnedFocusXZ.x, 0, size.x),
            Mathf.Clamp(requested.PinnedFocusXZ.y, 0, size.y)) : Vector2.zero;
        if (pinValid && pin != requested.PinnedFocusXZ) explanation += "Pinned focus was clamped to the world. ";
        int source = requested.SourceMemoryBudgetMiB, gpu = requested.GpuMemoryBudgetMiB;
        if (source < 1 || source > MaximumBudgetMiB) { source = 256; explanation += "Invalid source budget; using 256 MiB. "; }
        if (gpu < 1 || gpu > MaximumBudgetMiB) { gpu = 512; explanation += "Invalid GPU budget; using 512 MiB. "; }
        var presentation = requested.ContextualPresentationPreference;
        if (presentation != TerrainAuthoringPreviewContextualPresentation.CurrentSurface
            && presentation != TerrainAuthoringPreviewContextualPresentation.SimplifiedSurface)
        { presentation = TerrainAuthoringPreviewContextualPresentation.CurrentSurface; explanation += "Invalid contextual presentation; using current surface intent. "; }
        validated = new TerrainAuthoringPreviewQualitySnapshot(window, stride, mode, pinValid, pin, source, gpu, presentation);
        return true;
    }

    // Single update path for future controls/tooling. Validation completes before any preference write.
    internal static bool TryApply(WorldSettings settings, TerrainAuthoringPreviewQualitySnapshot requested, out string explanation)
    {
        if (!TryValidate(settings, requested, out var validated, out explanation)) return false;
        var previous = GetSnapshot(settings);
        if (validated.HasSameValues(previous)) return true;
        string key = PreferenceKey(settings);
        EditorPrefs.SetInt(key + ".Window", validated.EditableWindowSizeTiles);
        EditorPrefs.SetInt(key + ".Stride", validated.ContextualMinimumStride);
        EditorPrefs.SetInt(key + ".Focus", (int)validated.FocusMode);
        bool persistentWorld = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(settings)));
        EditorPrefs.SetBool(key + ".HasPin", validated.HasPinnedFocus && persistentWorld);
        EditorPrefs.SetFloat(key + ".PinX", validated.PinnedFocusXZ.x);
        EditorPrefs.SetFloat(key + ".PinZ", validated.PinnedFocusXZ.y);
        EditorPrefs.SetInt(key + ".SourceMiB", validated.SourceMemoryBudgetMiB);
        EditorPrefs.SetInt(key + ".GpuMiB", validated.GpuMemoryBudgetMiB);
        EditorPrefs.SetInt(key + ".Presentation", (int)validated.ContextualPresentationPreference);
        EditorPrefs.SetInt(key + ".Schema", SchemaVersion);
        generation++;
        cachedWorld = settings; cachedIntervals = settings.HeightTileIntervalsPerSide;
        cachedWorldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(settings); cachedKey = key;
        cachedSnapshot = validated.WithGeneration(generation); LastValidationMessage = explanation;
        TerrainAuthoringPreviewService.RequestGeographicDemandRefresh();
        return true;
    }

    internal static void InvalidateReadCache() { cachedSnapshot = null; }
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
