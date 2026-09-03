#ifndef WORLDMESHES_CLIPMAP_TERRAIN_VISUALIZATION_INCLUDED
#define WORLDMESHES_CLIPMAP_TERRAIN_VISUALIZATION_INCLUDED

/*
 * Shared authoring/debug visualization helpers for ClipmapTerrain.shader.
 *
 * Callers must declare the following properties before including this file:
 *
 *     float  _AuthoringVisualizationEnabled;
 *     float  _AuthoringVisualizationMode;
 *     float4 _AuthoringHeightRange;
 *     Texture2DArray<float> _AuthoringAnalysisTexture;
 *     float  _AuthoringAnalysisReady;
 *     float4 _AuthoringAnalysisDisplayRange;
 *
 *     float4 _AuthoringAnalysisCacheOriginTile;
 *     float4 _AuthoringAnalysisCacheSize;
 *     float  _AuthoringAnalysisSamplesPerSide;
 *     float  _AuthoringAnalysisSampleSpacing;
 *     float4 _AuthoringAnalysisWorldSizeXZ;
 *
 *     float  _AuthoringContoursEnabled;
 *     float  _AuthoringContourInterval;
 *
 *     float  _AuthoringChunkGridEnabled;
 *     float  _AuthoringChunkSize;
 *
 *     float  _AuthoringHeightTileGridEnabled;
 *     float  _AuthoringHeightTileWorldSize;
 *
 *     float  _AuthoringWorldBoundaryEnabled;
 *
 *     float  _AuthoringLODRegionsEnabled;
 *     float  _AuthoringLODLevel;
 *     float  _AuthoringLODCount;
 *
 * ClipmapTerrainHeight.hlsl must be included first because the world-boundary
 * overlay reuses GetTerrainWorldBoundaryDistance(...).
 */

// =========================================================
// BASE MODE IDS
// =========================================================

#define WORLDMESHES_AUTHORING_MODE_LIT         0.0
#define WORLDMESHES_AUTHORING_MODE_HEIGHT      1.0
#define WORLDMESHES_AUTHORING_MODE_SLOPE       2.0
#define WORLDMESHES_AUTHORING_MODE_CURVATURE   3.0
#define WORLDMESHES_AUTHORING_MODE_SCREE       4.0
#define WORLDMESHES_AUTHORING_MODE_ROUGHNESS   5.0
#define WORLDMESHES_AUTHORING_MODE_LOCALRELIEF 6.0

// =========================================================
// COMMON HELPERS
// =========================================================

float AuthoringVisualizationIsEnabled()
{
    return
        _AuthoringVisualizationEnabled >
        0.5;
}

float AuthoringVisualizationModeIs(
    float mode
)
{
    return
        abs(
            _AuthoringVisualizationMode -
            mode
        )
        <
        0.25;
}

float3 BlendAuthoringOverlay(
    float3 baseColor,
    float3 overlayColor,
    float mask,
    float opacity
)
{
    return
        lerp(
            baseColor,
            overlayColor,
            saturate(mask) *
            saturate(opacity)
        );
}

// =========================================================
// HEIGHT BASE MODE
// =========================================================

float GetNormalizedAuthoringHeight(
    float worldHeight
)
{
    float minimumHeight =
        _AuthoringHeightRange.x;

    float maximumHeight =
        _AuthoringHeightRange.y;

    float heightSpan =
        maximumHeight -
        minimumHeight;

    if (
        abs(
            heightSpan
        )
        <
        0.00001
    )
    {
        return
            0.5;
    }

    return
        saturate(
            (
                worldHeight -
                minimumHeight
            )
            /
            heightSpan
        );
}

float3 GetAuthoringHeightColor(
    float worldHeight
)
{
    float t =
        GetNormalizedAuthoringHeight(
            worldHeight
        );

    const float3 lowColor =
        float3(
            0.03,
            0.12,
            0.40
        );

    const float3 lowMidColor =
        float3(
            0.02,
            0.70,
            0.82
        );

    const float3 highMidColor =
        float3(
            0.25,
            0.72,
            0.18
        );

    const float3 highColor =
        float3(
            1.00,
            0.90,
            0.16
        );

    const float3 peakColor =
        float3(
            0.96,
            0.96,
            0.96
        );

    if (t < 0.25)
    {
        return
            lerp(
                lowColor,
                lowMidColor,
                t /
                0.25
            );
    }

    if (t < 0.5)
    {
        return
            lerp(
                lowMidColor,
                highMidColor,
                (
                    t -
                    0.25
                )
                /
                0.25
            );
    }

    if (t < 0.75)
    {
        return
            lerp(
                highMidColor,
                highColor,
                (
                    t -
                    0.5
                )
                /
                0.25
            );
    }

    return
        lerp(
            highColor,
            peakColor,
            (
                t -
                0.75
            )
            /
            0.25
        );
}

// =========================================================
// GENERIC RAW TERRAIN ANALYSIS BASE MODES
// =========================================================

float GetAuthoringAnalysisValue(
    float2 worldXZ,
    out float valid
)
{
    valid =
        0.0;

    if (
        _AuthoringAnalysisReady <
        0.5
    )
    {
        return
            0.0;
    }

    return
        WorldMeshesSampleTerrainAnalysisBilinear(
            _AuthoringAnalysisTexture,
            worldXZ,
            (int2)_AuthoringAnalysisCacheOriginTile.xy,
            (int2)_AuthoringAnalysisCacheSize.xy,
            (int)_AuthoringAnalysisSamplesPerSide,
            _AuthoringAnalysisSampleSpacing,
            _AuthoringAnalysisWorldSizeXZ.xy,
            valid
        );
}


float3 GetAuthoringUnsignedAnalysisColor(
    float value
)
{
    float minimumValue =
        _AuthoringAnalysisDisplayRange.x;

    float maximumValue =
        max(
            _AuthoringAnalysisDisplayRange.y,
            minimumValue +
                0.0001
        );

    float t =
        saturate(
            (
                value -
                minimumValue
            )
            /
            (
                maximumValue -
                minimumValue
            )
        );

    const float3 lowColor =
        float3(
            0.04,
            0.12,
            0.34
        );

    const float3 lowMidColor =
        float3(
            0.04,
            0.68,
            0.82
        );

    const float3 highMidColor =
        float3(
            0.94,
            0.86,
            0.12
        );

    const float3 highColor =
        float3(
            0.92,
            0.08,
            0.04
        );

    if (t < 0.333333)
    {
        return
            lerp(
                lowColor,
                lowMidColor,
                t /
                    0.333333
            );
    }

    if (t < 0.666667)
    {
        return
            lerp(
                lowMidColor,
                highMidColor,
                (
                    t -
                    0.333333
                )
                /
                0.333334
            );
    }

    return
        lerp(
            highMidColor,
            highColor,
            (
                t -
                0.666667
            )
            /
            0.333333
        );
}


float3 GetAuthoringSignedAnalysisColor(
    float value
)
{
    float displayMagnitude =
        max(
            max(
                abs(
                    _AuthoringAnalysisDisplayRange.x
                ),
                abs(
                    _AuthoringAnalysisDisplayRange.y
                )
            ),
            0.0001
        );

    float strength =
        saturate(
            abs(
                value
            )
            /
            displayMagnitude
        );

    const float3 zeroColor =
        float3(
            0.12,
            0.64,
            0.18
        );

    const float3 negativeColor =
        float3(
            0.08,
            0.30,
            1.00
        );

    const float3 positiveColor =
        float3(
            0.96,
            0.16,
            0.06
        );

    if (value < 0.0)
    {
        return
            lerp(
                zeroColor,
                negativeColor,
                strength
            );
    }

    return
        lerp(
            zeroColor,
            positiveColor,
            strength
        );
}


float3 GetAuthoringAnalysisColor(
    float2 worldXZ
)
{
    float valid;

    float value =
        GetAuthoringAnalysisValue(
            worldXZ,
            valid
        );

    if (valid < 0.5)
    {
        return
            float3(
                0.04,
                0.04,
                0.04
            );
    }

    bool signedField =
        _AuthoringAnalysisDisplayRange.z >
        0.5;

    return
        signedField
            ? GetAuthoringSignedAnalysisColor(
                value
            )
            : GetAuthoringUnsignedAnalysisColor(
                value
            );
}


// =========================================================
// SCREE SUITABILITY BASE MODE
// =========================================================

float3 GetAuthoringScreeSuitabilityColor(
    float3 positionWS,
    float3 normalWS
)
{
    float suitability =
        GetScreeSuitability(
            positionWS,
            normalWS
        );

    const float3 unsuitableColor =
        float3(
            0.015,
            0.020,
            0.030
        );

    const float3 lowColor =
        float3(
            0.05,
            0.22,
            0.78
        );

    const float3 mediumColor =
        float3(
            0.96,
            0.82,
            0.10
        );

    const float3 highColor =
        float3(
            0.96,
            0.10,
            0.04
        );

    if (suitability < 0.333333)
    {
        return
            lerp(
                unsuitableColor,
                lowColor,
                suitability /
                    0.333333
            );
    }

    if (suitability < 0.666667)
    {
        return
            lerp(
                lowColor,
                mediumColor,
                (
                    suitability -
                    0.333333
                )
                /
                0.333334
            );
    }

    return
        lerp(
            mediumColor,
            highColor,
            (
                suitability -
                0.666667
            )
            /
            0.333333
        );
}

// =========================================================
// ANTI-ALIASED PERIODIC LINES
// =========================================================

float GetPeriodicAuthoringLineMask1D(
    float coordinate,
    float lineWidthPixels
)
{
    float distanceToLine =
        abs(
            frac(
                coordinate +
                0.5
            )
            -
            0.5
        );

    float derivativeWidth =
        max(
            fwidth(
                coordinate
            ),
            0.000001
        );

    float halfWidth =
        derivativeWidth *
        max(
            lineWidthPixels,
            0.25
        )
        *
        0.5;

    float feather =
        derivativeWidth *
        1.25;

    float lineMask =
        1.0 -
        smoothstep(
            halfWidth,
            halfWidth +
                feather,
            distanceToLine
        );

    /*
     * Fade repeating lines once the camera is far enough that a
     * single pixel covers a large fraction of one complete period.
     * This avoids distant contour/grid moire turning into a solid
     * diagnostic tint.
     */
    float frequencyFade =
        1.0 -
        smoothstep(
            0.25,
            0.50,
            derivativeWidth
        );

    return
        lineMask *
        frequencyFade;
}

float GetAuthoringWorldGridMask(
    float2 worldXZ,
    float worldSpacing,
    float lineWidthPixels
)
{
    float safeSpacing =
        max(
            worldSpacing,
            0.0001
        );

    float2 coordinate =
        worldXZ /
        safeSpacing;

    float xMask =
        GetPeriodicAuthoringLineMask1D(
            coordinate.x,
            lineWidthPixels
        );

    float zMask =
        GetPeriodicAuthoringLineMask1D(
            coordinate.y,
            lineWidthPixels
        );

    return
        max(
            xMask,
            zMask
        );
}

// =========================================================
// CONTOURS
// =========================================================

float GetAuthoringContourMask(
    float worldHeight
)
{
    if (
        _AuthoringContoursEnabled <
        0.5
    )
    {
        return
            0.0;
    }

    float interval =
        max(
            _AuthoringContourInterval,
            0.0001
        );

    float contourCoordinate =
        worldHeight /
        interval;

    return
        GetPeriodicAuthoringLineMask1D(
            contourCoordinate,
            1.25
        );
}

// =========================================================
// CHUNK GRID
// =========================================================

float GetAuthoringChunkGridMask(
    float2 worldXZ
)
{
    if (
        _AuthoringChunkGridEnabled <
        0.5
    )
    {
        return
            0.0;
    }

    return
        GetAuthoringWorldGridMask(
            worldXZ,
            _AuthoringChunkSize,
            1.15
        );
}

// =========================================================
// HEIGHT TILE GRID
// =========================================================

float GetAuthoringHeightTileGridMask(
    float2 worldXZ
)
{
    if (
        _AuthoringHeightTileGridEnabled <
        0.5
    )
    {
        return
            0.0;
    }

    return
        GetAuthoringWorldGridMask(
            worldXZ,
            _AuthoringHeightTileWorldSize,
            1.6
        );
}

// =========================================================
// WORLD BOUNDARY
// =========================================================

float GetAuthoringWorldBoundaryMask(
    float2 worldXZ
)
{
    if (
        _AuthoringWorldBoundaryEnabled <
            0.5
        ||
        TerrainWorldBoundsAreReady() <
            0.5
    )
    {
        return
            0.0;
    }

    /*
     * Outside-world fragments have already been clipped by the caller.
     * This distance is therefore >= 0 for visible fragments.
     */
    float boundaryDistance =
        GetTerrainWorldBoundaryDistance(
            worldXZ
        );

    float derivativeWidth =
        max(
            fwidth(
                boundaryDistance
            ),
            0.0001
        );

    float lineWidth =
        derivativeWidth *
        2.0;

    return
        1.0 -
        smoothstep(
            lineWidth *
                0.35,
            lineWidth *
                1.75,
            boundaryDistance
        );
}

// =========================================================
// LOD REGION COLOR
// =========================================================

float3 GetAuthoringLODColor(
    float lodLevel
)
{
    int level =
        clamp(
            (int)floor(
                lodLevel +
                0.5
            ),
            0,
            9
        );

    if (level == 0)
    {
        return float3(0.12, 0.72, 1.00);
    }

    if (level == 1)
    {
        return float3(0.30, 0.95, 0.32);
    }

    if (level == 2)
    {
        return float3(1.00, 0.86, 0.18);
    }

    if (level == 3)
    {
        return float3(1.00, 0.42, 0.12);
    }

    if (level == 4)
    {
        return float3(0.94, 0.16, 0.58);
    }

    if (level == 5)
    {
        return float3(0.58, 0.28, 1.00);
    }

    if (level == 6)
    {
        return float3(0.10, 0.90, 0.78);
    }

    if (level == 7)
    {
        return float3(0.82, 0.92, 0.28);
    }

    if (level == 8)
    {
        return float3(0.95, 0.52, 0.72);
    }

    return
        float3(
            0.72,
            0.72,
            0.72
        );
}

// =========================================================
// BASE DIAGNOSTIC COLOR
// =========================================================

float3 GetAuthoringDiagnosticBaseColor(
    float3 positionWS,
    float3 normalWS
)
{
    if (
        AuthoringVisualizationModeIs(
            WORLDMESHES_AUTHORING_MODE_HEIGHT
        )
        >
        0.5
    )
    {
        return
            GetAuthoringHeightColor(
                positionWS.y
            );
    }

    bool rawAnalysisMode =
        AuthoringVisualizationModeIs(
            WORLDMESHES_AUTHORING_MODE_SLOPE
        )
        >
        0.5
        ||
        AuthoringVisualizationModeIs(
            WORLDMESHES_AUTHORING_MODE_CURVATURE
        )
        >
        0.5
        ||
        AuthoringVisualizationModeIs(
            WORLDMESHES_AUTHORING_MODE_ROUGHNESS
        )
        >
        0.5
        ||
        AuthoringVisualizationModeIs(
            WORLDMESHES_AUTHORING_MODE_LOCALRELIEF
        )
        >
        0.5;

    if (rawAnalysisMode)
    {
        return
            GetAuthoringAnalysisColor(
                positionWS.xz
            );
    }

    if (
        AuthoringVisualizationModeIs(
            WORLDMESHES_AUTHORING_MODE_SCREE
        )
        >
        0.5
    )
    {
        return
            GetAuthoringScreeSuitabilityColor(
                positionWS,
                normalWS
            );
    }

    return
        float3(
            1.0,
            1.0,
            1.0
        );
}

// =========================================================
// OVERLAY COMPOSITION
// =========================================================

float3 ApplyAuthoringVisualizationOverlays(
    float3 baseColor,
    float3 positionWS
)
{
    float3 color =
        baseColor;

    // -----------------------------------------------------
    // Broad LOD tint first
    // -----------------------------------------------------

    if (
        _AuthoringLODRegionsEnabled >
        0.5
    )
    {
        color =
            BlendAuthoringOverlay(
                color,
                GetAuthoringLODColor(
                    _AuthoringLODLevel
                ),
                1.0,
                0.28
            );
    }

    // -----------------------------------------------------
    // Chunk grid
    // -----------------------------------------------------

    float chunkMask =
        GetAuthoringChunkGridMask(
            positionWS.xz
        );

    color =
        BlendAuthoringOverlay(
            color,
            float3(
                0.00,
                0.78,
                1.00
            ),
            chunkMask,
            0.92
        );

    // -----------------------------------------------------
    // Height tile grid
    // -----------------------------------------------------

    float heightTileMask =
        GetAuthoringHeightTileGridMask(
            positionWS.xz
        );

    color =
        BlendAuthoringOverlay(
            color,
            float3(
                1.00,
                0.18,
                0.88
            ),
            heightTileMask,
            0.95
        );

    // -----------------------------------------------------
    // Height contours
    // -----------------------------------------------------

    float contourMask =
        GetAuthoringContourMask(
            positionWS.y
        );

    color =
        BlendAuthoringOverlay(
            color,
            float3(
                0.035,
                0.035,
                0.035
            ),
            contourMask,
            0.88
        );

    // -----------------------------------------------------
    // True logical world boundary last / strongest
    // -----------------------------------------------------

    float worldBoundaryMask =
        GetAuthoringWorldBoundaryMask(
            positionWS.xz
        );

    color =
        BlendAuthoringOverlay(
            color,
            float3(
                1.00,
                0.10,
                0.02
            ),
            worldBoundaryMask,
            1.0
        );

    return
        color;
}

#endif
