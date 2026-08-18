Shader "Custom/ClipmapTerrain"
{
    Properties
    {
        // =================================================
        // SURFACE
        // =================================================

        [MainColor]
        _BaseColor(
            "Base Color",
            Color
        ) = (1, 1, 1, 1)

        [MainTexture]
        _BaseMap(
            "Base Map",
            2D
        ) = "white" {}

        // =================================================
        // HEIGHT CACHE
        // =================================================

        [HideInInspector]
        _HeightCache(
            "Height Cache",
            2DArray
        ) = "" {}

        [HideInInspector]
        _HeightCacheOriginTile(
            "Height Cache Origin Tile",
            Vector
        ) = (0, 0, 0, 0)

        [HideInInspector]
        _HeightCacheSize(
            "Height Cache Size",
            Vector
        ) = (1, 1, 0, 0)

        [HideInInspector]
        _HeightTileSamplesPerSide(
            "Height Tile Samples Per Side",
            Float
        ) = 257

        [HideInInspector]
        _HeightSampleSpacing(
            "Height Sample Spacing",
            Float
        ) = 1

        [HideInInspector]
        _WorldSizeXZ(
            "World Size XZ",
            Vector
        ) = (1, 1, 0, 0)

        [HideInInspector]
        _HeightCacheReady(
            "Height Cache Ready",
            Float
        ) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardLit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #pragma target 3.5
            #pragma require 2darray

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"

            // =================================================
            // VERTEX INPUT
            // =================================================

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            // =================================================
            // VERTEX OUTPUT
            // =================================================

            struct Varyings
            {
                float4 positionHCS :
                    SV_POSITION;

                float2 uv :
                    TEXCOORD0;

                float3 positionWS :
                    TEXCOORD1;

                float3 normalWS :
                    TEXCOORD2;
            };

            // =================================================
            // SURFACE TEXTURE
            // =================================================

            TEXTURE2D(
                _BaseMap
            );

            SAMPLER(
                sampler_BaseMap
            );

            // =================================================
            // HEIGHT CACHE
            // =================================================

            TEXTURE2D_ARRAY(
                _HeightCache
            );

            // =================================================
            // PER-MATERIAL DATA
            // =================================================

            CBUFFER_START(
                UnityPerMaterial
            )

                half4 _BaseColor;

                float4 _BaseMap_ST;

                /*
                 * xy =
                 * absolute tile coordinate represented by
                 * cache-local tile (0, 0).
                 */
                float4 _HeightCacheOriginTile;

                /*
                 * x = cache width in tiles
                 * y = cache height in tiles
                 */
                float4 _HeightCacheSize;

                /*
                 * Number of samples along one side of
                 * each height tile.
                 */
                float _HeightTileSamplesPerSide;

                /*
                 * World-space distance between adjacent
                 * authoritative height samples.
                 */
                float _HeightSampleSpacing;

                /*
                 * x = world size X
                 * y = world size Z
                 */
                float4 _WorldSizeXZ;

                /*
                 * 0 = cache unavailable
                 * 1 = cache ready
                 */
                float _HeightCacheReady;

            CBUFFER_END

            // =================================================
            // SAMPLE TERRAIN HEIGHT
            // =================================================

            float SampleTerrainHeight(
                float2 worldXZ,
                out float valid
            )
            {
                valid =
                    0.0;

                // ---------------------------------------------
                // Cache not ready
                // ---------------------------------------------

                if (
                    _HeightCacheReady <
                    0.5
                )
                {
                    return 0.0;
                }

                // ---------------------------------------------
                // Safe metadata
                // ---------------------------------------------

                float sampleSpacing =
                    max(
                        _HeightSampleSpacing,
                        0.000001
                    );

                int samplesPerSide =
                    max(
                        (int)floor(
                            _HeightTileSamplesPerSide
                            +
                            0.5
                        ),
                        2
                    );

                int tileIntervals =
                    samplesPerSide -
                    1;

                int cacheWidth =
                    max(
                        (int)floor(
                            _HeightCacheSize.x
                            +
                            0.5
                        ),
                        1
                    );

                int cacheHeight =
                    max(
                        (int)floor(
                            _HeightCacheSize.y
                            +
                            0.5
                        ),
                        1
                    );

                float2 worldSize =
                    max(
                        _WorldSizeXZ.xy,
                        float2(
                            0.0,
                            0.0
                        )
                    );

                // ---------------------------------------------
                // Clamp lookup position to actual world
                // ---------------------------------------------

                float2 clampedWorldXZ =
                    clamp(
                        worldXZ,
                        float2(
                            0.0,
                            0.0
                        ),
                        worldSize
                    );

                // =================================================
                // GLOBAL HEIGHT SAMPLE
                // =================================================

                int2 globalSample =
                    (int2)floor(
                        clampedWorldXZ /
                        sampleSpacing
                        +
                        0.5
                    );

                int2 worldMaxSample =
                    (int2)floor(
                        worldSize /
                        sampleSpacing
                        +
                        0.5
                    );

                // =================================================
                // HEIGHT TILE GRID SIZE
                // =================================================

                int2 totalTileCount =
                    (
                        worldMaxSample
                        +
                        tileIntervals
                        -
                        1
                    )
                    /
                    tileIntervals;

                totalTileCount =
                    max(
                        totalTileCount,
                        int2(
                            1,
                            1
                        )
                    );

                // =================================================
                // ABSOLUTE TILE COORDINATE
                // =================================================

                int2 tileCoordinate =
                    globalSample /
                    tileIntervals;

                /*
                 * At the exact maximum world sample the raw
                 * coordinate points one tile beyond the grid.
                 */
                tileCoordinate =
                    min(
                        tileCoordinate,
                        totalTileCount -
                        1
                    );

                // =================================================
                // SAMPLE COORDINATE INSIDE TILE
                // =================================================

                int2 localSample =
                    globalSample
                    -
                    tileCoordinate *
                    tileIntervals;

                // =================================================
                // CACHE-LOCAL TILE COORDINATE
                // =================================================

                int2 cacheOrigin =
                    (int2)floor(
                        _HeightCacheOriginTile.xy
                        +
                        0.5
                    );

                int2 cacheLocalTile =
                    tileCoordinate
                    -
                    cacheOrigin;

                if (
                    cacheLocalTile.x < 0
                    ||
                    cacheLocalTile.y < 0
                    ||
                    cacheLocalTile.x >=
                        cacheWidth
                    ||
                    cacheLocalTile.y >=
                        cacheHeight
                )
                {
                    return 0.0;
                }

                // =================================================
                // TEXTURE ARRAY SLICE
                // =================================================

                int slice =
                    cacheLocalTile.x
                    +
                    cacheLocalTile.y *
                    cacheWidth;

                // =================================================
                // EXACT HEIGHT TEXEL
                // =================================================

                float terrainHeight =
                    LOAD_TEXTURE2D_ARRAY(
                        _HeightCache,
                        localSample,
                        slice
                    ).r;

                valid =
                    1.0;

                return terrainHeight;
            }

            // =================================================
            // CALCULATE TERRAIN NORMAL
            // =================================================

            float3 CalculateTerrainNormal(
                float2 worldXZ,
                float centerHeight
            )
            {
                /*
                 * Calculate the terrain slope directly from
                 * neighboring authoritative height samples.
                 *
                 * This means the normal comes from the same
                 * heightfield that displaced the geometry.
                 */

                float spacing =
                    max(
                        _HeightSampleSpacing,
                        0.000001
                    );

                float leftValid;
                float rightValid;
                float backValid;
                float forwardValid;

                float heightLeft =
                    SampleTerrainHeight(
                        worldXZ
                        -
                        float2(
                            spacing,
                            0.0
                        ),
                        leftValid
                    );

                float heightRight =
                    SampleTerrainHeight(
                        worldXZ
                        +
                        float2(
                            spacing,
                            0.0
                        ),
                        rightValid
                    );

                float heightBack =
                    SampleTerrainHeight(
                        worldXZ
                        -
                        float2(
                            0.0,
                            spacing
                        ),
                        backValid
                    );

                float heightForward =
                    SampleTerrainHeight(
                        worldXZ
                        +
                        float2(
                            0.0,
                            spacing
                        ),
                        forwardValid
                    );

                /*
                 * If a neighboring sample is temporarily outside
                 * the resident cache, fall back to the center
                 * height rather than producing an invalid slope.
                 */

                if (leftValid < 0.5)
                {
                    heightLeft =
                        centerHeight;
                }

                if (rightValid < 0.5)
                {
                    heightRight =
                        centerHeight;
                }

                if (backValid < 0.5)
                {
                    heightBack =
                        centerHeight;
                }

                if (forwardValid < 0.5)
                {
                    heightForward =
                        centerHeight;
                }

                /*
                 * Central-difference normal.
                 *
                 * X:
                 * left height - right height
                 *
                 * Y:
                 * horizontal distance across two samples
                 *
                 * Z:
                 * back height - forward height
                 */

                float3 normalWS =
                    float3(
                        heightLeft -
                            heightRight,

                        2.0 *
                            spacing,

                        heightBack -
                            heightForward
                    );

                return normalize(
                    normalWS
                );
            }

            // =================================================
            // VERTEX SHADER
            // =================================================

            Varyings vert(
                Attributes IN
            )
            {
                Varyings OUT;

                // ---------------------------------------------
                // Object → world
                // ---------------------------------------------

                float3 positionWS =
                    TransformObjectToWorld(
                        IN.positionOS.xyz
                    );

                // ---------------------------------------------
                // Default flat normal
                // ---------------------------------------------

                float3 normalWS =
                    float3(
                        0.0,
                        1.0,
                        0.0
                    );

                // ---------------------------------------------
                // Height lookup
                // ---------------------------------------------

                float heightValid;

                float terrainHeight =
                    SampleTerrainHeight(
                        positionWS.xz,
                        heightValid
                    );

                // ---------------------------------------------
                // Displace world Y
                // ---------------------------------------------

                if (
                    _HeightCacheReady >
                        0.5
                    &&
                    heightValid >
                        0.5
                )
                {
                    positionWS.y =
                        terrainHeight;

                    normalWS =
                        CalculateTerrainNormal(
                            positionWS.xz,
                            terrainHeight
                        );
                }

                // ---------------------------------------------
                // Output
                // ---------------------------------------------

                OUT.positionWS =
                    positionWS;

                OUT.normalWS =
                    normalWS;

                OUT.positionHCS =
                    TransformWorldToHClip(
                        positionWS
                    );

                OUT.uv =
                    TRANSFORM_TEX(
                        IN.uv,
                        _BaseMap
                    );

                return OUT;
            }

            // =================================================
            // FRAGMENT SHADER
            // =================================================

            half4 frag(
                Varyings IN
            ) : SV_Target
            {
                // ---------------------------------------------
                // Actual world boundary
                // ---------------------------------------------

                if (
                    _HeightCacheReady >
                    0.5
                )
                {
                    clip(
                        IN.positionWS.x
                    );

                    clip(
                        IN.positionWS.z
                    );

                    clip(
                        _WorldSizeXZ.x
                        -
                        IN.positionWS.x
                    );

                    clip(
                        _WorldSizeXZ.y
                        -
                        IN.positionWS.z
                    );
                }

                // ---------------------------------------------
                // Surface colour
                // ---------------------------------------------

                half4 surfaceColor =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        IN.uv
                    )
                    *
                    _BaseColor;

                // ---------------------------------------------
                // Terrain normal
                // ---------------------------------------------

                half3 normalWS =
                    normalize(
                        IN.normalWS
                    );

                // ---------------------------------------------
                // Main realtime light
                // ---------------------------------------------

                Light mainLight =
                    GetMainLight();

                half3 directLighting =
                    LightingLambert(
                        mainLight.color,
                        mainLight.direction,
                        normalWS
                    )
                    *
                    mainLight.distanceAttenuation;

                // ---------------------------------------------
                // Ambient / probe lighting
                // ---------------------------------------------

                half3 ambientLighting =
                    max(
                        SampleSH(
                            normalWS
                        ),
                        half3(
                            0.0,
                            0.0,
                            0.0
                        )
                    );

                // ---------------------------------------------
                // Final lighting
                // ---------------------------------------------

                half3 lighting =
                    directLighting
                    +
                    ambientLighting;

                return half4(
                    surfaceColor.rgb *
                        lighting,

                    surfaceColor.a
                );
            }

            ENDHLSL
        }
    }

    FallBack Off
}