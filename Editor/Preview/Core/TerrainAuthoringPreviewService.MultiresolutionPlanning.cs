/*
 * Transient multiresolution planning state for edit-mode Height preview.
 *
 * This partial does not allocate caches or start streaming. It records the
 * latest exact per-LOD residency intent so later streaming stages can consume
 * a stable, generation-aware plan.
 */
public static partial class TerrainAuthoringPreviewService
{
    private static TerrainAuthoringPreviewResidencyPlan
        latestMultiresolutionResidencyPlan;

    private static int
        nextMultiresolutionResidencyGeneration =
            1;

    private static string
        lastMultiresolutionPlanningError =
            "";

    internal static int
        LatestMultiresolutionResidencyGeneration =>
            latestMultiresolutionResidencyPlan != null
                ? latestMultiresolutionResidencyPlan.Generation
                : 0;

    internal static string
        LastMultiresolutionPlanningError =>
            lastMultiresolutionPlanningError;

    internal static bool TryGetLatestMultiresolutionResidencyPlan(
        out TerrainAuthoringPreviewResidencyPlan plan
    )
    {
        plan =
            latestMultiresolutionResidencyPlan;

        return
            plan != null
            &&
            plan.IsStructurallyValid;
    }

    internal static bool RecordMultiresolutionResidencyIntent(
        WorldSettings worldSettings,
        TerrainClipmapLayout layout,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        int candidateGeneration =
            nextMultiresolutionResidencyGeneration;

        if (
            !TerrainAuthoringPreviewLodResidencyUtility
                .TryBuildPlan(
                    worldSettings,
                    layout,
                    candidateGeneration,
                    out TerrainAuthoringPreviewResidencyPlan candidate,
                    out errorMessage
                )
        )
        {
            lastMultiresolutionPlanningError =
                errorMessage;

            return false;
        }

        bool equivalent =
            TerrainAuthoringPreviewStreamingPolicy
                .AreMultiresolutionResidencyPlansEquivalent(
                    latestMultiresolutionResidencyPlan,
                    candidate
                );

        if (
            equivalent
            &&
            latestMultiresolutionResidencyPlan != null
        )
        {
            candidate.Generation =
                latestMultiresolutionResidencyPlan.Generation;
        }
        else
        {
            if (
                nextMultiresolutionResidencyGeneration ==
                    int.MaxValue
            )
            {
                nextMultiresolutionResidencyGeneration =
                    1;
            }
            else
            {
                nextMultiresolutionResidencyGeneration++;
            }
        }

        latestMultiresolutionResidencyPlan =
            candidate;

        lastMultiresolutionPlanningError =
            "";

        return true;
    }

    internal static void ClearMultiresolutionResidencyIntent()
    {
        latestMultiresolutionResidencyPlan =
            null;

        lastMultiresolutionPlanningError =
            "";
    }
}
