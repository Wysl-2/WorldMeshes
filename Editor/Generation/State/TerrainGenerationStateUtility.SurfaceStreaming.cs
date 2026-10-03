using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainGenerationStateUtility
{
    public static GenerationStatus GetSurfaceStreamingStatus(
        WorldSettings worldSettings
    )
    {
        TerrainGenerationStateEvaluationContext context =
            new TerrainGenerationStateEvaluationContext(
                worldSettings,
                TerrainGenerationStateEvaluationMode.Operational
            );

        return GetSurfaceStreamingStatus(context);
    }

    internal static GenerationStatus GetSurfaceStreamingStatus(
        TerrainGenerationStateEvaluationContext context
    )
    {
        if (context == null)
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            context.TryGetSurfaceStreamingStatus(
                out GenerationStatus cachedStatus
            )
        )
        {
            return cachedStatus;
        }

        WorldSettings worldSettings =
            context.WorldSettings;

        if (worldSettings == null)
        {
            return context.CacheSurfaceStreamingStatus(
                GenerationStatus.NotGenerated
            );
        }

        GenerationStatus surfaceStatus =
            GetSurfaceMaskStatus(context);

        TerrainSurfaceMaskManifest manifest =
            context.SurfaceMaskManifest;

        if (
            surfaceStatus == GenerationStatus.NotGenerated
            || manifest == null
        )
        {
            return context.CacheSurfaceStreamingStatus(
                GenerationStatus.NotGenerated
            );
        }

        bool everGenerated =
            manifest.streamingGenerationRevision > 0
            || manifest.streamingPyramidCompilerVersion > 0
            || manifest.streamingPyramidPolicyVersion > 0
            || manifest.streamingPyramidIsComplete
            || !string.IsNullOrEmpty(
                manifest.streamingGenerationSignature
            );

        if (!everGenerated)
        {
            return context.CacheSurfaceStreamingStatus(
                GenerationStatus.NotGenerated
            );
        }

        if (
            surfaceStatus != GenerationStatus.Current
            || !IsSurfaceStreamingManifestCurrent(
                manifest,
                worldSettings
            )
        )
        {
            return context.CacheSurfaceStreamingStatus(
                GenerationStatus.OutOfDate
            );
        }

        return context.CacheSurfaceStreamingStatus(
            GenerationStatus.Current
        );
    }

    public static bool IsSurfaceStreamingManifestCurrent(
        TerrainSurfaceMaskManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (
            manifest == null
            || worldSettings == null
            || !manifest.isComplete
            || !manifest.streamingPyramidIsComplete
            || manifest.streamingPyramidCompilerVersion !=
                TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion
            || manifest.streamingPyramidPolicyVersion !=
                TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion
            || manifest.surfaceMaskGenerationRevision <= 0
            || string.IsNullOrEmpty(
                manifest.surfaceGenerationSignature
            )
            || manifest.streamingSourceSurfaceMaskGenerationRevision !=
                manifest.surfaceMaskGenerationRevision
            || !string.Equals(
                manifest.streamingSourceSurfaceGenerationSignature ?? "",
                manifest.surfaceGenerationSignature ?? "",
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        if (
            !TryBuildCurrentSurfaceStreamingTarget(
                worldSettings,
                manifest,
                out List<TerrainSurfaceStreamingLevelDescriptor> descriptors,
                out string signature,
                out _
            )
        )
        {
            return false;
        }

        return
            StreamingDescriptorsMatch(
                manifest,
                descriptors
            )
            && string.Equals(
                manifest.streamingGenerationSignature ?? "",
                signature ?? "",
                StringComparison.Ordinal
            );
    }

    public static bool IsSurfaceStreamingTargetMetadataCompatible(
        TerrainSurfaceMaskManifest manifest,
        WorldSettings worldSettings
    )
    {
        if (
            manifest == null
            || worldSettings == null
            || !manifest.isComplete
            || !manifest.streamingPyramidIsComplete
            || manifest.streamingGenerationRevision <= 0
            || manifest.streamingPyramidCompilerVersion !=
                TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion
            || manifest.streamingPyramidPolicyVersion !=
                TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion
            || string.IsNullOrEmpty(
                manifest.streamingGenerationSignature
            )
        )
        {
            return false;
        }

        if (
            !TryBuildCurrentSurfaceStreamingTarget(
                worldSettings,
                manifest,
                out List<TerrainSurfaceStreamingLevelDescriptor> descriptors,
                out _,
                out _
            )
        )
        {
            return false;
        }

        return StreamingDescriptorsMatch(
            manifest,
            descriptors
        );
    }

    public static bool TryBuildCurrentSurfaceStreamingTarget(
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        out List<TerrainSurfaceStreamingLevelDescriptor> descriptors,
        out string generationSignature,
        out string errorMessage
    )
    {
        descriptors =
            new List<TerrainSurfaceStreamingLevelDescriptor>();

        generationSignature = "";
        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is unavailable.";

            return false;
        }

        if (
            manifest == null
            || !manifest.isComplete
            || manifest.surfaceMaskGenerationRevision <= 0
            || string.IsNullOrEmpty(
                manifest.surfaceGenerationSignature
            )
        )
        {
            errorMessage =
                "The authoritative Surface manifest is unavailable or incomplete.";

            return false;
        }

        List<int> strides =
            new List<int>();

        if (
            !TerrainSurfaceStreamingPyramidPolicy.TryGetDerivedStrides(
                worldSettings,
                manifest,
                strides,
                out errorMessage
            )
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < strides.Count;
            index++
        )
        {
            if (
                !TerrainSurfaceStreamingPyramidPolicy.TryBuildLevelDescriptor(
                    worldSettings,
                    manifest,
                    strides[index],
                    out TerrainSurfaceStreamingLevelDescriptor descriptor,
                    out errorMessage
                )
            )
            {
                descriptors.Clear();
                return false;
            }

            descriptors.Add(descriptor);
        }

        generationSignature =
            TerrainSurfaceSignatureUtility.GetStreamingGenerationSignature(
                TerrainSurfaceMaskManifest.CurrentStreamingPyramidCompilerVersion,
                TerrainSurfaceStreamingPyramidPolicy.CurrentPolicyVersion,
                manifest.surfaceMaskGenerationRevision,
                manifest.surfaceGenerationSignature,
                manifest,
                descriptors
            );

        if (string.IsNullOrEmpty(generationSignature))
        {
            errorMessage =
                "The Surface Streaming generation signature could not be calculated.";

            descriptors.Clear();
            return false;
        }

        return true;
    }

    private static bool StreamingDescriptorsMatch(
        TerrainSurfaceMaskManifest manifest,
        IReadOnlyList<TerrainSurfaceStreamingLevelDescriptor> descriptors
    )
    {
        if (
            manifest == null
            || descriptors == null
            || manifest.StreamingLevelCount != descriptors.Count
        )
        {
            return false;
        }

        for (
            int index = 0;
            index < descriptors.Count;
            index++
        )
        {
            TerrainSurfaceStreamingLevelDescriptor expected =
                descriptors[index];

            if (
                !manifest.TryGetStreamingLevelDescriptor(
                    expected.SampleStride,
                    out TerrainSurfaceStreamingLevelDescriptor current
                )
                || current.SampleStride != expected.SampleStride
                || current.SamplesPerSide != expected.SamplesPerSide
                || current.TileGridWidth != expected.TileGridWidth
                || current.TileGridHeight != expected.TileGridHeight
                || current.TextureFormat != expected.TextureFormat
                || !Mathf.Approximately(
                    current.SampleSpacing,
                    expected.SampleSpacing
                )
                || !Mathf.Approximately(
                    current.TileWorldSize,
                    expected.TileWorldSize
                )
            )
            {
                return false;
            }
        }

        return true;
    }
}
