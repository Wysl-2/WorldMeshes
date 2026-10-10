using System;
using System.Collections.Generic;
using UnityEngine;

internal enum TerrainAuthoringPreviewHeightAcquisitionPurpose { DisplayAdmission, AuthoringReplacement, NativeWorking }

// Subordinate committed-source owner. Leases, including GPU-held retiring uses,
// count against the same byte budget. Imported textures are only borrowed.
internal sealed class TerrainAuthoringPreviewHeightSourceResidency : IDisposable
{
    private sealed class Entry
    {
        internal TerrainAuthoringPreviewHeightSourceLease Source;
        internal int Readers;
        internal bool CachePopulationAttempted;
        internal long Bytes, Use;
    }
    private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
    private readonly int thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
    private long clock, reserved;
    private bool disposed;
    internal long BudgetBytes { get; }
    internal long ResidentBytes { get; private set; }
    internal long LeasedBytes { get; private set; }
    internal long PeakBytes { get; private set; }
    internal bool LastAcquisitionBlocked { get; private set; }
    internal int Hits { get; private set; }
    internal int Misses { get; private set; }
    internal int Evictions { get; private set; }
    internal bool ReleaseComplete => disposed && entries.Count == 0 && reserved == 0;
    internal TerrainAuthoringPreviewHeightSourceResidency(int budgetMiB)
    { BudgetBytes = checked((long)budgetMiB * 1024 * 1024); }
    private void RequireThread()
    {
        if (thread != System.Threading.Thread.CurrentThread.ManagedThreadId)
            throw new InvalidOperationException("Committed Height residency requires its creating Unity main thread.");
    }
    internal bool TryAcquire(WorldSettings world, Vector2Int tile, int stride,
        TerrainAuthoringPreviewHeightAcquisitionPurpose purpose,
        out TerrainAuthoringPreviewHeightSourceLease lease, out string error)
    {
        RequireThread(); lease = null; error = ""; LastAcquisitionBlocked = false;
        if (disposed || world == null || !TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(world, stride))
        { error = "Committed Height source residency is unavailable or incompatible."; return false; }
        if (purpose == TerrainAuthoringPreviewHeightAcquisitionPurpose.NativeWorking && stride != 1)
        { error = "Native working acquisition requires exact native precision."; return false; }
        if (!TerrainAuthoringPreviewDerivedHeightCache.TryCaptureIdentity(world, tile, stride, out var identity, out error)) return false;
        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(world);
        string key = world.GetInstanceID() + ":" + identity.Namespace + ":" + identity.Source + ":" + tile + ":" + stride;
        if (entries.TryGetValue(key, out var entry))
        {
            if (TerrainAuthoringPreviewHeightSourceUtility.IsCurrent(world, entry.Source))
            {
                // A dirty native fallback can later populate the derived cache at
                // an eligible display boundary. Pinned readers retain their source.
                if (purpose == TerrainAuthoringPreviewHeightAcquisitionPurpose.DisplayAdmission && stride > 1
                    && entry.Source.SourceStride == 1 && !entry.CachePopulationAttempted && entry.Readers == 0)
                    Remove(key, entry);
                else { Hits++; lease = Borrow(entry); return true; }
            }
            else
            {
                if (entry.Readers > 0) { LastAcquisitionBlocked = true; error = "Committed Height source residency is blocked by a retiring source revision."; return false; }
                Remove(key, entry);
            }
        }
        Misses++;
        // Reserve decode/upload bytes first. A derived disk hit never needs
        // native headroom. Cold extraction/fallback reserves native bytes before loading.
        long native = checked((long)world.HeightTileSamplesPerSide * world.HeightTileSamplesPerSide * 8);
        long samples = TerrainHeightResolutionUtility.GetSamplesPerSide(world, stride);
        long transient = stride > 1 ? checked(samples * samples * 12) : native;
        bool nativeReserved = stride == 1;
        EvictToFit(transient);
        if (transient > BudgetBytes - ResidentBytes - reserved)
        { LastAcquisitionBlocked = true; error = "Committed Height source residency is blocked by its byte budget or pinned leases."; return false; }
        reserved += transient; PeakBytes = Math.Max(PeakBytes, ResidentBytes + reserved);
        bool AdmitNative()
        {
            if (nativeReserved) return true;
            EvictToFit(native);
            if (native > BudgetBytes - ResidentBytes - reserved)
            { LastAcquisitionBlocked = true; return false; }
            reserved += native; transient += native; nativeReserved = true;
            PeakBytes = Math.Max(PeakBytes, ResidentBytes + reserved); return true;
        }
        TerrainAuthoringPreviewHeightSourceLease acquired = null;
        try
        {
            Texture2D nativeSource = null;
            if (!TerrainAuthoringPreviewHeightSourceUtility.TryAcquireCommittedSource(world, tile, stride, ref nativeSource,
                purpose == TerrainAuthoringPreviewHeightAcquisitionPurpose.DisplayAdmission, out acquired, out error, AdmitNative)) return false;
            if (acquired == null || !acquired.HasIdentity || acquired.CommittedSignature != committed
                || !TerrainAuthoringPreviewHeightSourceUtility.IsCurrent(world, acquired))
            { error = "The committed Height identity changed during reusable acquisition."; return false; }
            long bytes = checked((long)acquired.Texture.width * acquired.Texture.height * 8);
            entry = new Entry { Source = acquired, Bytes = bytes, Use = ++clock,
                CachePopulationAttempted = purpose == TerrainAuthoringPreviewHeightAcquisitionPurpose.DisplayAdmission };
            entries.Add(key, entry); acquired = null;
            reserved -= transient; transient = 0; ResidentBytes += bytes;
            lease = Borrow(entry); return true;
        }
        finally { acquired?.Dispose(); reserved -= transient; }
    }
    private TerrainAuthoringPreviewHeightSourceLease Borrow(Entry entry)
    {
        if (entry.Readers++ == 0) LeasedBytes += entry.Bytes;
        entry.Use = ++clock;
        var source = entry.Source;
        return new TerrainAuthoringPreviewHeightSourceLease(source.Texture, source.SourceStride, source.NativeSamplesPerSide,
            source.OwnsTexture, source.Identity, source.CommittedSignature, () => Release(entry));
    }
    private void Release(Entry entry)
    {
        RequireThread();
        if (--entry.Readers == 0)
        {
            LeasedBytes -= entry.Bytes; entry.Use = ++clock;
            if (disposed)
            {
                string key = null; foreach (var pair in entries) if (ReferenceEquals(pair.Value, entry)) { key = pair.Key; break; }
                if (key != null) Remove(key, entry);
            }
        }
    }
    private void EvictToFit(long required)
    {
        while (required > BudgetBytes - ResidentBytes - reserved)
        {
            string key = null; Entry oldest = null;
            foreach (var pair in entries)
            {
                var item = pair.Value; if (item.Readers != 0) continue;
                // Large native assets lose references before small decoded sources.
                if (oldest == null || !item.Source.OwnsTexture && oldest.Source.OwnsTexture
                    || item.Source.OwnsTexture == oldest.Source.OwnsTexture && item.Use < oldest.Use)
                { oldest = item; key = pair.Key; }
            }
            if (oldest == null) return;
            Remove(key, oldest);
        }
    }
    private void Remove(string key, Entry entry)
    { entries.Remove(key); ResidentBytes -= entry.Bytes; entry.Source.Dispose(); Evictions++; }
    public void Dispose()
    {
        RequireThread(); disposed = true;
        foreach (var pair in new List<KeyValuePair<string, Entry>>(entries))
            if (pair.Value.Readers == 0) Remove(pair.Key, pair.Value);
    }
}
