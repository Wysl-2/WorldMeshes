using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TerrainRegionalElevationMultiSelectValidationUtility
{
    private enum ValidationOutcome
    {
        Pass,
        Fail,
        Blocked
    }

    private sealed class ValidationResult
    {
        public string Name;
        public ValidationOutcome Outcome;
        public string Details;
    }

    private struct NodeSpec
    {
        public Vector2 Position;
        public float Elevation;

        public NodeSpec(Vector2 position, float elevation)
        {
            Position = position;
            Elevation = elevation;
        }
    }

    private static readonly List<ValidationResult> results =
        new List<ValidationResult>();

    private static bool validationRunning;
    private static bool validationScheduled;
    private static WorldSettings worldSettings;
    private static TerrainAuthoringData realAuthoringData;
    private static int realRevisionBefore;
    private static string realCommittedBefore = "";
    private static string realOverallBefore = "";
    private static TerrainRegionalElevationSource realRegionalBefore;
    private static readonly List<string> realSelectionBefore = new List<string>();
    private static string realPrimaryBefore = "";
    private static bool baselineCaptured;

    public static bool IsRunning => validationRunning || validationScheduled;

    public static void ValidateRegionalElevationMultiSelect()
    {
        if (validationRunning || validationScheduled)
        {
            return;
        }

        validationScheduled = true;
        EditorApplication.delayCall -= RunScheduledValidation;
        EditorApplication.delayCall += RunScheduledValidation;
    }

    private static void RunScheduledValidation()
    {
        EditorApplication.delayCall -= RunScheduledValidation;
        if (!validationScheduled || validationRunning)
        {
            return;
        }

        validationScheduled = false;
        validationRunning = true;
        baselineCaptured = false;
        results.Clear();

        try
        {
            if (!TryValidatePrerequisites(out string prerequisiteError))
            {
                AddResult("Validation prerequisites", ValidationOutcome.Blocked, prerequisiteError);
                return;
            }

            CaptureRealBaseline();
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Pass,
                "Current WorldSettings, TerrainAuthoringData, and committed/overall signatures are available.");

            ValidateSelectionSemantics();
            ValidateSelectionPruningAndPrimaryFallback();
            ValidateRectangleAndPivotHelpers();
            ValidateCommonDeltaClamp();
            ValidateBatchElevation();
            ValidateBatchAtomicityAndNoOp();
            ValidateGroupInteractiveCommit();
            ValidateGroupInteractiveCancelAndNoOp();
            ValidateSelectionLock();
            ValidateSummaryStates();
            ValidateRealStateUnchanged();
        }
        catch (Exception exception)
        {
            AddResult("Unexpected validation exception", ValidationOutcome.Fail, exception.ToString());
        }
        finally
        {
            if (baselineCaptured)
            {
                RestoreRealEditorSelection();
            }

            validationRunning = false;
            WriteReport();
        }
    }

    private static bool TryValidatePrerequisites(out string errorMessage)
    {
        errorMessage = "";

        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage = "Validation cannot run in or while entering Play Mode.";
            return false;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            errorMessage = "Wait for Unity to finish compiling/importing and run validation again.";
            return false;
        }

        if (TerrainRegionalElevationService.HasActiveInteractiveEdit)
        {
            errorMessage = "Finish or cancel the active regional elevation interactive edit before validation.";
            return false;
        }

        worldSettings = AssetDatabase.LoadAssetAtPath<WorldSettings>(
            WorldMeshesPaths.WorldSettingsAssetPath);
        realAuthoringData = AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
            WorldMeshesPaths.TerrainAuthoringDataAssetPath);

        if (worldSettings == null || realAuthoringData == null)
        {
            errorMessage = "WorldSettings or TerrainAuthoringData could not be loaded.";
            return false;
        }

        string committed = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
            worldSettings,
            realAuthoringData);

        if (string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        {
            errorMessage = "Initialize the committed authoring heightfield before running Package 7 validation.";
            return false;
        }

        return true;
    }

    private static void CaptureRealBaseline()
    {
        realRevisionBefore = realAuthoringData.authoringRevision;
        realCommittedBefore = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);
        realOverallBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
            worldSettings,
            realAuthoringData);
        realRegionalBefore = realAuthoringData.RegionalElevationSource;

        realSelectionBefore.Clear();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            realSelectionBefore);
        realPrimaryBefore = TerrainRegionalElevationSelectionState.GetPrimaryStableId(
            realAuthoringData);
        baselineCaptured = true;
    }

    private static void ValidateSelectionSemantics()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(10f, 0f), 20f),
            new NodeSpec(new Vector2(20f, 0f), 30f));

        try
        {
            IReadOnlyList<TerrainElevationNode> nodes = GetNodes(data);
            string a = nodes[0].StableId;
            string b = nodes[1].StableId;
            string c = nodes[2].StableId;

            bool ok =
                TerrainRegionalElevationSelectionState.SelectOnly(data, a, out _) &&
                TerrainRegionalElevationSelectionState.AddNode(data, b, out _) &&
                TerrainRegionalElevationSelectionState.AddNode(data, c, out _) &&
                TerrainRegionalElevationSelectionState.GetSelectedCount(data) == 3 &&
                TerrainRegionalElevationSelectionState.GetPrimaryStableId(data) == c &&
                TerrainRegionalElevationSelectionState.ToggleNode(data, b, out _) &&
                TerrainRegionalElevationSelectionState.GetSelectedCount(data) == 2 &&
                !TerrainRegionalElevationSelectionState.IsSelected(data, b) &&
                TerrainRegionalElevationSelectionState.SelectOnly(data, b, out _) &&
                TerrainRegionalElevationSelectionState.GetSelectedCount(data) == 1 &&
                TerrainRegionalElevationSelectionState.GetPrimaryStableId(data) == b &&
                TerrainRegionalElevationSelectionState.ClearSelection(data, out _) &&
                TerrainRegionalElevationSelectionState.GetSelectedCount(data) == 0;

            AddResult(
                "StableId multi-selection replace/add/toggle/clear semantics",
                ok ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                ok
                    ? "Selection remains editor-only, supports additive/toggle behavior, and maintains one primary StableId."
                    : "One or more Package 7 selection operations produced an unexpected selection state.");
        }
        finally
        {
            DestroyData(data);
        }
    }

    private static void ValidateSelectionPruningAndPrimaryFallback()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(Vector2.zero, 10f),
            new NodeSpec(Vector2.right, 20f),
            new NodeSpec(Vector2.one, 30f));

        try
        {
            TerrainNodeElevationSource source = (TerrainNodeElevationSource)data.RegionalElevationSource;
            IReadOnlyList<TerrainElevationNode> nodes = source.Nodes;
            string a = nodes[0].StableId;
            string b = nodes[1].StableId;
            string c = nodes[2].StableId;

            TerrainRegionalElevationSelectionState.SetSelection(
                data,
                new[] { a, b, c },
                b,
                out _);

            source.RemoveNodeAtInternal(1);
            TerrainRegionalElevationSelectionState.ValidateSelection(data);

            bool ok =
                TerrainRegionalElevationSelectionState.GetSelectedCount(data) == 2 &&
                TerrainRegionalElevationSelectionState.IsSelected(data, a) &&
                TerrainRegionalElevationSelectionState.IsSelected(data, c) &&
                TerrainRegionalElevationSelectionState.GetPrimaryStableId(data) == a;

            AddResult(
                "Invalid StableIds are pruned and primary falls back deterministically",
                ok ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                ok
                    ? "Removing the primary node pruned only that StableId and selected the first remaining node in source order as primary."
                    : "Selection pruning cleared valid nodes or did not restore a deterministic primary.");
        }
        finally
        {
            DestroyData(data);
        }
    }

    private static void ValidateRectangleAndPivotHelpers()
    {
        Rect a = TerrainRegionalElevationGroupUtility.NormalizeGuiRect(
            new Vector2(10f, 20f),
            new Vector2(50f, 80f));
        Rect b = TerrainRegionalElevationGroupUtility.NormalizeGuiRect(
            new Vector2(50f, 80f),
            new Vector2(10f, 20f));

        bool rectOk =
            a == b &&
            TerrainRegionalElevationGroupUtility.ContainsGuiPoint(a, new Vector2(25f, 30f)) &&
            !TerrainRegionalElevationGroupUtility.ContainsGuiPoint(a, new Vector2(5f, 30f));

        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 0f), 0f),
            new NodeSpec(new Vector2(0f, 10f), 0f),
            new NodeSpec(new Vector2(10f, 10f), 0f));

        try
        {
            Vector2 pivot = TerrainRegionalElevationGroupUtility.CalculateAveragePositionXZ(GetNodes(data));
            bool pivotOk = pivot == new Vector2(5f, 5f);

            AddResult(
                "Marquee rectangle normalization and group pivot",
                rectOk && pivotOk ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                rectOk && pivotOk
                    ? "GUI rectangles normalize identically in either drag direction and a four-corner group pivots at (5,5)."
                    : $"Unexpected marquee helper or pivot result: pivot={pivot}." );
        }
        finally
        {
            DestroyData(data);
        }
    }

    private static void ValidateCommonDeltaClamp()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(20f, 20f), 0f),
            new NodeSpec(new Vector2(80f, 80f), 0f));

        try
        {
            Vector2 worldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(worldSettings);
            IReadOnlyList<TerrainElevationNode> nodes = GetNodes(data);

            Vector2 requested = new Vector2(worldSize.x, worldSize.y);
            Vector2 clamped = TerrainRegionalElevationGroupUtility.ClampCommonDeltaToWorld(
                worldSettings,
                nodes,
                requested);

            float expectedX = worldSize.x - 80f;
            float expectedZ = worldSize.y - 80f;
            bool ok =
                Mathf.Approximately(clamped.x, expectedX) &&
                Mathf.Approximately(clamped.y, expectedZ);

            AddResult(
                "Group world-bound clamp applies one common translation",
                ok ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                ok
                    ? "The requested translation was limited by the selected group extents, preserving relative spacing."
                    : $"Expected ({expectedX},{expectedZ}) but received {clamped}." );
        }
        finally
        {
            DestroyData(data);
        }
    }

    private static void ValidateBatchElevation()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(Vector2.zero, 20f),
            new NodeSpec(Vector2.right, 50f),
            new NodeSpec(Vector2.one, 100f));

        try
        {
            List<string> ids = GetIds(data);
            int revisionBefore = data.authoringRevision;

            bool setOk = TerrainRegionalElevationService.SetNodesElevation(
                data, worldSettings, ids, 120f, out _);
            bool all120 = AllElevations(data, 120f);
            bool oneRevision = data.authoringRevision == revisionBefore + 1;

            int revisionAfterSet = data.authoringRevision;
            bool raiseOk = TerrainRegionalElevationService.OffsetNodesElevation(
                data, worldSettings, ids, 25f, out _);
            bool all145 = AllElevations(data, 145f);
            bool secondRevision = data.authoringRevision == revisionAfterSet + 1;

            bool lowerOk = TerrainRegionalElevationService.OffsetNodesElevation(
                data, worldSettings, ids, -150f, out _);
            bool allMinus5 = AllElevations(data, -5f);

            AddResult(
                "Batch Set/Raise/Lower elevation are atomic service transactions",
                setOk && all120 && oneRevision && raiseOk && all145 && secondRevision && lowerOk && allMinus5
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                setOk && all120 && oneRevision && raiseOk && all145 && secondRevision && lowerOk && allMinus5
                    ? "Set, positive offset, and negative offset updated all selected nodes together and each changed operation advanced revision once."
                    : "Batch elevation values or revision semantics did not match Package 7 requirements.");
        }
        finally
        {
            DestroyData(data);
        }
    }

    private static void ValidateBatchAtomicityAndNoOp()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(Vector2.zero, 30f),
            new NodeSpec(Vector2.one, 40f));

        try
        {
            List<string> ids = GetIds(data);
            int revisionBefore = data.authoringRevision;
            float aBefore = GetNodes(data)[0].Elevation;
            float bBefore = GetNodes(data)[1].Elevation;

            List<string> invalid = new List<string> { ids[0], "missing-stable-id", ids[1] };
            bool rejected = !TerrainRegionalElevationService.SetNodesElevation(
                data, worldSettings, invalid, 999f, out _);
            bool unchangedAfterReject =
                GetNodes(data)[0].Elevation == aBefore &&
                GetNodes(data)[1].Elevation == bBefore &&
                data.authoringRevision == revisionBefore;

            bool zeroOk = TerrainRegionalElevationService.OffsetNodesElevation(
                data, worldSettings, ids, 0f, out _);
            bool noOp = data.authoringRevision == revisionBefore;

            List<string> duplicate = new List<string> { ids[0], ids[0] };
            bool duplicateRejected = !TerrainRegionalElevationService.OffsetNodesElevation(
                data, worldSettings, duplicate, 5f, out _);

            AddResult(
                "Batch validation is atomic and zero/duplicate inputs are safe",
                rejected && unchangedAfterReject && zeroOk && noOp && duplicateRejected
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                rejected && unchangedAfterReject && zeroOk && noOp && duplicateRejected
                    ? "Invalid StableIds fail before mutation, zero offset is a no-op, and duplicate StableIds are rejected instead of double-applying."
                    : "Batch validation allowed a partial/duplicate mutation or advanced revision for a no-op.");
        }
        finally
        {
            DestroyData(data);
        }
    }

    private static void ValidateGroupInteractiveCommit()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(10f, 10f), 20f),
            new NodeSpec(new Vector2(20f, 30f), 40f),
            new NodeSpec(new Vector2(50f, 60f), 80f));

        try
        {
            List<string> ids = GetIds(data);
            Vector2[] before = GetPositions(data);
            float[] elevations = GetElevations(data);
            int revisionBefore = data.authoringRevision;

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeGroupEdit(
                data, worldSettings, ids, "Validate Group Move", out _);
            bool sampleA = began && TerrainRegionalElevationService.UpdateInteractiveNodeGroupPositionDelta(
                new Vector2(5f, 10f), out _);
            bool sampleB = sampleA && TerrainRegionalElevationService.UpdateInteractiveNodeGroupPositionDelta(
                new Vector2(15f, -5f), out _);
            bool noHotRevision = data.authoringRevision == revisionBefore;
            bool committed = sampleB && TerrainRegionalElevationService.CommitInteractiveNodeGroupEdit(out _);

            Vector2[] after = GetPositions(data);
            bool sameDelta = true;
            bool elevationsPreserved = true;
            for (int index = 0; index < after.Length; index++)
            {
                sameDelta &= after[index] - before[index] == new Vector2(15f, -5f);
                elevationsPreserved &= GetNodes(data)[index].Elevation == elevations[index];
            }

            bool oneRevision = data.authoringRevision == revisionBefore + 1;

            AddResult(
                "Interactive group move commits one final transaction",
                began && sampleA && sampleB && noHotRevision && committed && sameDelta && elevationsPreserved && oneRevision
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                began && sampleA && sampleB && noHotRevision && committed && sameDelta && elevationsPreserved && oneRevision
                    ? "Multiple group samples used initial positions plus the current gesture delta; commit advanced revision once and preserved elevations/relative offsets."
                    : "Group interactive commit, revision, or common-delta semantics failed.");
        }
        finally
        {
            if (TerrainRegionalElevationService.HasActiveInteractiveGroupEdit)
            {
                TerrainRegionalElevationService.CancelInteractiveNodeGroupEdit(out _);
            }
            DestroyData(data);
        }
    }

    private static void ValidateGroupInteractiveCancelAndNoOp()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(5f, 5f), 10f),
            new NodeSpec(new Vector2(15f, 15f), 20f));

        try
        {
            List<string> ids = GetIds(data);
            Vector2[] before = GetPositions(data);
            int revisionBefore = data.authoringRevision;

            bool beganCancel = TerrainRegionalElevationService.BeginInteractiveNodeGroupEdit(
                data, worldSettings, ids, "Validate Group Cancel", out _);
            bool movedCancel = beganCancel && TerrainRegionalElevationService.UpdateInteractiveNodeGroupPositionDelta(
                new Vector2(25f, 10f), out _);
            bool cancelled = movedCancel && TerrainRegionalElevationService.CancelInteractiveNodeGroupEdit(out _);
            bool restored = PositionsEqual(data, before) && data.authoringRevision == revisionBefore;

            bool beganNoOp = TerrainRegionalElevationService.BeginInteractiveNodeGroupEdit(
                data, worldSettings, ids, "Validate Group NoOp", out _);
            bool movedAway = beganNoOp && TerrainRegionalElevationService.UpdateInteractiveNodeGroupPositionDelta(
                new Vector2(10f, 0f), out _);
            bool movedHome = movedAway && TerrainRegionalElevationService.UpdateInteractiveNodeGroupPositionDelta(
                Vector2.zero, out _);
            bool committedNoOp = movedHome && TerrainRegionalElevationService.CommitInteractiveNodeGroupEdit(out _);
            bool noOpRestored = PositionsEqual(data, before) && data.authoringRevision == revisionBefore;

            AddResult(
                "Group cancel and return-to-start restore complete starting state",
                beganCancel && movedCancel && cancelled && restored && beganNoOp && movedAway && movedHome && committedNoOp && noOpRestored
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                beganCancel && movedCancel && cancelled && restored && beganNoOp && movedAway && movedHome && committedNoOp && noOpRestored
                    ? "Cancel restored every node, and a gesture ending exactly at its start committed as a no-op."
                    : "Cancel/no-op group transaction semantics failed.");
        }
        finally
        {
            if (TerrainRegionalElevationService.HasActiveInteractiveGroupEdit)
            {
                TerrainRegionalElevationService.CancelInteractiveNodeGroupEdit(out _);
            }
            DestroyData(data);
        }
    }

    private static void ValidateSelectionLock()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(Vector2.zero, 10f),
            new NodeSpec(Vector2.one, 20f));

        try
        {
            List<string> ids = GetIds(data);
            TerrainRegionalElevationSelectionState.SetSelection(data, ids, ids[0], out _);
            bool began = TerrainRegionalElevationService.BeginInteractiveNodeGroupEdit(
                data, worldSettings, ids, "Validate Selection Lock", out _);
            bool selectionRejected = began && !TerrainRegionalElevationSelectionState.ToggleNode(
                data, ids[0], out _);
            bool countUnchanged = TerrainRegionalElevationSelectionState.GetSelectedCount(data) == 2;
            TerrainRegionalElevationService.CancelInteractiveNodeGroupEdit(out _);

            AddResult(
                "Selection is locked to the transaction-owned StableId set during group edits",
                began && selectionRejected && countUnchanged
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                began && selectionRejected && countUnchanged
                    ? "Selection mutation was rejected while the group transaction was active."
                    : "Selection changed while an interactive group edit owned the nodes.");
        }
        finally
        {
            if (TerrainRegionalElevationService.HasActiveInteractiveGroupEdit)
            {
                TerrainRegionalElevationService.CancelInteractiveNodeGroupEdit(out _);
            }
            DestroyData(data);
        }
    }

    private static void ValidateSummaryStates()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(Vector2.zero, 100f),
            new NodeSpec(Vector2.right, 100f),
            new NodeSpec(Vector2.one, 100f));

        try
        {
            List<string> ids = GetIds(data);
            TerrainRegionalElevationSelectionState.SetSelection(data, ids, ids[2], out _);

            bool commonBuilt = TerrainRegionalElevationGroupUtility.TryBuildSelectionSummary(
                data,
                out TerrainRegionalElevationSelectionSummary common,
                out _);

            GetNodes(data)[1].SetElevationInternal(150f);
            bool mixedBuilt = TerrainRegionalElevationGroupUtility.TryBuildSelectionSummary(
                data,
                out TerrainRegionalElevationSelectionSummary mixed,
                out _);

            bool ok =
                commonBuilt && common.Count == 3 && common.HasCommonElevation && common.CommonElevation == 100f &&
                mixedBuilt && !mixed.HasCommonElevation && mixed.MinimumElevation == 100f && mixed.MaximumElevation == 150f;

            AddResult(
                "Common and mixed selection elevation summaries",
                ok ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                ok
                    ? "Equal serialized elevations report a common value; differing values report Mixed with exact min/max range."
                    : "Selection summary did not classify common/mixed elevation correctly.");
        }
        finally
        {
            DestroyData(data);
        }
    }

    private static void ValidateRealStateUnchanged()
    {
        bool unchanged =
            realAuthoringData.authoringRevision == realRevisionBefore &&
            ReferenceEquals(realAuthoringData.RegionalElevationSource, realRegionalBefore) &&
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings) == realCommittedBefore &&
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, realAuthoringData) == realOverallBefore;

        AddResult(
            "Real authoring state remains unchanged",
            unchanged ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            unchanged
                ? "Transient Package 7 validation did not alter the real regional source, revision, committed signature, or overall signature."
                : "Real authoring state changed during Package 7 validation.");
    }

    private static TerrainAuthoringData CreateData(params NodeSpec[] specs)
    {
        TerrainAuthoringData data = ScriptableObject.CreateInstance<TerrainAuthoringData>();
        TerrainNodeElevationSource source = new TerrainNodeElevationSource();

        for (int index = 0; index < specs.Length; index++)
        {
            TerrainElevationNode node = new TerrainElevationNode();
            node.SetPositionXZInternal(specs[index].Position);
            node.SetElevationInternal(specs[index].Elevation);
            source.AddNodeInternal(node);
        }

        source.RepairNodeStableIds();
        data.SetRegionalElevationSourceInternal(source);
        data.authoringRevision = 100;
        return data;
    }

    private static IReadOnlyList<TerrainElevationNode> GetNodes(TerrainAuthoringData data)
    {
        return ((TerrainNodeElevationSource)data.RegionalElevationSource).Nodes;
    }

    private static List<string> GetIds(TerrainAuthoringData data)
    {
        List<string> ids = new List<string>();
        IReadOnlyList<TerrainElevationNode> nodes = GetNodes(data);
        for (int index = 0; index < nodes.Count; index++)
        {
            ids.Add(nodes[index].StableId);
        }
        return ids;
    }

    private static Vector2[] GetPositions(TerrainAuthoringData data)
    {
        IReadOnlyList<TerrainElevationNode> nodes = GetNodes(data);
        Vector2[] result = new Vector2[nodes.Count];
        for (int index = 0; index < nodes.Count; index++)
        {
            result[index] = nodes[index].PositionXZ;
        }
        return result;
    }

    private static float[] GetElevations(TerrainAuthoringData data)
    {
        IReadOnlyList<TerrainElevationNode> nodes = GetNodes(data);
        float[] result = new float[nodes.Count];
        for (int index = 0; index < nodes.Count; index++)
        {
            result[index] = nodes[index].Elevation;
        }
        return result;
    }

    private static bool AllElevations(TerrainAuthoringData data, float value)
    {
        IReadOnlyList<TerrainElevationNode> nodes = GetNodes(data);
        for (int index = 0; index < nodes.Count; index++)
        {
            if (nodes[index].Elevation != value)
            {
                return false;
            }
        }
        return true;
    }

    private static bool PositionsEqual(TerrainAuthoringData data, Vector2[] expected)
    {
        IReadOnlyList<TerrainElevationNode> nodes = GetNodes(data);
        if (nodes.Count != expected.Length)
        {
            return false;
        }

        for (int index = 0; index < nodes.Count; index++)
        {
            if (nodes[index].PositionXZ != expected[index])
            {
                return false;
            }
        }
        return true;
    }

    private static void DestroyData(TerrainAuthoringData data)
    {
        if (data == null)
        {
            return;
        }

        TerrainRegionalElevationSelectionState.ForceClearSelection(data);
        TerrainRegionalElevationChangeTracker.Forget(data);
        Undo.ClearUndo(data);
        UnityEngine.Object.DestroyImmediate(data);
    }

    private static void RestoreRealEditorSelection()
    {
        if (realAuthoringData == null)
        {
            return;
        }

        TerrainRegionalElevationSelectionState.ForceClearSelection(null);
        if (realSelectionBefore.Count > 0 &&
            realAuthoringData.RegionalElevationSource is TerrainNodeElevationSource)
        {
            TerrainRegionalElevationSelectionState.SetSelection(
                realAuthoringData,
                realSelectionBefore,
                realPrimaryBefore,
                out _);
        }
    }

    private static void AddResult(
        string name,
        ValidationOutcome outcome,
        string details)
    {
        results.Add(new ValidationResult
        {
            Name = name,
            Outcome = outcome,
            Details = details
        });
    }

    private static void WriteReport()
    {
        int passed = 0;
        int failed = 0;
        int blocked = 0;
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("WorldMeshes Regional Elevation Multi-Select Validation");
        builder.AppendLine("=====================================================");
        builder.AppendLine();

        for (int index = 0; index < results.Count; index++)
        {
            ValidationResult result = results[index];
            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    passed++;
                    break;
                case ValidationOutcome.Fail:
                    failed++;
                    break;
                default:
                    blocked++;
                    break;
            }

            builder.Append(result.Outcome.ToString().ToUpperInvariant());
            builder.Append(" - ");
            builder.AppendLine(result.Name);
            builder.Append("       ");
            builder.AppendLine(result.Details);
            builder.AppendLine();
        }

        builder.AppendLine("-----------------------------------------------------");
        builder.AppendLine($"{passed} passed");
        builder.AppendLine($"{failed} failed");
        builder.AppendLine($"{blocked} blocked");
        builder.AppendLine();
        builder.AppendLine(
            failed == 0 && blocked == 0
                ? "Regional elevation multi-select validation: PASSED"
                : "Regional elevation multi-select validation: FAILED OR BLOCKED");

        Debug.Log(builder.ToString());
    }
}
