using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Stage 13B GPU composition executor.
 *
 * Responsibilities:
 * - load/cache the authoring composition compute shader
 * - validate an existing preview-cache target
 * - calculate authoritative absolute authoring-space addressing
 * - evaluate the CURRENT ordered height-modifier stack for one tile
 * - dispatch supported additive TerrainStampModifier operations
 * - expose narrow tile-composition diagnostics
 *
 * Non-responsibilities:
 * - modifier mutation
 * - dirty-region calculation
 * - preview lifecycle/scheduling
 * - preview cache allocation/lifetime
 * - renderer binding
 * - authoringRevision/signature mutation
 *
 * IMPORTANT:
 * TerrainAuthoringPreviewService resets every dirty slice from the
 * committed base BEFORE calling this compositor. This class therefore
 * never accumulates edits from a previous composite result.
 */
public sealed class TerrainHeightCompositor
{
    public const string ComputeShaderAssetPath =
        "Assets/WorldMeshes/Shaders/Terrain/Authoring/" +
        "TerrainHeightComposition.compute";

    private const string IdentityKernelName =
        "IdentityComposite";

    private const string AdditiveStampKernelName =
        "ApplyAdditiveStamp";

    private const string ValidationKernelName =
        "ValidationAddConstant";

    private ComputeShader computeShader;

    private int identityKernel =
        -1;

    private int additiveStampKernel =
        -1;

    private int validationKernel =
        -1;

    private uint identityThreadGroupSizeX;

    private uint identityThreadGroupSizeY;

    private uint additiveStampThreadGroupSizeX;

    private uint additiveStampThreadGroupSizeY;

    private uint validationThreadGroupSizeX;

    private uint validationThreadGroupSizeY;

    /*
     * Backward-compatible Stage 13A diagnostic naming.
     *
     * These count successfully reconstructed/composited TILES, not
     * individual modifier compute dispatches. One dirty tile can now
     * execute zero, one, or many additive stamp dispatches.
     */
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
                additiveStampKernel >= 0
                &&
                validationKernel >= 0
                &&
                identityThreadGroupSizeX > 0
                &&
                identityThreadGroupSizeY > 0
                &&
                additiveStampThreadGroupSizeX > 0
                &&
                additiveStampThreadGroupSizeY > 0
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
                "The Stage 13B terrain composition compute shader " +
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

            additiveStampKernel =
                computeShader.FindKernel(
                    AdditiveStampKernelName
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
                "One or more required Stage 13B compute kernels " +
                "could not be found.\n\n" +
                "Required:\n" +
                "- " + IdentityKernelName + "\n" +
                "- " + AdditiveStampKernelName + "\n" +
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
                additiveStampKernel,
                out additiveStampThreadGroupSizeX,
                out additiveStampThreadGroupSizeY,
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
            additiveStampThreadGroupSizeX == 0
            ||
            additiveStampThreadGroupSizeY == 0
            ||
            validationThreadGroupSizeX == 0
            ||
            validationThreadGroupSizeY == 0
        )
        {
            ResetShaderState();

            errorMessage =
                "The Stage 13B compute shader reported an invalid " +
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

    /*
     * Backward-compatible Stage 13A identity overload.
     *
     * TerrainHeightCompositorValidationUtility uses this directly.
     * The real Stage 13B preview path should use the overload that also
     * receives TerrainAuthoringData.
     */
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
                "The Stage 13A identity compositor calculated an " +
                "invalid compute dispatch size.";

            return false;
        }

        try
        {
            SetCommonKernelParameters(
                identityKernel,
                heightCache,
                tileCoordinate,
                tileWorldOriginXZ,
                sliceIndex,
                samplesPerSide,
                sampleSpacing,
                tileWorldSize,
                worldSizeXZ
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
                "The Stage 13A identity GPU compositor could not " +
                $"dispatch tile ({tileCoordinate.x}, " +
                $"{tileCoordinate.y}) to cache slice " +
                $"{sliceIndex}.\n\n" +
                exception.Message;

            return false;
        }

        MarkTileCompositionSucceeded();

        return true;
    }

    /*
     * Stage 13B production composition entry point.
     *
     * The caller must have already restored this slice from committed
     * base data. The complete CURRENT modifier stack is then evaluated
     * in serialized list order.
     */
    public bool TryComposeTile(
        RenderTexture heightCache,
        Vector2Int tileCoordinate,
        int sliceIndex,
        int samplesPerSide,
        float sampleSpacing,
        float tileWorldSize,
        Vector2 worldSizeXZ,
        TerrainAuthoringData authoringData,
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

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";

            return false;
        }

        Vector2 tileMinXZ =
            tileWorldOriginXZ;

        Vector2 tileMaxXZ =
            tileWorldOriginXZ
            +
            new Vector2(
                tileWorldSize,
                tileWorldSize
            );

        IReadOnlyList<TerrainHeightModifier>
            modifiers =
                authoringData.HeightModifiers;

        for (
            int modifierIndex = 0;
            modifierIndex < modifiers.Count;
            modifierIndex++
        )
        {
            TerrainHeightModifier modifier =
                modifiers[
                    modifierIndex
                ];

            if (modifier == null)
            {
                errorMessage =
                    $"Height modifier index {modifierIndex} is null.";

                return false;
            }

            if (!modifier.Enabled)
            {
                continue;
            }

            Bounds modifierBounds =
                modifier
                    .GetAffectedWorldBounds();

            if (
                !OverlapsTileXZ(
                    modifierBounds,
                    tileMinXZ,
                    tileMaxXZ
                )
            )
            {
                continue;
            }

            /*
             * Stage 13B supports only TerrainStampModifier.
             *
             * An unsupported enabled modifier that overlaps this tile
             * is a transaction failure. Silently skipping it would let
             * PreviewService falsely acknowledge OverallAuthoringSignature.
             */
            if (
                !(modifier is TerrainStampModifier stampModifier)
            )
            {
                errorMessage =
                    "Unsupported enabled terrain height modifier at " +
                    $"index {modifierIndex}: " +
                    $"{modifier.GetType().Name}.";

                return false;
            }

            if (
                stampModifier.BlendMode !=
                TerrainHeightBlendMode.Additive
            )
            {
                errorMessage =
                    "Unsupported terrain height blend mode at modifier " +
                    $"index {modifierIndex}: " +
                    $"{stampModifier.BlendMode}.";

                return false;
            }

            TerrainHeightStampAsset stampAsset =
                stampModifier.StampAsset;

            /*
             * Stage 13B null/unconfigured stamp policy:
             * the modifier safely contributes nothing.
             */
            if (
                stampAsset == null
                ||
                stampAsset.HeightTexture == null
            )
            {
                continue;
            }

            if (
                Mathf.Approximately(
                    stampModifier.HeightDelta,
                    0f
                )
            )
            {
                continue;
            }

            if (
                !ValidateStampTexture(
                    stampAsset.HeightTexture,
                    modifierIndex,
                    out errorMessage
                )
            )
            {
                return false;
            }

            if (
                !TryDispatchAdditiveStamp(
                    heightCache,
                    tileCoordinate,
                    tileWorldOriginXZ,
                    sliceIndex,
                    samplesPerSide,
                    sampleSpacing,
                    tileWorldSize,
                    worldSizeXZ,
                    stampModifier,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }

        /*
         * Count a tile as successfully reconstructed even if no stamp
         * dispatch was required. Resetting it to committed base may be
         * the correct final result after moving/removing/disabling a
         * modifier.
         */
        MarkTileCompositionSucceeded();

        return true;
    }

    private bool TryDispatchAdditiveStamp(
        RenderTexture heightCache,
        Vector2Int tileCoordinate,
        Vector2 tileWorldOriginXZ,
        int sliceIndex,
        int samplesPerSide,
        float sampleSpacing,
        float tileWorldSize,
        Vector2 worldSizeXZ,
        TerrainStampModifier stampModifier,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        TerrainHeightStampAsset stampAsset =
            stampModifier.StampAsset;

        if (
            stampAsset == null
            ||
            stampAsset.HeightTexture == null
        )
        {
            return true;
        }

        Vector2 stampSize =
            stampModifier.SizeXZ;

        Vector2 stampPosition =
            stampModifier.PositionXZ;

        Vector2 stampMinXZ =
            stampPosition
            -
            stampSize *
            0.5f;

        int groupsX =
            DivideRoundUp(
                samplesPerSide,
                additiveStampThreadGroupSizeX
            );

        int groupsY =
            DivideRoundUp(
                samplesPerSide,
                additiveStampThreadGroupSizeY
            );

        if (
            groupsX <= 0
            ||
            groupsY <= 0
        )
        {
            errorMessage =
                "The Stage 13B compositor calculated an invalid " +
                "additive-stamp dispatch size.";

            return false;
        }

        try
        {
            SetCommonKernelParameters(
                additiveStampKernel,
                heightCache,
                tileCoordinate,
                tileWorldOriginXZ,
                sliceIndex,
                samplesPerSide,
                sampleSpacing,
                tileWorldSize,
                worldSizeXZ
            );

            computeShader.SetTexture(
                additiveStampKernel,
                "_StampTexture",
                stampAsset.HeightTexture
            );

            computeShader.SetVector(
                "_StampMinXZ",
                new Vector4(
                    stampMinXZ.x,
                    stampMinXZ.y,
                    0f,
                    0f
                )
            );

            computeShader.SetVector(
                "_StampSizeXZ",
                new Vector4(
                    stampSize.x,
                    stampSize.y,
                    0f,
                    0f
                )
            );

            computeShader.SetFloat(
                "_StampHeightDelta",
                stampModifier.HeightDelta
            );

            computeShader.Dispatch(
                additiveStampKernel,
                groupsX,
                groupsY,
                1
            );
        }
        catch (Exception exception)
        {
            errorMessage =
                "The Stage 13B additive stamp could not be dispatched " +
                $"for tile ({tileCoordinate.x}, {tileCoordinate.y}) " +
                $"and cache slice {sliceIndex}.\n\n" +
                exception.Message;

            return false;
        }

        return true;
    }

    private void SetCommonKernelParameters(
        int kernel,
        RenderTexture heightCache,
        Vector2Int tileCoordinate,
        Vector2 tileWorldOriginXZ,
        int sliceIndex,
        int samplesPerSide,
        float sampleSpacing,
        float tileWorldSize,
        Vector2 worldSizeXZ
    )
    {
        computeShader.SetTexture(
            kernel,
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
    }

    /*
     * Linear numeric stamp data is required because the red channel is
     * interpreted directly as a 0..1 height weight.
     *
     * Bilinear + Clamp are enforced here so the GPU sampling contract
     * is deterministic for Stage 13B. Mipmaps are not required because
     * the compute shader explicitly samples mip 0, though the README
     * still recommends disabling them.
     */
    private static bool ValidateStampTexture(
        Texture2D stampTexture,
        int modifierIndex,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (stampTexture == null)
        {
            return true;
        }

        string assetPath =
            AssetDatabase.GetAssetPath(
                stampTexture
            );

        if (
            string.IsNullOrEmpty(
                assetPath
            )
        )
        {
            /*
             * Transient/non-asset textures have no importer settings
             * to inspect. They are allowed here; production stamp
             * authoring normally uses persistent imported assets.
             */
            return true;
        }

        TextureImporter importer =
            AssetImporter.GetAtPath(
                assetPath
            )
            as TextureImporter;

        if (importer == null)
        {
            errorMessage =
                "The height-stamp texture importer could not be " +
                $"inspected for modifier index {modifierIndex}.\n\n" +
                assetPath;

            return false;
        }

        if (importer.sRGBTexture)
        {
            errorMessage =
                "Height-stamp textures must be imported as linear " +
                "numeric data (sRGB OFF).\n\n" +
                $"Modifier index: {modifierIndex}\n" +
                $"Texture: {assetPath}";

            return false;
        }

        if (
            importer.filterMode !=
            FilterMode.Bilinear
        )
        {
            errorMessage =
                "Height-stamp textures must use Bilinear filtering " +
                "for Stage 13B.\n\n" +
                $"Modifier index: {modifierIndex}\n" +
                $"Texture: {assetPath}";

            return false;
        }

        if (
            importer.wrapMode !=
            TextureWrapMode.Clamp
        )
        {
            errorMessage =
                "Height-stamp textures must use Clamp wrapping for " +
                "Stage 13B.\n\n" +
                $"Modifier index: {modifierIndex}\n" +
                $"Texture: {assetPath}";

            return false;
        }

        return true;
    }

    private static bool OverlapsTileXZ(
        Bounds modifierBounds,
        Vector2 tileMinXZ,
        Vector2 tileMaxXZ
    )
    {
        Vector3 modifierMin =
            modifierBounds.min;

        Vector3 modifierMax =
            modifierBounds.max;

        /*
         * Inclusive comparisons are intentional.
         *
         * If a stamp edge lies exactly on a tile edge, both duplicated
         * border samples are allowed to evaluate the same absolute
         * world coordinate.
         */
        return
            modifierMax.x >=
                tileMinXZ.x
            &&
            modifierMin.x <=
                tileMaxXZ.x
            &&
            modifierMax.z >=
                tileMinXZ.y
            &&
            modifierMin.z <=
                tileMaxXZ.y;
    }

    private void MarkTileCompositionSucceeded()
    {
        currentTransactionDispatchTileCount++;

        lastDispatchTileCount =
            currentTransactionDispatchTileCount;

        totalDispatchTileCount++;
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
                "The Stage 13B world-addressing parameters are invalid.";

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

        additiveStampKernel =
            -1;

        validationKernel =
            -1;

        identityThreadGroupSizeX =
            0;

        identityThreadGroupSizeY =
            0;

        additiveStampThreadGroupSizeX =
            0;

        additiveStampThreadGroupSizeY =
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
