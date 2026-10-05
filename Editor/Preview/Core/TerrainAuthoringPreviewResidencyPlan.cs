using UnityEngine;

/*
 * Side-effect-free edit-mode multiresolution Height residency intent.
 *
 * These objects contain only representation identity and bounded tile-window
 * planning state. They never own GPU resources or streaming execution state.
 */
internal sealed class TerrainAuthoringPreviewLodResidencyPlan
{
    public int Level;
    public int SampleStride;
    public int SamplesPerSide;
    public float SampleSpacing;
    public Vector3 Anchor;
    public Vector2 RequiredMinimumXZ;
    public Vector2 RequiredMaximumXZ;
    public TerrainHeightCacheWindow RequiredWindow;
    public TerrainHeightCacheWindow DesiredWindow;

    public bool IsStructurallyValid
    {
        get
        {
            return
                Level >= 0
                &&
                SampleStride > 0
                &&
                TerrainHeightResolutionUtility
                    .IsPowerOfTwo(
                        SampleStride
                    )
                &&
                SamplesPerSide > 1
                &&
                IsFinite(
                    SampleSpacing
                )
                &&
                SampleSpacing > 0f
                &&
                RequiredWindow.IsValid
                &&
                DesiredWindow.IsValid
                &&
                DesiredWindow.Contains(
                    RequiredWindow
                );
        }
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

internal sealed class TerrainAuthoringPreviewResidencyPlan
{
    public int Generation;
    public int LevelCount;
    public Vector2 MinimumXZ;
    public Vector2 MaximumXZ;
    public Vector3 CoverageCenter;
    public TerrainAuthoringPreviewLodResidencyPlan[] Levels;

    public bool IsStructurallyValid
    {
        get
        {
            if (
                LevelCount <= 0
                ||
                Levels == null
                ||
                Levels.Length !=
                    LevelCount
            )
            {
                return false;
            }

            for (
                int level = 0;
                level < LevelCount;
                level++
            )
            {
                TerrainAuthoringPreviewLodResidencyPlan levelPlan =
                    Levels[level];

                if (
                    levelPlan == null
                    ||
                    levelPlan.Level !=
                        level
                    ||
                    !levelPlan.IsStructurallyValid
                )
                {
                    return false;
                }
            }

            return true;
        }
    }
}
