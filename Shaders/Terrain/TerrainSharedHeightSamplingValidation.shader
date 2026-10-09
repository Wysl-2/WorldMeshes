// Explicit validator draw only. Reuses the production height/normal/stitch implementation.
Shader "Hidden/WorldMeshes/TerrainSharedHeightSamplingValidation"
{
    Properties
    {
        [HideInInspector] _HeightCache("Height Cache", 2DArray) = "" {}
        [HideInInspector] _HeightCacheOriginTile("Height Origin", Vector) = (0,0,0,0)
        [HideInInspector] _HeightCacheSize("Height Size", Vector) = (1,1,0,0)
        [HideInInspector] _HeightTileSamplesPerSide("Height Samples", Float) = 17
        [HideInInspector] _HeightSampleSpacing("Height Spacing", Float) = 1
        [HideInInspector] _HeightNormalSampleSpacingFine("Fine Spacing", Float) = 1
        [HideInInspector] _HeightNormalSampleSpacingCoarse("Coarse Spacing", Float) = 1
        [HideInInspector] _HeightCacheReady("Height Ready", Float) = 0
        [HideInInspector] _WorldSizeXZ("World Size", Vector) = (0,0,0,0)
        [HideInInspector] _WorldBoundsReady("World Ready", Float) = 0
        [HideInInspector] _ClipmapTransitionOffset("Transition Offset", Vector) = (0,0,0,0)
        [HideInInspector] _SharedHeightProbeClipWorld("Clip World", Float) = 0
        // Explicitly selected transient Editor shared-page material variant.
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
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "HeightSamplingProbe"
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ WORLDMESHES_EDITOR_SHARED_HEIGHT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _HeightCacheOriginTile, _HeightCacheSize;
                float _HeightTileSamplesPerSide, _HeightSampleSpacing;
                float _HeightNormalSampleSpacingFine, _HeightNormalSampleSpacingCoarse;
                float _HeightCacheReady, _WorldBoundsReady, _SharedHeightProbeClipWorld;
                float4 _WorldSizeXZ, _ClipmapTransitionOffset;
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
            CBUFFER_END
            #include "Assets/WorldMeshes/Shaders/Terrain/ClipmapTerrainHeight.hlsl"
            struct Attributes { float3 positionOS : POSITION; float2 probeXZ : TEXCOORD0; float2 transition : TEXCOORD1; };
            struct Varyings { float4 positionHCS : SV_POSITION; float4 value : TEXCOORD0; float2 worldXZ : TEXCOORD1; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 point = ApplyClipmapTransitionOffset(float3(input.probeXZ.x, 999.0, input.probeXZ.y), input.transition.x);
                float3 normal;
                float valid = ApplyTerrainHeightDisplacement(point, normal, input.transition.x);
                output.positionHCS = float4(input.positionOS.xy, 0.0, 1.0);
                output.value = float4(point.y, valid, normal.x, normal.z); output.worldXZ = point.xz;
                return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                if (_SharedHeightProbeClipWorld > 0.5) ClipTerrainFragmentToWorld(input.worldXZ);
                return input.value;
            }
            ENDHLSL
        }
    }
}
