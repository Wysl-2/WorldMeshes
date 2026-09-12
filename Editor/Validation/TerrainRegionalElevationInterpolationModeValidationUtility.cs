using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package I1 validation for persistent interpolation-mode identity.
 *
 * Output-affecting tests use transient TerrainAuthoringData. The real
 * WorldSettings is immutable layout/signature context, so live preview/runtime
 * notifications remain suppressed while mutation diagnostics still expose the
 * complete regional dirty-tile classification.
 */
public static class TerrainRegionalElevationInterpolationModeValidationUtility
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

        public NodeSpec(
            Vector2 position,
            float elevation)
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
    private static readonly List<string> realSelectedIdsBefore =
        new List<string>();
    private static string realPrimaryBefore = "";

    public static bool IsRunning =>
        validationRunning || validationScheduled;

    public static void ValidateInterpolationModeFoundation()
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
        results.Clear();

        try
        {
            if (!TryValidatePrerequisites(
                out string prerequisiteError))
            {
                AddResult(
                    "Validation prerequisites",
                    ValidationOutcome.Blocked,
                    prerequisiteError);
                return;
            }

            CaptureRealBaseline();

            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Pass,
                "Current WorldSettings, TerrainAuthoringData, and committed/overall signatures are available.");

            ValidateEnumAndDefault();
            ValidateIdwRegression();
            ValidateSignatureModeIdentity();
            ValidateInvalidAndUnsupportedSafety();
            ValidateSnapshotModeIdentity();
            ValidateServiceNoOpAndTransaction();
            ValidateInteractiveConflictGuard();
            ValidateUndoRedo();
            ValidateRealStateUnchanged();
        }
        catch (Exception exception)
        {
            AddResult(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString());
        }
        finally
        {
            validationRunning = false;
            WriteReport();
        }
    }

    private static bool TryValidatePrerequisites(
        out string errorMessage)
    {
        errorMessage = "";

        if (Application.isPlaying ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage =
                "Validation cannot run in or while entering Play Mode.";
            return false;
        }

        if (EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            errorMessage =
                "Wait for Unity to finish compiling/importing and run validation again.";
            return false;
        }

        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath);

        realAuthoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath);

        if (worldSettings == null ||
            realAuthoringData == null)
        {
            errorMessage =
                "WorldSettings or TerrainAuthoringData could not be loaded.";
            return false;
        }

        string committed =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings);

        string overall =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData);

        if (string.IsNullOrEmpty(committed) ||
            string.IsNullOrEmpty(overall))
        {
            errorMessage =
                "Initialize the committed authoring heightfield before running Package I1 validation.";
            return false;
        }

        return true;
    }

    private static void CaptureRealBaseline()
    {
        realRevisionBefore =
            realAuthoringData.authoringRevision;

        realCommittedBefore =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings);

        realOverallBefore =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData);

        realRegionalBefore =
            realAuthoringData.RegionalElevationSource;

        realSelectedIdsBefore.Clear();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            realSelectedIdsBefore);

        realPrimaryBefore =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(
                realAuthoringData);
    }

    private static void ValidateEnumAndDefault()
    {
        TerrainNodeElevationSource source =
            new TerrainNodeElevationSource();

        bool passed =
            (int)TerrainNodeElevationInterpolationMode.InverseDistanceWeighted == 0 &&
            (int)TerrainNodeElevationInterpolationMode.TriangulatedLinear == 1 &&
            (int)TerrainNodeElevationInterpolationMode.TriangulatedSmooth == 2 &&
            source.InterpolationMode ==
                TerrainNodeElevationInterpolationMode.InverseDistanceWeighted &&
            TerrainNodeElevationInterpolationModeUtility.IsKnown(
                TerrainNodeElevationInterpolationMode.InverseDistanceWeighted) &&
            TerrainNodeElevationInterpolationModeUtility.IsKnown(
                TerrainNodeElevationInterpolationMode.TriangulatedLinear) &&
            TerrainNodeElevationInterpolationModeUtility.IsKnown(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth) &&
            TerrainNodeElevationInterpolationModeUtility.IsImplemented(
                TerrainNodeElevationInterpolationMode.InverseDistanceWeighted) &&
            TerrainNodeElevationInterpolationModeUtility.IsImplemented(
                TerrainNodeElevationInterpolationMode.TriangulatedLinear) &&
            !TerrainNodeElevationInterpolationModeUtility.IsImplemented(
                TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        AddResult(
            "Enum values and existing-source default remain serialized-compatible",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "IDW=0, Linear=1, Smooth=2; the existing default remains IDW, IDW and Linear are production-capable after I4, and Smooth remains unavailable."
                : "Interpolation enum/default/implementation availability did not match the current package contract.");
    }

    private static void ValidateIdwRegression()
    {
        bool oneNode = EvaluateEquals(
            CreateSource(
                new NodeSpec(new Vector2(10f, 20f), 42f)),
            new Vector2(-500f, 900f),
            42f,
            0f);

        bool midpoint = EvaluateEquals(
            CreateSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f)),
            new Vector2(5f, 0f),
            50f,
            0.0001f);

        bool exactNode = EvaluateEquals(
            CreateSource(
                new NodeSpec(Vector2.zero, 15f),
                new NodeSpec(new Vector2(100f, 0f), 200f)),
            Vector2.zero,
            15f,
            0f);

        bool coincident = EvaluateEquals(
            CreateSource(
                new NodeSpec(new Vector2(25f, 25f), 20f),
                new NodeSpec(new Vector2(25f, 25f), 40f),
                new NodeSpec(new Vector2(100f, 25f), 1000f)),
            new Vector2(25f, 25f),
            30f,
            0.0001f);

        bool arbitrary = EvaluateEquals(
            CreateSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f)),
            new Vector2(2f, 0f),
            5.882353f,
            0.0005f);

        bool passed =
            oneNode &&
            midpoint &&
            exactNode &&
            coincident &&
            arbitrary;

        AddResult(
            "Existing IDW CPU mathematics remain unchanged",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "One-node, midpoint, exact-node, coincident-node averaging, and arbitrary p=2 IDW samples match the pre-I1 mathematical behavior."
                : "At least one fixed IDW regression sample changed.");
    }

    private static void ValidateSignatureModeIdentity()
    {
        TerrainNodeElevationSource idw =
            CreateSource(
                new NodeSpec(Vector2.zero, 10f),
                new NodeSpec(new Vector2(100f, 0f), 110f),
                new NodeSpec(new Vector2(0f, 100f), 210f));

        TerrainNodeElevationSource linear =
            CreateSource(
                new NodeSpec(Vector2.zero, 10f),
                new NodeSpec(new Vector2(100f, 0f), 110f),
                new NodeSpec(new Vector2(0f, 100f), 210f));

        linear.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedLinear);

        StringBuilder idwBuilder = new StringBuilder();
        StringBuilder linearBuilder = new StringBuilder();

        bool idwSignatureOk =
            idw.TryAppendDeterministicSignatureData(
                idwBuilder,
                out string idwError);

        bool linearSignatureOk =
            linear.TryAppendDeterministicSignatureData(
                linearBuilder,
                out string linearError);

        bool passed =
            idwSignatureOk &&
            linearSignatureOk &&
            idwBuilder.ToString() != linearBuilder.ToString();

        AddResult(
            "Interpolation mode participates in deterministic regional output identity",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Identical nodes with IDW versus Triangulated Linear produce different deterministic source signatures."
                : idwError + " " + linearError);
    }

    private static void ValidateInvalidAndUnsupportedSafety()
    {
        TerrainNodeElevationSource invalid =
            CreateSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(100f, 0f), 100f));

        invalid.SetInterpolationModeInternal(
            (TerrainNodeElevationInterpolationMode)999);

        StringBuilder invalidSignature = new StringBuilder();

        bool invalidRejected =
            !invalid.TryValidateOutputData(out _) &&
            !invalid.TryAppendDeterministicSignatureData(
                invalidSignature,
                out _) &&
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                invalid,
                new Vector2(50f, 0f),
                out _,
                out _);

        TerrainNodeElevationSource linear =
            CreateSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(100f, 0f), 100f));

        linear.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedLinear);

        bool linearEvaluationSupported =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                linear,
                new Vector2(50f, 0f),
                out float linearHeight,
                out string linearError) &&
            Mathf.Abs(linearHeight - 50f) <= 0.0001f;

        TerrainAuthoringData linearData =
            CreateDataFromSource(linear);

        bool linearCompositionSupported =
            TerrainRegionalElevationCompositionUtility.TryResolveNodeSource(
                linearData,
                out TerrainNodeElevationSource resolvedLinearSource,
                out bool regionalCompositionRequired,
                out string linearCompositionError) &&
            ReferenceEquals(resolvedLinearSource, linear) &&
            regionalCompositionRequired &&
            string.IsNullOrEmpty(linearCompositionError);

        TerrainNodeElevationSource smooth =
            CreateSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(100f, 0f), 100f));

        smooth.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        bool smoothRejected =
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                smooth,
                new Vector2(50f, 0f),
                out _,
                out string smoothError) &&
            smoothError.Contains("CPU");

        bool passed =
            invalidRejected &&
            linearEvaluationSupported &&
            linearCompositionSupported &&
            smoothRejected;

        AddResult(
            "Invalid modes fail explicitly while current CPU/GPU interpolation capabilities remain enforced",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Undefined enum data remains invalid; Linear is now accepted by CPU and GPU composition, while Smooth remains CPU/GPU-unsupported."
                : linearError + " " + linearCompositionError + " " + smoothError);

        ClearFixture(linearData);
    }

    private static void ValidateSnapshotModeIdentity()
    {
        TerrainAuthoringData data =
            CreateData(
                new NodeSpec(Vector2.zero, 25f),
                new NodeSpec(new Vector2(100f, 100f), 125f));

        try
        {
            TerrainNodeElevationSource source =
                data.RegionalElevationSource as TerrainNodeElevationSource;

            bool firstCaptured =
                TerrainRegionalElevationSnapshot.TryCapture(
                    data,
                    out TerrainRegionalElevationSnapshot first,
                    out string firstError);

            source.SetInterpolationModeInternal(
                TerrainNodeElevationInterpolationMode.TriangulatedLinear);

            bool secondCaptured =
                TerrainRegionalElevationSnapshot.TryCapture(
                    data,
                    out TerrainRegionalElevationSnapshot second,
                    out string secondError);

            bool passed =
                firstCaptured &&
                secondCaptured &&
                !first.OutputEquals(second) &&
                !first.StateEquals(second);

            AddResult(
                "Regional snapshot equality detects interpolation-only output changes",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "The same nodes under a different interpolation mode compare as different output/state."
                    : firstError + " " + secondError);
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static void ValidateServiceNoOpAndTransaction()
    {
        TerrainAuthoringData data =
            CreateData(
                new NodeSpec(Vector2.zero, 10f),
                new NodeSpec(new Vector2(100f, 100f), 110f));

        try
        {
            TerrainNodeElevationSource source =
                data.RegionalElevationSource as TerrainNodeElevationSource;

            int revisionBefore = data.authoringRevision;

            string committedBefore =
                TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                    worldSettings);

            string overallBefore =
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    data);

            bool sameMode =
                TerrainRegionalElevationService.SetInterpolationMode(
                    data,
                    worldSettings,
                    TerrainNodeElevationInterpolationMode.InverseDistanceWeighted,
                    out string sameModeError);

            TerrainRegionalElevationMutationDiagnostics noOpDiagnostics =
                TerrainRegionalElevationService.LastMutationDiagnostics;

            bool noOpPassed =
                sameMode &&
                source.InterpolationMode ==
                    TerrainNodeElevationInterpolationMode.InverseDistanceWeighted &&
                data.authoringRevision == revisionBefore &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    data) == overallBefore &&
                noOpDiagnostics != null &&
                !noOpDiagnostics.Changed;

            bool changed =
                TerrainRegionalElevationService.SetInterpolationMode(
                    data,
                    worldSettings,
                    TerrainNodeElevationInterpolationMode.TriangulatedLinear,
                    out string changeError);

            TerrainRegionalElevationMutationDiagnostics changedDiagnostics =
                TerrainRegionalElevationService.LastMutationDiagnostics;

            string overallAfter =
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    data);

            bool transactionPassed =
                changed &&
                source.InterpolationMode ==
                    TerrainNodeElevationInterpolationMode.TriangulatedLinear &&
                data.authoringRevision == revisionBefore + 1 &&
                committedBefore ==
                    TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                        worldSettings) &&
                overallBefore != overallAfter &&
                changedDiagnostics != null &&
                changedDiagnostics.Changed &&
                changedDiagnostics.DirtyTileCount == ExpectedTileCount() &&
                changedDiagnostics.PreviewNotificationMode == "Suppressed" &&
                changedDiagnostics.RuntimeInvalidationMode == "Suppressed";

            AddResult(
                "Service same-mode call is a no-op and a real mode change is one regional transaction",
                noOpPassed && transactionPassed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                noOpPassed && transactionPassed
                    ? "Transient mode mutation advanced revision once, changed only overall identity, classified the complete logical world dirty, and suppressed live notifications because the fixture is not the live project asset."
                    : sameModeError + " " + changeError);
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static void ValidateInteractiveConflictGuard()
    {
        TerrainAuthoringData data =
            CreateData(
                new NodeSpec(Vector2.zero, 20f),
                new NodeSpec(new Vector2(100f, 0f), 120f));

        try
        {
            TerrainNodeElevationSource source =
                data.RegionalElevationSource as TerrainNodeElevationSource;

            string stableId =
                source.Nodes[0].StableId;

            bool began =
                TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                    data,
                    worldSettings,
                    stableId,
                    "Interpolation Conflict Validation",
                    out string beginError);

            string modeError = "";

            bool rejected =
                began &&
                !TerrainRegionalElevationService.SetInterpolationMode(
                    data,
                    worldSettings,
                    TerrainNodeElevationInterpolationMode.TriangulatedLinear,
                    out modeError);

            bool unchanged =
                source.InterpolationMode ==
                    TerrainNodeElevationInterpolationMode.InverseDistanceWeighted;

            bool cancelled =
                TerrainRegionalElevationService.CancelInteractiveNodeEdit(
                    out string cancelError);

            bool passed =
                began &&
                rejected &&
                unchanged &&
                cancelled;

            AddResult(
                "Interpolation mutation is rejected while a regional interactive edit owns authoring state",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "The service blocked interpolation-mode mutation during an active Scene-compatible interactive node transaction."
                    : beginError + " " + modeError + " " + cancelError);
        }
        finally
        {
            if (TerrainRegionalElevationService.HasActiveInteractiveEdit)
            {
                TerrainRegionalElevationService.CancelInteractiveNodeEdit(
                    out _);
            }

            ClearFixture(data);
        }
    }

    private static void ValidateUndoRedo()
    {
        TerrainAuthoringData data =
            CreateData(
                new NodeSpec(Vector2.zero, 30f),
                new NodeSpec(new Vector2(100f, 0f), 130f));

        try
        {
            TerrainNodeElevationSource source =
                data.RegionalElevationSource as TerrainNodeElevationSource;

            int revisionBefore =
                data.authoringRevision;

            string overallBefore =
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    data);

            bool changed =
                TerrainRegionalElevationService.SetInterpolationMode(
                    data,
                    worldSettings,
                    TerrainNodeElevationInterpolationMode.TriangulatedLinear,
                    out string changeError);

            string overallChanged =
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    data);

            int revisionChanged =
                data.authoringRevision;

            Undo.PerformUndo();

            TerrainNodeElevationSource undone =
                data.RegionalElevationSource as TerrainNodeElevationSource;

            bool undoOk =
                changed &&
                undone != null &&
                undone.InterpolationMode ==
                    TerrainNodeElevationInterpolationMode.InverseDistanceWeighted &&
                data.authoringRevision == revisionBefore &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    data) == overallBefore &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount ==
                    ExpectedTileCount();

            Undo.PerformRedo();

            TerrainNodeElevationSource redone =
                data.RegionalElevationSource as TerrainNodeElevationSource;

            bool redoOk =
                redone != null &&
                redone.InterpolationMode ==
                    TerrainNodeElevationInterpolationMode.TriangulatedLinear &&
                data.authoringRevision == revisionChanged &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    data) == overallChanged &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount ==
                    ExpectedTileCount();

            AddResult(
                "Undo/Redo restores interpolation mode, revision, signature, and whole-world derived-state classification",
                undoOk && redoOk
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                undoOk && redoOk
                    ? "One Undo returned the transient source to IDW and one Redo restored Triangulated Linear with the expected output identity."
                    : changeError);
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static void ValidateRealStateUnchanged()
    {
        List<string> selectedAfter =
            new List<string>();

        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            selectedAfter);

        string primaryAfter =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(
                realAuthoringData);

        bool selectionSame =
            primaryAfter == realPrimaryBefore &&
            SequenceEqual(
                realSelectedIdsBefore,
                selectedAfter);

        bool passed =
            realAuthoringData.authoringRevision == realRevisionBefore &&
            ReferenceEquals(
                realAuthoringData.RegionalElevationSource,
                realRegionalBefore) &&
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings) == realCommittedBefore &&
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData) == realOverallBefore &&
            selectionSame;

        AddResult(
            "Real authoring and Package 7 selection state remain unchanged",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Transient Package I1 validation did not alter the real regional source, revision, committed/overall identity, selected StableIds, or primary StableId."
                : "The real authoring or regional selection state changed during Package I1 validation.");
    }

    private static TerrainNodeElevationSource CreateSource(
        params NodeSpec[] nodes)
    {
        TerrainNodeElevationSource source =
            new TerrainNodeElevationSource();

        if (nodes != null)
        {
            for (int index = 0; index < nodes.Length; index++)
            {
                TerrainElevationNode node =
                    new TerrainElevationNode();

                node.SetPositionXZInternal(
                    nodes[index].Position);

                node.SetElevationInternal(
                    nodes[index].Elevation);

                source.AddNodeInternal(node);
            }
        }

        source.RepairNodeStableIds();
        return source;
    }

    private static TerrainAuthoringData CreateData(
        params NodeSpec[] nodes)
    {
        return CreateDataFromSource(
            CreateSource(nodes));
    }

    private static TerrainAuthoringData CreateDataFromSource(
        TerrainNodeElevationSource source)
    {
        TerrainAuthoringData data =
            ScriptableObject.CreateInstance<TerrainAuthoringData>();

        data.sourceMode =
            TerrainHeightSourceMode.Flat;

        data.SetRegionalElevationSourceInternal(source);
        return data;
    }

    private static bool EvaluateEquals(
        TerrainNodeElevationSource source,
        Vector2 sample,
        float expected,
        float tolerance)
    {
        if (!TerrainNodeElevationEvaluator.TryEvaluateHeight(
            source,
            sample,
            out float actual,
            out _))
        {
            return false;
        }

        return Mathf.Abs(actual - expected) <= tolerance;
    }

    private static int ExpectedTileCount()
    {
        return
            worldSettings.HeightTileGridWidth *
            worldSettings.HeightTileGridHeight;
    }

    private static bool SequenceEqual(
        IReadOnlyList<string> a,
        IReadOnlyList<string> b)
    {
        if (a == null || b == null || a.Count != b.Count)
        {
            return false;
        }

        for (int index = 0; index < a.Count; index++)
        {
            if (!string.Equals(
                a[index],
                b[index],
                StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void ClearFixture(
        TerrainAuthoringData data)
    {
        if (data == null)
        {
            return;
        }

        TerrainRegionalElevationChangeTracker.Forget(data);
        Undo.ClearUndo(data);
        UnityEngine.Object.DestroyImmediate(data);
    }

    private static void AddResult(
        string name,
        ValidationOutcome outcome,
        string details)
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Outcome = outcome,
                Details = details ?? ""
            });
    }

    private static void WriteReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Regional Elevation Interpolation Mode Validation");

        builder.AppendLine(
            "===========================================================");

        builder.AppendLine();

        int passed = 0;
        int failed = 0;
        int blocked = 0;

        for (int index = 0; index < results.Count; index++)
        {
            ValidationResult result =
                results[index];

            string label =
                result.Outcome.ToString().ToUpperInvariant();

            builder.AppendLine(
                label + " - " + result.Name);

            builder.AppendLine(
                "       " +
                result.Details.Replace(
                    "\n",
                    "\n       "));

            builder.AppendLine();

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
        }

        builder.AppendLine(
            "-----------------------------------------------------------");

        builder.AppendLine(
            passed + " passed");

        builder.AppendLine(
            failed + " failed");

        builder.AppendLine(
            blocked + " blocked");

        builder.AppendLine();

        builder.AppendLine(
            failed == 0 && blocked == 0
                ? "Regional elevation interpolation mode validation: PASSED"
                : failed > 0
                    ? "Regional elevation interpolation mode validation: FAILED"
                    : "Regional elevation interpolation mode validation: BLOCKED");

        Debug.Log(
            builder.ToString());
    }
}
