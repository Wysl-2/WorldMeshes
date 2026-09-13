using UnityEngine;

public sealed class TerrainRuntimeInvalidationModifierBaseline
{
    public string StableId { get; internal set; }
    public int Index { get; internal set; }
    public string ModifierType { get; internal set; }
    public bool Exists { get; internal set; }
    public bool Enabled { get; internal set; }
    public Bounds AffectedWorldBounds { get; internal set; }
    public string ContentSignature { get; internal set; }

    public bool IsStamp { get; internal set; }
    public Vector2 PositionXZ { get; internal set; }
    public Vector2 SizeXZ { get; internal set; }
    public float RotationDegrees { get; internal set; }
    public float HeightDelta { get; internal set; }
}

public sealed class TerrainRuntimeInvalidationSettingsBaseline
{
    public int GridWidth { get; internal set; }
    public int GridHeight { get; internal set; }
    public float ChunkSize { get; internal set; }
    public int HeightfieldResolutionPerChunk { get; internal set; }
    public int HeightTileChunkSpan { get; internal set; }
    public int CollisionResolution { get; internal set; }

    public string SurfaceSettingsSignature { get; internal set; }
    public string CollisionSettingsSignature { get; internal set; }
}
