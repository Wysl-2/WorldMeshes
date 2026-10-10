using System;
using UnityEditor;
using UnityEngine;

/*
 * Authoritative committed Height acquisition, independent of the destination
 * representation. Native assets are borrowed; decoded derived textures are
 * owned by explicit leases. No source textures are retained globally.
 */
internal static class TerrainAuthoringPreviewHeightSourceUtility
{
    private static bool cacheIoUnavailable;
    private static readonly System.Collections.Generic.HashSet<string> generationFailures =
        new System.Collections.Generic.HashSet<string>();
    private static int nativeLoads, cacheHits, generatedEntries, nativeFallbacks;
    private static int rejectedEntries, readFailures, writeFailures;
    private static string lastWarning = "";

    internal static TerrainAuthoringPreviewSourceCacheSnapshot CaptureDiagnostics() =>
        new TerrainAuthoringPreviewSourceCacheSnapshot(nativeLoads, cacheHits, generatedEntries,
            nativeFallbacks, rejectedEntries, readFailures, writeFailures, lastWarning);

    // Called by the established committed-change boundary. Files survive;
    // there is no memoized AssetDatabase identity or whole-world invalidation.
    internal static void ResetTransientFailures()
    {
        cacheIoUnavailable = false;
        generationFailures.Clear();
        lastWarning = "";
    }

    internal static bool IsCurrent(WorldSettings settings, TerrainAuthoringPreviewHeightSourceLease lease)
    {
        if (lease == null || lease.Texture == null || !lease.HasIdentity) return false;
        try
        {
            return TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings) == lease.CommittedSignature
                && TerrainAuthoringPreviewDerivedHeightCache.TryCaptureIdentity(settings, lease.Identity.Tile,
                    lease.SourceStride, out var current, out _)
                && lease.Identity.Matches(current) && (!lease.OwnsTexture || current.Persistable);
        }
        catch (Exception) { return false; }
    }

    // reusableNative belongs only to the caller's current geographical group.
    // Dirty work passes allowGeneration=false: modifier updates never write files.
    internal static bool TryAcquireCommittedSource(WorldSettings settings, Vector2Int tile, int requestedStride,
        ref Texture2D reusableNative, bool allowGeneration,
        out TerrainAuthoringPreviewHeightSourceLease lease, out string error, Func<bool> admitNative = null)
    {
        lease = null;
        error = "";
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings);
        if (string.IsNullOrEmpty(committed)
            || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, requestedStride)
            || tile.x < 0 || tile.y < 0 || tile.x >= settings.HeightTileGridWidth || tile.y >= settings.HeightTileGridHeight)
        {
            error = "The committed Height manifest, tile, or representation stride is incompatible.";
            return false;
        }

        TerrainAuthoringPreviewDerivedHeightCache.EntryIdentity identity;
        try
        {
            if (!TerrainAuthoringPreviewDerivedHeightCache.TryCaptureIdentity(settings, tile, requestedStride,
                out identity, out error)) return false;
        }
        catch (Exception exception)
        {
            // Source identity I/O is optional; the existing native loader is
            // still useful on a device where persistent data cannot be accessed.
            Warn("Committed derived Height identity is unavailable: " + exception.Message);
            return TryBorrowNative(settings, tile, ref reusableNative, default, committed, requestedStride > 1, out lease, out error, admitNative);
        }

        string failureKey = identity.Namespace + identity.Source;
        if (requestedStride > 1 && identity.Persistable && !cacheIoUnavailable
            && !generationFailures.Contains(failureKey) && generationFailures.Count < 128)
        {
            var result = TerrainAuthoringPreviewDerivedHeightCache.TryRead(
                TerrainAuthoringPreviewDerivedHeightCache.RootPath, identity, out byte[] bytes, out string detail);
            if (result == TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Hit)
            {
                if (TryLeaseDerived(settings, identity, committed, bytes, out lease, out detail))
                {
                    cacheHits++;
                    return true;
                }
                // A changed source is a transaction failure, never a fallback
                // to the previously captured native data.
                if (!IdentityStillCurrent(settings, identity, committed))
                { error = "The committed Height source changed during acquisition."; return false; }
                generationFailures.Add(failureKey);
                Warn("Committed derived Height upload failed; using native data. " + detail);
            }
            else
            {
                if (result == TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Rejected) rejectedEntries++;
                if (result == TerrainAuthoringPreviewDerivedHeightCache.ReadResult.Failed)
                {
                    readFailures++;
                    cacheIoUnavailable = true;
                    Warn("Committed derived Height cache could not be read; using native data. " + detail);
                }
                else if (allowGeneration)
                {
                    if (!EnsureAdmittedNative(settings, tile, ref reusableNative, admitNative, out error)) return false;
                    if (TerrainAuthoringPreviewDerivedHeightCache.TryExtract(reusableNative,
                        settings.HeightTileSamplesPerSide, requestedStride, out bytes, out detail))
                    {
                        bool written = TerrainAuthoringPreviewDerivedHeightCache.TryWrite(
                            TerrainAuthoringPreviewDerivedHeightCache.RootPath, identity, bytes,
                            () => IdentityStillCurrent(settings, identity, committed), out detail);
                        if (!IdentityStillCurrent(settings, identity, committed))
                        { error = "The committed Height source changed during derived generation."; return false; }
                        if (written)
                        {
                            generatedEntries++;
                            if (TryLeaseDerived(settings, identity, committed, bytes, out lease, out detail)) return true;
                            generationFailures.Add(failureKey);
                            Warn("Committed derived Height upload failed; using native data. " + detail);
                        }
                        else
                        {
                            writeFailures++;
                            cacheIoUnavailable = true;
                            Warn("Committed derived Height cache could not be written; using native data. " + detail);
                        }
                    }
                    else
                    {
                        generationFailures.Add(failureKey);
                        Warn("Committed derived Height extraction failed; using native data. " + detail);
                    }
                }
            }
        }
        if (!IdentityStillCurrent(settings, identity, committed))
        { error = "The committed Height source changed during acquisition."; return false; }
        return TryBorrowNative(settings, tile, ref reusableNative, identity, committed, requestedStride > 1, out lease, out error, admitNative);
    }

    private static bool IdentityStillCurrent(WorldSettings settings,
        TerrainAuthoringPreviewDerivedHeightCache.EntryIdentity identity, string committed)
    {
        try
        {
            return TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(settings) == committed
                && TerrainAuthoringPreviewDerivedHeightCache.TryCaptureIdentity(settings, identity.Tile,
                    identity.Stride, out var current, out _)
                && identity.Matches(current) && (!identity.Persistable || current.Persistable);
        }
        catch (Exception) { return false; }
    }

    private static bool TryLeaseDerived(WorldSettings settings,
        TerrainAuthoringPreviewDerivedHeightCache.EntryIdentity identity, string committed, byte[] bytes,
        out TerrainAuthoringPreviewHeightSourceLease lease, out string error)
    {
        lease = null;
        if (!TerrainAuthoringPreviewDerivedHeightCache.TryCreateTexture(bytes, identity.Samples, out var texture, out error)) return false;
        lease = new TerrainAuthoringPreviewHeightSourceLease(texture, identity.Stride, identity.NativeSamples, true, identity, committed);
        if (IsCurrent(settings, lease)) return true;
        lease.Dispose();
        lease = null;
        error = "The committed Height source changed during derived upload.";
        return false;
    }

    // The explicit source owner reserves native bytes only when a disk-derived
    // hit cannot serve the request. Legacy callers keep their existing contract.
    private static bool EnsureAdmittedNative(WorldSettings settings, Vector2Int tile, ref Texture2D native,
        Func<bool> admission, out string error)
    {
        if (admission != null && !admission())
        { error = "Committed Height native acquisition is blocked by the source byte budget."; return false; }
        return EnsureNative(settings, tile, ref native, out error);
    }

    private static bool EnsureNative(WorldSettings settings, Vector2Int tile, ref Texture2D native, out string error)
    {
        if (native != null && TryValidateNativeSource(native, settings.HeightTileSamplesPerSide, out error)) return true;
        return TryLoadCommittedNativeTile(settings, tile, out native, out error);
    }

    private static bool TryBorrowNative(WorldSettings settings, Vector2Int tile, ref Texture2D native,
        TerrainAuthoringPreviewDerivedHeightCache.EntryIdentity identity, string committed, bool fallback,
        out TerrainAuthoringPreviewHeightSourceLease lease, out string error, Func<bool> admitNative = null)
    {
        lease = null;
        if (!EnsureAdmittedNative(settings, tile, ref native, admitNative, out error)) return false;
        var nativeIdentity = identity.IsValid
            ? new TerrainAuthoringPreviewDerivedHeightCache.EntryIdentity(identity.Namespace, identity.Source,
                tile, identity.NativeSamples, 1, identity.Persistable) : default;
        lease = new TerrainAuthoringPreviewHeightSourceLease(native, 1, settings.HeightTileSamplesPerSide, false, nativeIdentity, committed);
        if (lease.HasIdentity && !IsCurrent(settings, lease))
        {
            lease.Dispose(); lease = null;
            error = "The committed Height source changed during native acquisition.";
            return false;
        }
        if (fallback) nativeFallbacks++;
        return true;
    }

    private static void Warn(string message)
    {
        if (!string.IsNullOrEmpty(lastWarning)) return;
        lastWarning = message;
        Debug.LogWarning(message);
    }

    internal static bool TryLoadCommittedNativeTile(
        WorldSettings worldSettings,
        Vector2Int worldTile,
        out Texture2D source,
        out string errorMessage
    )
    {
        source = null;
        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage = "WorldSettings is null.";
            return false;
        }

        if (
            worldTile.x < 0
            || worldTile.y < 0
            || worldTile.x >= worldSettings.HeightTileGridWidth
            || worldTile.y >= worldSettings.HeightTileGridHeight
        )
        {
            errorMessage =
                $"Committed Height tile {worldTile} is outside the world tile grid.";
            return false;
        }

        return TryLoadCommittedNativeTile(
            worldSettings.HeightTileSamplesPerSide,
            worldTile,
            out source,
            out errorMessage
        );
    }

    // Existing caches use the native topology captured at initialization.
    internal static bool TryLoadCommittedNativeTile(
        int nativeSamplesPerSide,
        Vector2Int worldTile,
        out Texture2D source,
        out string errorMessage
    )
    {
        source = null;
        errorMessage = "";

        if (
            nativeSamplesPerSide <= 1
            || worldTile.x < 0
            || worldTile.y < 0
        )
        {
            errorMessage = "The committed Height source topology or tile is invalid.";
            return false;
        }

        string sourcePath =
            TerrainAuthoringStateUtility.GetAuthoringHeightTilePath(
                worldTile.x,
                worldTile.y
            );

        try
        {
            using (WorldMeshesProfiler.PreviewLoadTiles.Auto())
            {
                source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Committed Height tile {worldTile} could not be loaded.\n\n" +
                sourcePath + "\n\n" + exception.Message;
            return false;
        }

        if (
            !TryValidateNativeSource(
                source,
                nativeSamplesPerSide,
                out errorMessage
            )
        )
        {
            errorMessage =
                $"Committed Height tile {worldTile}: " +
                errorMessage + "\n\n" + sourcePath;
            source = null;
            return false;
        }

        nativeLoads++;
        return true;
    }

    internal static bool TryValidateNativeSource(
        Texture2D source,
        int nativeSamplesPerSide,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (source == null)
        {
            errorMessage = "The native committed Height source is missing.";
            return false;
        }

        if (
            nativeSamplesPerSide <= 1
            || source.width != nativeSamplesPerSide
            || source.height != nativeSamplesPerSide
        )
        {
            errorMessage =
                "The committed Height source has unexpected native dimensions.\n\n" +
                $"Expected: {nativeSamplesPerSide} x {nativeSamplesPerSide}\n" +
                $"Actual: {source.width} x {source.height}";
            return false;
        }

        if (source.format != TextureFormat.RFloat)
        {
            errorMessage =
                "The committed Height source must use TextureFormat.RFloat.\n\n" +
                $"Actual: {source.format}";
            return false;
        }

        return true;
    }
}

