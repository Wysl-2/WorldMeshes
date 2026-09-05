using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public enum TerrainLiveStampValidationStatus
{
    Ready,
    Scheduled,
    WaitingForPreview,
    Passed,
    Failed
}

public sealed class TerrainLiveStampValidationBaseline
{
    public string Label { get; internal set; }
    public int CacheTextureId { get; internal set; }
    public long FullCacheBuilds { get; internal set; }
    public long TotalIncrementalSliceUpdates { get; internal set; }
    public long TotalCompositeTileCount { get; internal set; }
    public long TotalModifierConsideredCount { get; internal set; }
    public long TotalModifierDispatchCount { get; internal set; }
    public long TotalComputeDispatchCount { get; internal set; }
    public long RendererBindingCount { get; internal set; }
    public string OverallAuthoringSignature { get; internal set; }
    public string CommittedHeightfieldSignature { get; internal set; }
    public int AuthoringRevision { get; internal set; }
    public float MinimumPreviewHeight { get; internal set; }
    public float MaximumPreviewHeight { get; internal set; }
}

public sealed class TerrainLiveStampValidationReport
{
    public string Operation { get; internal set; }
    public bool Passed { get; internal set; }
    public int ExpectedDirtyTileCount { get; internal set; }
    public long ActualIncrementalSliceCount { get; internal set; }
    public long ActualCompositeTileCount { get; internal set; }
    public long ModifierConsideredCount { get; internal set; }
    public long ModifierDispatchCount { get; internal set; }
    public long ComputeDispatchCount { get; internal set; }
    public string DirtyTileSummary { get; internal set; }
    public string Summary { get; internal set; }
    public string Details { get; internal set; }
}

/*
 * Controlled edit-mode live stamp integration workflow.
 *
 * This is deliberately not the production modifier-authoring UI.
 * Every persistent modifier edit performed here goes through
 * TerrainAuthoringModifierService.
 */
[InitializeOnLoad]
public static class TerrainLiveStampValidationUtility
{
    private const int MaximumPreviewWaitCycles =
        120;

    private enum RequestedOperation
    {
        None,
        Add,
        ApplyParameters,
        Move,
        Remove,
        ReorderEarlier,
        ReorderLater,
        Undo,
        Redo,
        Reset
    }

    private static readonly List<string> controlledStampIds =
        new List<string>();

    private static readonly List<Vector2Int> expectedDirtyTiles =
        new List<Vector2Int>();

    private static RequestedOperation scheduledOperation =
        RequestedOperation.None;

    private static TerrainLiveStampValidationStatus status =
        TerrainLiveStampValidationStatus.Ready;

    private static string statusMessage =
        "Ready for controlled live stamp validation.";

    private static int previewWaitCycleCount;
    private static string pendingOperationLabel = "";
    private static string expectedOverallAfterMutation = "";
    private static int revisionAfterRequestedOperation;
    private static int selectedControlledIndex;

    private static TerrainHeightStampAsset pendingStampAsset;
    private static Vector2 pendingPositionXZ = Vector2.zero;
    private static Vector2 pendingSizeXZ = new Vector2(256f, 256f);
    private static float pendingHeightDelta = 30f;
    private static float pendingFalloff = 0.25f;
    private static bool pendingEnabled = true;

    private static TerrainLiveStampValidationBaseline lastBaseline;
    private static TerrainLiveStampValidationReport lastReport;

    private static string borderValidationSummary = "Not run.";
    private static bool borderValidationPassed;

    private static string editorPrefsPrefix;

    static TerrainLiveStampValidationUtility()
    {
        editorPrefsPrefix =
            "WorldMeshes.LiveStampValidation."
            +
            Application.dataPath
            +
            ".";

        LoadPersistentState();

        /*
         * Do not create or modify AssetDatabase content from this
         * [InitializeOnLoad] static constructor. Unity can invoke it while the
         * import pipeline is still processing scripts/assets. The generated
         * default fixture is created lazily by validation prerequisites once
         * the editor is idle.
         */

        if (
            !EditorPrefs.HasKey(Key("PositionX"))
            ||
            !EditorPrefs.HasKey(Key("PositionZ"))
        )
        {
            ResetPendingPositionToWorldCenter();
        }
    }

    public static bool IsBusy =>
        status == TerrainLiveStampValidationStatus.Scheduled
        ||
        status == TerrainLiveStampValidationStatus.WaitingForPreview;

    public static TerrainLiveStampValidationStatus Status =>
        status;

    public static string StatusLabel
    {
        get
        {
            switch (status)
            {
                case TerrainLiveStampValidationStatus.Scheduled:
                    return "Scheduled";
                case TerrainLiveStampValidationStatus.WaitingForPreview:
                    return "Waiting For Preview";
                case TerrainLiveStampValidationStatus.Passed:
                    return "Passed";
                case TerrainLiveStampValidationStatus.Failed:
                    return "Failed";
                default:
                    return "Ready";
            }
        }
    }

    public static string StatusMessage => statusMessage;
    public static TerrainLiveStampValidationBaseline LastBaseline => lastBaseline;
    public static TerrainLiveStampValidationReport LastReport => lastReport;
    public static string BorderValidationSummary => borderValidationSummary;
    public static bool BorderValidationPassed => borderValidationPassed;
    public static int ControlledStampCount => controlledStampIds.Count;

    public static int SelectedControlledIndex
    {
        get
        {
            if (controlledStampIds.Count <= 0)
            {
                return -1;
            }

            return Mathf.Clamp(
                selectedControlledIndex,
                0,
                controlledStampIds.Count - 1
            );
        }
        set
        {
            if (controlledStampIds.Count <= 0)
            {
                selectedControlledIndex = 0;
                return;
            }

            selectedControlledIndex = Mathf.Clamp(
                value,
                0,
                controlledStampIds.Count - 1
            );

            SavePersistentState();
            LoadPendingSettingsFromSelected();
        }
    }

    public static string SelectedStableId
    {
        get
        {
            int index = SelectedControlledIndex;

            if (
                index < 0
                ||
                index >= controlledStampIds.Count
            )
            {
                return "";
            }

            return controlledStampIds[index];
        }
    }

    public static TerrainHeightStampAsset PendingStampAsset
    {
        get => pendingStampAsset;
        set
        {
            pendingStampAsset = value;
            SavePersistentState();
        }
    }

    public static Vector2 PendingPositionXZ
    {
        get => pendingPositionXZ;
        set
        {
            pendingPositionXZ = new Vector2(
                SanitizeFinite(value.x, 0f),
                SanitizeFinite(value.y, 0f)
            );
            SavePersistentState();
        }
    }

    public static Vector2 PendingSizeXZ
    {
        get => pendingSizeXZ;
        set
        {
            pendingSizeXZ = new Vector2(
                Mathf.Max(0.01f, Mathf.Abs(SanitizeFinite(value.x, 0.01f))),
                Mathf.Max(0.01f, Mathf.Abs(SanitizeFinite(value.y, 0.01f)))
            );
            SavePersistentState();
        }
    }

    public static float PendingHeightDelta
    {
        get => pendingHeightDelta;
        set
        {
            pendingHeightDelta = SanitizeFinite(value, 0f);
            SavePersistentState();
        }
    }

    public static float PendingFalloff
    {
        get => pendingFalloff;
        set
        {
            pendingFalloff = Mathf.Clamp01(
                SanitizeFinite(value, 0f)
            );
            SavePersistentState();
        }
    }

    public static bool PendingEnabled
    {
        get => pendingEnabled;
        set
        {
            pendingEnabled = value;
            SavePersistentState();
        }
    }

    public static string GetControlledStampLabel(int index)
    {
        if (
            index < 0
            ||
            index >= controlledStampIds.Count
        )
        {
            return "Invalid";
        }

        string stableId = controlledStampIds[index];

        bool exists =
            TryFindModifier(
                stableId,
                out _,
                out _
            );

        string shortId =
            stableId.Length > 8
                ? stableId.Substring(0, 8)
                : stableId;

        return
            $"Test Stamp {index + 1} [{shortId}]"
            +
            (exists ? "" : " (missing/removed)");
    }

    public static void ResetPendingPositionToWorldCenter()
    {
        WorldSettings worldSettings = LoadWorldSettings();

        if (worldSettings == null)
        {
            PendingPositionXZ = Vector2.zero;
            return;
        }

        Vector2 worldSize =
            TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(
                worldSettings
            );

        PendingPositionXZ = worldSize * 0.5f;
    }

    public static void LoadPendingSettingsFromSelected()
    {
        if (
            !TryFindSelectedStamp(
                out TerrainStampModifier modifier,
                out _
            )
        )
        {
            return;
        }

        pendingStampAsset = modifier.StampAsset;
        pendingPositionXZ = modifier.PositionXZ;
        pendingSizeXZ = modifier.SizeXZ;
        pendingHeightDelta = modifier.HeightDelta;
        pendingFalloff = modifier.Falloff;
        pendingEnabled = modifier.Enabled;

        SavePersistentState();
    }

    public static void RequestAddTestStamp()
    {
        RequestOperation(RequestedOperation.Add, "Add Test Stamp");
    }

    public static void RequestApplyParameters()
    {
        RequestOperation(
            RequestedOperation.ApplyParameters,
            "Apply Test Stamp Parameters"
        );
    }

    public static void RequestMoveTestStamp()
    {
        RequestOperation(RequestedOperation.Move, "Move Test Stamp");
    }

    public static void RequestRemoveTestStamp()
    {
        RequestOperation(RequestedOperation.Remove, "Remove Test Stamp");
    }

    public static void RequestReorderEarlier()
    {
        RequestOperation(
            RequestedOperation.ReorderEarlier,
            "Move Test Stamp Earlier"
        );
    }

    public static void RequestReorderLater()
    {
        RequestOperation(
            RequestedOperation.ReorderLater,
            "Move Test Stamp Later"
        );
    }

    public static void RequestUndo()
    {
        RequestOperation(RequestedOperation.Undo, "Undo Test Edit");
    }

    public static void RequestRedo()
    {
        RequestOperation(RequestedOperation.Redo, "Redo Test Edit");
    }

    public static void RequestResetTestState()
    {
        RequestOperation(RequestedOperation.Reset, "Reset Test State");
    }

    public static void RequestValidateSelectedBorders()
    {
        if (IsBusy)
        {
            return;
        }

        EditorApplication.delayCall -= ValidateSelectedBordersScheduled;
        EditorApplication.delayCall += ValidateSelectedBordersScheduled;
    }

    private static void RequestOperation(
        RequestedOperation operation,
        string label
    )
    {
        if (IsBusy)
        {
            return;
        }

        scheduledOperation = operation;
        pendingOperationLabel = label;

        status = TerrainLiveStampValidationStatus.Scheduled;
        statusMessage = label + " is scheduled.";

        EditorApplication.delayCall -= ExecuteScheduledOperation;
        EditorApplication.delayCall += ExecuteScheduledOperation;
    }

    private static void ExecuteScheduledOperation()
    {
        EditorApplication.delayCall -= ExecuteScheduledOperation;

        if (scheduledOperation == RequestedOperation.None)
        {
            SetReady("No validation operation is scheduled.");
            return;
        }

        if (
            !TryValidateOperationPrerequisites(
                out TerrainAuthoringData authoringData,
                out WorldSettings worldSettings,
                out string prerequisiteError
            )
        )
        {
            Fail(pendingOperationLabel, prerequisiteError);
            scheduledOperation = RequestedOperation.None;
            return;
        }

        lastBaseline = CaptureBaseline(
            pendingOperationLabel,
            authoringData,
            worldSettings
        );

        expectedDirtyTiles.Clear();
        expectedOverallAfterMutation =
            lastBaseline.OverallAuthoringSignature;

        revisionAfterRequestedOperation =
            lastBaseline.AuthoringRevision;

        bool success =
            ExecuteOperation(
                scheduledOperation,
                authoringData,
                worldSettings,
                out string operationError
            );

        if (!success)
        {
            Fail(pendingOperationLabel, operationError);
            scheduledOperation = RequestedOperation.None;
            return;
        }

        TerrainAuthoringPreviewService.CopyPendingDirtyTiles(
            expectedDirtyTiles
        );

        expectedOverallAfterMutation =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                authoringData
            );

        revisionAfterRequestedOperation =
            authoringData.authoringRevision;

        previewWaitCycleCount = 0;

        status =
            TerrainLiveStampValidationStatus.WaitingForPreview;

        statusMessage =
            pendingOperationLabel
            +
            " completed. Waiting for the preview transaction.";

        EditorApplication.delayCall -= WaitForPreviewAndValidate;
        EditorApplication.delayCall += WaitForPreviewAndValidate;
    }

    private static bool ExecuteOperation(
        RequestedOperation operation,
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage = "";

        switch (operation)
        {
            case RequestedOperation.Add:
                return ExecuteAdd(
                    authoringData,
                    worldSettings,
                    out errorMessage
                );

            case RequestedOperation.ApplyParameters:
                return ExecuteApplyParameters(
                    authoringData,
                    worldSettings,
                    out errorMessage
                );

            case RequestedOperation.Move:
                return ExecuteMove(
                    authoringData,
                    worldSettings,
                    out errorMessage
                );

            case RequestedOperation.Remove:
                return ExecuteRemove(
                    authoringData,
                    worldSettings,
                    out errorMessage
                );

            case RequestedOperation.ReorderEarlier:
                return ExecuteReorder(
                    authoringData,
                    worldSettings,
                    -1,
                    out errorMessage
                );

            case RequestedOperation.ReorderLater:
                return ExecuteReorder(
                    authoringData,
                    worldSettings,
                    1,
                    out errorMessage
                );

            case RequestedOperation.Undo:
                Undo.PerformUndo();
                return true;

            case RequestedOperation.Redo:
                Undo.PerformRedo();
                return true;

            case RequestedOperation.Reset:
                return ExecuteReset(
                    authoringData,
                    worldSettings,
                    out errorMessage
                );

            default:
                errorMessage = "Unsupported validation operation.";
                return false;
        }
    }

    private static bool ExecuteAdd(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !TerrainAuthoringModifierService.AddStampModifier(
                authoringData,
                worldSettings,
                pendingStampAsset,
                pendingPositionXZ,
                pendingSizeXZ,
                pendingHeightDelta,
                pendingFalloff,
                out string stableId,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (string.IsNullOrEmpty(stableId))
        {
            errorMessage =
                "The modifier service did not return a stable ID.";
            return false;
        }

        controlledStampIds.Add(stableId);
        selectedControlledIndex =
            controlledStampIds.Count - 1;

        SavePersistentState();

        if (!pendingEnabled)
        {
            if (
                !TerrainAuthoringModifierService.SetModifierEnabled(
                    authoringData,
                    worldSettings,
                    stableId,
                    false,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool ExecuteApplyParameters(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !TryFindSelectedStamp(
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        string stableId = modifier.StableId;

        if (modifier.StampAsset != pendingStampAsset)
        {
            if (
                !TerrainAuthoringModifierService.SetStampAsset(
                    authoringData,
                    worldSettings,
                    stableId,
                    pendingStampAsset,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }

        if (modifier.SizeXZ != pendingSizeXZ)
        {
            if (
                !TerrainAuthoringModifierService.SetStampSizeXZ(
                    authoringData,
                    worldSettings,
                    stableId,
                    pendingSizeXZ,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }

        if (
            !Mathf.Approximately(
                modifier.HeightDelta,
                pendingHeightDelta
            )
        )
        {
            if (
                !TerrainAuthoringModifierService.SetStampHeightDelta(
                    authoringData,
                    worldSettings,
                    stableId,
                    pendingHeightDelta,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }

        if (
            !Mathf.Approximately(
                modifier.Falloff,
                pendingFalloff
            )
        )
        {
            if (
                !TerrainAuthoringModifierService.SetStampFalloff(
                    authoringData,
                    worldSettings,
                    stableId,
                    pendingFalloff,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }

        if (
            !TryFindSelectedStamp(
                out modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (modifier.Enabled != pendingEnabled)
        {
            if (
                !TerrainAuthoringModifierService.SetModifierEnabled(
                    authoringData,
                    worldSettings,
                    stableId,
                    pendingEnabled,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool ExecuteMove(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !TryFindSelectedStamp(
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        return TerrainAuthoringModifierService.SetStampPositionXZ(
            authoringData,
            worldSettings,
            modifier.StableId,
            pendingPositionXZ,
            out errorMessage
        );
    }

    private static bool ExecuteRemove(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage = "";

        string stableId = SelectedStableId;

        if (string.IsNullOrEmpty(stableId))
        {
            errorMessage =
                "No controlled test stamp is selected.";
            return false;
        }

        if (
            !TryFindModifier(
                stableId,
                out _,
                out _
            )
        )
        {
            errorMessage =
                "The selected controlled stamp is already missing. "
                +
                "Undo may restore it, or Reset Test State can clear "
                +
                "the tracked test state.";

            return false;
        }

        /*
         * The stable ID remains tracked after removal so Undo/Redo of
         * the removal can still target the same serialized identity.
         */
        return TerrainAuthoringModifierService.RemoveModifier(
            authoringData,
            worldSettings,
            stableId,
            out errorMessage
        );
    }

    private static bool ExecuteReorder(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        int direction,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !TryFindSelectedStamp(
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TryFindModifier(
                modifier.StableId,
                out _,
                out int currentIndex
            )
        )
        {
            errorMessage =
                "The selected modifier index could not be resolved.";
            return false;
        }

        int targetIndex =
            Mathf.Clamp(
                currentIndex + direction,
                0,
                Mathf.Max(
                    0,
                    authoringData.HeightModifierCount - 1
                )
            );

        return TerrainAuthoringModifierService.ReorderModifier(
            authoringData,
            worldSettings,
            modifier.StableId,
            targetIndex,
            out errorMessage
        );
    }

    private static bool ExecuteReset(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        out string errorMessage
    )
    {
        errorMessage = "";

        List<string> ids =
            new List<string>(controlledStampIds);

        foreach (string stableId in ids)
        {
            if (
                !TryFindModifier(
                    stableId,
                    out _,
                    out _
                )
            )
            {
                continue;
            }

            if (
                !TerrainAuthoringModifierService.RemoveModifier(
                    authoringData,
                    worldSettings,
                    stableId,
                    out errorMessage
                )
            )
            {
                return false;
            }
        }

        controlledStampIds.Clear();
        selectedControlledIndex = 0;
        SavePersistentState();

        return true;
    }

    private static void WaitForPreviewAndValidate()
    {
        EditorApplication.delayCall -= WaitForPreviewAndValidate;

        previewWaitCycleCount++;

        if (previewWaitCycleCount > MaximumPreviewWaitCycles)
        {
            Fail(
                pendingOperationLabel,
                "Timed out waiting for the preview transaction to settle."
            );

            scheduledOperation = RequestedOperation.None;
            return;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            EditorApplication.delayCall += WaitForPreviewAndValidate;
            return;
        }

        WorldSettings worldSettings = LoadWorldSettings();
        TerrainAuthoringData authoringData = LoadAuthoringData();

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            Fail(
                pendingOperationLabel,
                "WorldSettings or TerrainAuthoringData became unavailable."
            );

            scheduledOperation = RequestedOperation.None;
            return;
        }

        string currentOverall =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                authoringData
            );

        bool previewSettled =
            TerrainAuthoringPreviewService.PendingDirtyTileCount == 0
            &&
            TerrainAuthoringPreviewService.Status ==
                TerrainAuthoringPreviewStatus.Ready
            &&
            TerrainAuthoringPreviewService.SourceOverallAuthoringSignature ==
                currentOverall;

        if (!previewSettled)
        {
            if (
                TerrainAuthoringPreviewService.Status ==
                    TerrainAuthoringPreviewStatus.Error
            )
            {
                Fail(
                    pendingOperationLabel,
                    "The preview entered Error state while waiting for "
                    +
                    "the requested live stamp transaction.\n\n"
                    +
                    TerrainAuthoringPreviewService.StatusMessage
                );

                scheduledOperation = RequestedOperation.None;
                return;
            }

            EditorApplication.delayCall += WaitForPreviewAndValidate;
            return;
        }

        EvaluateSettledOperation(
            authoringData,
            worldSettings
        );

        scheduledOperation = RequestedOperation.None;
    }

    private static void EvaluateSettledOperation(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings
    )
    {
        TerrainLiveStampValidationBaseline after =
            CaptureBaseline(
                "After " + pendingOperationLabel,
                authoringData,
                worldSettings
            );

        List<string> checks = new List<string>();
        bool passed = true;

        int expectedDirtyTileCount =
            expectedDirtyTiles.Count;

        long incrementalDelta =
            after.TotalIncrementalSliceUpdates
            -
            lastBaseline.TotalIncrementalSliceUpdates;

        long compositeTileDelta =
            after.TotalCompositeTileCount
            -
            lastBaseline.TotalCompositeTileCount;

        long consideredDelta =
            after.TotalModifierConsideredCount
            -
            lastBaseline.TotalModifierConsideredCount;

        long modifierDispatchDelta =
            after.TotalModifierDispatchCount
            -
            lastBaseline.TotalModifierDispatchCount;

        long computeDispatchDelta =
            after.TotalComputeDispatchCount
            -
            lastBaseline.TotalComputeDispatchCount;

        AddCheck(
            checks,
            ref passed,
            after.CacheTextureId == lastBaseline.CacheTextureId,
            "Cache Texture ID remained unchanged",
            $"before={lastBaseline.CacheTextureId}, after={after.CacheTextureId}"
        );

        AddCheck(
            checks,
            ref passed,
            after.FullCacheBuilds == lastBaseline.FullCacheBuilds,
            "Full Cache Builds remained unchanged",
            $"before={lastBaseline.FullCacheBuilds}, after={after.FullCacheBuilds}"
        );

        AddCheck(
            checks,
            ref passed,
            after.RendererBindingCount == lastBaseline.RendererBindingCount,
            "Renderer binding count remained unchanged",
            $"before={lastBaseline.RendererBindingCount}, after={after.RendererBindingCount}"
        );

        AddCheck(
            checks,
            ref passed,
            after.CommittedHeightfieldSignature ==
                lastBaseline.CommittedHeightfieldSignature,
            "CommittedHeightfieldSignature remained unchanged",
            ShortSignature(after.CommittedHeightfieldSignature)
        );

        AddCheck(
            checks,
            ref passed,
            after.OverallAuthoringSignature ==
                expectedOverallAfterMutation,
            "OverallAuthoringSignature matches the requested authoring state",
            ShortSignature(after.OverallAuthoringSignature)
        );

        AddCheck(
            checks,
            ref passed,
            TerrainAuthoringPreviewService.SourceOverallAuthoringSignature ==
                after.OverallAuthoringSignature,
            "Preview acknowledges the current OverallAuthoringSignature",
            ShortSignature(
                TerrainAuthoringPreviewService
                    .SourceOverallAuthoringSignature
            )
        );

        AddCheck(
            checks,
            ref passed,
            after.AuthoringRevision ==
                revisionAfterRequestedOperation,
            "Preview refresh did not add another authoringRevision",
            $"expected={revisionAfterRequestedOperation}, after={after.AuthoringRevision}"
        );

        AddCheck(
            checks,
            ref passed,
            incrementalDelta == expectedDirtyTileCount,
            "Incremental slice updates match the dirty tile set",
            $"expected={expectedDirtyTileCount}, actual={incrementalDelta}"
        );

        AddCheck(
            checks,
            ref passed,
            compositeTileDelta == expectedDirtyTileCount,
            "Compositor tile count matches the dirty tile set",
            $"expected={expectedDirtyTileCount}, actual={compositeTileDelta}"
        );

        long expectedConsidered =
            (long)expectedDirtyTileCount
            *
            authoringData.HeightModifierCount;

        AddCheck(
            checks,
            ref passed,
            consideredDelta == expectedConsidered,
            "Modifier considered count is internally consistent",
            $"expected={expectedConsidered}, actual={consideredDelta}"
        );

        long expectedModifierDispatches =
            CalculateExpectedModifierDispatches(
                authoringData,
                worldSettings,
                expectedDirtyTiles
            );

        AddCheck(
            checks,
            ref passed,
            modifierDispatchDelta == expectedModifierDispatches,
            "Modifier dispatch count matches the current overlapping stack",
            $"expected={expectedModifierDispatches}, actual={modifierDispatchDelta}"
        );

        AddCheck(
            checks,
            ref passed,
            computeDispatchDelta == expectedModifierDispatches,
            "Compute dispatch count matches dispatched modifiers",
            $"expected={expectedModifierDispatches}, actual={computeDispatchDelta}"
        );

        StringBuilder details = new StringBuilder();

        foreach (string check in checks)
        {
            details.AppendLine(check);
        }

        lastReport =
            new TerrainLiveStampValidationReport
            {
                Operation = pendingOperationLabel,
                Passed = passed,
                ExpectedDirtyTileCount = expectedDirtyTileCount,
                ActualIncrementalSliceCount = incrementalDelta,
                ActualCompositeTileCount = compositeTileDelta,
                ModifierConsideredCount = consideredDelta,
                ModifierDispatchCount = modifierDispatchDelta,
                ComputeDispatchCount = computeDispatchDelta,
                DirtyTileSummary = BuildTileSummary(expectedDirtyTiles),
                Summary = passed
                    ? "All automatic transaction checks passed."
                    : "One or more automatic transaction checks failed.",
                Details = details.ToString()
            };

        status = passed
            ? TerrainLiveStampValidationStatus.Passed
            : TerrainLiveStampValidationStatus.Failed;

        statusMessage =
            pendingOperationLabel
            +
            ": "
            +
            lastReport.Summary;
    }

    private static void ValidateSelectedBordersScheduled()
    {
        EditorApplication.delayCall -= ValidateSelectedBordersScheduled;

        borderValidationPassed = false;

        if (
            !TryValidateOperationPrerequisites(
                out _,
                out WorldSettings worldSettings,
                out string prerequisiteError
            )
        )
        {
            borderValidationSummary =
                "Blocked: " + prerequisiteError;
            return;
        }

        if (
            !TryFindSelectedStamp(
                out TerrainStampModifier modifier,
                out string modifierError
            )
        )
        {
            borderValidationSummary =
                "Blocked: " + modifierError;
            return;
        }

        HashSet<Vector2Int> tiles =
            new HashSet<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                modifier.GetAffectedWorldBounds(),
                tiles,
                1
            );

        if (tiles.Count < 2)
        {
            borderValidationSummary =
                "Blocked: the selected stamp does not currently touch "
                +
                "enough tiles for a shared-border comparison.";
            return;
        }

        Dictionary<Vector2Int, float[]> sliceValues =
            new Dictionary<Vector2Int, float[]>();

        foreach (Vector2Int tile in tiles)
        {
            if (
                !TerrainAuthoringPreviewService.TryReadCompositeSlice(
                    tile.x,
                    tile.y,
                    out float[] values,
                    out string readbackError
                )
            )
            {
                borderValidationSummary =
                    $"Failed to read preview tile ({tile.x}, {tile.y}): "
                    +
                    readbackError;
                return;
            }

            sliceValues[tile] = values;
        }

        int samplesPerSide =
            TerrainAuthoringPreviewService.SamplesPerSide;

        int xBorderPairs = 0;
        int zBorderPairs = 0;
        int cornerGroups = 0;
        int mismatches = 0;
        float largestDifference = 0f;

        foreach (Vector2Int tile in tiles)
        {
            Vector2Int right =
                new Vector2Int(tile.x + 1, tile.y);

            if (tiles.Contains(right))
            {
                xBorderPairs++;

                float[] a = sliceValues[tile];
                float[] b = sliceValues[right];

                for (
                    int sampleZ = 0;
                    sampleZ < samplesPerSide;
                    sampleZ++
                )
                {
                    float valueA =
                        a[
                            sampleZ * samplesPerSide
                            +
                            (samplesPerSide - 1)
                        ];

                    float valueB =
                        b[
                            sampleZ * samplesPerSide
                        ];

                    if (
                        !ValuesMatch(
                            valueA,
                            valueB,
                            out float difference
                        )
                    )
                    {
                        mismatches++;
                        largestDifference =
                            Mathf.Max(
                                largestDifference,
                                difference
                            );
                    }
                }
            }

            Vector2Int top =
                new Vector2Int(tile.x, tile.y + 1);

            if (tiles.Contains(top))
            {
                zBorderPairs++;

                float[] a = sliceValues[tile];
                float[] b = sliceValues[top];

                for (
                    int sampleX = 0;
                    sampleX < samplesPerSide;
                    sampleX++
                )
                {
                    float valueA =
                        a[
                            (samplesPerSide - 1)
                            *
                            samplesPerSide
                            +
                            sampleX
                        ];

                    float valueB =
                        b[sampleX];

                    if (
                        !ValuesMatch(
                            valueA,
                            valueB,
                            out float difference
                        )
                    )
                    {
                        mismatches++;
                        largestDifference =
                            Mathf.Max(
                                largestDifference,
                                difference
                            );
                    }
                }
            }

            Vector2Int rightTop =
                new Vector2Int(tile.x + 1, tile.y + 1);

            if (
                tiles.Contains(right)
                &&
                tiles.Contains(top)
                &&
                tiles.Contains(rightTop)
            )
            {
                cornerGroups++;

                float v00 =
                    sliceValues[tile][
                        samplesPerSide
                        *
                        samplesPerSide
                        -
                        1
                    ];

                float v10 =
                    sliceValues[right][
                        (samplesPerSide - 1)
                        *
                        samplesPerSide
                    ];

                float v01 =
                    sliceValues[top][
                        samplesPerSide - 1
                    ];

                float v11 =
                    sliceValues[rightTop][0];

                float minimum =
                    Mathf.Min(
                        Mathf.Min(v00, v10),
                        Mathf.Min(v01, v11)
                    );

                float maximum =
                    Mathf.Max(
                        Mathf.Max(v00, v10),
                        Mathf.Max(v01, v11)
                    );

                if (
                    !ValuesMatch(
                        minimum,
                        maximum,
                        out float difference
                    )
                )
                {
                    mismatches++;
                    largestDifference =
                        Mathf.Max(
                            largestDifference,
                            difference
                        );
                }
            }
        }

        if (
            xBorderPairs == 0
            &&
            zBorderPairs == 0
        )
        {
            borderValidationSummary =
                "Blocked: no adjacent affected preview tiles were found.";
            return;
        }

        borderValidationPassed =
            mismatches == 0;

        borderValidationSummary =
            (borderValidationPassed ? "PASS" : "FAIL")
            +
            $": X borders={xBorderPairs}, "
            +
            $"Z borders={zBorderPairs}, "
            +
            $"four-tile corners={cornerGroups}, "
            +
            $"mismatches={mismatches}, "
            +
            $"largest difference={largestDifference:R}.";
    }

    private static bool TryValidateOperationPrerequisites(
        out TerrainAuthoringData authoringData,
        out WorldSettings worldSettings,
        out string errorMessage
    )
    {
        authoringData = null;
        worldSettings = null;
        errorMessage = "";

        if (
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            errorMessage =
                "Live stamp validation is available only in Edit Mode.";
            return false;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            errorMessage =
                "Wait for Unity to finish compiling/importing.";
            return false;
        }

        if (pendingStampAsset == null)
        {
            if (
                !TerrainLiveStampValidationFixtureUtility
                    .TryGetOrCreateStampAsset(
                        out pendingStampAsset,
                        out string fixtureError
                    )
            )
            {
                errorMessage =
                    "The generated live-stamp validation fixture could not "
                    +
                    "be prepared. "
                    +
                    fixtureError;

                return false;
            }

            SavePersistentState();
        }

        if (
            !TerrainAuthoringPreviewService.Enabled
            ||
            !TerrainAuthoringPreviewService.CacheReady
            ||
            TerrainAuthoringPreviewService.Status !=
                TerrainAuthoringPreviewStatus.Ready
        )
        {
            errorMessage =
                "Height Preview must be enabled and Ready.";
            return false;
        }

        if (
            TerrainAuthoringPreviewService.PendingDirtyTileCount != 0
        )
        {
            errorMessage =
                "Wait for the current dirty preview transaction to finish.";
            return false;
        }

        authoringData = LoadAuthoringData();
        worldSettings = LoadWorldSettings();

        if (
            authoringData == null
            ||
            worldSettings == null
        )
        {
            errorMessage =
                "WorldSettings or TerrainAuthoringData could not be loaded.";
            return false;
        }

        string currentOverall =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                authoringData
            );

        if (
            TerrainAuthoringPreviewService.SourceOverallAuthoringSignature !=
                currentOverall
        )
        {
            errorMessage =
                "The preview does not currently represent the complete "
                +
                "OverallAuthoringSignature.";
            return false;
        }

        return true;
    }

    private static TerrainLiveStampValidationBaseline CaptureBaseline(
        string label,
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings
    )
    {
        return new TerrainLiveStampValidationBaseline
        {
            Label = label,
            CacheTextureId =
                TerrainAuthoringPreviewService.CacheTextureInstanceId,
            FullCacheBuilds =
                TerrainAuthoringPreviewService.FullCommittedBuildCount,
            TotalIncrementalSliceUpdates =
                TerrainAuthoringPreviewService.TotalIncrementalSliceUpdates,
            TotalCompositeTileCount =
                TerrainAuthoringPreviewService.TotalCompositeDispatchTileCount,
            TotalModifierConsideredCount =
                TerrainAuthoringPreviewService
                    .TotalCompositeModifierConsideredCount,
            TotalModifierDispatchCount =
                TerrainAuthoringPreviewService
                    .TotalCompositeModifierDispatchCount,
            TotalComputeDispatchCount =
                TerrainAuthoringPreviewService
                    .TotalCompositeComputeDispatchCount,
            RendererBindingCount =
                TerrainAuthoringPreviewService.DiagnosticBindingApplyCount,
            OverallAuthoringSignature =
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                ),
            CommittedHeightfieldSignature =
                TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                    worldSettings
                ),
            AuthoringRevision =
                authoringData.authoringRevision,
            MinimumPreviewHeight =
                TerrainAuthoringPreviewService.MinimumPreviewHeight,
            MaximumPreviewHeight =
                TerrainAuthoringPreviewService.MaximumPreviewHeight
        };
    }

    private static long CalculateExpectedModifierDispatches(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        IReadOnlyList<Vector2Int> tiles
    )
    {
        if (
            authoringData == null
            ||
            worldSettings == null
            ||
            tiles == null
        )
        {
            return 0L;
        }

        float tileWorldSize =
            worldSettings.HeightTileWorldSize;

        long count = 0L;

        IReadOnlyList<TerrainHeightModifier> modifiers =
            authoringData.HeightModifiers;

        foreach (Vector2Int tile in tiles)
        {
            Vector2 tileMin =
                new Vector2(
                    tile.x * tileWorldSize,
                    tile.y * tileWorldSize
                );

            Vector2 tileMax =
                tileMin
                +
                new Vector2(
                    tileWorldSize,
                    tileWorldSize
                );

            foreach (TerrainHeightModifier modifier in modifiers)
            {
                if (
                    modifier == null
                    ||
                    !modifier.Enabled
                )
                {
                    continue;
                }

                Bounds bounds =
                    modifier.GetAffectedWorldBounds();

                Vector3 minimum = bounds.min;
                Vector3 maximum = bounds.max;

                bool overlaps =
                    maximum.x >= tileMin.x
                    &&
                    minimum.x <= tileMax.x
                    &&
                    maximum.z >= tileMin.y
                    &&
                    minimum.z <= tileMax.y;

                if (!overlaps)
                {
                    continue;
                }

                if (
                    !(modifier is TerrainStampModifier stamp)
                    ||
                    stamp.BlendMode != TerrainHeightBlendMode.Additive
                    ||
                    stamp.StampAsset == null
                    ||
                    stamp.StampAsset.HeightTexture == null
                    ||
                    Mathf.Approximately(stamp.HeightDelta, 0f)
                )
                {
                    continue;
                }

                count++;
            }
        }

        return count;
    }

    private static bool TryFindSelectedStamp(
        out TerrainStampModifier modifier,
        out string errorMessage
    )
    {
        modifier = null;
        errorMessage = "";

        string stableId = SelectedStableId;

        if (string.IsNullOrEmpty(stableId))
        {
            errorMessage =
                "No controlled test stamp is selected.";
            return false;
        }

        if (
            !TryFindModifier(
                stableId,
                out TerrainHeightModifier heightModifier,
                out _
            )
        )
        {
            errorMessage =
                "The selected controlled stamp is missing from "
                +
                "TerrainAuthoringData.";
            return false;
        }

        modifier =
            heightModifier as TerrainStampModifier;

        if (modifier == null)
        {
            errorMessage =
                "The selected controlled modifier is not a TerrainStampModifier.";
            return false;
        }

        return true;
    }

    private static bool TryFindModifier(
        string stableId,
        out TerrainHeightModifier modifier,
        out int index
    )
    {
        modifier = null;
        index = -1;

        if (string.IsNullOrEmpty(stableId))
        {
            return false;
        }

        TerrainAuthoringData authoringData =
            LoadAuthoringData();

        if (authoringData == null)
        {
            return false;
        }

        IReadOnlyList<TerrainHeightModifier> modifiers =
            authoringData.HeightModifiers;

        for (
            int modifierIndex = 0;
            modifierIndex < modifiers.Count;
            modifierIndex++
        )
        {
            TerrainHeightModifier candidate =
                modifiers[modifierIndex];

            if (
                candidate != null
                &&
                candidate.StableId == stableId
            )
            {
                modifier = candidate;
                index = modifierIndex;
                return true;
            }
        }

        return false;
    }

    private static TerrainAuthoringData LoadAuthoringData()
    {
        return AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
            WorldMeshesPaths.TerrainAuthoringDataAssetPath
        );
    }

    private static WorldSettings LoadWorldSettings()
    {
        return AssetDatabase.LoadAssetAtPath<WorldSettings>(
            WorldMeshesPaths.WorldSettingsAssetPath
        );
    }

    private static bool ValuesMatch(
        float a,
        float b,
        out float difference
    )
    {
        difference = Mathf.Abs(a - b);

        float scale =
            Mathf.Max(
                1f,
                Mathf.Abs(a),
                Mathf.Abs(b)
            );

        float tolerance =
            Mathf.Max(
                0.00001f,
                scale * 0.00001f
            );

        return difference <= tolerance;
    }

    private static void AddCheck(
        List<string> checks,
        ref bool passed,
        bool condition,
        string name,
        string details
    )
    {
        if (!condition)
        {
            passed = false;
        }

        checks.Add(
            (condition ? "PASS: " : "FAIL: ")
            +
            name
            +
            " ("
            +
            details
            +
            ")"
        );
    }

    private static string BuildTileSummary(
        IReadOnlyList<Vector2Int> tiles
    )
    {
        if (
            tiles == null
            ||
            tiles.Count == 0
        )
        {
            return "none";
        }

        StringBuilder builder = new StringBuilder();

        for (
            int index = 0;
            index < tiles.Count;
            index++
        )
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append("(");
            builder.Append(tiles[index].x);
            builder.Append(",");
            builder.Append(tiles[index].y);
            builder.Append(")");
        }

        return builder.ToString();
    }

    private static string ShortSignature(string signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return "(none)";
        }

        return signature.Length <= 12
            ? signature
            : signature.Substring(0, 12);
    }

    private static void Fail(
        string operation,
        string message
    )
    {
        status = TerrainLiveStampValidationStatus.Failed;

        statusMessage =
            operation
            +
            ": "
            +
            message;

        lastReport =
            new TerrainLiveStampValidationReport
            {
                Operation = operation,
                Passed = false,
                Summary = message,
                Details = message,
                DirtyTileSummary =
                    BuildTileSummary(expectedDirtyTiles)
            };
    }

    private static void SetReady(string message)
    {
        status = TerrainLiveStampValidationStatus.Ready;
        statusMessage = message;
    }

    private static float SanitizeFinite(
        float value,
        float fallback
    )
    {
        return
            float.IsNaN(value)
            ||
            float.IsInfinity(value)
                ? fallback
                : value;
    }

    private static string Key(string suffix)
    {
        return editorPrefsPrefix + suffix;
    }

    private static void LoadPersistentState()
    {
        controlledStampIds.Clear();

        string savedIds =
            EditorPrefs.GetString(
                Key("ControlledIds"),
                ""
            );

        if (!string.IsNullOrEmpty(savedIds))
        {
            string[] ids = savedIds.Split('|');

            foreach (string id in ids)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    controlledStampIds.Add(id);
                }
            }
        }

        selectedControlledIndex =
            EditorPrefs.GetInt(
                Key("SelectedIndex"),
                0
            );

        pendingPositionXZ =
            new Vector2(
                EditorPrefs.GetFloat(Key("PositionX"), 0f),
                EditorPrefs.GetFloat(Key("PositionZ"), 0f)
            );

        pendingSizeXZ =
            new Vector2(
                Mathf.Max(
                    0.01f,
                    EditorPrefs.GetFloat(Key("SizeX"), 256f)
                ),
                Mathf.Max(
                    0.01f,
                    EditorPrefs.GetFloat(Key("SizeZ"), 256f)
                )
            );

        pendingHeightDelta =
            EditorPrefs.GetFloat(
                Key("HeightDelta"),
                30f
            );

        pendingFalloff =
            Mathf.Clamp01(
                EditorPrefs.GetFloat(
                    Key("Falloff"),
                    0.25f
                )
            );

        pendingEnabled =
            EditorPrefs.GetBool(
                Key("Enabled"),
                true
            );

        string assetGuid =
            EditorPrefs.GetString(
                Key("StampAssetGuid"),
                ""
            );

        if (!string.IsNullOrEmpty(assetGuid))
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    assetGuid
                );

            pendingStampAsset =
                AssetDatabase.LoadAssetAtPath<TerrainHeightStampAsset>(
                    path
                );
        }
    }

    private static void SavePersistentState()
    {
        EditorPrefs.SetString(
            Key("ControlledIds"),
            string.Join("|", controlledStampIds)
        );

        EditorPrefs.SetInt(
            Key("SelectedIndex"),
            selectedControlledIndex
        );

        EditorPrefs.SetFloat(
            Key("PositionX"),
            pendingPositionXZ.x
        );

        EditorPrefs.SetFloat(
            Key("PositionZ"),
            pendingPositionXZ.y
        );

        EditorPrefs.SetFloat(
            Key("SizeX"),
            pendingSizeXZ.x
        );

        EditorPrefs.SetFloat(
            Key("SizeZ"),
            pendingSizeXZ.y
        );

        EditorPrefs.SetFloat(
            Key("HeightDelta"),
            pendingHeightDelta
        );

        EditorPrefs.SetFloat(
            Key("Falloff"),
            pendingFalloff
        );

        EditorPrefs.SetBool(
            Key("Enabled"),
            pendingEnabled
        );

        string assetPath =
            pendingStampAsset != null
                ? AssetDatabase.GetAssetPath(
                    pendingStampAsset
                )
                : "";

        string assetGuid =
            string.IsNullOrEmpty(assetPath)
                ? ""
                : AssetDatabase.AssetPathToGUID(
                    assetPath
                );

        EditorPrefs.SetString(
            Key("StampAssetGuid"),
            assetGuid
        );
    }
}
