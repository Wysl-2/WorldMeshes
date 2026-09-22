using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum TerrainAuthoringSceneViewFollowSource
{
    Camera,
    ScenePivot
}

public enum TerrainAuthoringSceneViewStatus
{
    Disabled,
    PlayMode,
    ClipmapUnavailable,
    SceneViewUnavailable,
    WaitingForHeightCache,
    Following,
    Frozen,
    Error
}

/*
 * Editor-only owner of transient Scene View placement and residency intent.
 *
 * Responsibilities:
 *
 * - choose the controlling Scene View and follow/frozen target
 * - clamp the target to the logical world rectangle
 * - calculate independently-snapped LOD layouts
 * - apply layouts through TerrainClipmapLayoutApplier
 * - publish the local residency intent required by that placement
 * - avoid hierarchy writes when the snapped layout did not change
 * - restore the generated canonical hierarchy for serialization and
 *   Play Mode handoff
 *
 * This service deliberately does NOT own active/staging caches, source
 * loading, composition, cache activation, or GPU resource lifetime. Those
 * responsibilities belong to TerrainAuthoringPreviewService.
 */
[InitializeOnLoad]
public static partial class TerrainAuthoringSceneViewController
{
    private enum FollowTargetApplyResult
    {
        Applied,
        WaitingForResidency,
        Failed
    }

    // =====================================================
    // EDITOR PREFERENCES
    // =====================================================

    private const string FollowEnabledEditorPrefsKey =
        "WorldMeshes.TerrainAuthoringSceneView.FollowEnabled";

    private const string FollowSourceEditorPrefsKey =
        "WorldMeshes.TerrainAuthoringSceneView.FollowSource";

    /*
     * Follow is intentionally opt-in on first use.
     *
     * This avoids moving generated scene transforms immediately
     * after the feature is first imported.
     */
    private const bool DefaultFollowEnabled =
        false;

    private const TerrainAuthoringSceneViewFollowSource
        DefaultFollowSource =
            TerrainAuthoringSceneViewFollowSource.Camera;

    // =====================================================
    // SHARED CLIPMAP STATE
    // =====================================================

    private static WorldSettings worldSettings;

    private static Transform clipmapRoot;

    private static readonly TerrainClipmapLayoutApplier
        layoutApplier =
            new TerrainClipmapLayoutApplier();

    /*
     * Two reusable layouts avoid per-SceneGUI allocation.
     *
     * candidateLayout is overwritten during calculation.
     * appliedLayout describes the layout currently expected on the
     * generated hierarchy.
     *
     * References are swapped after a successful application.
     */
    private static TerrainClipmapLayout candidateLayout =
        new TerrainClipmapLayout();

    private static TerrainClipmapLayout appliedLayout =
        new TerrainClipmapLayout();

    // =====================================================
    // FOLLOW / FREEZE STATE
    // =====================================================

    /*
     * Freeze is intentionally session-only.
     *
     * Persisting only a frozen boolean across an editor restart
     * would be misleading because the frozen target/layout itself
     * is transient editor-session state.
     */
    private static bool freezePreview;

    private static bool hasFrozenTarget;

    private static Vector3 frozenTarget =
        Vector3.zero;

    private static bool hasLastFollowTarget;

    private static Vector3 lastFollowTarget =
        Vector3.zero;

    private static bool hasLastAppliedLOD0Anchor;

    private static Vector3 lastAppliedLOD0Anchor =
        Vector3.zero;

    // =====================================================
    // STATUS
    // =====================================================

    private static TerrainAuthoringSceneViewStatus status =
        TerrainAuthoringSceneViewStatus.Disabled;

    private static string statusMessage =
        "Scene View clipmap following is disabled.";

    // =====================================================
    // LIFECYCLE / REENTRANCY
    // =====================================================

    private static bool reapplyScheduled;

    private static bool suspendedForPlayMode;

    private static bool isApplyingPlacement;

    private static bool isRestoringCanonicalPlacement;

    // =====================================================
    // INITIALIZATION
    // =====================================================

    static TerrainAuthoringSceneViewController()
    {
        InitializeSceneViewLifecycle();
    }

    // =====================================================
    // PUBLIC SETTINGS
    // =====================================================

    public static bool FollowSceneView
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    FollowEnabledEditorPrefsKey,
                    DefaultFollowEnabled
                );
        }

        set
        {
            bool oldValue =
                FollowSceneView;

            if (oldValue == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                FollowEnabledEditorPrefsKey,
                value
            );

            if (!value)
            {
                freezePreview =
                    false;

                hasFrozenTarget =
                    false;

                hasLastFollowTarget =
                    false;

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

                RepaintEditorViews();

                return;
            }

            freezePreview =
                false;

            hasFrozenTarget =
                false;

            RequestReapply();
        }
    }

    public static TerrainAuthoringSceneViewFollowSource FollowSource
    {
        get
        {
            int storedValue =
                EditorPrefs.GetInt(
                    FollowSourceEditorPrefsKey,
                    (int)DefaultFollowSource
                );

            if (
                storedValue <
                    (int)TerrainAuthoringSceneViewFollowSource.Camera
                ||
                storedValue >
                    (int)TerrainAuthoringSceneViewFollowSource.ScenePivot
            )
            {
                return
                    DefaultFollowSource;
            }

            return
                (TerrainAuthoringSceneViewFollowSource)
                storedValue;
        }

        set
        {
            int integerValue =
                (int)value;

            if (
                integerValue <
                    (int)TerrainAuthoringSceneViewFollowSource.Camera
                ||
                integerValue >
                    (int)TerrainAuthoringSceneViewFollowSource.ScenePivot
            )
            {
                value =
                    DefaultFollowSource;
            }

            if (FollowSource == value)
            {
                return;
            }

            EditorPrefs.SetInt(
                FollowSourceEditorPrefsKey,
                (int)value
            );

            if (
                FollowSceneView
                &&
                !freezePreview
            )
            {
                RequestReapply();
            }

            RepaintEditorViews();
        }
    }

    public static bool FreezePreview
    {
        get
        {
            return
                freezePreview;
        }

        set
        {
            bool newValue =
                FollowSceneView
                &&
                value;

            if (
                freezePreview ==
                newValue
            )
            {
                return;
            }

            freezePreview =
                newValue;

            if (freezePreview)
            {
                CaptureFrozenTarget();

                if (
                    status ==
                        TerrainAuthoringSceneViewStatus.WaitingForHeightCache
                    &&
                    hasFrozenTarget
                )
                {
                    SetWaitingForHeightCacheStatus();
                }
                else
                {
                    SetStatus(
                        TerrainAuthoringSceneViewStatus.Frozen,
                        hasFrozenTarget
                            ? "The clipmap preview is frozen at its current follow target."
                            : "The clipmap preview is frozen."
                    );
                }

                RepaintEditorViews();

                return;
            }

            hasFrozenTarget =
                false;

            RequestReapply();
        }
    }

    // =====================================================
    // PUBLIC STATUS / DIAGNOSTICS
    // =====================================================

    public static TerrainAuthoringSceneViewStatus Status
    {
        get
        {
            return
                status;
        }
    }

    public static string StatusLabel
    {
        get
        {
            switch (status)
            {
                case TerrainAuthoringSceneViewStatus.PlayMode:
                    return
                        "Runtime Owns Clipmap";

                case TerrainAuthoringSceneViewStatus.ClipmapUnavailable:
                    return
                        "Clipmap Unavailable";

                case TerrainAuthoringSceneViewStatus.SceneViewUnavailable:
                    return
                        "Scene View Unavailable";

                case TerrainAuthoringSceneViewStatus.WaitingForHeightCache:
                    return
                        "Waiting For Height Cache";

                case TerrainAuthoringSceneViewStatus.Following:
                    return
                        "Following";

                case TerrainAuthoringSceneViewStatus.Frozen:
                    return
                        "Frozen";

                case TerrainAuthoringSceneViewStatus.Error:
                    return
                        "Error";

                default:
                    return
                        "Disabled";
            }
        }
    }

    public static string StatusMessage
    {
        get
        {
            return
                statusMessage;
        }
    }

    public static event Action AppliedClipmapBoundsChanged;

    public static bool TryGetAppliedClipmapBounds(
        out Vector2 minimumXZ,
        out Vector2 maximumXZ
    )
    {
        return
            layoutApplier.TryGetAppliedBounds(
                out minimumXZ,
                out maximumXZ
            );
    }

    public static bool HasFollowTarget
    {
        get
        {
            return
                hasLastFollowTarget;
        }
    }

    public static Vector2 FollowTargetXZ
    {
        get
        {
            return
                new Vector2(
                    lastFollowTarget.x,
                    lastFollowTarget.z
                );
        }
    }

    public static bool HasAppliedLOD0Anchor
    {
        get
        {
            return
                hasLastAppliedLOD0Anchor;
        }
    }

    public static Vector2 AppliedLOD0AnchorXZ
    {
        get
        {
            return
                new Vector2(
                    lastAppliedLOD0Anchor.x,
                    lastAppliedLOD0Anchor.z
                );
        }
    }

    // =====================================================
    // PUBLIC COMMANDS
    // =====================================================

    /*
     * Use after generated hierarchy synchronization or any editor
     * operation that may have replaced/reset clipmap children.
     *
     * Height-cache residency remains owned by PreviewService and may
     * be requested only after a candidate layout is calculated.
     */
    public static void RequestReapply()
    {
        appliedLayout.Invalidate();

        layoutApplier
            .InvalidateHierarchyReferences();

        ScheduleReapply();
    }

    /*
     * Move the edit-mode preview to the logical world center.
     *
     * When following is enabled, the preview is frozen at the
     * center so the next Scene View repaint does not immediately
     * move it back to the camera/pivot.
     *
     * When following is disabled, the generated canonical hierarchy
     * is restored instead.
     */
    public static bool CenterOnWorld()
    {
        if (
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
            ||
            suspendedForPlayMode
        )
        {
            return false;
        }

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

                RepaintEditorViews();

                return false;
            }

            if (waitingForResidency)
            {
                SetWaitingForHeightCacheStatus();

                RepaintEditorViews();

                return true;
            }

            return
                RestoreCanonicalHierarchy(
                    true
                );
        }

        if (
            !TryEnsureWorldSettings(
                out string settingsError
            )
        )
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.Error,
                settingsError
            );

            RepaintEditorViews();

            return false;
        }

        Vector3 centerTarget =
            TerrainClipmapLayoutUtility
                .CalculateWorldCenterPosition(
                    worldSettings,
                    0f
                );

        centerTarget =
            TerrainClipmapLayoutUtility
                .ClampTargetXZToWorld(
                    worldSettings,
                    centerTarget
                );

        freezePreview =
            true;

        hasFrozenTarget =
            true;

        frozenTarget =
            centerTarget;

        FollowTargetApplyResult applyResult =
            TryApplyFollowTarget(
                centerTarget,
                out string applyError
            );

        if (
            applyResult ==
                FollowTargetApplyResult.Failed
        )
        {
            freezePreview =
                false;

            hasFrozenTarget =
                false;

            SetStatus(
                TerrainAuthoringSceneViewStatus.Error,
                applyError
            );

            RepaintEditorViews();

            return false;
        }

        if (
            applyResult ==
                FollowTargetApplyResult.WaitingForResidency
        )
        {
            SetWaitingForHeightCacheStatus();

            RepaintEditorViews();

            return true;
        }

        SetStatus(
            TerrainAuthoringSceneViewStatus.Frozen,
            "The clipmap preview is centered on the world and frozen."
        );

        RepaintEditorViews();

        return true;
    }

    // =====================================================
    // SCENE VIEW CALLBACK
    // =====================================================

    private static void OnSceneViewGUI(
        SceneView sceneView
    )
    {
        if (
            !FollowSceneView
            ||
            freezePreview
            ||
            suspendedForPlayMode
            ||
            !TerrainAuthoringPreviewService
                .IsEditorLifecycleStable
        )
        {
            return;
        }

        if (
            sceneView == null
            ||
            sceneView.camera == null
        )
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.SceneViewUnavailable,
                "No usable Scene View camera is available."
            );

            return;
        }

        /*
         * Package 08A makes Scene View ownership explicit. Only the current
         * controlling Scene View may publish placement/residency intent.
         */
        if (
            !TryUseAsControllingSceneView(
                sceneView
            )
        )
        {
            return;
        }

        if (
            !TryGetSceneViewTarget(
                sceneView,
                out Vector3 target,
                out string targetError
            )
        )
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.SceneViewUnavailable,
                targetError
            );

            return;
        }

        FollowTargetApplyResult applyResult =
            TryApplyFollowTarget(
                target,
                out string applyError
            );

        if (
            applyResult ==
                FollowTargetApplyResult.Failed
        )
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.Error,
                applyError
            );

            return;
        }

        if (
            applyResult ==
                FollowTargetApplyResult.WaitingForResidency
        )
        {
            SetWaitingForHeightCacheStatus();

            return;
        }

        SetFollowingStatus();
    }

    // =====================================================
    // GET SCENE VIEW TARGET
    // =====================================================

    private static bool TryGetSceneViewTarget(
        SceneView sceneView,
        out Vector3 target,
        out string errorMessage
    )
    {
        target =
            Vector3.zero;

        errorMessage =
            "";

        if (sceneView == null)
        {
            errorMessage =
                "No active Scene View is available.";

            return false;
        }

        switch (FollowSource)
        {
            case TerrainAuthoringSceneViewFollowSource.ScenePivot:
            {
                target =
                    sceneView.pivot;

                break;
            }

            default:
            {
                if (sceneView.camera == null)
                {
                    errorMessage =
                        "The active Scene View does not have a camera.";

                    return false;
                }

                target =
                    sceneView
                        .camera
                        .transform
                        .position;

                break;
            }
        }

        return true;
    }

    // =====================================================
    // APPLY FOLLOW TARGET
    // =====================================================

    private static FollowTargetApplyResult TryApplyFollowTarget(
        Vector3 target,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !TryEnsureConfiguration(
                out errorMessage
            )
        )
        {
            return
                FollowTargetApplyResult.Failed;
        }

        Vector3 clampedTarget =
            TerrainClipmapLayoutUtility
                .ClampTargetXZToWorld(
                    worldSettings,
                    target
                );

        /*
         * Scene camera/pivot Y does not move the terrain hierarchy.
         * Generated clipmap placement remains on canonical Y = 0.
         */
        clampedTarget.y =
            0f;

        hasLastFollowTarget =
            true;

        lastFollowTarget =
            clampedTarget;

        if (
            !TerrainClipmapLayoutUtility
                .TryCalculateLayout(
                    worldSettings,
                    clampedTarget,
                    0f,
                    candidateLayout,
                    out string layoutError
                )
        )
        {
            errorMessage =
                "The editor clipmap layout could not be calculated.\n\n" +
                layoutError;

            return
                FollowTargetApplyResult.Failed;
        }

        /*
         * Package 03A still lets PreviewService evaluate residency before
         * placement. Package 08A adds one ownership rule: temporary editor
         * suspension may use already-safe active coverage, but it must not
         * publish new streaming intent until lifecycle stability returns.
         */
        if (TerrainAuthoringPreviewService.Enabled)
        {
            bool activeCoverageSafe =
                TerrainAuthoringPreviewService
                    .CanActiveCacheCoverWorldBounds(
                        candidateLayout.MinimumXZ,
                        candidateLayout.MaximumXZ
                    );

            if (
                TerrainAuthoringPreviewService
                    .CanRunEditorPreviewWork
            )
            {
                bool requestSucceeded =
                    TerrainAuthoringPreviewService
                        .RequestResidencyForWorldBounds(
                            candidateLayout.MinimumXZ,
                            candidateLayout.MaximumXZ,
                            out string residencyError
                        );

                if (
                    !requestSucceeded
                    &&
                    !activeCoverageSafe
                )
                {
                    errorMessage =
                        "The editor height-cache residency request failed.\n\n" +
                        residencyError;

                    return
                        FollowTargetApplyResult.Failed;
                }
            }

            if (!activeCoverageSafe)
            {
                return
                    FollowTargetApplyResult.WaitingForResidency;
            }
        }

        /*
         * Camera movement commonly changes every SceneGUI event,
         * while the independently-snapped clipmap layout changes
         * only when one or more LOD grids cross a snapping boundary.
         *
         * The residency check intentionally occurs first. An equal layout is
         * not safe if the active cache was replaced with different coverage.
         */
        if (
            appliedLayout.IsValid
            &&
            LayoutsMatch(
                appliedLayout,
                candidateLayout
            )
        )
        {
            return
                FollowTargetApplyResult.Applied;
        }

        isApplyingPlacement =
            true;

        try
        {
            if (
                !layoutApplier.TryApply(
                    candidateLayout,
                    out string applyError
                )
            )
            {
                errorMessage =
                    "The editor clipmap layout could not be applied.\n\n" +
                    applyError;

                return
                    FollowTargetApplyResult.Failed;
            }
        }
        finally
        {
            isApplyingPlacement =
                false;
        }

        TerrainClipmapLayout previousAppliedLayout =
            appliedLayout;

        appliedLayout =
            candidateLayout;

        candidateLayout =
            previousAppliedLayout;

        if (
            appliedLayout.TryGetLOD(
                0,
                out Vector3 lod0Anchor,
                out _
            )
        )
        {
            hasLastAppliedLOD0Anchor =
                true;

            lastAppliedLOD0Anchor =
                lod0Anchor;
        }

        RepaintWorldMeshesWindows();

        return
            FollowTargetApplyResult.Applied;
    }

    // =====================================================
    // LAYOUT COMPARISON
    // =====================================================

    private static bool LayoutsMatch(
        TerrainClipmapLayout a,
        TerrainClipmapLayout b
    )
    {
        if (
            a == null
            ||
            b == null
            ||
            !a.IsValid
            ||
            !b.IsValid
            ||
            a.LevelCount !=
                b.LevelCount
        )
        {
            return false;
        }

        for (
            int level = 0;
            level < a.LevelCount;
            level++
        )
        {
            Vector3 aAnchor =
                a.GetAnchor(
                    level
                );

            Vector3 bAnchor =
                b.GetAnchor(
                    level
                );

            if (
                !Approximately(
                    aAnchor,
                    bAnchor
                )
                ||
                !Mathf.Approximately(
                    a.GetSpacing(
                        level
                    ),
                    b.GetSpacing(
                        level
                    )
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    // =====================================================
    // ENSURE CONFIGURATION
    // =====================================================

    private static bool TryEnsureConfiguration(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !TryEnsureWorldSettings(
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TerrainWorldSceneUtility
                .TryFindActiveClipmapRoot(
                    out Transform currentClipmapRoot,
                    out string sceneLookupError
                )
        )
        {
            errorMessage =
                sceneLookupError;

            return false;
        }

        if (currentClipmapRoot == null)
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.ClipmapUnavailable,
                "WorldRoot/Clipmap was not found in the active scene. " +
                "Generate clipmap meshes and run Setup / Repair World Hierarchy."
            );

            errorMessage =
                "WorldRoot/Clipmap is unavailable.";

            return false;
        }

        if (
            clipmapRoot !=
            currentClipmapRoot
        )
        {
            clipmapRoot =
                currentClipmapRoot;

            appliedLayout.Invalidate();

            layoutApplier
                .InvalidateHierarchyReferences();
        }

        layoutApplier.Configure(
            clipmapRoot,
            worldSettings
        );

        return true;
    }

    private static bool TryEnsureWorldSettings(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (worldSettings != null)
        {
            return true;
        }

        worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        if (worldSettings != null)
        {
            return true;
        }

        errorMessage =
            "WorldSettings could not be loaded from:\n" +
            WorldMeshesPaths.WorldSettingsAssetPath;

        return false;
    }

    // =====================================================
    // CANONICAL RESIDENCY
    // =====================================================

    private static bool TryEnsureCanonicalResidency(
        out bool waitingForResidency,
        out string errorMessage
    )
    {
        waitingForResidency =
            false;

        errorMessage =
            "";

        if (!TerrainAuthoringPreviewService.Enabled)
        {
            return true;
        }

        if (
            !TryEnsureWorldSettings(
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TerrainAuthoringPreviewResidencyUtility
                .TryCalculateCanonicalClipmapBounds(
                    worldSettings,
                    out Vector2 minimumXZ,
                    out Vector2 maximumXZ,
                    out errorMessage
                )
        )
        {
            return false;
        }

        bool hadActiveCache =
            TerrainAuthoringPreviewService
                .TryGetActiveResidentWindow(
                    out _
                );

        bool requestSucceeded =
            TerrainAuthoringPreviewService
                .RequestResidencyForWorldBounds(
                    minimumXZ,
                    maximumXZ,
                    out errorMessage
                );

        bool activeCoverageSafe =
            TerrainAuthoringPreviewService
                .CanActiveCacheCoverWorldBounds(
                    minimumXZ,
                    maximumXZ
                );

        if (
            !requestSucceeded
            &&
            !activeCoverageSafe
        )
        {
            return false;
        }

        if (activeCoverageSafe)
        {
            errorMessage =
                "";

            waitingForResidency =
                false;

            return true;
        }

        /*
         * With no active cache there is no old cache/layout mismatch to
         * preserve, so canonical hierarchy restoration may proceed while the
         * first local cache is being prepared. If a cache is already active,
         * retain the last safe hierarchy until canonical residency arrives.
         */
        waitingForResidency =
            hadActiveCache;

        return true;
    }

    // =====================================================
    // CANONICAL HIERARCHY RESTORE
    // =====================================================

    /*
     * Restore the serialized/generated clipmap state:
     *
     * - clipmap root at canonical world center
     * - coarse LOD local positions reset to zero
     * - stitch transition offsets reset to zero
     *
     * TerrainClipmapLayoutApplier.TryReset() deliberately leaves
     * the root position unchanged, so the canonical root placement
     * is restored explicitly here.
     */
    private static bool RestoreCanonicalHierarchy(
        bool updateStatus
    )
    {
        if (
            Application.isPlaying
            ||
            isApplyingPlacement
            ||
            isRestoringCanonicalPlacement
        )
        {
            return false;
        }

        if (
            !TryEnsureWorldSettings(
                out string settingsError
            )
        )
        {
            if (updateStatus)
            {
                SetStatus(
                    TerrainAuthoringSceneViewStatus.Error,
                    settingsError
                );
            }

            return false;
        }

        if (
            !TerrainWorldSceneUtility
                .TryFindActiveClipmapRoot(
                    out Transform currentClipmapRoot,
                    out string sceneLookupError
                )
        )
        {
            if (updateStatus)
            {
                SetStatus(
                    TerrainAuthoringSceneViewStatus.Error,
                    sceneLookupError
                );
            }

            return false;
        }

        if (currentClipmapRoot == null)
        {
            clipmapRoot =
                null;

            appliedLayout.Invalidate();

            if (updateStatus)
            {
                SetStatus(
                    TerrainAuthoringSceneViewStatus.ClipmapUnavailable,
                    "WorldRoot/Clipmap was not found in the active scene."
                );
            }

            return false;
        }

        clipmapRoot =
            currentClipmapRoot;

        layoutApplier.Configure(
            clipmapRoot,
            worldSettings
        );

        isRestoringCanonicalPlacement =
            true;

        bool resetSucceeded =
            true;

        string resetError =
            "";

        try
        {
            resetSucceeded =
                layoutApplier.TryReset(
                    out resetError
                );

            Vector3 canonicalPosition =
                TerrainClipmapLayoutUtility
                    .CalculateWorldCenterPosition(
                        worldSettings,
                        0f
                    );

            /*
             * WorldRoot is generated at identity, so canonical
             * hierarchy state is stored as local transforms.
             */
            clipmapRoot.localPosition =
                canonicalPosition;

            clipmapRoot.localRotation =
                Quaternion.identity;

            clipmapRoot.localScale =
                Vector3.one;
        }
        finally
        {
            isRestoringCanonicalPlacement =
                false;
        }

        appliedLayout.Invalidate();

        hasLastAppliedLOD0Anchor =
            false;

        if (
            updateStatus
            &&
            !FollowSceneView
        )
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.Disabled,
                resetSucceeded
                    ? "Scene View clipmap following is disabled. The canonical hierarchy is restored."
                    : "Scene View clipmap following is disabled. The clipmap root was centered, but generated LOD reset reported an error."
            );
        }

        if (
            !resetSucceeded
            &&
            !string.IsNullOrEmpty(
                resetError
            )
        )
        {
            Debug.LogWarning(
                "WorldMeshes could not completely reset the " +
                "generated clipmap hierarchy while restoring " +
                "canonical editor placement.\n\n" +
                resetError
            );
        }

        return
            resetSucceeded;
    }

    // =====================================================
    // FREEZE TARGET
    // =====================================================

    private static void CaptureFrozenTarget()
    {
        hasFrozenTarget =
            false;

        if (hasLastFollowTarget)
        {
            frozenTarget =
                lastFollowTarget;

            hasFrozenTarget =
                true;

            return;
        }

        SceneView sceneView =
            SceneView.lastActiveSceneView;

        if (
            sceneView == null
            ||
            !TryGetSceneViewTarget(
                sceneView,
                out Vector3 target,
                out _
            )
            ||
            !TryEnsureWorldSettings(
                out _
            )
        )
        {
            return;
        }

        frozenTarget =
            TerrainClipmapLayoutUtility
                .ClampTargetXZToWorld(
                    worldSettings,
                    target
                );

        frozenTarget.y =
            0f;

        hasFrozenTarget =
            true;
    }

    // =====================================================
    // SCHEDULED REAPPLY
    // =====================================================

    private static void ScheduleReapply()
    {
        if (reapplyScheduled)
        {
            return;
        }

        reapplyScheduled =
            true;

        EditorApplication.delayCall +=
            ExecuteScheduledReapply;
    }

    private static void ExecuteScheduledReapply()
    {
        reapplyScheduled =
            false;

        if (
            !TerrainAuthoringPreviewService
                .IsEditorLifecycleStable
        )
        {
            if (
                Application.isPlaying
                ||
                EditorApplication
                    .isPlayingOrWillChangePlaymode
                ||
                suspendedForPlayMode
            )
            {
                SetStatus(
                    TerrainAuthoringSceneViewStatus.PlayMode,
                    "Runtime owns clipmap placement while Play Mode is active."
                );

                RepaintEditorViews();
            }

            /*
             * PreviewService lifecycle resume will request a new reapply after
             * compilation/import or another temporary suspension clears.
             */
            return;
        }

        RefreshControllingSceneViewOwnership(
            false
        );

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

            RepaintEditorViews();

            return;
        }

        if (
            freezePreview
            &&
            hasFrozenTarget
        )
        {
            FollowTargetApplyResult frozenApplyResult =
                TryApplyFollowTarget(
                    frozenTarget,
                    out string frozenApplyError
                );

            if (
                frozenApplyResult ==
                    FollowTargetApplyResult.Applied
            )
            {
                SetStatus(
                    TerrainAuthoringSceneViewStatus.Frozen,
                    "The frozen edit-mode clipmap preview was reapplied."
                );
            }
            else if (
                frozenApplyResult ==
                    FollowTargetApplyResult.WaitingForResidency
            )
            {
                SetWaitingForHeightCacheStatus();
            }
            else
            {
                SetStatus(
                    TerrainAuthoringSceneViewStatus.Error,
                    frozenApplyError
                );
            }

            RepaintEditorViews();

            return;
        }

        if (
            !TryGetControllingSceneView(
                out SceneView sceneView
            )
        )
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.SceneViewUnavailable,
                "No active Scene View is available for clipmap following."
            );

            RepaintEditorViews();

            return;
        }

        if (
            !TryGetSceneViewTarget(
                sceneView,
                out Vector3 target,
                out string targetError
            )
        )
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.SceneViewUnavailable,
                targetError
            );

            RepaintEditorViews();

            return;
        }

        FollowTargetApplyResult applyResult =
            TryApplyFollowTarget(
                target,
                out string applyError
            );

        if (
            applyResult ==
                FollowTargetApplyResult.Applied
        )
        {
            if (freezePreview)
            {
                frozenTarget =
                    lastFollowTarget;

                hasFrozenTarget =
                    true;

                SetStatus(
                    TerrainAuthoringSceneViewStatus.Frozen,
                    "The edit-mode clipmap preview is frozen at the resolved Scene View target."
                );
            }
            else
            {
                SetFollowingStatus();
            }
        }
        else if (
            applyResult ==
                FollowTargetApplyResult.WaitingForResidency
        )
        {
            SetWaitingForHeightCacheStatus();
        }
        else
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.Error,
                applyError
            );
        }

        RepaintEditorViews();
    }

    // =====================================================
    // EDITOR EVENTS
    // =====================================================

    private static void OnHeightCacheCoverageChanged()
    {
        if (
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
            ||
            suspendedForPlayMode
            ||
            !TerrainAuthoringPreviewService
                .TryGetHeightCacheWorldCoverage(
                    out _,
                    out _
                )
        )
        {
            return;
        }

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

            RepaintEditorViews();

            return;
        }

        Vector3 desiredTarget;

        bool frozenTargetRequested =
            freezePreview
            &&
            hasFrozenTarget;

        if (frozenTargetRequested)
        {
            desiredTarget =
                frozenTarget;
        }
        else if (hasLastFollowTarget)
        {
            desiredTarget =
                lastFollowTarget;
        }
        else
        {
            ScheduleReapply();

            return;
        }

        FollowTargetApplyResult applyResult =
            TryApplyFollowTarget(
                desiredTarget,
                out string applyError
            );

        if (
            applyResult ==
                FollowTargetApplyResult.Applied
        )
        {
            if (frozenTargetRequested)
            {
                SetStatus(
                    TerrainAuthoringSceneViewStatus.Frozen,
                    "The frozen edit-mode clipmap preview was reapplied after height-cache residency changed."
                );
            }
            else
            {
                SetFollowingStatus();
            }
        }
        else if (
            applyResult ==
                FollowTargetApplyResult.WaitingForResidency
        )
        {
            /*
             * The desired target changed before the completed synchronous
             * cache refresh. RequestResidencyForWorldBounds schedules the
             * latest target rather than recursing into another build here.
             */
            SetWaitingForHeightCacheStatus();
        }
        else
        {
            SetStatus(
                TerrainAuthoringSceneViewStatus.Error,
                applyError
            );
        }

        RepaintEditorViews();
    }

    private static void OnHierarchyChanged()
    {
        if (
            isApplyingPlacement
            ||
            isRestoringCanonicalPlacement
        )
        {
            return;
        }

        clipmapRoot =
            null;

        appliedLayout.Invalidate();

        layoutApplier
            .InvalidateAppliedBounds();

        layoutApplier
            .InvalidateHierarchyReferences();

        if (FollowSceneView)
        {
            ScheduleReapply();
        }
    }

    private static void OnProjectChanged()
    {
        /*
         * WorldSettings is a ScriptableObject and normally retains
         * the same reference when edited. Clearing the cache here
         * also handles asset replacement/moves safely.
         */
        worldSettings =
            null;

        RequestReapply();
    }

    private static void OnUndoRedo()
    {
        RequestReapply();
    }

    private static void OnSceneSaving(
        Scene scene,
        string path
    )
    {
        if (
            Application.isPlaying
            ||
            !scene.IsValid()
            ||
            scene !=
                SceneManager.GetActiveScene()
        )
        {
            return;
        }

        /*
         * This controller owns only the active WorldMeshes scene.
         * Never disturb another additively loaded scene while it is
         * being saved.
         *
         * Never serialize transient Scene View placement.
         */
        RestoreCanonicalHierarchy(
            false
        );
    }

    private static void OnSceneSaved(
        Scene scene
    )
    {
        if (
            !scene.IsValid()
            ||
            scene !=
                SceneManager.GetActiveScene()
        )
        {
            return;
        }

        if (FollowSceneView)
        {
            RequestReapply();
        }
    }

    private static void OnActiveSceneChangedInEditMode(
        Scene previousScene,
        Scene newScene
    )
    {
        clipmapRoot =
            null;

        worldSettings =
            null;

        HandleActiveSceneOwnershipChanged();

        hasLastFollowTarget =
            false;

        hasLastAppliedLOD0Anchor =
            false;

        appliedLayout.Invalidate();

        layoutApplier
            .InvalidateAppliedBounds();

        layoutApplier
            .InvalidateHierarchyReferences();

        RequestReapply();
    }

    private static void OnPlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
            {
                /*
                 * Runtime TerrainClipmapController must receive the
                 * generated canonical hierarchy rather than the
                 * transient Scene View placement.
                 */
                RestoreCanonicalHierarchy(
                    false
                );

                suspendedForPlayMode =
                    true;

                SetStatus(
                    TerrainAuthoringSceneViewStatus.PlayMode,
                    "Editor clipmap placement was restored to canonical state for Play Mode."
                );

                RepaintEditorViews();

                break;
            }

            case PlayModeStateChange.EnteredPlayMode:
            {
                suspendedForPlayMode =
                    true;

                SetStatus(
                    TerrainAuthoringSceneViewStatus.PlayMode,
                    "Runtime owns clipmap placement while Play Mode is active."
                );

                RepaintEditorViews();

                break;
            }

            case PlayModeStateChange.ExitingPlayMode:
            {
                /*
                 * Do not touch runtime hierarchy while Play Mode is
                 * still unwinding.
                 */
                break;
            }

            case PlayModeStateChange.EnteredEditMode:
            {
                suspendedForPlayMode =
                    false;

                clipmapRoot =
                    null;

                appliedLayout.Invalidate();

                layoutApplier
                    .InvalidateHierarchyReferences();

                RefreshControllingSceneViewOwnership(
                    false
                );

                RequestReapply();

                break;
            }
        }
    }

    private static void OnBeforeAssemblyReload()
    {
        ShutdownSceneViewLifecycle();
    }

    private static void OnEditorQuitting()
    {
        ShutdownSceneViewLifecycle();
    }

    private static void OnAppliedBoundsChanged()
    {
        AppliedClipmapBoundsChanged?.Invoke();
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static bool Approximately(
        Vector3 a,
        Vector3 b
    )
    {
        return
            Mathf.Approximately(
                a.x,
                b.x
            )
            &&
            Mathf.Approximately(
                a.y,
                b.y
            )
            &&
            Mathf.Approximately(
                a.z,
                b.z
            );
    }

    private static void SetFollowingStatus()
    {
        SetStatus(
            TerrainAuthoringSceneViewStatus.Following,
            FollowSource ==
                TerrainAuthoringSceneViewFollowSource.Camera
                ? "The edit-mode clipmap is following the active Scene View camera."
                : "The edit-mode clipmap is following the active Scene View pivot."
        );
    }

    private static void SetWaitingForHeightCacheStatus()
    {
        SetStatus(
            TerrainAuthoringSceneViewStatus.WaitingForHeightCache,
            "The desired Scene View clipmap lies outside the active " +
            "resident height cache. WorldMeshes is preparing the " +
            "required local cache while retaining the last safe " +
            "clipmap placement."
        );
    }

    private static void SetStatus(
        TerrainAuthoringSceneViewStatus newStatus,
        string message
    )
    {
        bool changed =
            status !=
                newStatus
            ||
            statusMessage !=
                message;

        status =
            newStatus;

        statusMessage =
            string.IsNullOrEmpty(
                message
            )
                ? ""
                : message;

        if (changed)
        {
            RepaintWorldMeshesWindows();
        }
    }

    private static void RepaintEditorViews()
    {
        SceneView.RepaintAll();

        RepaintWorldMeshesWindows();
    }

    private static void RepaintWorldMeshesWindows()
    {
        WorldMeshesEditorWindow[] windows =
            Resources
                .FindObjectsOfTypeAll<WorldMeshesEditorWindow>();

        foreach (
            WorldMeshesEditorWindow window
            in windows
        )
        {
            if (window != null)
            {
                window.Repaint();
            }
        }
    }
}
