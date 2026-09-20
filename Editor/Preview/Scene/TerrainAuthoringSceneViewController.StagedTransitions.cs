using UnityEngine;

public static partial class TerrainAuthoringSceneViewController
{
    private static void OnHeightCacheTransitionFailed(
        TerrainHeightCacheWindow failedWindow,
        string failureMessage
    )
    {
        if (
            Application.isPlaying
            ||
            UnityEditor.EditorApplication
                .isPlayingOrWillChangePlaymode
            ||
            suspendedForPlayMode
        )
        {
            return;
        }

        /*
         * A Package 03A size-recovery transition may fail even though the
         * previous active cache still safely covers the current clipmap.
         * Re-evaluate the current target instead of forcing an immediate
         * Scene View error. Coverage-critical failures naturally resolve to
         * Error on that re-evaluation because the old active cache cannot
         * satisfy the desired layout.
         */
        if (
            TerrainAuthoringPreviewService.CacheReady
            &&
            TerrainAuthoringPreviewService.Status ==
                TerrainAuthoringPreviewStatus.Ready
        )
        {
            if (!FollowSceneView)
            {
                if (
                    !TryEnsureCanonicalResidency(
                        out bool waitingForResidency,
                        out string residencyError
                    )
                )
                {
                    SetStatus(
                        TerrainAuthoringSceneViewStatus.Error,
                        residencyError
                    );
                }
                else if (waitingForResidency)
                {
                    SetWaitingForHeightCacheStatus();
                }
                else
                {
                    RestoreCanonicalHierarchy(
                        true
                    );
                }
            }
            else
            {
                RequestReapply();
            }

            RepaintEditorViews();

            return;
        }

        SetStatus(
            TerrainAuthoringSceneViewStatus.Error,
            "The requested height-cache transition failed. " +
            "The previous resident terrain preview remains active " +
            "where it is still valid.\n\n" +
            (
                string.IsNullOrEmpty(
                    failureMessage
                )
                    ? $"Failed target: {failedWindow}"
                    : failureMessage
            )
        );

        RepaintEditorViews();
    }
}
