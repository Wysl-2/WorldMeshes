Shader "Custom/ClipmapTerrain"
{
    Properties
    {
        // =================================================
        // SURFACE
        // =================================================

        [MainColor]
        _BaseColor(
            "Ground Color",
            Color
        ) = (1, 1, 1, 1)

        [MainTexture]
        _BaseMap(
            "Ground Map",
            2D
        ) = "white" {}

        _BaseMapWorldSize(
            "Ground World Size",
            Float
        ) = 8

        // =================================================
        // SLOPE / ROCK
        // =================================================

        _SlopeColor(
            "Rock Color",
            Color
        ) = (1, 1, 1, 1)

        _SlopeMap(
            "Rock Map",
            2D
        ) = "white" {}

        _SlopeMapWorldSize(
            "Rock World Size",
            Float
        ) = 6

        _SlopeBlendStart(
            "Rock Blend Start",
            Range(0, 1)
        ) = 0.2

        _SlopeBlendEnd(
            "Rock Blend End",
            Range(0, 1)
        ) = 0.5

        _SlopeTriplanarSharpness(
            "Rock Triplanar Sharpness",
            Range(1, 16)
        ) = 4

        // =================================================
        // SCREE
        // =================================================

        _ScreeColor(
            "Scree Color",
            Color
        ) = (1, 1, 1, 1)

        _ScreeMap(
            "Scree Map",
            2D
        ) = "white" {}

        _ScreeMapWorldSize(
            "Scree World Size",
            Float
        ) = 8

        _ScreeTriplanarSharpness(
            "Scree Triplanar Sharpness",
            Range(1, 16)
        ) = 4

        [HideInInspector]
        _ScreeSlopeMin(
            "Scree Slope Minimum",
            Range(0, 90)
        ) = 15

        [HideInInspector]
        _ScreeSlopePreferredMin(
            "Scree Slope Preferred Minimum",
            Range(0, 90)
        ) = 25

        [HideInInspector]
        _ScreeSlopePreferredMax(
            "Scree Slope Preferred Maximum",
            Range(0, 90)
        ) = 40

        [HideInInspector]
        _ScreeSlopeMax(
            "Scree Slope Maximum",
            Range(0, 90)
        ) = 55

        [HideInInspector]
        _ScreeCurvatureScale(
            "Scree Curvature Scale",
            Range(1, 256)
        ) = 16

        [HideInInspector]
        _ScreeConvexRejectStart(
            "Scree Convex Reject Start",
            Range(0, 0.5)
        ) = 0.03

        [HideInInspector]
        _ScreeConvexRejectEnd(
            "Scree Convex Reject End",
            Range(0, 0.5)
        ) = 0.12

        [HideInInspector]
        _ScreeGeologyScale(
            "Scree Geological Patch Scale",
            Range(1, 512)
        ) = 120

        [HideInInspector]
        _ScreeGeologyStrength(
            "Scree Geological Patch Strength",
            Range(0, 1)
        ) = 0.45

        // =================================================
        // PBR
        // =================================================

        _Metallic(
            "Metallic",
            Range(0, 1)
        ) = 0

        _Smoothness(
            "Smoothness",
            Range(0, 1)
        ) = 0.15

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
        _HeightNormalSampleSpacingFine(
            "Height Normal Sample Spacing Fine",
            Float
        ) = 0

        [HideInInspector]
        _HeightNormalSampleSpacingCoarse(
            "Height Normal Sample Spacing Coarse",
            Float
        ) = 0

        [HideInInspector]
        _HeightCacheReady(
            "Height Cache Ready",
            Float
        ) = 0

        // =================================================
        // RUNTIME SURFACE MASK CACHE
        // =================================================

        [HideInInspector]
        _SurfaceMaskCache(
            "Surface Mask Cache",
            2DArray
        ) = "" {}

        [HideInInspector]
        _SurfaceMaskCacheOriginTile(
            "Surface Mask Cache Origin Tile",
            Vector
        ) = (0, 0, 0, 0)

        [HideInInspector]
        _SurfaceMaskCacheSize(
            "Surface Mask Cache Size",
            Vector
        ) = (1, 1, 0, 0)

        [HideInInspector]
        _SurfaceMaskSamplesPerSide(
            "Surface Mask Samples Per Side",
            Float
        ) = 257

        [HideInInspector]
        _SurfaceMaskSampleSpacing(
            "Surface Mask Sample Spacing",
            Float
        ) = 1

        [HideInInspector]
        _SurfaceMaskCacheReady(
            "Surface Mask Cache Ready",
            Float
        ) = 0

        // =================================================
        // WORLD BOUNDS
        // =================================================

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

        // =================================================
        // CLIPMAP TRANSITION
        // =================================================

        [HideInInspector]
        _ClipmapTransitionOffset(
            "Clipmap Transition Offset",
            Vector
        ) = (0, 0, 0, 0)

        // =================================================
        // AUTHORING VISUALIZATION
        // =================================================

        [HideInInspector]
        _AuthoringVisualizationEnabled(
            "Authoring Visualization Enabled",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringVisualizationMode(
            "Authoring Visualization Mode",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringHeightRange(
            "Authoring Height Range",
            Vector
        ) = (0, 1, 0, 0)

        [HideInInspector]
        _AuthoringCurvatureScale(
            "Authoring Curvature Scale",
            Float
        ) = 16

        [HideInInspector]
        _AuthoringAnalysisTexture(
            "Authoring Analysis Texture",
            2DArray
        ) = "" {}

        [HideInInspector]
        _AuthoringAnalysisReady(
            "Authoring Analysis Ready",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringAnalysisDisplayRange(
            "Authoring Analysis Display Range",
            Vector
        ) = (0, 1, 0, 0)

        [HideInInspector]
        _AuthoringScreeSlopeAnalysis(
            "Authoring Scree Slope Analysis",
            2DArray
        ) = "" {}

        [HideInInspector]
        _AuthoringScreeCurvatureAnalysis(
            "Authoring Scree Curvature Analysis",
            2DArray
        ) = "" {}

        [HideInInspector]
        _AuthoringScreeAnalysisReady(
            "Authoring Scree Analysis Ready",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringAnalysisCacheOriginTile(
            "Authoring Analysis Cache Origin Tile",
            Vector
        ) = (0, 0, 0, 0)

        [HideInInspector]
        _AuthoringAnalysisCacheSize(
            "Authoring Analysis Cache Size",
            Vector
        ) = (1, 1, 0, 0)

        [HideInInspector]
        _AuthoringAnalysisSamplesPerSide(
            "Authoring Analysis Samples Per Side",
            Float
        ) = 257

        [HideInInspector]
        _AuthoringAnalysisSampleSpacing(
            "Authoring Analysis Sample Spacing",
            Float
        ) = 1

        [HideInInspector]
        _AuthoringAnalysisWorldSizeXZ(
            "Authoring Analysis World Size XZ",
            Vector
        ) = (0, 0, 0, 0)

        [HideInInspector]
        _AuthoringContoursEnabled(
            "Authoring Contours Enabled",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringContourInterval(
            "Authoring Contour Interval",
            Float
        ) = 10

        [HideInInspector]
        _AuthoringChunkGridEnabled(
            "Authoring Chunk Grid Enabled",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringChunkSize(
            "Authoring Chunk Size",
            Float
        ) = 128

        [HideInInspector]
        _AuthoringHeightTileGridEnabled(
            "Authoring Height Tile Grid Enabled",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringHeightTileWorldSize(
            "Authoring Height Tile World Size",
            Float
        ) = 256

        [HideInInspector]
        _AuthoringWorldBoundaryEnabled(
            "Authoring World Boundary Enabled",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringLODRegionsEnabled(
            "Authoring LOD Regions Enabled",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringLODLevel(
            "Authoring LOD Level",
            Float
        ) = 0

        [HideInInspector]
        _AuthoringLODCount(
            "Authoring LOD Count",
            Float
        ) = 1

        // =================================================
        // TRUE DISPLACED WIREFRAME
        // =================================================

        [HideInInspector]
        _AuthoringWireframeOnly(
            "Authoring Wireframe Only",
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

            // ---------------------------------------------
            // URP lighting variants
            // ---------------------------------------------

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // =================================================
            // VERTEX INPUT
            // =================================================

            struct Attributes
            {
                float4 positionOS : POSITION;

                /*
                 * Generated clipmap-specific vertex data.
                 *
                 * TEXCOORD3.x:
                 *
                 * 0 = vertex remains attached to coarse side
                 * 1 = vertex follows the fine-side offset
                 *
                 * yzw are reserved for future clipmap data.
                 */
                float4 clipmapData : TEXCOORD3;
            };

            // =================================================
            // VERTEX OUTPUT
            // =================================================

            struct Varyings
            {
                float4 positionHCS :
                    SV_POSITION;

                float3 positionWS :
                    TEXCOORD0;

                float3 normalWS :
                    TEXCOORD1;

                half3 vertexLighting :
                    TEXCOORD2;

                half fogFactor :
                    TEXCOORD3;
            };

            // =================================================
            // SURFACE TEXTURES
            // =================================================

            TEXTURE2D(
                _BaseMap
            );

            SAMPLER(
                sampler_BaseMap
            );

            TEXTURE2D(
                _SlopeMap
            );

            SAMPLER(
                sampler_SlopeMap
            );

            TEXTURE2D(
                _ScreeMap
            );

            SAMPLER(
                sampler_ScreeMap
            );

            /*
             * Stage 8 synchronized baked runtime suitability cache.
             *
             * Current channel layout:
             *     R = Scree Suitability
             */
            Texture2DArray<float>
                _SurfaceMaskCache;

            /*
             * Stage 9 generic editor-only raw Terrain Analysis texture.
             * Slope, Curvature, Roughness, and Local Relief all use this
             * same transient binding path.
             */
            Texture2DArray<float>
                _AuthoringAnalysisTexture;

            /*
             * Stage 6 edit-mode Scree Suitability inputs.
             * Runtime leaves _AuthoringScreeAnalysisReady at zero and uses
             * the direct terrain-analysis fallback.
             */
            Texture2DArray<float>
                _AuthoringScreeSlopeAnalysis;

            Texture2DArray<float>
                _AuthoringScreeCurvatureAnalysis;

            // =================================================
            // PER-MATERIAL / MPB DATA
            // =================================================

            CBUFFER_START(
                UnityPerMaterial
            )

                half4 _BaseColor;

                float4 _BaseMap_ST;
                float _BaseMapWorldSize;

                half4 _SlopeColor;

                float4 _SlopeMap_ST;
                float _SlopeMapWorldSize;

                float _SlopeBlendStart;
                float _SlopeBlendEnd;
                float _SlopeTriplanarSharpness;

                half4 _ScreeColor;

                float4 _ScreeMap_ST;
                float _ScreeMapWorldSize;
                float _ScreeTriplanarSharpness;

                float _ScreeSlopeMin;
                float _ScreeSlopePreferredMin;
                float _ScreeSlopePreferredMax;
                float _ScreeSlopeMax;

                float _ScreeCurvatureScale;
                float _ScreeConvexRejectStart;
                float _ScreeConvexRejectEnd;

                float _ScreeGeologyScale;
                float _ScreeGeologyStrength;

                half _Metallic;
                half _Smoothness;

                float4 _HeightCacheOriginTile;
                float4 _HeightCacheSize;
                float _HeightTileSamplesPerSide;
                float _HeightSampleSpacing;
                float _HeightNormalSampleSpacingFine;
                float _HeightNormalSampleSpacingCoarse;

                float4 _SurfaceMaskCacheOriginTile;
                float4 _SurfaceMaskCacheSize;
                float _SurfaceMaskSamplesPerSide;
                float _SurfaceMaskSampleSpacing;
                float _SurfaceMaskCacheReady;

                float4 _WorldSizeXZ;
                float _WorldBoundsReady;

                float _HeightCacheReady;

                float4 _ClipmapTransitionOffset;

                /*
                 * Editor authoring visualization state.
                 *
                 * These values are written transiently through
                 * MaterialPropertyBlock. They are hidden from the
                 * terrain material inspector and remain disabled at
                 * runtime.
                 */
                float _AuthoringVisualizationEnabled;
                float _AuthoringVisualizationMode;
                float4 _AuthoringHeightRange;
                float _AuthoringCurvatureScale;

                float _AuthoringAnalysisReady;
                float4 _AuthoringAnalysisDisplayRange;
                float _AuthoringScreeAnalysisReady;
                float4 _AuthoringAnalysisCacheOriginTile;
                float4 _AuthoringAnalysisCacheSize;
                float _AuthoringAnalysisSamplesPerSide;
                float _AuthoringAnalysisSampleSpacing;
                float4 _AuthoringAnalysisWorldSizeXZ;

                float _AuthoringContoursEnabled;
                float _AuthoringContourInterval;

                float _AuthoringChunkGridEnabled;
                float _AuthoringChunkSize;

                float _AuthoringHeightTileGridEnabled;
                float _AuthoringHeightTileWorldSize;

                float _AuthoringWorldBoundaryEnabled;

                float _AuthoringLODRegionsEnabled;
                float _AuthoringLODLevel;
                float _AuthoringLODCount;

                /*
                 * Stage 6 editor-only fill suppression.
                 *
                 * The transient displaced wireframe renderer sets this
                 * through MaterialPropertyBlock only while Wireframe Only
                 * mode is active.
                 */
                float _AuthoringWireframeOnly;

            CBUFFER_END

            // =================================================
            // SHARED TERRAIN / VISUALIZATION CODE
            // =================================================

            #include "Assets/WorldMeshes/Shaders/Terrain/ClipmapTerrainHeight.hlsl"
            #include "Assets/WorldMeshes/Shaders/Terrain/ClipmapTerrainAnalysis.hlsl"
            #include "Assets/WorldMeshes/Shaders/Terrain/TerrainAnalysisSampling.hlsl"
            #include "Assets/WorldMeshes/Shaders/Terrain/TerrainSurfaceMaskSampling.hlsl"
            #include "Assets/WorldMeshes/Shaders/Terrain/ClipmapTerrainSuitability.hlsl"
            #include "Assets/WorldMeshes/Shaders/Terrain/ClipmapTerrainVisualization.hlsl"

            // =================================================
            // SAMPLE ROCK TEXTURE - TRIPLANAR
            // =================================================

            half4 SampleSlopeTriplanar(
                float3 positionWS,
                half3 normalWS
            )
            {
                float worldSize =
                    max(
                        _SlopeMapWorldSize,
                        0.0001
                    );

                float3 weights =
                    pow(
                        abs(
                            (float3)normalWS
                        ),
                        max(
                            _SlopeTriplanarSharpness,
                            1.0
                        )
                    );

                weights /=
                    max(
                        weights.x
                        +
                        weights.y
                        +
                        weights.z,
                        0.0001
                    );

                float2 uvX =
                    positionWS.zy
                    /
                    worldSize;

                float2 uvY =
                    positionWS.xz
                    /
                    worldSize;

                float2 uvZ =
                    positionWS.xy
                    /
                    worldSize;

                uvX =
                    uvX
                    *
                    _SlopeMap_ST.xy
                    +
                    _SlopeMap_ST.zw;

                uvY =
                    uvY
                    *
                    _SlopeMap_ST.xy
                    +
                    _SlopeMap_ST.zw;

                uvZ =
                    uvZ
                    *
                    _SlopeMap_ST.xy
                    +
                    _SlopeMap_ST.zw;

                half4 sampleX =
                    SAMPLE_TEXTURE2D(
                        _SlopeMap,
                        sampler_SlopeMap,
                        uvX
                    );

                half4 sampleY =
                    SAMPLE_TEXTURE2D(
                        _SlopeMap,
                        sampler_SlopeMap,
                        uvY
                    );

                half4 sampleZ =
                    SAMPLE_TEXTURE2D(
                        _SlopeMap,
                        sampler_SlopeMap,
                        uvZ
                    );

                return
                    (
                        sampleX *
                            weights.x
                        +
                        sampleY *
                            weights.y
                        +
                        sampleZ *
                            weights.z
                    )
                    *
                    _SlopeColor;
            }

            // =================================================
            // SAMPLE SCREE TEXTURE - TRIPLANAR
            // =================================================

            half4 SampleScreeTriplanar(
                float3 positionWS,
                half3 normalWS
            )
            {
                float worldSize =
                    max(
                        _ScreeMapWorldSize,
                        0.0001
                    );

                float3 weights =
                    pow(
                        abs(
                            (float3)normalWS
                        ),
                        max(
                            _ScreeTriplanarSharpness,
                            1.0
                        )
                    );

                weights /=
                    max(
                        weights.x +
                        weights.y +
                        weights.z,
                        0.0001
                    );

                float2 uvX =
                    positionWS.zy /
                    worldSize;

                float2 uvY =
                    positionWS.xz /
                    worldSize;

                float2 uvZ =
                    positionWS.xy /
                    worldSize;

                uvX =
                    uvX *
                    _ScreeMap_ST.xy +
                    _ScreeMap_ST.zw;

                uvY =
                    uvY *
                    _ScreeMap_ST.xy +
                    _ScreeMap_ST.zw;

                uvZ =
                    uvZ *
                    _ScreeMap_ST.xy +
                    _ScreeMap_ST.zw;

                half4 sampleX =
                    SAMPLE_TEXTURE2D(
                        _ScreeMap,
                        sampler_ScreeMap,
                        uvX
                    );

                half4 sampleY =
                    SAMPLE_TEXTURE2D(
                        _ScreeMap,
                        sampler_ScreeMap,
                        uvY
                    );

                half4 sampleZ =
                    SAMPLE_TEXTURE2D(
                        _ScreeMap,
                        sampler_ScreeMap,
                        uvZ
                    );

                return
                    (
                        sampleX *
                            weights.x +
                        sampleY *
                            weights.y +
                        sampleZ *
                            weights.z
                    )
                    *
                    _ScreeColor;
            }

            // =================================================
            // VERTEX SHADER
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

                float3 normalWS;

                ApplyTerrainHeightDisplacement(
                    positionWS,
                    normalWS,
                    IN.clipmapData.x
                );

                OUT.positionWS =
                    positionWS;

                OUT.normalWS =
                    normalWS;

                OUT.positionHCS =
                    TransformWorldToHClip(
                        positionWS
                    );

                OUT.vertexLighting =
                    VertexLighting(
                        positionWS,
                        normalWS
                    );

                OUT.fogFactor =
                    ComputeFogFactor(
                        OUT.positionHCS.z
                    );

                return
                    OUT;
            }

            // =================================================
            // FRAGMENT SHADER
            // =================================================

            half4 frag(
                Varyings IN
            ) : SV_Target
            {
                // ---------------------------------------------
                // Authoritative logical world boundary
                // ---------------------------------------------

                ClipTerrainFragmentToWorld(
                    IN.positionWS.xz
                );

                /*
                 * Wireframe Only keeps the source terrain renderers,
                 * transforms, height-cache bindings, and MPB ownership
                 * intact while suppressing only filled color/depth output.
                 *
                 * TerrainAuthoringWireframeRenderer draws a separate
                 * displaced depth proxy before its line overlay so hidden
                 * terrain edges remain depth-occluded.
                 */
                if (
                    _AuthoringWireframeOnly >
                    0.5
                )
                {
                    clip(
                        -1.0
                    );
                }

                half3 normalWS =
                    normalize(
                        IN.normalWS
                    );

                // ---------------------------------------------
                // Unlit authoring base modes
                // ---------------------------------------------

                /*
                 * Height, Slope, Curvature, and Scree Suitability modes intentionally bypass PBR
                 * lighting and fog so the diagnostic color has a
                 * stable meaning everywhere in the Scene View.
                 *
                 * The editor controller falls back to Lit if a
                 * height-dependent mode is selected while no valid
                 * authoring height preview is available.
                 */
                if (
                    AuthoringVisualizationIsEnabled() >
                        0.5
                    &&
                    AuthoringVisualizationModeIs(
                        WORLDMESHES_AUTHORING_MODE_LIT
                    )
                    <
                    0.5
                )
                {
                    float3 diagnosticColor =
                        GetAuthoringDiagnosticBaseColor(
                            IN.positionWS,
                            normalWS
                        );

                    diagnosticColor =
                        ApplyAuthoringVisualizationOverlays(
                            diagnosticColor,
                            IN.positionWS
                        );

                    return
                        half4(
                            diagnosticColor,
                            1.0
                        );
                }

                // ---------------------------------------------
                // Ground texture - world-space XZ
                // ---------------------------------------------

                float baseMapWorldSize =
                    max(
                        _BaseMapWorldSize,
                        0.0001
                    );

                float2 baseMapUV =
                    IN.positionWS.xz
                    /
                    baseMapWorldSize;

                baseMapUV =
                    baseMapUV
                    *
                    _BaseMap_ST.xy
                    +
                    _BaseMap_ST.zw;

                half4 groundColor =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        baseMapUV
                    )
                    *
                    _BaseColor;

                // ---------------------------------------------
                // Rock texture - world-space triplanar
                // ---------------------------------------------

                half4 rockColor =
                    SampleSlopeTriplanar(
                        IN.positionWS,
                        normalWS
                    );

                float slopeAmount =
                    1.0
                    -
                    saturate(
                        normalWS.y
                    );

                float blendStart =
                    min(
                        _SlopeBlendStart,
                        _SlopeBlendEnd
                    );

                float blendEnd =
                    max(
                        max(
                            _SlopeBlendStart,
                            _SlopeBlendEnd
                        ),
                        blendStart
                        +
                        0.0001
                    );

                float rockBlend =
                    smoothstep(
                        blendStart,
                        blendEnd,
                        slopeAmount
                    );

                // ---------------------------------------------
                // Scree texture - shared suitability mask
                // ---------------------------------------------

                half4 screeColor =
                    SampleScreeTriplanar(
                        IN.positionWS,
                        normalWS
                    );

                float screeSuitability =
                    GetScreeSuitability(
                        IN.positionWS,
                        normalWS
                    );

                /*
                 * The existing steep-rock surface keeps priority. Scree
                 * fills suitable ground/moderate slopes beneath that layer
                 * rather than painting over cliff rock.
                 */
                float screeBlend =
                    screeSuitability *
                    (
                        1.0 -
                        rockBlend
                    );

                half4 groundAndScreeColor =
                    lerp(
                        groundColor,
                        screeColor,
                        screeBlend
                    );

                half4 surfaceColor =
                    lerp(
                        groundAndScreeColor,
                        rockColor,
                        rockBlend
                    );

                // =================================================
                // URP PBR SURFACE DATA
                // =================================================

                SurfaceData surfaceData =
                    (SurfaceData)0;

                surfaceData.albedo =
                    surfaceColor.rgb;

                surfaceData.metallic =
                    saturate(
                        _Metallic
                    );

                surfaceData.specular =
                    half3(
                        0.0,
                        0.0,
                        0.0
                    );

                surfaceData.smoothness =
                    saturate(
                        _Smoothness
                    );

                surfaceData.normalTS =
                    half3(
                        0.0,
                        0.0,
                        1.0
                    );

                surfaceData.emission =
                    half3(
                        0.0,
                        0.0,
                        0.0
                    );

                surfaceData.occlusion =
                    1.0;

                surfaceData.alpha =
                    surfaceColor.a;

                surfaceData.clearCoatMask =
                    0.0;

                surfaceData.clearCoatSmoothness =
                    0.0;

                // =================================================
                // URP LIGHTING INPUT
                // =================================================

                InputData inputData =
                    (InputData)0;

                inputData.positionWS =
                    IN.positionWS;

                inputData.positionCS =
                    IN.positionHCS;

                inputData.normalWS =
                    normalWS;

                inputData.viewDirectionWS =
                    GetWorldSpaceNormalizeViewDir(
                        IN.positionWS
                    );

                inputData.shadowCoord =
                    TransformWorldToShadowCoord(
                        IN.positionWS
                    );

                inputData.fogCoord =
                    IN.fogFactor;

                inputData.vertexLighting =
                    IN.vertexLighting;

                inputData.bakedGI =
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

                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(
                        IN.positionHCS
                    );

                inputData.shadowMask =
                    half4(
                        1.0,
                        1.0,
                        1.0,
                        1.0
                    );

                // =================================================
                // URP PBR LIGHTING
                // =================================================

                half4 color =
                    UniversalFragmentPBR(
                        inputData,
                        surfaceData
                    );

                color.rgb =
                    MixFog(
                        color.rgb,
                        inputData.fogCoord
                    );

                /*
                 * Lit remains the normal PBR terrain path.
                 * Diagnostic overlays are composed after fog so
                 * authoring lines remain legible at long distance.
                 */
                if (
                    AuthoringVisualizationIsEnabled() >
                    0.5
                )
                {
                    color.rgb =
                        ApplyAuthoringVisualizationOverlays(
                            color.rgb,
                            IN.positionWS
                        );
                }

                return
                    color;
            }

            ENDHLSL
        }
    }

    FallBack Off
}
