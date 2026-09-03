using UnityEngine;

public enum TerrainAnalysisDependencyRadiusMode
{
    NativeSampleSpacing = 0,
    AnalysisScale = 1
}

public enum TerrainAnalysisVisualizationKind
{
    Unsigned = 0,
    Signed = 1
}

/*
 * Small immutable descriptor for one terrain-analysis type.
 *
 * Core services consume this metadata rather than adding a new switch
 * statement every time an analysis type is introduced.
 */
public sealed class TerrainAnalysisDefinition
{
    public TerrainAnalysisType Type
    {
        get;
    }

    public string DisplayName
    {
        get;
    }

    public bool RequiresScale
    {
        get;
    }

    public string ComputeKernelName
    {
        get;
    }

    public TerrainAnalysisDependencyRadiusMode
        DependencyRadiusMode
    {
        get;
    }

    public TerrainAnalysisVisualizationKind
        VisualizationKind
    {
        get;
    }

    public float VisualizationMinimum
    {
        get;
    }

    public float VisualizationMaximum
    {
        get;
    }

    /*
     * When > 0, the visualization maximum is derived from:
     *
     *     key.ScaleMeters * VisualizationMaximumScaleMultiplier
     *
     * This is useful for metre-valued scale-dependent fields such as
     * Local Relief.
     */
    public float VisualizationMaximumScaleMultiplier
    {
        get;
    }

    public TerrainAnalysisDefinition(
        TerrainAnalysisType type,
        string displayName,
        bool requiresScale,
        string computeKernelName,
        TerrainAnalysisDependencyRadiusMode dependencyRadiusMode,
        TerrainAnalysisVisualizationKind visualizationKind,
        float visualizationMinimum,
        float visualizationMaximum,
        float visualizationMaximumScaleMultiplier = 0f
    )
    {
        Type =
            type;

        DisplayName =
            string.IsNullOrEmpty(
                displayName
            )
                ? type.ToString()
                : displayName;

        RequiresScale =
            requiresScale;

        ComputeKernelName =
            computeKernelName ?? "";

        DependencyRadiusMode =
            dependencyRadiusMode;

        VisualizationKind =
            visualizationKind;

        VisualizationMinimum =
            visualizationMinimum;

        VisualizationMaximum =
            visualizationMaximum;

        VisualizationMaximumScaleMultiplier =
            Mathf.Max(
                0f,
                visualizationMaximumScaleMultiplier
            );
    }

    public bool TryValidateKey(
        TerrainAnalysisKey key,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (key.Type != Type)
        {
            errorMessage =
                "Terrain analysis key type does not match the requested definition.";

            return false;
        }

        if (
            RequiresScale
            &&
            !key.HasScale
        )
        {
            errorMessage =
                DisplayName +
                " analysis requires a world-space scale.";

            return false;
        }

        if (
            !RequiresScale
            &&
            key.HasScale
        )
        {
            errorMessage =
                DisplayName +
                " analysis is scale-independent and must not specify a scale.";

            return false;
        }

        return true;
    }

    public bool TryGetDependencyRadiusMeters(
        TerrainAnalysisKey key,
        float sampleSpacing,
        out float dependencyRadiusMeters
    )
    {
        dependencyRadiusMeters =
            0f;

        if (
            !TryValidateKey(
                key,
                out _
            )
        )
        {
            return false;
        }

        float safeSampleSpacing =
            Mathf.Max(
                0.000001f,
                sampleSpacing
            );

        switch (DependencyRadiusMode)
        {
            case TerrainAnalysisDependencyRadiusMode
                .NativeSampleSpacing:
            {
                dependencyRadiusMeters =
                    safeSampleSpacing;

                return true;
            }

            case TerrainAnalysisDependencyRadiusMode
                .AnalysisScale:
            {
                dependencyRadiusMeters =
                    Mathf.Max(
                        key.ScaleMeters,
                        safeSampleSpacing
                    );

                return true;
            }

            default:
                return false;
        }
    }

    public void GetVisualizationRange(
        TerrainAnalysisKey key,
        out float minimum,
        out float maximum
    )
    {
        minimum =
            VisualizationMinimum;

        maximum =
            VisualizationMaximum;

        if (
            VisualizationMaximumScaleMultiplier >
                0f
            &&
            key.HasScale
        )
        {
            maximum =
                Mathf.Max(
                    minimum +
                        0.0001f,
                    key.ScaleMeters *
                        VisualizationMaximumScaleMultiplier
                );
        }

        if (
            VisualizationKind ==
                TerrainAnalysisVisualizationKind.Signed
        )
        {
            float magnitude =
                Mathf.Max(
                    Mathf.Abs(
                        minimum
                    ),
                    Mathf.Abs(
                        maximum
                    )
                );

            magnitude =
                Mathf.Max(
                    magnitude,
                    0.0001f
                );

            minimum =
                -magnitude;

            maximum =
                magnitude;
        }
        else if (
            maximum <=
            minimum
        )
        {
            maximum =
                minimum +
                0.0001f;
        }
    }
}
