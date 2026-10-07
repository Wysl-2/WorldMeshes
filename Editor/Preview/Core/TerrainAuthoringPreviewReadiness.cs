using UnityEngine;

public enum TerrainAuthoringPreviewReadiness
{
    Ready,
    Loading,
    OutsideWorld,
    PreviewUnavailable
}

public enum TerrainAuthoringPreviewInteractionReadiness
{
    Ready,
    Updating,
    Loading,
    OutsideWorld,
    PreviewUnavailable
}

internal static class TerrainAuthoringPreviewReadinessPolicy
{
    internal static bool IsInteractionAllowed(
        TerrainAuthoringPreviewInteractionReadiness readiness
    ) => readiness == TerrainAuthoringPreviewInteractionReadiness.Ready
        || readiness == TerrainAuthoringPreviewInteractionReadiness.Updating;

    internal static TerrainAuthoringPreviewInteractionReadiness EvaluateInteractionTile(
        bool previewAvailable,
        bool insideWorld,
        bool hasActiveCache,
        bool committedSourceCurrent,
        bool resident,
        bool finalCompositeReady,
        bool pendingResidentDirty
    )
    {
        if (!insideWorld) return TerrainAuthoringPreviewInteractionReadiness.OutsideWorld;
        if (!previewAvailable) return TerrainAuthoringPreviewInteractionReadiness.PreviewUnavailable;
        if (!hasActiveCache || !committedSourceCurrent || !resident || !finalCompositeReady)
            return TerrainAuthoringPreviewInteractionReadiness.Loading;
        return pendingResidentDirty ? TerrainAuthoringPreviewInteractionReadiness.Updating
            : TerrainAuthoringPreviewInteractionReadiness.Ready;
    }

    internal static TerrainAuthoringPreviewReadiness EvaluateTile(
        bool previewAvailable,
        bool insideWorld,
        bool hasActiveCache,
        bool committedSourceCurrent,
        bool resident,
        bool finalCompositeReady,
        bool pendingResidentDirty
    )
    {
        if (!insideWorld)
        {
            return
                TerrainAuthoringPreviewReadiness.OutsideWorld;
        }

        if (!previewAvailable)
        {
            return
                TerrainAuthoringPreviewReadiness.PreviewUnavailable;
        }

        if (
            !hasActiveCache
            ||
            !committedSourceCurrent
            ||
            !resident
            ||
            !finalCompositeReady
            ||
            pendingResidentDirty
        )
        {
            return
                TerrainAuthoringPreviewReadiness.Loading;
        }

        return
            TerrainAuthoringPreviewReadiness.Ready;
    }
}

/*
 * Preview readiness keeps blocked-edit wording beside the readiness interpretation
 * so authoring tools do not invent different meanings for the same state.
 */
internal static class TerrainAuthoringPreviewReadinessFeedback
{
    internal static string GetMessage(TerrainAuthoringPreviewInteractionReadiness readiness)
    {
        switch (readiness)
        {
            case TerrainAuthoringPreviewInteractionReadiness.Ready:
            case TerrainAuthoringPreviewInteractionReadiness.Updating:
                return "";
            case TerrainAuthoringPreviewInteractionReadiness.Loading:
                return "Usable resident terrain for this modifier is not available yet.";
            case TerrainAuthoringPreviewInteractionReadiness.OutsideWorld:
                return "This modifier does not currently overlap editable terrain.";
            default:
                return "Terrain Height Preview is unavailable. Surface-dependent editing requires a stable authoring preview.";
        }
    }

    internal static string GetMessage(
        TerrainAuthoringPreviewReadiness readiness
    )
    {
        switch (readiness)
        {
            case TerrainAuthoringPreviewReadiness.Ready:
                return "";

            case TerrainAuthoringPreviewReadiness.Loading:
                return
                    "Terrain surface for this modifier is still loading.";

            case TerrainAuthoringPreviewReadiness.OutsideWorld:
                return
                    "This modifier does not currently overlap editable terrain.";

            default:
                return
                    "Terrain Height Preview is unavailable. " +
                    "Surface-dependent editing requires a ready authoring preview.";
        }
    }
}

