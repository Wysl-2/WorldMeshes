using UnityEngine;

public partial class TerrainClipmapDisplacementValidator :
    MonoBehaviour
{
    // =====================================================
    // RUNTIME STATE
    // =====================================================

    private TerrainHeightmapStreamer streamer;

    private TerrainClipmapController
        clipmapController;

    private Coroutine validationRoutine;

    // =====================================================
    // SHADER PROPERTY IDS
    // =====================================================

    private static readonly int HeightCachePropertyId =
        Shader.PropertyToID(
            "_HeightCache"
        );

    private static readonly int
        HeightCacheOriginTilePropertyId =
            Shader.PropertyToID(
                "_HeightCacheOriginTile"
            );

    private static readonly int HeightCacheSizePropertyId =
        Shader.PropertyToID(
            "_HeightCacheSize"
        );

    private static readonly int
        HeightTileSamplesPerSidePropertyId =
            Shader.PropertyToID(
                "_HeightTileSamplesPerSide"
            );

    private static readonly int
        HeightSampleSpacingPropertyId =
            Shader.PropertyToID(
                "_HeightSampleSpacing"
            );

    private static readonly int HeightCacheReadyPropertyId =
        Shader.PropertyToID(
            "_HeightCacheReady"
        );

    private static readonly int
        ClipmapTransitionOffsetPropertyId =
            Shader.PropertyToID(
                "_ClipmapTransitionOffset"
            );

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public bool LastValidationPassed
    {
        get
        {
            return
                MultiresolutionValidationStatus ==
                TerrainRuntimeValidationStatus.Passed;
        }
    }

    public bool IsValidating
    {
        get
        {
            return validationRoutine != null;
        }
    }

    // =====================================================
    // DISABLE
    // =====================================================

    private void OnDisable()
    {
        CancelRuntimeValidation();
    }

    // =====================================================
    // BEGIN VALIDATION
    // =====================================================

    [ContextMenu("Validate Clipmap Displacement")]
    public void BeginValidation()
    {
        BeginMultiresolutionValidation();
    }
}
