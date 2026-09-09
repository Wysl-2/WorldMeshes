Shader "Hidden/WorldMeshes/ClipmapTerrainWireframe"
{
    Properties
    {
        // =================================================
        // SHARED HEIGHT CACHE / WORLD / STITCH STATE
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
        _HeightCacheReady(
            "Height Cache Ready",
            Float
        ) = 0

        [HideInInspector]
        _WorldSizeXZ(
            "World Size XZ",
            Vector
        ) = (0, 0, 0, 0)

        [HideInInspector]
        _WorldBoundsReady(
            "World Bounds Ready",
            Float
        ) = 0

        [HideInInspector]
        _ClipmapTransitionOffset(
            "Clipmap Transition Offset",
            Vector
        ) = (0, 0, 0, 0)

        // =================================================
        // WIREFRAME APPEARANCE
        // =================================================

        [HideInInspector]
        _WireframeColor(
            "Wireframe Color",
            Color
        ) = (1, 1, 1, 1)

        [HideInInspector]
        _WireframeOpacity(
            "Wireframe Opacity",
            Range(0, 1)
        ) = 0.8

        // =================================================
        // TRANSIENT MATERIAL RENDER STATE
        // =================================================

        [HideInInspector]
        _WireframeZWrite(
            "Wireframe ZWrite",
            Float
        ) = 0

        [HideInInspector]
        _WireframeColorMask(
            "Wireframe Color Mask",
            Float
        ) = 15

        [HideInInspector]
        _WireframeSrcBlend(
            "Wireframe Source Blend",
            Float
        ) = 5

        [HideInInspector]
        _WireframeDstBlend(
            "Wireframe Destination Blend",
            Float
        ) = 10

        [HideInInspector]
        _WireframeDepthBias(
            "Wireframe Clip Depth Bias",
            Float
        ) = 0.00001
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off

        ZTest LEqual
        ZWrite [_WireframeZWrite]

        Blend [_WireframeSrcBlend] [_WireframeDstBlend]

        ColorMask [_WireframeColorMask]

        Pass
        {
            Name "AuthoringWireframe"

            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #pragma target 3.5
            #pragma require 2darray

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // =================================================
            // INPUT / OUTPUT
            // =================================================

            struct Attributes
            {
                float4 positionOS :
                    POSITION;

                /*
                 * Copied directly from the generated source mesh.
                 *
                 * TEXCOORD3.x:
                 *
                 * 0 = coarse / stationary stitch side
                 * 1 = fine / adaptive stitch side
                 */
                float4 clipmapData :
                    TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionHCS :
                    SV_POSITION;

                float3 positionWS :
                    TEXCOORD0;
            };

            // =================================================
            // SHARED TERRAIN STATE
            // =================================================

            CBUFFER_START(
                UnityPerMaterial
            )

                float4 _HeightCacheOriginTile;
                float4 _HeightCacheSize;
                float _HeightTileSamplesPerSide;
                float _HeightSampleSpacing;

                float4 _WorldSizeXZ;
                float _WorldBoundsReady;

                float _HeightCacheReady;

                float4 _ClipmapTransitionOffset;

                float4 _WireframeColor;
                float _WireframeOpacity;

                float _WireframeZWrite;
                float _WireframeColorMask;
                float _WireframeSrcBlend;
                float _WireframeDstBlend;
                float _WireframeDepthBias;

            CBUFFER_END

            #include "Assets/WorldMeshes/Shaders/Terrain/ClipmapTerrainHeight.hlsl"

            // =================================================
            // VERTEX
            // =================================================

            Varyings vert(
                Attributes IN
            )
            {
                Varyings OUT;

                float3 positionWS =
                    TransformObjectToWorld(
                        IN.positionOS.xyz
                    );

                positionWS =
                    ApplyClipmapTransitionOffset(
                        positionWS,
                        IN.clipmapData.x
                    );

                ApplyTerrainHeightDisplacementPositionOnly(
                    positionWS
                );

                OUT.positionWS =
                    positionWS;

                OUT.positionHCS =
                    TransformWorldToHClip(
                        positionWS
                    );

                /*
                 * Only the visible line material uses this small
                 * clip-space depth bias. The invisible depth material
                 * sets the property to zero.
                 */
                if (
                    _WireframeDepthBias >
                    0.0
                )
                {
                    #if UNITY_REVERSED_Z

                        OUT.positionHCS.z +=
                            _WireframeDepthBias *
                            OUT.positionHCS.w;

                    #else

                        OUT.positionHCS.z -=
                            _WireframeDepthBias *
                            OUT.positionHCS.w;

                    #endif
                }

                return
                    OUT;
            }

            // =================================================
            // FRAGMENT
            // =================================================

            half4 frag(
                Varyings IN
            ) : SV_Target
            {
                ClipTerrainFragmentToWorld(
                    IN.positionWS.xz
                );

                return
                    half4(
                        _WireframeColor.rgb,
                        saturate(
                            _WireframeColor.a *
                            _WireframeOpacity
                        )
                    );
            }

            ENDHLSL
        }
    }

    FallBack Off
}
