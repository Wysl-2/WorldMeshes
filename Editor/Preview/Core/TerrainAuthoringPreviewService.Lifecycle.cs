using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[Flags]
internal enum TerrainAuthoringPreviewSuspensionReason
{
    None = 0,
    Compiling = 1 << 0,
    AssetDatabaseUpdating = 1 << 1,
    PlayMode = 1 << 2,
    AssemblyReload = 1 << 3,
    EditorQuitting = 1 << 4
}

/*
 * Package 08A lifecycle coordination for the edit-mode preview.
 *
 * Temporary editor instability pauses work but keeps healthy active/staging
 * resources alive. Ownership boundaries (Play Mode, active-scene replacement,
 * preview disable, assembly reload, and editor shutdown) release edit-mode GPU
 * resources through the existing PreviewService cache/binding paths.
 *
 * SceneViewController owns placement/residency intent. PreviewService owns the
 * resources. After a temporary suspension, intent is requested again before
 * streaming is allowed to continue so an obsolete Scene View destination
 * cannot resume blindly.
 */
public static partial class TerrainAuthoringPreviewService
{
    internal static event Action
        ResidencyIntentRefreshRequested;

    /*
     * Lets the Scene View owner synchronize its lightweight ownership state
     * immediately before PreviewService advances staging. This prevents an old
     * Scene View transaction from activating for one final update after focus
     * has already moved to another Scene View.
     */
    internal static event Action
        PreviewStreamingUpdatePreparing;

    private static bool lifecycleCallbacksRegistered;

    private static TerrainAuthoringPreviewSuspensionReason
        suspensionReasons =
            TerrainAuthoringPreviewSuspensionReason.None;

    private static bool lifecycleResumePending;

    private static bool lifecycleResumeScheduled;

    private const TerrainAuthoringPreviewSuspensionReason
        TemporarySuspensionMask =
            TerrainAuthoringPreviewSuspensionReason.Compiling
            |
            TerrainAuthoringPreviewSuspensionReason.AssetDatabaseUpdating;

    private const TerrainAuthoringPreviewSuspensionReason
        OwnershipSuspensionMask =
            TerrainAuthoringPreviewSuspensionReason.PlayMode
            |
            TerrainAuthoringPreviewSuspensionReason.AssemblyReload
            |
            TerrainAuthoringPreviewSuspensionReason.EditorQuitting;

    internal static TerrainAuthoringPreviewSuspensionReason
        LifecycleSuspensionReasons =>
            suspensionReasons;

    internal static bool LifecycleResumePending =>
        lifecycleResumePending;

    internal static bool HasLifecycleActiveCache =>
        activeCache != null
        &&
        activeCache.IsReady;

    internal static bool HasLifecycleStagingCache =>
        stagingCache != null;

    /*
     * Scene View placement can still operate when Height Preview is disabled,
     * so lifecycle stability is intentionally separate from Enabled.
     */
    internal static bool IsEditorLifecycleStable
    {
        get
        {
            return
                suspensionReasons ==
                    TerrainAuthoringPreviewSuspensionReason.None
                &&
                !lifecycleResumePending
                &&
                !Application.isPlaying
                &&
                !EditorApplication
                    .isPlayingOrWillChangePlaymode
                &&
                !EditorApplication.isCompiling
                &&
                !EditorApplication.isUpdating;
        }
    }

    internal static bool CanRunEditorPreviewWork =>
        Enabled
        &&
        IsEditorLifecycleStable;

    private static void InitializePreviewLifecycle()
    {
        RegisterEditorCallbacks();

        RefreshTransientSuspensionState();

        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            SetLifecycleSuspensionReason(
                TerrainAuthoringPreviewSuspensionReason.PlayMode,
                true
            );

            SetStatus(
                TerrainAuthoringPreviewStatus.PlayMode,
                "Runtime owns terrain height-cache residency while Play Mode is active."
            );

            return;
        }

        NotifyCommittedHeightfieldChanged();
    }

    private static void RegisterEditorCallbacks()
    {
        if (lifecycleCallbacksRegistered)
        {
            return;
        }

        lifecycleCallbacksRegistered =
            true;

        EditorApplication.playModeStateChanged +=
            OnPlayModeStateChanged;

        EditorApplication.hierarchyChanged +=
            OnHierarchyChanged;

        EditorApplication.projectChanged +=
            OnProjectChanged;

        Undo.undoRedoPerformed +=
            OnUndoRedo;

        EditorSceneManager.activeSceneChangedInEditMode +=
            OnPreviewActiveSceneChangedInEditMode;

        AssemblyReloadEvents.beforeAssemblyReload +=
            OnBeforeAssemblyReload;

        EditorApplication.quitting +=
            OnEditorQuitting;

        /*
         * Package 08A owns the update registration so temporary lifecycle
         * state is refreshed before incremental streaming is advanced.
         */
        EditorApplication.update +=
            OnPreviewLifecycleEditorUpdate;
    }

    private static void UnregisterEditorCallbacks()
    {
        if (!lifecycleCallbacksRegistered)
        {
            return;
        }

        lifecycleCallbacksRegistered =
            false;

        EditorApplication.playModeStateChanged -=
            OnPlayModeStateChanged;

        EditorApplication.hierarchyChanged -=
            OnHierarchyChanged;

        EditorApplication.projectChanged -=
            OnProjectChanged;

        Undo.undoRedoPerformed -=
            OnUndoRedo;

        EditorSceneManager.activeSceneChangedInEditMode -=
            OnPreviewActiveSceneChangedInEditMode;

        AssemblyReloadEvents.beforeAssemblyReload -=
            OnBeforeAssemblyReload;

        EditorApplication.quitting -=
            OnEditorQuitting;

        EditorApplication.update -=
            OnPreviewLifecycleEditorUpdate;

        EditorApplication.delayCall -=
            ExecuteScheduledRefresh;

        CancelLifecycleResumeSchedule();

        refreshScheduled =
            false;
    }

    private static void OnPreviewLifecycleEditorUpdate()
    {
        RefreshTransientSuspensionState();

        PreviewStreamingUpdatePreparing?.Invoke();

        OnStreamingEditorUpdate();
    }

    internal static void RefreshTransientSuspensionState()
    {
        bool wasTemporarilySuspended =
            HasAnySuspension(
                TemporarySuspensionMask
            );

        SetLifecycleSuspensionReason(
            TerrainAuthoringPreviewSuspensionReason.Compiling,
            EditorApplication.isCompiling
        );

        SetLifecycleSuspensionReason(
            TerrainAuthoringPreviewSuspensionReason.AssetDatabaseUpdating,
            EditorApplication.isUpdating
        );

        bool temporarilySuspended =
            HasAnySuspension(
                TemporarySuspensionMask
            );

        if (temporarilySuspended)
        {
            /*
             * Preserve active/staging resources. Only execution is suspended.
             * Current Scene View intent must be refreshed before work resumes.
             */
            lifecycleResumePending =
                true;

            CancelLifecycleResumeSchedule();

            return;
        }

        if (
            wasTemporarilySuspended
            &&
            lifecycleResumePending
            &&
            !HasAnySuspension(
                OwnershipSuspensionMask
            )
        )
        {
            ScheduleLifecycleResume();
        }
    }

    private static void SetLifecycleSuspensionReason(
        TerrainAuthoringPreviewSuspensionReason reason,
        bool active
    )
    {
        if (active)
        {
            suspensionReasons |=
                reason;
        }
        else
        {
            suspensionReasons &=
                ~reason;
        }
    }

    private static bool HasAnySuspension(
        TerrainAuthoringPreviewSuspensionReason mask
    )
    {
        return
            (suspensionReasons & mask) !=
                TerrainAuthoringPreviewSuspensionReason.None;
    }

    internal static void RequestLifecycleResumeFromCurrentState()
    {
        if (
            HasAnySuspension(
                TerrainAuthoringPreviewSuspensionReason.AssemblyReload
                |
                TerrainAuthoringPreviewSuspensionReason.EditorQuitting
            )
        )
        {
            return;
        }

        lifecycleResumePending =
            true;

        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            SetLifecycleSuspensionReason(
                TerrainAuthoringPreviewSuspensionReason.PlayMode,
                true
            );

            CancelLifecycleResumeSchedule();

            return;
        }

        RefreshTransientSuspensionState();

        if (
            !HasAnySuspension(
                TemporarySuspensionMask
                |
                OwnershipSuspensionMask
            )
        )
        {
            ScheduleLifecycleResume();
        }
    }

    private static void ScheduleLifecycleResume()
    {
        if (
            lifecycleResumeScheduled
            ||
            !lifecycleResumePending
            ||
            HasAnySuspension(
                TemporarySuspensionMask
                |
                OwnershipSuspensionMask
            )
        )
        {
            return;
        }

        lifecycleResumeScheduled =
            true;

        EditorApplication.delayCall +=
            ExecuteLifecycleResume;
    }

    private static void CancelLifecycleResumeSchedule()
    {
        if (!lifecycleResumeScheduled)
        {
            return;
        }

        EditorApplication.delayCall -=
            ExecuteLifecycleResume;

        lifecycleResumeScheduled =
            false;
    }

    private static void ExecuteLifecycleResume()
    {
        lifecycleResumeScheduled =
            false;

        RefreshTransientSuspensionState();

        if (
            HasAnySuspension(
                TerrainAuthoringPreviewSuspensionReason.AssemblyReload
                |
                TerrainAuthoringPreviewSuspensionReason.EditorQuitting
            )
        )
        {
            return;
        }

        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
            ||
            HasAnySuspension(
                TerrainAuthoringPreviewSuspensionReason.PlayMode
            )
        )
        {
            return;
        }

        if (
            HasAnySuspension(
                TemporarySuspensionMask
            )
            ||
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            lifecycleResumePending =
                true;

            return;
        }

        /*
         * RefreshTransientSuspensionState can observe the exact transition
         * from temporary suspension to stable state while this callback is
         * already executing. Remove any redundant resume callback it queued.
         */
        CancelLifecycleResumeSchedule();

        /*
         * Remove the resume barrier before asking the Scene View controller
         * for current intent. The controller can now safely calculate a fresh
         * residency request. Preview refresh is queued afterward.
         */
        lifecycleResumePending =
            false;

        ResidencyIntentRefreshRequested?.Invoke();

        if (Enabled)
        {
            ScheduleRefresh();
        }
    }

    private static void HandlePreviewEnabledChanged(
        bool enabled
    )
    {
        if (!enabled)
        {
            lifecycleResumePending =
                false;

            CancelLifecycleResumeSchedule();

            ReleaseEditModePreviewResources(
                "Terrain authoring preview was disabled.",
                true
            );

            SetStatus(
                TerrainAuthoringPreviewStatus.Disabled,
                "Terrain authoring preview is disabled."
            );

            RepaintEditorViews();

            return;
        }

        /*
         * Enabling never revives old residency. The current Scene View,
         * frozen target, or canonical placement will establish fresh intent.
         */
        committedRebuildRequested =
            true;

        clipmapRebindRequested =
            true;

        overallSignatureAcknowledgementRequested =
            false;

        ClearTransitionFailureSuppression();

        RequestLifecycleResumeFromCurrentState();
    }

    private static void ReleaseEditModePreviewResources(
        string reason,
        bool notifyObservers
    )
    {
        EditorApplication.delayCall -=
            ExecuteScheduledRefresh;

        refreshScheduled =
            false;

        ClearRequestedResidency();

        ReleaseBinding();

        ReleaseAllPreviewCaches(
            notifyObservers
        );

        heightCompositor.Dispose();

        dirtyCompositeTiles.Clear();

        overallSignatureAcknowledgementRequested =
            false;
    }

    internal static void ClearTransientResidencyIntent(
        string reason
    )
    {
        string cancellationReason =
            string.IsNullOrEmpty(
                reason
            )
                ? "Transient Scene View residency intent was cleared."
                : reason;

        bool hadIntent =
            hasLatestRequiredResidencyWindow
            ||
            hasDesiredResidencyWindow
            ||
            hasRequestedResidencyWindow;

        bool hadStreamingWork =
            hasPendingStreamingStart
            ||
            (
                currentTransition != null
                &&
                TransitionInProgress
            );

        bool preserveCommittedRebuild =
            committedRebuildRequested
            ||
            (
                hasPendingStreamingStart
                &&
                pendingStreamingCommittedRebuild
            )
            ||
            (
                currentTransition != null
                &&
                currentTransition
                    .CommittedRebuildRequested
            );

        if (hadStreamingWork)
        {
            CancelCurrentStreamingTransition(
                cancellationReason,
                false
            );
        }
        else
        {
            lastStreamingCancellationReason =
                cancellationReason;
        }

        /*
         * Failed/cancelled transition diagnostics describe the old owner's
         * destination and must not become authoritative after intent changes.
         */
        if (
            currentTransition != null
            &&
            !TransitionInProgress
        )
        {
            currentTransition =
                null;
        }

        hasLatestRequiredResidencyWindow =
            false;

        latestRequiredResidencyWindow =
            default;

        ClearDesiredResidency();
        ClearRequestedResidency();
        ClearTransitionFailureSuppression();

        streamingProgress =
            0f;

        if (preserveCommittedRebuild)
        {
            committedRebuildRequested =
                true;
        }

        if (
            hadIntent
            ||
            hadStreamingWork
        )
        {
            streamingRequestGeneration++;
        }

        lastStreamingFailureMessage =
            "";

        if (
            streamingState ==
                TerrainAuthoringPreviewStreamingState.Failed
        )
        {
            SetStreamingState(
                TerrainAuthoringPreviewStreamingState.Idle,
                cancellationReason
            );
        }
        else
        {
            lastPublishedStreamingSnapshot =
                "";

            PublishStreamingStateIfChanged();
        }
    }

    private static void HandlePreviewPlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
            {
                SetLifecycleSuspensionReason(
                    TerrainAuthoringPreviewSuspensionReason.PlayMode,
                    true
                );

                lifecycleResumePending =
                    false;

                CancelLifecycleResumeSchedule();

                ReleaseEditModePreviewResources(
                    "Entering Play Mode released edit-mode preview ownership.",
                    true
                );

                SetStatus(
                    TerrainAuthoringPreviewStatus.PlayMode,
                    "The editor preview released its height cache for Play Mode."
                );

                RepaintEditorViews();

                break;
            }

            case PlayModeStateChange.EnteredPlayMode:
            {
                SetLifecycleSuspensionReason(
                    TerrainAuthoringPreviewSuspensionReason.PlayMode,
                    true
                );

                /*
                 * ExitingEditMode normally performed the release already.
                 * Repeating the established release path is intentionally safe
                 * if PreviewService initialized later in the handoff.
                 */
                ReleaseEditModePreviewResources(
                    "Play Mode owns terrain residency.",
                    true
                );

                SetStatus(
                    TerrainAuthoringPreviewStatus.PlayMode,
                    "Runtime owns terrain height-cache residency while Play Mode is active."
                );

                RepaintEditorViews();

                break;
            }

            case PlayModeStateChange.ExitingPlayMode:
            {
                /*
                 * Runtime hierarchy/cache ownership is still unwinding.
                 * Edit-mode reconstruction waits for EnteredEditMode.
                 */
                break;
            }

            case PlayModeStateChange.EnteredEditMode:
            {
                SetLifecycleSuspensionReason(
                    TerrainAuthoringPreviewSuspensionReason.PlayMode,
                    false
                );

                committedRebuildRequested =
                    true;

                clipmapRebindRequested =
                    true;

                overallSignatureAcknowledgementRequested =
                    false;

                RequestLifecycleResumeFromCurrentState();

                break;
            }
        }
    }

    private static void OnPreviewActiveSceneChangedInEditMode(
        Scene previousScene,
        Scene newScene
    )
    {
        if (
            HasAnySuspension(
                TerrainAuthoringPreviewSuspensionReason.AssemblyReload
                |
                TerrainAuthoringPreviewSuspensionReason.EditorQuitting
            )
        )
        {
            return;
        }

        /*
         * Active cache ownership belongs to the active edit-mode scene.
         * A replacement scene gets fresh transient resources even if it uses
         * the same WorldSettings asset.
         */
        lifecycleResumePending =
            false;

        CancelLifecycleResumeSchedule();

        ReleaseEditModePreviewResources(
            "Active scene changed.",
            true
        );

        committedRebuildRequested =
            true;

        clipmapRebindRequested =
            true;

        overallSignatureAcknowledgementRequested =
            false;

        SetStatus(
            TerrainAuthoringPreviewStatus.Preparing,
            "The active scene changed. Terrain preview residency will be rebuilt from current editor state."
        );

        RequestLifecycleResumeFromCurrentState();
    }

    private static void BeginTerminalPreviewShutdown(
        TerrainAuthoringPreviewSuspensionReason reason
    )
    {
        SetLifecycleSuspensionReason(
            reason,
            true
        );

        lifecycleResumePending =
            false;

        UnregisterEditorCallbacks();

        /*
         * No outbound preview/coverage notifications are needed while the
         * editor domain is already being destroyed.
         */
        ReleaseEditModePreviewResources(
            "Editor preview lifecycle is shutting down.",
            false
        );
    }
}
