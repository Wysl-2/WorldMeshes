using System;
using UnityEngine;

// A source covers one whole geographical tile at an explicit native-relative
// stride. AssetDatabase sources are borrowed; decoded disk sources are owned.
internal sealed class TerrainAuthoringPreviewHeightSourceLease : IDisposable
{
    private Action release;
    private readonly int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
    internal Texture2D Texture { get; private set; }
    internal readonly int SourceStride;
    internal readonly int NativeSamplesPerSide;
    internal readonly bool OwnsTexture;
    internal readonly string CommittedSignature;
    internal readonly TerrainAuthoringPreviewDerivedHeightCache.EntryIdentity Identity;
    internal readonly bool HasIdentity;

    internal TerrainAuthoringPreviewHeightSourceLease(Texture2D texture, int sourceStride,
        int nativeSamples, bool ownsTexture,
        TerrainAuthoringPreviewDerivedHeightCache.EntryIdentity identity = default,
        string committedSignature = "", Action release = null)
    {
        Texture = texture; this.release = release;
        SourceStride = sourceStride;
        NativeSamplesPerSide = nativeSamples;
        OwnsTexture = ownsTexture;
        Identity = identity;
        HasIdentity = identity.IsValid;
        CommittedSignature = committedSignature;
    }

    internal bool CanMaterializeAt(int destinationStride)
    {
        return Texture != null && TerrainHeightResolutionUtility.IsPowerOfTwo(SourceStride)
            && TerrainHeightResolutionUtility.IsPowerOfTwo(destinationStride)
            && SourceStride <= destinationStride && destinationStride % SourceStride == 0
            && NativeSamplesPerSide > 1 && (NativeSamplesPerSide - 1) % destinationStride == 0;
    }

    public void Dispose()
    {
        if (release != null && threadId != System.Threading.Thread.CurrentThread.ManagedThreadId)
            throw new InvalidOperationException("Shared Height source leases require their creating Unity main thread.");
        Texture2D texture = Texture;
        Texture = null;
        var callback = release; release = null;
        if (callback != null) callback();
        else if (OwnsTexture && texture != null) UnityEngine.Object.DestroyImmediate(texture);
    }
}
