using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Reusable edit-time composition context for baking the persistent
 * authoring state into generated runtime height tiles.
 *
 * The context does not implement modifier mathematics. It reuses the
 * same TerrainHeightCompositor and compute shader as the live editor
 * preview so editor and generated runtime output share one modifier
 * evaluation contract.
 *
 * Responsibilities:
 * - collect the in-world tiles touched by current enabled modifiers
 * - own one reusable transient RFloat Texture2DArray RenderTexture
 * - copy a committed authoring tile into transient slice 0
 * - invoke TerrainHeightCompositor with the actual world tile coordinate
 * - synchronously read final composed samples back for asset baking
 */
internal sealed class TerrainRuntimeHeightCompositionContext :
    IDisposable
{
    private readonly HashSet<Vector2Int>
        affectedTiles =
            new HashSet<Vector2Int>();

    private readonly TerrainHeightCompositor
        compositor =
            new TerrainHeightCompositor();

    private WorldSettings worldSettings;
    private TerrainAuthoringData authoringData;
    private RenderTexture compositionTarget;

    private Vector2 worldSizeXZ =
        Vector2.zero;

    private int samplesPerSide;
    private float sampleSpacing;
    private float tileWorldSize;
    private bool prepared;
    private int compositedTileCount;
    private bool regionalCompositionRequired;

    public int AffectedTileCount =>
        affectedTiles.Count;

    public int CompositedTileCount =>
        compositedTileCount;

    public int ModifierConsideredCount =>
        compositor.LastModifierConsideredCount;

    public int ModifierDispatchCount =>
        compositor.LastModifierDispatchCount;

    public int ComputeDispatchCount =>
        compositor.LastComputeDispatchCount;

    public int RegionalElevationDispatchCount =>
        compositor.LastRegionalElevationDispatchCount;

    public bool RegionalCompositionRequired =>
        regionalCompositionRequired;

    public bool TryPrepare(
        WorldSettings settings,
        TerrainAuthoringData data,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        DisposeTarget();

        affectedTiles.Clear();

        worldSettings =
            settings;

        authoringData =
            data;

        prepared =
            false;

        compositedTileCount =
            0;

        regionalCompositionRequired =
            false;

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            errorMessage =
                "Runtime height composition received an invalid " +
                "authoring context.";

            return false;
        }

        samplesPerSide =
            worldSettings.HeightTileSamplesPerSide;

        tileWorldSize =
            worldSettings.HeightTileWorldSize;

        sampleSpacing =
            worldSettings.chunkSize
            /
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            );

        worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            samplesPerSide <= 1
            ||
            !IsFinitePositive(
                tileWorldSize
            )
            ||
            !IsFinitePositive(
                sampleSpacing
            )
            ||
            !IsFinitePositive(
                worldSizeXZ.x
            )
            ||
            !IsFinitePositive(
                worldSizeXZ.y
            )
        )
        {
            errorMessage =
                "The runtime height composition layout is invalid.";

            return false;
        }

        if (
            !TerrainRegionalElevationCompositionUtility
                .TryCollectRequiredHeightTiles(
                    worldSettings,
                    authoringData,
                    affectedTiles,
                    0,
                    out regionalCompositionRequired,
                    out errorMessage
                )
        )
        {
            return false;
        }

        /*
         * If neither a regional source nor an enabled modifier affects the
         * logical world, generated runtime output equals committed base data
         * and no GPU composition capability is required.
         */
        if (affectedTiles.Count == 0)
        {
            prepared =
                true;

            return true;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "Runtime height composition requires compute-shader " +
                "support because authored composition affects the world.";

            return false;
        }

        if (!SystemInfo.supports2DArrayTextures)
        {
            errorMessage =
                "Runtime height composition requires 2D texture-array " +
                "support because authored composition affects the world.";

            return false;
        }

        if (
            !SystemInfo.SupportsRenderTextureFormat(
                RenderTextureFormat.RFloat
            )
        )
        {
            errorMessage =
                "Runtime height composition requires an RFloat " +
                "RenderTexture.";

            return false;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            errorMessage =
                "Runtime height composition requires AsyncGPUReadback " +
                "to bake composed GPU samples into runtime assets.";

            return false;
        }

        if (
            !compositor.TryPrepare(
                out errorMessage
            )
        )
        {
            return false;
        }

        compositionTarget =
            new RenderTexture(
                samplesPerSide,
                samplesPerSide,
                0,
                RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear
            );

        compositionTarget.name =
            "WorldMeshes Runtime Height Composition";

        compositionTarget.dimension =
            TextureDimension.Tex2DArray;

        compositionTarget.volumeDepth =
            1;

        compositionTarget.enableRandomWrite =
            true;

        compositionTarget.useMipMap =
            false;

        compositionTarget.autoGenerateMips =
            false;

        compositionTarget.wrapMode =
            TextureWrapMode.Clamp;

        compositionTarget.filterMode =
            FilterMode.Point;

        if (!compositionTarget.Create())
        {
            DisposeTarget();

            errorMessage =
                "Could not create the transient RFloat runtime height " +
                "composition target.";

            return false;
        }

        compositor.BeginTransactionDiagnostics();

        prepared =
            true;

        return true;
    }

    public bool RequiresComposition(
        Vector2Int tileCoordinate
    )
    {
        return
            affectedTiles.Contains(
                tileCoordinate
            );
    }

    public bool TryComposeCommittedTile(
        Texture2D committedTile,
        Vector2Int tileCoordinate,
        float[] outputHeightData,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (!prepared)
        {
            errorMessage =
                "The runtime height composition context is not prepared.";

            return false;
        }

        if (
            !RequiresComposition(
                tileCoordinate
            )
        )
        {
            errorMessage =
                $"Tile ({tileCoordinate.x}, {tileCoordinate.y}) was " +
                "not classified as composition-affected.";

            return false;
        }

        if (
            compositionTarget == null
            ||
            !compositionTarget.IsCreated()
        )
        {
            errorMessage =
                "The transient runtime height composition target is " +
                "not available.";

            return false;
        }

        if (committedTile == null)
        {
            errorMessage =
                $"Committed height tile ({tileCoordinate.x}, " +
                $"{tileCoordinate.y}) is null.";

            return false;
        }

        if (
            committedTile.width !=
                samplesPerSide
            ||
            committedTile.height !=
                samplesPerSide
        )
        {
            errorMessage =
                $"Committed height tile ({tileCoordinate.x}, " +
                $"{tileCoordinate.y}) has unexpected dimensions. " +
                $"Expected {samplesPerSide} x {samplesPerSide}, " +
                $"received {committedTile.width} x " +
                $"{committedTile.height}.";

            return false;
        }

        if (
            outputHeightData == null
            ||
            outputHeightData.Length !=
                samplesPerSide *
                samplesPerSide
        )
        {
            errorMessage =
                "The runtime height composition output buffer has an " +
                "unexpected length.";

            return false;
        }

        try
        {
            Graphics.CopyTexture(
                committedTile,
                0,
                0,
                compositionTarget,
                0,
                0
            );
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Could not copy committed height tile " +
                $"({tileCoordinate.x}, {tileCoordinate.y}) into the " +
                "runtime composition target.\n\n" +
                exception.Message;

            return false;
        }

        if (
            !compositor.TryComposeTile(
                compositionTarget,
                tileCoordinate,
                0,
                samplesPerSide,
                sampleSpacing,
                tileWorldSize,
                worldSizeXZ,
                authoringData,
                out string compositorError
            )
        )
        {
            errorMessage =
                "The shared terrain height compositor could not bake " +
                $"tile ({tileCoordinate.x}, {tileCoordinate.y}).\n\n" +
                compositorError;

            return false;
        }

        AsyncGPUReadbackRequest request =
            AsyncGPUReadback.Request(
                compositionTarget,
                0,
                0,
                samplesPerSide,
                0,
                samplesPerSide,
                0,
                1,
                TextureFormat.RFloat,
                null
            );

        request.WaitForCompletion();

        if (request.hasError)
        {
            errorMessage =
                $"GPU readback failed while baking runtime height tile " +
                $"({tileCoordinate.x}, {tileCoordinate.y}).";

            return false;
        }

        var data =
            request.GetData<float>();

        if (
            data.Length !=
            outputHeightData.Length
        )
        {
            errorMessage =
                "GPU readback returned an unexpected runtime height " +
                $"sample count. Expected {outputHeightData.Length}, " +
                $"received {data.Length}.";

            return false;
        }

        for (
            int index = 0;
            index < data.Length;
            index++
        )
        {
            outputHeightData[index] =
                data[index];
        }

        compositedTileCount++;

        return true;
    }

    public void Dispose()
    {
        prepared =
            false;

        worldSettings =
            null;

        authoringData =
            null;

        affectedTiles.Clear();

        regionalCompositionRequired =
            false;

        DisposeTarget();

        compositor.Dispose();
    }

    private void DisposeTarget()
    {
        if (compositionTarget == null)
        {
            return;
        }

        if (compositionTarget.IsCreated())
        {
            compositionTarget.Release();
        }

        UnityEngine.Object.DestroyImmediate(
            compositionTarget
        );

        compositionTarget =
            null;
    }

    private static bool IsFinitePositive(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            )
            &&
            value > 0f;
    }
}
