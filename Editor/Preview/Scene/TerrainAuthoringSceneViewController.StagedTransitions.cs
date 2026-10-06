using UnityEngine;

public static partial class TerrainAuthoringSceneViewController
{
    internal sealed class DisplayPlacementSnapshot
    {
        internal TerrainClipmapLayout Layout;
        internal Transform[] Transforms;
        internal Vector3[] Positions;
    }

    internal static bool TryPreflightDisplayPlacement(TerrainAuthoringPreviewDisplayIntent intent,
        System.Collections.Generic.List<TerrainClipmapRendererBinding> renderers, out string error)
    {
        error = "";
        if (intent == null || intent.Root == null || intent.Settings == null
            || intent.OwnershipGeneration != SceneViewOwnershipGeneration
            || !TryEnsureConfiguration(out error) || clipmapRoot != intent.Root || worldSettings != intent.Settings)
        { if (string.IsNullOrEmpty(error)) error = "The display placement owner or hierarchy changed."; return false; }
        return layoutApplier.TryPreflight(intent.Layout, out error)
            && layoutApplier.TryGetRendererBindings(renderers, out error);
    }

    internal static DisplayPlacementSnapshot BeginDisplayPlacement()
    {
        var transforms = clipmapRoot.GetComponentsInChildren<Transform>(true);
        var snapshot = new DisplayPlacementSnapshot
        {
            Layout = appliedLayout.CreateSnapshot(), Transforms = transforms,
            Positions = new Vector3[transforms.Length]
        };
        for (int i = 0; i < transforms.Length; i++) snapshot.Positions[i] = transforms[i].localPosition;
        isApplyingPlacement = true;
        return snapshot;
    }

    internal static bool TryApplyDisplayPlacement(TerrainAuthoringPreviewDisplayIntent intent, out string error)
    {
        error = "";
        return isApplyingPlacement && layoutApplier.TryApply(intent.Layout, out error);
    }

    internal static void CommitDisplayPlacement(TerrainAuthoringPreviewDisplayIntent intent)
    {
        appliedLayout = intent.Layout.CreateSnapshot();
        hasLastAppliedLOD0Anchor = appliedLayout.TryGetLOD(0, out lastAppliedLOD0Anchor, out _);
    }

    internal static bool RestoreDisplayPlacement(DisplayPlacementSnapshot snapshot)
    {
        if (snapshot == null) return true;
        bool restored = true;
        for (int i = 0; i < snapshot.Transforms.Length; i++)
        {
            if (snapshot.Transforms[i] == null) { restored = false; continue; }
            snapshot.Transforms[i].localPosition = snapshot.Positions[i];
        }
        if (snapshot.Layout.IsValid) restored &= layoutApplier.TryApply(snapshot.Layout, out _);
        else layoutApplier.InvalidateAppliedBounds();
        appliedLayout = snapshot.Layout.CreateSnapshot();
        hasLastAppliedLOD0Anchor = appliedLayout.TryGetLOD(0, out lastAppliedLOD0Anchor, out _);
        return restored;
    }

    internal static void EndDisplayPlacement() { isApplyingPlacement = false; }

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
         * A size-recovery transition may fail even though the
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
