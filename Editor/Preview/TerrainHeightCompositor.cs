using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * GPU executor for edit-mode terrain-height composition.
 *
 * Responsibilities:
 * - load/cache the authoring composition compute shader
 * - validate an existing preview-cache target
 * - calculate authoritative absolute authoring-space addressing
 * - evaluate the CURRENT ordered height-modifier stack for one tile
 * - dispatch supported additive TerrainStampModifier operations
 * - calculate a conservative contribution interval for the completed tile
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
 * The caller must reset a dirty slice from committed base data before
 * calling the production composition overload. Composition therefore
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

    private int identityKernel = -1;
    private int additiveStampKernel = -1;
    private int validationKernel = -1;

    private uint identityThreadGroupSizeX;
    private uint identityThreadGroupSizeY;
    private uint additiveStampThreadGroupSizeX;
    private uint additiveStampThreadGroupSizeY;
    private uint validationThreadGroupSizeX;
    private uint validationThreadGroupSizeY;

    /*
     * Backward-compatible diagnostic naming.
     *
     * These values count successfully reconstructed/composited TILES,
     * not individual modifier GPU dispatches. One tile can execute zero,
     * one, or many additive stamp dispatches.
     */
    private int currentTransactionDispatchTileCount;
    private int lastDispatchTileCount;
    private long totalDispatchTileCount;

    /*
     * Live integration diagnostics.
     *
     * "Considered" counts modifier-list entries visited by tile
     * transactions. "Modifier dispatch" counts supported modifier
     * operations that reached a GPU dispatch. "Compute dispatch" counts
     * the corresponding additive-stamp compute dispatches.
     */
    private int currentTransactionModifierConsideredCount;
    private int lastModifierConsideredCount;
    private long totalModifierConsideredCount;

    private int currentTransactionModifierDispatchCount;
    private int lastModifierDispatchCount;
    private long totalModifierDispatchCount;

    private int currentTransactionComputeDispatchCount;
    private int lastComputeDispatchCount;
    private long totalComputeDispatchCount;

    public bool IsPrepared
    {
        get
        {
            return
                computeShader != null
                && identityKernel >= 0
                && additiveStampKernel >= 0
                && validationKernel >= 0
                && identityThreadGroupSizeX > 0
                && identityThreadGroupSizeY > 0
                && additiveStampThreadGroupSizeX > 0
                && additiveStampThreadGroupSizeY > 0
                && validationThreadGroupSizeX > 0
                && validationThreadGroupSizeY > 0;
        }
    }

    public int LastDispatchTileCount =>
        lastDispatchTileCount;

    public long TotalDispatchTileCount =>
        totalDispatchTileCount;

    public int LastModifierConsideredCount =>
        lastModifierConsideredCount;

    public long TotalModifierConsideredCount =>
        totalModifierConsideredCount;

    public int LastModifierDispatchCount =>
        lastModifierDispatchCount;

    public long TotalModifierDispatchCount =>
        totalModifierDispatchCount;

    public int LastComputeDispatchCount =>
        lastComputeDispatchCount;

    public long TotalComputeDispatchCount =>
        totalComputeDispatchCount;

    public uint IdentityThreadGroupSizeX =>
        identityThreadGroupSizeX;

    public uint IdentityThreadGroupSizeY =>
        identityThreadGroupSizeY;

    public bool TryPrepare(
        out string errorMessage
    )
    {
        errorMessage = "";

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
            AssetDatabase.LoadAssetAtPath<ComputeShader>(
                ComputeShaderAssetPath
            );

        if (computeShader == null)
        {
            ResetShaderState();

            errorMessage =
                "The terrain height composition compute shader " +
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
                "One or more required terrain height composition " +
                "kernels could not be found.\n\n" +
                "Required:\n" +
                "- " + IdentityKernelName + "\n" +
                "- " + AdditiveStampKernelName + "\n" +
                "- " + ValidationKernelName + "\n\n" +
                exception.Message;

            return false;
        }

        computeShader.GetKernelThreadGroupSizes(
            identityKernel,
            out identityThreadGroupSizeX,
            out identityThreadGroupSizeY,
            out _
        );

        computeShader.GetKernelThreadGroupSizes(
            additiveStampKernel,
            out additiveStampThreadGroupSizeX,
            out additiveStampThreadGroupSizeY,
            out _
        );

        computeShader.GetKernelThreadGroupSizes(
            validationKernel,
            out validationThreadGroupSizeX,
            out validationThreadGroupSizeY,
            out _
        );

        if (
            identityThreadGroupSizeX == 0
            || identityThreadGroupSizeY == 0
            || additiveStampThreadGroupSizeX == 0
            || additiveStampThreadGroupSizeY == 0
            || validationThreadGroupSizeX == 0
            || validationThreadGroupSizeY == 0
        )
        {
            ResetShaderState();

            errorMessage =
                "The terrain height composition compute shader " +
                "reported an invalid thread-group size.";

            return false;
        }

        return true;
    }

    public void BeginTransactionDiagnostics()
    {
        currentTransactionDispatchTileCount = 0;
        lastDispatchTileCount = 0;

        currentTransactionModifierConsideredCount = 0;
        lastModifierConsideredCount = 0;

        currentTransactionModifierDispatchCount = 0;
        lastModifierDispatchCount = 0;

        currentTransactionComputeDispatchCount = 0;
        lastComputeDispatchCount = 0;
    }

    /*
     * Backward-compatible identity overload used by the existing GPU
     * compositor validation utility.
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
        errorMessage = "";

        if (!TryPrepare(out errorMessage))
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

        if (groupsX <= 0 || groupsY <= 0)
        {
            errorMessage =
                "The identity compositor calculated an invalid " +
                "compute dispatch size.";

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
                "The identity GPU compositor could not dispatch tile " +
                $"({tileCoordinate.x}, {tileCoordinate.y}) to cache " +
                $"slice {sliceIndex}.\n\n" +
                exception.Message;

            return false;
        }

        MarkTileCompositionSucceeded();

        return true;
    }

    /*
     * Compatibility overload for callers that do not require the
     * conservative contribution interval.
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
        return
            TryComposeTile(
                heightCache,
                tileCoordinate,
                sliceIndex,
                samplesPerSide,
                sampleSpacing,
                tileWorldSize,
                worldSizeXZ,
                authoringData,
                out _,
                out _,
                out errorMessage
            );
    }

    /*
     * Production composition entry point.
     *
     * The caller must have already restored this slice from committed
     * base data. The complete CURRENT modifier stack is evaluated in
     * serialized list order.
     *
     * minimumContribution / maximumContribution describe a conservative
     * additive interval for every modifier actually participating in
     * this tile's completed composition:
     *
     *     positive HeightDelta -> [0, HeightDelta]
     *     negative HeightDelta -> [HeightDelta, 0]
     *
     * This interval is deliberately conservative. It does not inspect
     * texture pixels or attempt to claim a tighter bound.
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
        out float minimumContribution,
        out float maximumContribution,
        out string errorMessage
    )
    {
        minimumContribution = 0f;
        maximumContribution = 0f;
        errorMessage = "";

        if (!TryPrepare(out errorMessage))
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
            + new Vector2(
                tileWorldSize,
                tileWorldSize
            );

        IReadOnlyList<TerrainHeightModifier> modifiers =
            authoringData.HeightModifiers;

        for (
            int modifierIndex = 0;
            modifierIndex < modifiers.Count;
            modifierIndex++
        )
        {
            TerrainHeightModifier modifier =
                modifiers[modifierIndex];

            MarkModifierConsidered();

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
                modifier.GetAffectedWorldBounds();

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
             * The current compositor supports only TerrainStampModifier.
             * An unsupported enabled modifier that overlaps this tile is
             * a transaction failure. Silently skipping it would allow
             * PreviewService to falsely acknowledge the overall state.
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
             * Null/unconfigured stamps safely contribute nothing.
             */
            if (
                stampAsset == null
                || stampAsset.HeightTexture == null
            )
            {
                continue;
            }

            float heightDelta =
                stampModifier.HeightDelta;

            if (Mathf.Approximately(heightDelta, 0f))
            {
                continue;
            }

            if (
                !ValidateStampTexture(
                    stampAsset.HeightTexture,
                    stampModifier,
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

            MarkModifierDispatchSucceeded();

            if (heightDelta > 0f)
            {
                maximumContribution += heightDelta;
            }
            else
            {
                minimumContribution += heightDelta;
            }

            if (
                !IsFinite(minimumContribution)
                || !IsFinite(maximumContribution)
                || maximumContribution < minimumContribution
            )
            {
                errorMessage =
                    "The conservative modifier contribution range " +
                    "overflowed or became invalid while composing " +
                    $"tile ({tileCoordinate.x}, {tileCoordinate.y}).";

                return false;
            }
        }

        /*
         * Count a tile as successfully reconstructed even if no stamp
         * dispatch was required. Resetting it to committed base may be
         * the correct result after move/remove/disable operations.
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
        errorMessage = "";

        TerrainHeightStampAsset stampAsset =
            stampModifier.StampAsset;

        if (
            stampAsset == null
            || stampAsset.HeightTexture == null
        )
        {
            return true;
        }

        Vector2 stampSize =
            stampModifier.SizeXZ;

        Vector2 stampPosition =
            stampModifier.PositionXZ;

        TerrainStampTransformUtility
            .GetWorldAxes(
                stampModifier.RotationDegrees,
                out Vector2 stampRightXZ,
                out Vector2 stampForwardXZ
            );

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

        if (groupsX <= 0 || groupsY <= 0)
        {
            errorMessage =
                "The additive stamp compositor calculated an invalid " +
                "compute dispatch size.";

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
                "_StampTextureSize",
                new Vector4(
                    Mathf.Max(
                        1,
                        stampAsset.HeightTexture.width
                    ),
                    Mathf.Max(
                        1,
                        stampAsset.HeightTexture.height
                    ),
                    0f,
                    0f
                )
            );

            computeShader.SetInt(
                "_StampMipCount",
                Mathf.Max(
                    1,
                    stampAsset.HeightTexture.mipmapCount
                )
            );

            computeShader.SetVector(
                "_StampCenterXZ",
                new Vector4(
                    stampPosition.x,
                    stampPosition.y,
                    0f,
                    0f
                )
            );

            computeShader.SetVector(
                "_StampRightXZ",
                new Vector4(
                    stampRightXZ.x,
                    stampRightXZ.y,
                    0f,
                    0f
                )
            );

            computeShader.SetVector(
                "_StampForwardXZ",
                new Vector4(
                    stampForwardXZ.x,
                    stampForwardXZ.y,
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

            computeShader.SetInt(
                "_StampFlipX",
                stampModifier.FlipX
                    ? 1
                    : 0
            );

            computeShader.SetInt(
                "_StampFlipZ",
                stampModifier.FlipZ
                    ? 1
                    : 0
            );

            computeShader.SetFloat(
                "_StampSourceInputMin",
                stampModifier.SourceInputMin
            );

            computeShader.SetFloat(
                "_StampSourceInputMax",
                stampModifier.SourceInputMax
            );

            computeShader.SetFloat(
                "_StampSourceGamma",
                stampModifier.SourceGamma
            );

            computeShader.SetFloat(
                "_StampHeightDelta",
                stampModifier.HeightDelta
            );

            /*
             * Exact footprint-falloff contract:
             *
             * nearestEdge =
             *     min(uv.x, 1-uv.x, uv.y, 1-uv.y)
             *
             * edgeDistance01 =
             *     saturate(nearestEdge * 2)
             *
             * Falloff == 0:
             *     footprintFalloff = 1
             *
             * Falloff > 0:
             *     footprintFalloff =
             *         smoothstep(0, Falloff, edgeDistance01)
             *
             * finalWeight =
             *     remappedWeight * footprintFalloff
             *
             * The compute shader contains the authoritative GPU
             * implementation; this comment documents the same contract
             * for the later runtime compositor.
             */
            computeShader.SetFloat(
                "_StampFalloff",
                stampModifier.Falloff
            );

            computeShader.SetFloat(
                "_StampSmoothingRadius",
                stampModifier.SmoothingRadius
            );

            computeShader.SetFloat(
                "_StampSmoothingStrength",
                stampModifier.SmoothingStrength
            );

            computeShader.Dispatch(
                additiveStampKernel,
                groupsX,
                groupsY,
                1
            );

            MarkComputeDispatchSucceeded();
        }
        catch (Exception exception)
        {
            errorMessage =
                "The additive height stamp could not be dispatched " +
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
     * Height stamps are numeric data:
     *
     *     red 0 = no contribution
     *     red 1 = full HeightDelta contribution
     *
     * The shader clamps sampled red values to 0..1 so the conservative
     * contribution-range contract remains guaranteed.
     *
     * Bilinear + Clamp remain mandatory. Small-radius smoothing uses mip 0.
     * Medium/large-radius smoothing automatically samples prefiltered mip
     * levels and manually blends adjacent levels for continuous Radius edits.
     */
    private static bool ValidateStampTexture(
        Texture2D stampTexture,
        TerrainStampModifier stampModifier,
        int modifierIndex,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (stampTexture == null)
        {
            return true;
        }

        if (
            RequiresMipAssistedSmoothing(
                stampTexture,
                stampModifier
            )
            &&
            stampTexture.mipmapCount <= 1
        )
        {
            errorMessage =
                "Large-radius terrain-stamp smoothing requires source " +
                "mipmaps so broad filters can use prefiltered height data.\n\n" +
                $"Modifier index: {modifierIndex}\n" +
                $"Texture: {stampTexture.name}\n\n" +
                "Enable Generate Mip Maps on the height-stamp texture importer.";

            return false;
        }

        string assetPath =
            AssetDatabase.GetAssetPath(
                stampTexture
            );

        if (string.IsNullOrEmpty(assetPath))
        {
            /*
             * Transient/non-asset textures have no importer settings to
             * inspect. Persistent production stamps normally use imported
             * assets and therefore take the validation path below.
             */
            return true;
        }

        TextureImporter importer =
            AssetImporter.GetAtPath(
                assetPath
            ) as TextureImporter;

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

        if (importer.filterMode != FilterMode.Bilinear)
        {
            errorMessage =
                "Height-stamp textures must use Bilinear filtering.\n\n" +
                $"Modifier index: {modifierIndex}\n" +
                $"Texture: {assetPath}";

            return false;
        }

        if (importer.wrapMode != TextureWrapMode.Clamp)
        {
            errorMessage =
                "Height-stamp textures must use Clamp wrapping.\n\n" +
                $"Modifier index: {modifierIndex}\n" +
                $"Texture: {assetPath}";

            return false;
        }

        return true;
    }

    private static bool RequiresMipAssistedSmoothing(
        Texture2D stampTexture,
        TerrainStampModifier stampModifier
    )
    {
        if (
            stampTexture == null
            ||
            stampModifier == null
            ||
            stampModifier.SmoothingRadius <= 0f
            ||
            stampModifier.SmoothingStrength <= 0f
        )
        {
            return false;
        }

        Vector2 stampSize =
            stampModifier.SizeXZ;

        float texelWorldSizeX =
            stampSize.x
            /
            Mathf.Max(
                1,
                stampTexture.width
            );

        float texelWorldSizeZ =
            stampSize.y
            /
            Mathf.Max(
                1,
                stampTexture.height
            );

        float radiusInTexelsX =
            stampModifier.SmoothingRadius
            /
            Mathf.Max(
                0.000001f,
                texelWorldSizeX
            );

        float radiusInTexelsZ =
            stampModifier.SmoothingRadius
            /
            Mathf.Max(
                0.000001f,
                texelWorldSizeZ
            );

        return
            Mathf.Max(
                radiusInTexelsX,
                radiusInTexelsZ
            )
            >
            4f;
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
         * border samples evaluate the same absolute world coordinate.
         */
        return
            modifierMax.x >= tileMinXZ.x
            && modifierMin.x <= tileMaxXZ.x
            && modifierMax.z >= tileMinXZ.y
            && modifierMin.z <= tileMaxXZ.y;
    }

    private void MarkModifierConsidered()
    {
        currentTransactionModifierConsideredCount++;

        lastModifierConsideredCount =
            currentTransactionModifierConsideredCount;

        totalModifierConsideredCount++;
    }

    private void MarkModifierDispatchSucceeded()
    {
        currentTransactionModifierDispatchCount++;

        lastModifierDispatchCount =
            currentTransactionModifierDispatchCount;

        totalModifierDispatchCount++;
    }

    private void MarkComputeDispatchSucceeded()
    {
        currentTransactionComputeDispatchCount++;

        lastComputeDispatchCount =
            currentTransactionComputeDispatchCount;

        totalComputeDispatchCount++;
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
        errorMessage = "";

        if (!TryPrepare(out errorMessage))
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

        if (groupsX <= 0 || groupsY <= 0)
        {
            errorMessage =
                "The validation GPU write calculated an invalid " +
                "compute dispatch size.";

            return false;
        }

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
                "The validation GPU write could not be dispatched.\n\n" +
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
        worldXZ = Vector2.zero;
        errorMessage = "";

        if (
            samplesPerSide <= 1
            || sampleX < 0
            || sampleZ < 0
            || sampleX >= samplesPerSide
            || sampleZ >= samplesPerSide
        )
        {
            errorMessage =
                "The requested sample coordinate is outside the " +
                "height tile.";

            return false;
        }

        if (
            !IsFinite(sampleSpacing)
            || sampleSpacing <= 0f
            || !IsFinite(tileWorldSize)
            || tileWorldSize <= 0f
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
                Mathf.Abs(tileWorldSize) *
                0.000001f
            );

        if (
            Mathf.Abs(
                expectedTileWorldSize -
                tileWorldSize
            ) > tolerance
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
            + new Vector2(
                sampleX * sampleSpacing,
                sampleZ * sampleSpacing
            );

        return
            IsFinite(worldXZ.x)
            && IsFinite(worldXZ.y);
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
        tileWorldOriginXZ = Vector2.zero;
        errorMessage = "";

        if (
            samplesPerSide <= 1
            || !IsFinite(sampleSpacing)
            || sampleSpacing <= 0f
            || !IsFinite(tileWorldSize)
            || tileWorldSize <= 0f
            || !IsFinite(worldSizeXZ.x)
            || !IsFinite(worldSizeXZ.y)
            || worldSizeXZ.x <= 0f
            || worldSizeXZ.y <= 0f
        )
        {
            errorMessage =
                "The compositor world-addressing parameters are invalid.";

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
            ) > tolerance
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
        errorMessage = "";

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

        if (heightCache.dimension != TextureDimension.Tex2DArray)
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

        if (heightCache.format != RenderTextureFormat.RFloat)
        {
            errorMessage =
                "The compositor requires an RFloat height cache.";

            return false;
        }

        if (
            samplesPerSide <= 1
            || heightCache.width != samplesPerSide
            || heightCache.height != samplesPerSide
        )
        {
            errorMessage =
                "The compositor sample dimensions do not match the " +
                "height-cache slice dimensions.";

            return false;
        }

        if (
            sliceIndex < 0
            || sliceIndex >= heightCache.volumeDepth
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
                tileCoordinate.x * tileWorldSize,
                tileCoordinate.y * tileWorldSize
            );
    }

    private static int DivideRoundUp(
        int value,
        uint divisor
    )
    {
        if (value <= 0 || divisor == 0)
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
        computeShader = null;

        identityKernel = -1;
        additiveStampKernel = -1;
        validationKernel = -1;

        identityThreadGroupSizeX = 0;
        identityThreadGroupSizeY = 0;
        additiveStampThreadGroupSizeX = 0;
        additiveStampThreadGroupSizeY = 0;
        validationThreadGroupSizeX = 0;
        validationThreadGroupSizeY = 0;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value)
            && !float.IsInfinity(value);
    }
}
