using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/*
 * Package 08A ownership layer for Scene View driven terrain placement.
 *
 * SceneViewController owns only placement/residency intent. PreviewService
 * remains the sole owner of active/staging GPU resources. A Scene View owner
 * change invalidates ordinary navigation intent, while an explicit frozen
 * target remains independent of Scene View focus.
 */
public static partial class TerrainAuthoringSceneViewController
{
    private static SceneView controllingSceneView;

    private static int controllingSceneViewInstanceId;

    private static long sceneViewOwnershipGeneration;

    private static bool editorCallbacksRegistered;

    private static bool lifecycleShuttingDown;

    internal static bool HasControllingSceneView =>
        IsUsableSceneView(
            controllingSceneView
        );

    internal static int ControllingSceneViewInstanceId =>
        HasControllingSceneView
            ? controllingSceneViewInstanceId
            : 0;

    internal static long SceneViewOwnershipGeneration =>
        sceneViewOwnershipGeneration;

    private static void InitializeSceneViewLifecycle()
    {
        RegisterEditorCallbacks();

        RefreshControllingSceneViewOwnership(
            false
        );

        RequestReapply();
    }

    private static void RegisterEditorCallbacks()
    {
        if (editorCallbacksRegistered)
        {
            return;
        }

        editorCallbacksRegistered =
            true;

        layoutApplier.AppliedBoundsChanged +=
            OnAppliedBoundsChanged;

        TerrainAuthoringPreviewService
            .HeightCacheCoverageChanged +=
                OnHeightCacheCoverageChanged;

        TerrainAuthoringPreviewService
            .HeightCacheTransitionFailed +=
                OnHeightCacheTransitionFailed;

        TerrainAuthoringPreviewService
            .ResidencyIntentRefreshRequested +=
                OnResidencyIntentRefreshRequested;

        TerrainAuthoringPreviewService
            .PreviewStreamingUpdatePreparing +=
                OnPreviewStreamingUpdatePreparing;

        SceneView.duringSceneGui +=
            OnSceneViewGUI;

        EditorApplication.update +=
            OnSceneViewOwnershipUpdate;

        EditorApplication.playModeStateChanged +=
            OnPlayModeStateChanged;

        EditorApplication.hierarchyChanged +=
            OnHierarchyChanged;

        EditorApplication.projectChanged +=
            OnProjectChanged;

        Undo.undoRedoPerformed +=
            OnUndoRedo;

        EditorSceneManager.sceneSaving +=
            OnSceneSaving;

        EditorSceneManager.sceneSaved +=
            OnSceneSaved;

        EditorSceneManager.activeSceneChangedInEditMode +=
            OnActiveSceneChangedInEditMode;

        AssemblyReloadEvents.beforeAssemblyReload +=
            OnBeforeAssemblyReload;

        EditorApplication.quitting +=
            OnEditorQuitting;
    }

    private static void UnregisterEditorCallbacks()
    {
        if (!editorCallbacksRegistered)
        {
            return;
        }

        editorCallbacksRegistered =
            false;

        layoutApplier.AppliedBoundsChanged -=
            OnAppliedBoundsChanged;

        TerrainAuthoringPreviewService
            .HeightCacheCoverageChanged -=
                OnHeightCacheCoverageChanged;

        TerrainAuthoringPreviewService
            .HeightCacheTransitionFailed -=
                OnHeightCacheTransitionFailed;

        TerrainAuthoringPreviewService
            .ResidencyIntentRefreshRequested -=
                OnResidencyIntentRefreshRequested;

        TerrainAuthoringPreviewService
            .PreviewStreamingUpdatePreparing -=
                OnPreviewStreamingUpdatePreparing;

        SceneView.duringSceneGui -=
            OnSceneViewGUI;

        EditorApplication.update -=
            OnSceneViewOwnershipUpdate;

        EditorApplication.playModeStateChanged -=
            OnPlayModeStateChanged;

        EditorApplication.hierarchyChanged -=
            OnHierarchyChanged;

        EditorApplication.projectChanged -=
            OnProjectChanged;

        Undo.undoRedoPerformed -=
            OnUndoRedo;

        EditorSceneManager.sceneSaving -=
            OnSceneSaving;

        EditorSceneManager.sceneSaved -=
            OnSceneSaved;

        EditorSceneManager.activeSceneChangedInEditMode -=
            OnActiveSceneChangedInEditMode;

        AssemblyReloadEvents.beforeAssemblyReload -=
            OnBeforeAssemblyReload;

        EditorApplication.quitting -=
            OnEditorQuitting;

        EditorApplication.delayCall -=
            ExecuteScheduledReapply;

        reapplyScheduled =
            false;
    }

    private static bool IsUsableSceneView(
        SceneView sceneView
    )
    {
        return
            sceneView != null
            &&
            sceneView.camera != null;
    }

    private static SceneView ResolvePreferredSceneView()
    {
        SceneView lastActive =
            SceneView.lastActiveSceneView;

        if (
            IsUsableSceneView(
                lastActive
            )
        )
        {
            return
                lastActive;
        }

        if (
            IsUsableSceneView(
                controllingSceneView
            )
        )
        {
            return
                controllingSceneView;
        }

        return
            null;
    }

    private static void OnSceneViewOwnershipUpdate()
    {
        if (
            lifecycleShuttingDown
            ||
            suspendedForPlayMode
            ||
            !TerrainAuthoringPreviewService
                .IsEditorLifecycleStable
        )
        {
            return;
        }

        RefreshControllingSceneViewOwnership(
            true
        );
    }

    private static bool RefreshControllingSceneViewOwnership(
        bool requestReapply
    )
    {
        return
            SetControllingSceneView(
                ResolvePreferredSceneView(),
                requestReapply
            );
    }

    private static bool SetControllingSceneView(
        SceneView sceneView,
        bool requestReapply
    )
    {
        bool usable =
            IsUsableSceneView(
                sceneView
            );

        int nextInstanceId =
            usable
                ? sceneView.GetInstanceID()
                : 0;

        if (
            nextInstanceId ==
                controllingSceneViewInstanceId
            &&
            (
                nextInstanceId == 0
                ||
                IsUsableSceneView(
                    controllingSceneView
                )
            )
        )
        {
            if (usable)
            {
                controllingSceneView =
                    sceneView;
            }

            return false;
        }

        controllingSceneView =
            usable
                ? sceneView
                : null;

        controllingSceneViewInstanceId =
            nextInstanceId;

        sceneViewOwnershipGeneration =
            CalculateNextSceneViewOwnershipGeneration(
                sceneViewOwnershipGeneration
            );

        /*
         * lastFollowTarget belongs to the previous Scene View owner. Never let
         * a later cache-coverage callback resurrect that destination.
         */
        hasLastFollowTarget =
            false;

        bool frozenTargetOwnsIntent =
            freezePreview
            &&
            hasFrozenTarget;

        if (
            FollowSceneView
            &&
            !frozenTargetOwnsIntent
        )
        {
            TerrainAuthoringPreviewService
                .ClearTransientResidencyIntent(
                    nextInstanceId == 0
                        ? "The controlling Scene View was closed."
                        : "The controlling Scene View changed."
                );
        }

        if (
            requestReapply
            &&
            FollowSceneView
            &&
            !frozenTargetOwnsIntent
        )
        {
            if (nextInstanceId != 0)
            {
                RequestReapply();
            }
            else
            {
                SetStatus(
                    TerrainAuthoringSceneViewStatus.SceneViewUnavailable,
                    "No active Scene View is available for clipmap following."
                );

                RepaintEditorViews();
            }
        }

        return true;
    }

    private static long CalculateNextSceneViewOwnershipGeneration(
        long currentGeneration
    )
    {
        return
            currentGeneration < long.MaxValue
                ? currentGeneration + 1L
                : 1L;
    }

    private static bool TryUseAsControllingSceneView(
        SceneView sceneView
    )
    {
        if (
            lifecycleShuttingDown
            ||
            !IsUsableSceneView(
                sceneView
            )
        )
        {
            return false;
        }

        SceneView preferred =
            ResolvePreferredSceneView();

        if (
            preferred != null
            &&
            preferred != sceneView
        )
        {
            return false;
        }

        if (preferred == null)
        {
            preferred =
                sceneView;
        }

        SetControllingSceneView(
            preferred,
            false
        );

        return
            controllingSceneViewInstanceId ==
                sceneView.GetInstanceID();
    }

    private static bool TryGetControllingSceneView(
        out SceneView sceneView
    )
    {
        sceneView =
            IsUsableSceneView(
                controllingSceneView
            )
                ? controllingSceneView
                : null;

        return
            sceneView != null;
    }

    private static void OnPreviewStreamingUpdatePreparing()
    {
        if (
            lifecycleShuttingDown
            ||
            suspendedForPlayMode
            ||
            !TerrainAuthoringPreviewService
                .IsEditorLifecycleStable
        )
        {
            return;
        }

        RefreshControllingSceneViewOwnership(
            true
        );
    }

    private static void OnResidencyIntentRefreshRequested()
    {
        if (
            lifecycleShuttingDown
            ||
            suspendedForPlayMode
        )
        {
            return;
        }

        RefreshControllingSceneViewOwnership(
            false
        );

        RequestReapply();
    }

    private static void HandleActiveSceneOwnershipChanged()
    {
        /*
         * Frozen world-space intent belongs to the previous active scene.
         * Scene replacement therefore differs from merely switching Scene
         * View windows: the target must be rediscovered for the new scene.
         */
        freezePreview =
            false;

        hasFrozenTarget =
            false;

        frozenTarget =
            Vector3.zero;

        TerrainAuthoringPreviewService
            .ClearTransientResidencyIntent(
                "The active edit-mode scene changed."
            );

        RefreshControllingSceneViewOwnership(
            false
        );
    }

    private static void ShutdownSceneViewLifecycle()
    {
        if (lifecycleShuttingDown)
        {
            return;
        }

        lifecycleShuttingDown =
            true;

        /*
         * Unregister first so restoring canonical transforms cannot cause this
         * controller to schedule new hierarchy work during domain teardown.
         */
        UnregisterEditorCallbacks();

        RestoreCanonicalHierarchy(
            false
        );

        controllingSceneView =
            null;

        controllingSceneViewInstanceId =
            0;

        hasLastFollowTarget =
            false;

        hasLastAppliedLOD0Anchor =
            false;
    }
}
