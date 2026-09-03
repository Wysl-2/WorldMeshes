using UnityEngine;

/*
 * Runtime metadata for the baked terrain surface-mask tiles.
 *
 * Stage 8 currently stores one normalized R8 channel:
 *
 *     R = final Scree suitability
 *
 * The channel-layout version is explicit so future packed channels can be
 * added without silently reinterpreting old generated assets.
 */
public sealed class TerrainSurfaceMaskManifest :
    ScriptableObject
{
    public const int CurrentCompilerVersion =
        1;

    public const int CurrentChannelLayoutVersion =
        1;

    public const string SurfaceTileAddressPrefix =
        "TerrainSurface/SurfaceTile";

    // =====================================================
    // GENERATED OUTPUT STATE
    // =====================================================

    public bool isComplete =
        false;

    public int compilerVersion =
        0;

    public int channelLayoutVersion =
        0;

    public int surfaceMaskGenerationRevision =
        0;

    // =====================================================
    // SOURCE HEIGHTMAP STATE
    // =====================================================

    public int sourceHeightmapGenerationRevision =
        0;

    public string sourceHeightmapSignature =
        "";

    public string sourceAuthoringSignature =
        "";

    public string sourceAuthoringContentHash =
        "";

    // =====================================================
    // SURFACE SETTINGS / GENERATED SIGNATURES
    // =====================================================

    public string surfaceSettingsSignature =
        "";

    public string surfaceGenerationSignature =
        "";

    // =====================================================
    // TILE LAYOUT
    // =====================================================

    public int tileGridWidth =
        0;

    public int tileGridHeight =
        0;

    public float tileWorldSize =
        0f;

    public int samplesPerSide =
        0;

    public float sampleSpacing =
        0f;

    public Vector2 worldSizeXZ =
        Vector2.zero;

    public int TileCount =>
        Mathf.Max(
            0,
            tileGridWidth
        )
        *
        Mathf.Max(
            0,
            tileGridHeight
        );

    public bool IsTileCoordinateValid(
        int tileX,
        int tileZ
    )
    {
        return
            tileX >= 0
            &&
            tileZ >= 0
            &&
            tileX < tileGridWidth
            &&
            tileZ < tileGridHeight;
    }

    public string GetSurfaceTileAddress(
        int tileX,
        int tileZ
    )
    {
        return
            $"{SurfaceTileAddressPrefix}_" +
            $"{tileX}_{tileZ}";
    }

    public bool MatchesHeightLayout(
        TerrainHeightmapManifest heightManifest
    )
    {
        if (heightManifest == null)
        {
            return false;
        }

        return
            tileGridWidth ==
                heightManifest.heightTileGridWidth
            &&
            tileGridHeight ==
                heightManifest.heightTileGridHeight
            &&
            samplesPerSide ==
                heightManifest.heightTileSamplesPerSide
            &&
            Mathf.Approximately(
                tileWorldSize,
                heightManifest.heightTileWorldSize
            )
            &&
            Mathf.Approximately(
                sampleSpacing,
                heightManifest.HeightSampleSpacing
            )
            &&
            Mathf.Approximately(
                worldSizeXZ.x,
                heightManifest.WorldSizeX
            )
            &&
            Mathf.Approximately(
                worldSizeXZ.y,
                heightManifest.WorldSizeZ
            );
    }
}
