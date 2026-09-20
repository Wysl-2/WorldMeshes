using UnityEngine;

public enum TerrainAuthoringPreviewReadiness
{
    Ready,
    Loading,
    OutsideWorld,
    PreviewUnavailable
}

internal static class TerrainAuthoringPreviewReadinessPolicy
{
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
