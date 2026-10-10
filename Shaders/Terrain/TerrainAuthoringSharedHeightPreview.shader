// The authoring display has its own small surface program; the runtime terrain
// material retains its normal PBR and baked-surface shader unchanged.
Shader "Hidden/WorldMeshes/TerrainAuthoringSharedHeightPreview"
{
    Properties
    {
        [MainColor] _BaseColor("Ground Color", Color) = (1,1,1,1)
        [MainTexture] _BaseMap("Ground Map", 2D) = "white" {}
        _BaseMapWorldSize("Ground World Size", Float) = 8
        _SlopeColor("Rock Color", Color) = (1,1,1,1)
        _SlopeMap("Rock Map", 2D) = "white" {}
        _SlopeMapWorldSize("Rock World Size", Float) = 6
        _SlopeBlendStart("Rock Blend Start", Float) = 0.2
        _SlopeBlendEnd("Rock Blend End", Float) = 0.5

        [HideInInspector] _HeightCache("Height Cache", 2DArray) = "" {}
        [HideInInspector] _HeightCacheOriginTile("Height Origin", Vector) = (0,0,0,0)
        [HideInInspector] _HeightCacheSize("Height Size", Vector) = (1,1,0,0)
        [HideInInspector] _HeightTileSamplesPerSide("Height Samples", Float) = 257
        [HideInInspector] _HeightSampleSpacing("Height Spacing", Float) = 1
        [HideInInspector] _HeightNormalSampleSpacingFine("Fine Normal Spacing", Float) = 0
        [HideInInspector] _HeightNormalSampleSpacingCoarse("Coarse Normal Spacing", Float) = 0
        [HideInInspector] _HeightCacheReady("Height Ready", Float) = 0
        [HideInInspector] _WorldSizeXZ("World Size", Vector) = (0,0,0,0)
        [HideInInspector] _WorldBoundsReady("World Ready", Float) = 0
        [HideInInspector] _ClipmapTransitionOffset("Clipmap Transition", Vector) = (0,0,0,0)

        [HideInInspector] _EditorSharedHeightEnabled("Editor Shared Height Enabled", Float) = 0
        [HideInInspector] _EditorSharedHeightMap("Editor Shared Height Map", 2D) = "black" {}
        [HideInInspector] _EditorSharedHeightMapWindow("Editor Shared Height Map Window", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightTopology("Editor Shared Height Topology", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool0("Editor Shared Height Pool 0", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo0("Editor Shared Height Pool Info 0", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool1("Editor Shared Height Pool 1", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo1("Editor Shared Height Pool Info 1", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool2("Editor Shared Height Pool 2", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo2("Editor Shared Height Pool Info 2", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool3("Editor Shared Height Pool 3", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo3("Editor Shared Height Pool Info 3", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool4("Editor Shared Height Pool 4", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo4("Editor Shared Height Pool Info 4", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool5("Editor Shared Height Pool 5", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo5("Editor Shared Height Pool Info 5", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool6("Editor Shared Height Pool 6", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo6("Editor Shared Height Pool Info 6", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool7("Editor Shared Height Pool 7", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo7("Editor Shared Height Pool Info 7", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool8("Editor Shared Height Pool 8", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo8("Editor Shared Height Pool Info 8", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool9("Editor Shared Height Pool 9", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo9("Editor Shared Height Pool Info 9", Vector) = (0,0,0,0)
        [HideInInspector] _EditorSharedHeightPool10("Editor Shared Height Pool 10", 2DArray) = "" {}
        [HideInInspector] _EditorSharedHeightPoolInfo10("Editor Shared Height Pool Info 10", Vector) = (0,0,0,0)

        [HideInInspector] _AuthoringVisualizationEnabled("Visualize", Float) = 0
        [HideInInspector] _AuthoringVisualizationMode("Visualize Mode", Float) = 0
        [HideInInspector] _AuthoringHeightRange("Height Range", Vector) = (0,1,0,0)
        [HideInInspector] _AuthoringWireframeOnly("Wireframe Only", Float) = 0
        [HideInInspector] _AuthoringContoursEnabled("Contours", Float) = 0
        [HideInInspector] _AuthoringContourInterval("Contour Interval", Float) = 10
        [HideInInspector] _AuthoringChunkGridEnabled("Chunk Grid", Float) = 0
        [HideInInspector] _AuthoringChunkSize("Chunk Size", Float) = 128
        [HideInInspector] _AuthoringHeightTileGridEnabled("Height Tile Grid", Float) = 0
        [HideInInspector] _AuthoringHeightTileWorldSize("Height Tile World Size", Float) = 256
        [HideInInspector] _AuthoringWorldBoundaryEnabled("World Boundary", Float) = 0
        [HideInInspector] _AuthoringLODRegionsEnabled("LOD Regions", Float) = 0
        [HideInInspector] _AuthoringLODLevel("LOD Level", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert
            #pragma fragment frag
            // This dedicated material always draws the shared geographical pages.
            // An optional shader_feature could select/strip the non-sampling
            // variant, which leaves all vertices invalid and therefore hidden.
            #define WORLDMESHES_EDITOR_SHARED_HEIGHT 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_SlopeMap);
            SAMPLER(sampler_SlopeMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float _BaseMapWorldSize;
                half4 _SlopeColor;
                float4 _SlopeMap_ST;
                float _SlopeMapWorldSize;
                float _SlopeBlendStart;
                float _SlopeBlendEnd;
                float4 _HeightCacheOriginTile;
                float4 _HeightCacheSize;
                float _HeightTileSamplesPerSide;
                float _HeightSampleSpacing;
                float _HeightNormalSampleSpacingFine;
                float _HeightNormalSampleSpacingCoarse;
                float _HeightCacheReady;
                float4 _WorldSizeXZ;
                float _WorldBoundsReady;
                float4 _ClipmapTransitionOffset;
                float _EditorSharedHeightEnabled;
                float4 _EditorSharedHeightMapWindow;
                float4 _EditorSharedHeightTopology;
                float4 _EditorSharedHeightPoolInfo0;
                float4 _EditorSharedHeightPoolInfo1;
                float4 _EditorSharedHeightPoolInfo2;
                float4 _EditorSharedHeightPoolInfo3;
                float4 _EditorSharedHeightPoolInfo4;
                float4 _EditorSharedHeightPoolInfo5;
                float4 _EditorSharedHeightPoolInfo6;
                float4 _EditorSharedHeightPoolInfo7;
                float4 _EditorSharedHeightPoolInfo8;
                float4 _EditorSharedHeightPoolInfo9;
                float4 _EditorSharedHeightPoolInfo10;
                float _AuthoringVisualizationEnabled;
                float _AuthoringVisualizationMode;
                float4 _AuthoringHeightRange;
                float _AuthoringWireframeOnly;
                float _AuthoringContoursEnabled;
                float _AuthoringContourInterval;
                float _AuthoringChunkGridEnabled;
                float _AuthoringChunkSize;
                float _AuthoringHeightTileGridEnabled;
                float _AuthoringHeightTileWorldSize;
                float _AuthoringWorldBoundaryEnabled;
                float _AuthoringLODRegionsEnabled;
                float _AuthoringLODLevel;
            CBUFFER_END

            // Reuse the authoritative page lookup and mixed-resolution seam
            // profile. Unlike the normal terrain shader this program does not
            // compile scree, curvature, analysis, PBR or multi-light paths.
            #include "Assets/WorldMeshes/Shaders/Terrain/ClipmapTerrainHeight.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 clipmapData : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float heightValid : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS = ApplyClipmapTransitionOffset(positionWS, input.clipmapData.x);
                // Always sample the published shared Height map. This shader
                // has no non-shared variant and never invents valid flat terrain.
                float valid = 0.0;
                float height = SampleEditorSharedTerrainHeight(positionWS.xz, valid);
                if (valid > 0.5) positionWS.y = height;
                output.positionWS = positionWS;
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.heightValid = valid;
                return output;
            }

            float GridLineMask(float2 worldXZ, float spacing)
            {
                float2 coordinate = worldXZ / max(spacing, 0.0001);
                float2 derivative = max(fwidth(coordinate), 0.00001);
                float2 distanceToLine = abs(frac(coordinate + 0.5) - 0.5);
                float2 coverage = 1.0 - smoothstep(derivative * 0.5, derivative * 1.75, distanceToLine);
                return saturate(max(coverage.x, coverage.y));
            }

            half4 frag(Varyings input) : SV_Target
            {
                ClipTerrainFragmentToWorld(input.positionWS.xz);
                if (_AuthoringWireframeOnly > 0.5) clip(-1.0);
                // Missing-page and invalid shared-height results are not valid
                // terrain. Show an unmistakable diagnostic pattern rather than
                // silently hiding the entire clipmap when sampling fails.
                // The untouched source material/runtime shader cannot use this.
                if (_EditorSharedHeightEnabled < 0.5 || input.heightValid < 0.99999)
                {
                    float2 cell = floor(input.positionWS.xz * 0.04);
                    float checker = frac((cell.x + cell.y) * 0.5) * 2.0;
                    return half4(lerp(half3(0.35, 0.0, 0.35),
                        half3(1.0, 0.15, 1.0), checker), 1.0);
                }

                float3 tangentX = ddx(input.positionWS);
                float3 tangentY = ddy(input.positionWS);
                float3 normalWS = normalize(cross(tangentX, tangentY));
                if (normalWS.y < 0.0) normalWS = -normalWS;
                float slope = saturate(1.0 - normalWS.y);

                float2 groundUV = input.positionWS.xz / max(_BaseMapWorldSize, 0.0001);
                groundUV = groundUV * _BaseMap_ST.xy + _BaseMap_ST.zw;
                half4 ground = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, groundUV) * _BaseColor;

                float2 rockUV = input.positionWS.xz / max(_SlopeMapWorldSize, 0.0001);
                rockUV = rockUV * _SlopeMap_ST.xy + _SlopeMap_ST.zw;
                half4 rock = SAMPLE_TEXTURE2D(_SlopeMap, sampler_SlopeMap, rockUV) * _SlopeColor;
                float rockWeight = smoothstep(min(_SlopeBlendStart, _SlopeBlendEnd),
                    max(max(_SlopeBlendStart, _SlopeBlendEnd), _SlopeBlendStart + 0.0001), slope);
                half3 color = lerp(ground.rgb, rock.rgb, rockWeight);

                // Low-cost diffuse cue, not the runtime PBR lighting model.
                float light = 0.45 + 0.55 * saturate(dot(normalWS, normalize(float3(0.4, 0.8, 0.35))));
                color *= light;

                if (_AuthoringVisualizationEnabled > 0.5)
                {
                    if (abs(_AuthoringVisualizationMode - 1.0) < 0.25)
                    {
                        float t = saturate((input.positionWS.y - _AuthoringHeightRange.x)
                            / max(_AuthoringHeightRange.y - _AuthoringHeightRange.x, 0.00001));
                        color = lerp(half3(0.04, 0.16, 0.48), half3(0.93, 0.87, 0.28), t);
                    }
                    else if (abs(_AuthoringVisualizationMode - 2.0) < 0.25)
                    {
                        color = lerp(half3(0.08, 0.20, 0.63), half3(0.95, 0.24, 0.09), slope);
                    }
                    else if (_AuthoringVisualizationMode > 2.5)
                    {
                        // Specialized raw analysis modes are not compiled into
                        // the resilient primary preview. Neutral indicates
                        // unavailable display data; it is not a measurement.
                        color = half3(0.18, 0.18, 0.18);
                    }
                }

                if (_AuthoringLODRegionsEnabled > 0.5)
                {
                    color = lerp(color, half3(0.1, 0.8, 0.7), 0.16 * frac(_AuthoringLODLevel * 0.37 + 0.2));
                }
                if (_AuthoringChunkGridEnabled > 0.5)
                    color = lerp(color, half3(0.0, 0.8, 1.0), GridLineMask(input.positionWS.xz, _AuthoringChunkSize) * 0.9);
                if (_AuthoringHeightTileGridEnabled > 0.5)
                    color = lerp(color, half3(1.0, 0.2, 0.9), GridLineMask(input.positionWS.xz, _AuthoringHeightTileWorldSize) * 0.9);
                if (_AuthoringContoursEnabled > 0.5)
                {
                    float contour = input.positionWS.y / max(_AuthoringContourInterval, 0.0001);
                    float derivative = max(fwidth(contour), 0.00001);
                    float mask = 1.0 - smoothstep(0.5 * derivative, 1.75 * derivative,
                        abs(frac(contour + 0.5) - 0.5));
                    color = lerp(color, half3(0.04, 0.04, 0.04), saturate(mask) * 0.88);
                }
                if (_AuthoringWorldBoundaryEnabled > 0.5)
                {
                    float boundary = GetTerrainWorldBoundaryDistance(input.positionWS.xz);
                    float width = max(fwidth(boundary), 0.0001);
                    float mask = 1.0 - smoothstep(width * 0.35, width * 1.75, boundary);
                    color = lerp(color, half3(1.0, 0.1, 0.02), saturate(mask));
                }
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
