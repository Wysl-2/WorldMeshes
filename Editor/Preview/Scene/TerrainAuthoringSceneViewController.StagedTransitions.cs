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
