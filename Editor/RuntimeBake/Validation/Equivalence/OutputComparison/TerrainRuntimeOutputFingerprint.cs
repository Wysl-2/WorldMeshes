using UnityEngine;

public enum TerrainRuntimeOutputFingerprintKind
{
    Heightmap,
    SurfaceMask,
    CollisionMesh
}

/*
 * Small immutable payload identity record. GUID/path are diagnostic metadata;
 * PayloadHash is derived only from deterministic generated content plus the
 * logical representation/coordinate/structural shape of that payload.
 */
public sealed class TerrainRuntimeOutputFingerprint
{
    public TerrainRuntimeOutputFingerprintKind Kind { get; private set; }
    public int Variant { get; private set; }
    public Vector2Int Coordinate { get; private set; }

    public string AssetPath { get; private set; }
    public string AssetGuid { get; private set; }
    public string PayloadHash { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public string FormatLabel { get; private set; }

    public int VertexCount { get; private set; }
    public int SubMeshCount { get; private set; }
    public int IndexCount { get; private set; }

    internal TerrainRuntimeOutputFingerprint(
        TerrainRuntimeOutputFingerprintKind kind,
        int variant,
        Vector2Int coordinate,
        string assetPath,
        string assetGuid,
        string payloadHash,
        int width,
        int height,
        string formatLabel,
        int vertexCount,
        int subMeshCount,
        int indexCount
    )
    {
        Kind = kind;
        Variant = Mathf.Max(0, variant);
        Coordinate = coordinate;
        AssetPath = assetPath ?? "";
        AssetGuid = assetGuid ?? "";
        PayloadHash = payloadHash ?? "";
        Width = Mathf.Max(0, width);
        Height = Mathf.Max(0, height);
        FormatLabel = formatLabel ?? "";
        VertexCount = Mathf.Max(0, vertexCount);
        SubMeshCount = Mathf.Max(0, subMeshCount);
        IndexCount = Mathf.Max(0, indexCount);
    }
}
