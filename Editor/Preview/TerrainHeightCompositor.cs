using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Stage 13A GPU composition executor.
 *
 * Responsibilities:
 * - load/cache the authoring composition compute shader
 * - validate an existing preview-cache target
 * - calculate authoritative absolute authoring-space addressing
 * - dispatch compute work into one existing texture-array slice
 * - expose narrow dispatch diagnostics
 *
 * Non-responsibilities:
 * - modifier mutation
 * - dirty-region calculation
 * - preview lifecycle/scheduling
 * - preview cache allocation/lifetime
 * - renderer binding
 * - authoringRevision/signature mutation
 *
 * Stage 13A uses IdentityComposite for the real preview path. The
 * pipeline therefore proves in-place GPU slice composition without
 * changing visible terrain yet. Stage 13B replaces the identity
 * operation with the first real additive TerrainStampModifier.
 */
public sealed class TerrainHeightCompositor
{
    public const string ComputeShaderAssetPath =
        "Assets/WorldMeshes/Shaders/Terrain/Authoring/" +
        "TerrainHeightComposition.compute";

    private const string IdentityKernelName =
        "IdentityComposite";

    private const string ValidationKernelName =
        "ValidationAddConstant";

    private ComputeShader computeShader;

    private int identityKernel =
        -1;

    private int validationKernel =
        -1;

    private uint identityThreadGroupSizeX;

    private uint identityThreadGroupSizeY;

    private uint validationThreadGroupSizeX;

    private uint validationThreadGroupSizeY;

    private int currentTransactionDispatchTileCount;

    private int lastDispatchTileCount;

    private long totalDispatchTileCount;

    public bool IsPrepared
    {
        get
        {
            return
                computeShader != null
                &&
                identityKernel >= 0
                &&
                validationKernel >= 0
                &&
                identityThreadGroupSizeX > 0
                &&
                identityThreadGroupSizeY > 0
                &&
                validationThreadGroupSizeX > 0
                &&
                validationThreadGroupSizeY > 0;
        }
    }

    public int LastDispatchTileCount
    {
        get
        {
            return
                lastDispatchTileCount;
        }
    }

    public long TotalDispatchTileCount
    {
        get
        {
            return
                totalDispatchTileCount;
        }
    }

    public uint IdentityThreadGroupSizeX
    {
        get
        {
            return
                identityThreadGroupSizeX;
        }
    }

    public uint IdentityThreadGroupSizeY
    {
        get
        {
            return
                identityThreadGroupSizeY;
        }
    }

    public bool TryPrepare(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (IsPrepared)
        {
            return true;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            errorMessage =
                "The current graphics device does not support " +
                "compute shaders.";

            return false;
        }

        computeShader =
            AssetDatabase
                .LoadAssetAtPath<ComputeShader>(
                    ComputeShaderAssetPath
                );

        if (computeShader == null)
        {
            ResetShaderState();

            errorMessage =
                "The Stage 13A terrain composition compute shader " +
                "could not be loaded:\n\n" +
                ComputeShaderAssetPath;

            return false;
        }

        try
        {
            identityKernel =
                computeShader.FindKernel(
                    IdentityKernelName
                );

            validationKernel =
                computeShader.FindKernel(
                    ValidationKernelName
                );
        }
        catch (Exception exception)
        {
            ResetShaderState();

            errorMessage =
                "One or more required Stage 13A compute kernels " +
                "could not be found.\n\n" +
                "Required:\n" +
                "- " + IdentityKernelName + "\n" +
                "- " + ValidationKernelName + "\n\n" +
                exception.Message;

            return false;
        }

        computeShader
            .GetKernelThreadGroupSizes(
                identityKernel,
                out identityThreadGroupSizeX,
                out identityThreadGroupSizeY,
                out _
            );

        computeShader
            .GetKernelThreadGroupSizes(
                validationKernel,
                out validationThreadGroupSizeX,
                out validationThreadGroupSizeY,
                out _
            );

        if (
            identityThreadGroupSizeX == 0
            ||
            identityThreadGroupSizeY == 0
            ||
            validationThreadGroupSizeX == 0
            ||
            validationThreadGroupSizeY == 0
        )
        {
            ResetShaderState();

            errorMessage =
                "The Stage 13A compute shader reported an invalid " +
                "thread-group size.";

            return false;
        }

        return true;
    }

    public void BeginTransactionDiagnostics()
    {
        currentTransactionDispatchTileCount =
            0;

        lastDispatchTileCount =
            0;
    }

    public bool TryComposeTile(
        RenderTexture heightCache,
        Vector2Int tileCoordinate,
        int sliceIndex,
        int samplesPerSide,
        float sampleSpacing,
        float tileWorldSize,
        Vector2 worldSizeXZ,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !TryPrepare(
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !ValidateHeightCacheTarget(
                heightCache,
                sliceIndex,
                samplesPerSide,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !ValidateWorldAddressingContract(
                tileCoordinate,
                samplesPerSide,
                sampleSpacing,
                tileWorldSize,
                worldSizeXZ,
                out Vector2 tileWorldOriginXZ,
                out errorMessage
            )
        )
        {
            return false;
        }

        int groupsX =
            DivideRoundUp(
                samplesPerSide,
                identityThreadGroupSizeX
            );

        int groupsY =
            DivideRoundUp(
                samplesPerSide,
                identityThreadGroupSizeY
            );

        if (
            groupsX <= 0
            ||
            groupsY <= 0
        )
        {
            errorMessage =
                "The Stage 13A compositor calculated an invalid " +
                "compute dispatch size.";

            return false;
        }

        try
        {
            computeShader.SetTexture(
                identityKernel,
                "_HeightCache",
                heightCache
            );

            computeShader.SetInt(
                "_TargetSlice",
                sliceIndex
            );

            computeShader.SetInt(
                "_SamplesPerSide",
                samplesPerSide
            );

            computeShader.SetFloat(
                "_SampleSpacing",
                sampleSpacing
            );

            computeShader.SetFloat(
                "_TileWorldSize",
                tileWorldSize
            );

            computeShader.SetInts(
                "_TileCoordinate",
                tileCoordinate.x,
                tileCoordinate.y
            );

            computeShader.SetVector(
                "_TileWorldOriginXZ",
                new Vector4(
                    tileWorldOriginXZ.x,
                    tileWorldOriginXZ.y,
                    0f,
                    0f
                )
            );

            computeShader.SetVector(
                "_WorldSizeXZ",
                new Vector4(
                    worldSizeXZ.x,
                    worldSizeXZ.y,
                    0f,
                    0f
                )
            );

            computeShader.Dispatch(
                identityKernel,
                groupsX,
                groupsY,
                1
            );
        }
        catch (Exception exception)
        {
            errorMessage =
                "The Stage 13A GPU compositor could not dispatch " +
                $"tile ({tileCoordinate.x}, {tileCoordinate.y}) " +
                $"to cache slice {sliceIndex}.\n\n" +
                exception.Message;

            return false;
        }

        currentTransactionDispatchTileCount++;

        lastDispatchTileCount =
            currentTransactionDispatchTileCount;

        totalDispatchTileCount++;

        return true;
    }

    internal bool TryValidationAddConstant(
        RenderTexture validationCache,
        int sliceIndex,
        int samplesPerSide,
        float heightDelta,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !TryPrepare(
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !ValidateHeightCacheTarget(
                validationCache,
                sliceIndex,
                samplesPerSide,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (!IsFinite(heightDelta))
        {
            errorMessage =
                "The validation height delta is not finite.";

            return false;
        }

        int groupsX =
            DivideRoundUp(
                samplesPerSide,
                validationThreadGroupSizeX
            );

        int groupsY =
            DivideRoundUp(
                samplesPerSide,
                validationThreadGroupSizeY
            );

        try
        {
            computeShader.SetTexture(
                validationKernel,
                "_HeightCache",
                validationCache
            );

            computeShader.SetInt(
                "_TargetSlice",
                sliceIndex
            );

            computeShader.SetInt(
                "_SamplesPerSide",
                samplesPerSide
            );

            computeShader.SetFloat(
                "_ValidationHeightDelta",
                heightDelta
            );

            computeShader.Dispatch(
                validationKernel,
                groupsX,
                groupsY,
                1
            );
        }
        catch (Exception exception)
        {
            errorMessage =
                "The Stage 13A validation GPU write could not be " +
                "dispatched.\n\n" +
                exception.Message;

            return false;
        }

        return true;
    }

    internal static bool TryCalculateSampleWorldXZ(
        Vector2Int tileCoordinate,
        int sampleX,
        int sampleZ,
        int samplesPerSide,
        float sampleSpacing,
        float tileWorldSize,
        out Vector2 worldXZ,
        out string errorMessage
    )
    {
        worldXZ =
            Vector2.zero;

        errorMessage =
            "";

        if (
            samplesPerSide <= 1
            ||
            sampleX < 0
            ||
            sampleZ < 0
            ||
            sampleX >= samplesPerSide
            ||
            sampleZ >= samplesPerSide
        )
        {
            errorMessage =
                "The requested sample coordinate is outside the " +
                "height tile.";

            return false;
        }

        if (
            !IsFinite(sampleSpacing)
            ||
            sampleSpacing <= 0f
            ||
            !IsFinite(tileWorldSize)
            ||
            tileWorldSize <= 0f
        )
        {
            errorMessage =
                "Sample spacing or tile world size is invalid.";

            return false;
        }

        float expectedTileWorldSize =
            (samplesPerSide - 1) *
            sampleSpacing;

        float tolerance =
            Mathf.Max(
                0.00001f,
                Mathf.Abs(
                    tileWorldSize
                ) *
                0.000001f
            );

        if (
            Mathf.Abs(
                expectedTileWorldSize -
                tileWorldSize
            )
            >
            tolerance
        )
        {
            errorMessage =
                "The height-tile addressing contract is inconsistent.\n" +
                $"Samples Per Side: {samplesPerSide}\n" +
                $"Sample Spacing: {sampleSpacing:R}\n" +
                $"Expected Tile Size: {expectedTileWorldSize:R}\n" +
                $"Actual Tile Size: {tileWorldSize:R}";

            return false;
        }

        Vector2 tileWorldOrigin =
            CalculateTileWorldOrigin(
                tileCoordinate,
                tileWorldSize
            );

        worldXZ =
            tileWorldOrigin
            +
            new Vector2(
                sampleX *
                    sampleSpacing,
                sampleZ *
                    sampleSpacing
            );

        return
            IsFinite(
                worldXZ.x
            )
            &&
            IsFinite(
                worldXZ.y
            );
    }

    private static bool ValidateWorldAddressingContract(
        Vector2Int tileCoordinate,
        int samplesPerSide,
        float sampleSpacing,
        float tileWorldSize,
        Vector2 worldSizeXZ,
        out Vector2 tileWorldOriginXZ,
        out string errorMessage
    )
    {
        tileWorldOriginXZ =
            Vector2.zero;

        errorMessage =
            "";

        if (
            samplesPerSide <= 1
            ||
            !IsFinite(sampleSpacing)
            ||
            sampleSpacing <= 0f
            ||
            !IsFinite(tileWorldSize)
            ||
            tileWorldSize <= 0f
            ||
            !IsFinite(worldSizeXZ.x)
            ||
            !IsFinite(worldSizeXZ.y)
            ||
            worldSizeXZ.x <= 0f
            ||
            worldSizeXZ.y <= 0f
        )
        {
            errorMessage =
                "The Stage 13A world-addressing parameters are invalid.";

            return false;
        }

        float expectedTileWorldSize =
            (samplesPerSide - 1) *
            sampleSpacing;

        float tolerance =
            Mathf.Max(
                0.00001f,
                tileWorldSize *
                0.000001f
            );

        if (
            Mathf.Abs(
                expectedTileWorldSize -
                tileWorldSize
            )
            >
            tolerance
        )
        {
            errorMessage =
                "The compositor cannot use absolute sample addressing " +
                "because tileWorldSize does not equal " +
                "(samplesPerSide - 1) * sampleSpacing.\n\n" +
                $"Expected: {expectedTileWorldSize:R}\n" +
                $"Actual: {tileWorldSize:R}";

            return false;
        }

        tileWorldOriginXZ =
            CalculateTileWorldOrigin(
                tileCoordinate,
                tileWorldSize
            );

        return true;
    }

    private static bool ValidateHeightCacheTarget(
        RenderTexture heightCache,
        int sliceIndex,
        int samplesPerSide,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (heightCache == null)
        {
            errorMessage =
                "The compositor height cache is null.";

            return false;
        }

        if (!heightCache.IsCreated())
        {
            errorMessage =
                "The compositor height cache has not been created.";

            return false;
        }

        if (
            heightCache.dimension !=
            TextureDimension.Tex2DArray
        )
        {
            errorMessage =
                "The compositor requires a 2D texture-array " +
                "RenderTexture.";

            return false;
        }

        if (!heightCache.enableRandomWrite)
        {
            errorMessage =
                "The compositor height cache was not created with " +
                "enableRandomWrite.";

            return false;
        }

        if (
            heightCache.format !=
            RenderTextureFormat.RFloat
        )
        {
            errorMessage =
                "The compositor requires an RFloat height cache.";

            return false;
        }

        if (
            samplesPerSide <= 1
            ||
            heightCache.width !=
                samplesPerSide
            ||
            heightCache.height !=
                samplesPerSide
        )
        {
            errorMessage =
                "The compositor sample dimensions do not match the " +
                "height-cache slice dimensions.";

            return false;
        }

        if (
            sliceIndex < 0
            ||
            sliceIndex >=
                heightCache.volumeDepth
        )
        {
            errorMessage =
                $"Target slice {sliceIndex} is outside the height " +
                $"cache depth ({heightCache.volumeDepth}).";

            return false;
        }

        return true;
    }

    private static Vector2 CalculateTileWorldOrigin(
        Vector2Int tileCoordinate,
        float tileWorldSize
    )
    {
        return
            new Vector2(
                tileCoordinate.x *
                    tileWorldSize,
                tileCoordinate.y *
                    tileWorldSize
            );
    }

    private static int DivideRoundUp(
        int value,
        uint divisor
    )
    {
        if (
            value <= 0
            ||
            divisor == 0
        )
        {
            return 0;
        }

        return
            Mathf.CeilToInt(
                value /
                (float)divisor
            );
    }

    private void ResetShaderState()
    {
        computeShader =
            null;

        identityKernel =
            -1;

        validationKernel =
            -1;

        identityThreadGroupSizeX =
            0;

        identityThreadGroupSizeY =
            0;

        validationThreadGroupSizeX =
            0;

        validationThreadGroupSizeY =
            0;
    }

    private static bool IsFinite(
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
            );
    }
}
