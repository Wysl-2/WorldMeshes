using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package 6 validation for Scene-tool production helpers and the Package 5
 * interactive mutation path used by Scene handles.
 *
 * Actual SceneView pointer hit-testing is intentionally covered by the manual
 * README checklist; deterministic authoring semantics are exercised here.
 */
public static class TerrainRegionalElevationSceneToolValidationUtility
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
    private static string realSelectedStableIdBefore = "";
    private static bool sceneToolEnabledBefore;
    private static bool baselineCaptured;

    public static bool IsRunning => validationRunning || validationScheduled;

    public static void ValidateRegionalElevationSceneTool()
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
            TerrainRegionalElevationSceneTool.SetEnabled(false);
            TerrainRegionalElevationSceneTool.SetContext(worldSettings, realAuthoringData);

            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Pass,
                "Current WorldSettings, TerrainAuthoringData, and committed/overall signatures are available.");

            ValidateCoordinateMappingAndClamp();
            ValidateToolClampServiceIntegration();
            ValidateSharedSelectionState();
            ValidateInteractiveXZDrag();
            ValidateInteractiveElevationDrag();
            ValidateInteractiveCancelAndNoOp();
            ValidateToolDisableCancellation();
            ValidateContextLossCancellation();
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
                RestoreEditorState();
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
            errorMessage = "Initialize the committed authoring heightfield before running Package 6 validation.";
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
        realSelectedStableIdBefore =
            TerrainRegionalElevationSelectionState.GetSelectedNodeStableId(realAuthoringData);
        sceneToolEnabledBefore = TerrainRegionalElevationSceneTool.Enabled;
        baselineCaptured = true;
    }

    private static void ValidateCoordinateMappingAndClamp()
    {
        Vector3 scenePosition =
            TerrainRegionalElevationSceneTool.GetNodeScenePosition(
                new Vector2(100f, 250f),
                175f);

        Vector2 xz =
            TerrainRegionalElevationSceneTool.GetPositionXZFromScenePosition(
                new Vector3(125f, 999f, 300f));

        Vector2 worldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(worldSettings);
        Vector2 clampedLow = TerrainRegionalElevationSceneTool.ClampPositionXZToWorld(
            worldSettings,
            new Vector2(-50f, -25f));
        Vector2 clampedHigh = TerrainRegionalElevationSceneTool.ClampPositionXZToWorld(
            worldSettings,
            new Vector2(worldSize.x + 100f, worldSize.y + 200f));
        Vector2 inRange = new Vector2(worldSize.x * 0.25f, worldSize.y * 0.75f);
        Vector2 clampedInRange = TerrainRegionalElevationSceneTool.ClampPositionXZToWorld(
            worldSettings,
            inRange);

        bool mappingPassed =
            scenePosition == new Vector3(100f, 175f, 250f) &&
            xz == new Vector2(125f, 300f);

        AddResult(
            "Node Scene coordinate mapping preserves XZ plus absolute elevation",
            mappingPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            mappingPassed
                ? "PositionXZ (100,250) + 175m maps to Scene (100,175,250), and Scene Y is excluded when converting back to XZ."
                : $"Unexpected mapping: scene={scenePosition}, xz={xz}.");

        bool clampPassed =
            clampedLow == Vector2.zero &&
            clampedHigh == worldSize &&
            clampedInRange == inRange &&
            TerrainRegionalElevationSceneTool.SanitizeSceneElevation(-250f) == -250f &&
            TerrainRegionalElevationSceneTool.SanitizeSceneElevation(0f) == 0f &&
            TerrainRegionalElevationSceneTool.SanitizeSceneElevation(5000f) == 5000f;

        AddResult(
            "Scene XZ movement clamps to world bounds while elevation remains unrestricted",
            clampPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            clampPassed
                ? "Scene X/Z is constrained to the canonical world rectangle; finite elevation values -250/0/5000m remain unchanged."
                : "Scene movement clamping or elevation passthrough did not match Package 6 semantics.");
    }

    private static void ValidateToolClampServiceIntegration()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(10f, 20f), 75f),
            new NodeSpec(new Vector2(100f, 100f), 120f));

        try
        {
            TerrainElevationNode node =
                ((TerrainNodeElevationSource)data.RegionalElevationSource).Nodes[0];
            Vector2 worldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(worldSettings);
            Vector2 requestedOutside = new Vector2(-50f, worldSize.y + 100f);
            Vector2 clamped = TerrainRegionalElevationSceneTool.ClampPositionXZToWorld(
                worldSettings,
                requestedOutside);

            string beginError = "";
            string updateError = "";
            string commitError = "";

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                data,
                worldSettings,
                node.StableId,
                "Validate Scene Clamp Integration",
                out beginError);
            bool updated = began && TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                clamped,
                out updateError);
            bool committed = updated && TerrainRegionalElevationService.CommitInteractiveNodeEdit(
                out commitError);

            bool passed =
                committed &&
                clamped == new Vector2(0f, worldSize.y) &&
                node.PositionXZ == clamped;

            AddResult(
                "Scene world-bound clamp feeds the Package 5 interactive service",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "An out-of-world Scene request was clamped to (0, worldHeight) before the production interactive service stored it."
                    : beginError + " " + updateError + " " + commitError);
        }
        finally
        {
            CancelInteractiveIfNeeded();
            ClearFixture(data);
        }
    }

    private static void ValidateSharedSelectionState()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(0f, 0f), 20f),
            new NodeSpec(new Vector2(100f, 0f), 60f));

        try
        {
            TerrainNodeElevationSource source =
                (TerrainNodeElevationSource)data.RegionalElevationSource;
            string firstId = source.Nodes[0].StableId;
            string secondId = source.Nodes[1].StableId;
            int revisionBefore = data.authoringRevision;
            string committedBefore = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);
            string overallBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data);

            bool firstSelected = TerrainRegionalElevationSelectionState.TrySelectNode(
                data,
                firstId,
                out string firstError);
            bool secondSelected = TerrainRegionalElevationSelectionState.TrySelectNode(
                data,
                secondId,
                out string secondError);
            string selected = TerrainRegionalElevationSelectionState.GetSelectedNodeStableId(data);
            bool cleared = TerrainRegionalElevationSelectionState.ClearSelection(
                data,
                out string clearError);

            bool passed =
                firstSelected &&
                secondSelected &&
                selected == secondId &&
                cleared &&
                string.IsNullOrEmpty(TerrainRegionalElevationSelectionState.GetSelectedNodeStableId(data)) &&
                data.authoringRevision == revisionBefore &&
                TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings) == committedBefore &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data) == overallBefore;

            AddResult(
                "Shared selection is single-StableId editor state only",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "Selecting A then B replaced the single selection, clearing removed it, and no terrain data/revision/signature changed."
                    : firstError + " " + secondError + " " + clearError);
        }
        finally
        {
            TerrainRegionalElevationSelectionState.ForceClearSelection(data);
            ClearFixture(data);
        }
    }

    private static void ValidateInteractiveXZDrag()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(10f, 20f), 80f),
            new NodeSpec(new Vector2(100f, 100f), 120f));

        try
        {
            TerrainNodeElevationSource source =
                (TerrainNodeElevationSource)data.RegionalElevationSource;
            TerrainElevationNode node = source.Nodes[0];
            string stableId = node.StableId;
            float elevationBefore = node.Elevation;
            int revisionBefore = data.authoringRevision;
            string committedBefore = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);
            string overallBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data);
            int expectedTiles = ExpectedTileCount();

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                data,
                worldSettings,
                stableId,
                "Validate Scene XZ Drag",
                out string beginError);

            string updateAError = "";
            bool updateA = began && TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                new Vector2(30f, 40f),
                out updateAError);
            bool sampleStateA =
                updateA &&
                data.authoringRevision == revisionBefore &&
                TerrainRegionalElevationService.ActiveInteractiveDirtyTileCount == expectedTiles &&
                TerrainRegionalElevationService.LastInteractivePreviewDirtyTileCount == expectedTiles &&
                TerrainRegionalElevationService.LastInteractiveRuntimeInvalidationCount == 0;

            string updateBError = "";
            bool updateB = updateA && TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                new Vector2(50f, 70f),
                out updateBError);
            bool sampleStateB =
                updateB &&
                data.authoringRevision == revisionBefore &&
                TerrainRegionalElevationService.LastInteractiveRuntimeInvalidationCount == 0;

            string commitError = "";
            bool committed = updateB && TerrainRegionalElevationService.CommitInteractiveNodeEdit(
                out commitError);

            TerrainRegionalElevationMutationDiagnostics diagnostics =
                TerrainRegionalElevationService.LastMutationDiagnostics;

            bool passed =
                sampleStateA &&
                sampleStateB &&
                committed &&
                node.StableId == stableId &&
                node.PositionXZ == new Vector2(50f, 70f) &&
                node.Elevation == elevationBefore &&
                data.authoringRevision == revisionBefore + 1 &&
                TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings) == committedBefore &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data) != overallBefore &&
                diagnostics != null &&
                diagnostics.Changed &&
                diagnostics.DirtyTileCount == expectedTiles;

            AddResult(
                "XZ Scene drag uses one interactive transaction and whole-world live-preview classification",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "Multiple position samples kept revision/runtime commit state unchanged during drag; final commit preserved elevation/StableId, advanced revision once, and classified the complete world dirty."
                    : beginError + " " + updateAError + " " + updateBError + " " + commitError);
        }
        finally
        {
            CancelInteractiveIfNeeded();
            ClearFixture(data);
        }
    }

    private static void ValidateInteractiveElevationDrag()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(25f, 35f), 90f),
            new NodeSpec(new Vector2(100f, 100f), 120f));

        try
        {
            TerrainNodeElevationSource source =
                (TerrainNodeElevationSource)data.RegionalElevationSource;
            TerrainElevationNode node = source.Nodes[0];
            string stableId = node.StableId;
            Vector2 positionBefore = node.PositionXZ;
            int revisionBefore = data.authoringRevision;
            int expectedTiles = ExpectedTileCount();

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                data,
                worldSettings,
                stableId,
                "Validate Scene Elevation Drag",
                out string beginError);
            string updateAError = "";
            bool updateA = began && TerrainRegionalElevationService.UpdateInteractiveNodeElevation(
                130f,
                out updateAError);
            string updateBError = "";
            bool updateB = updateA && TerrainRegionalElevationService.UpdateInteractiveNodeElevation(
                175f,
                out updateBError);
            bool runtimeStayedDeferred =
                TerrainRegionalElevationService.LastInteractiveRuntimeInvalidationCount == 0 &&
                data.authoringRevision == revisionBefore;
            string commitError = "";
            bool committed = updateB && TerrainRegionalElevationService.CommitInteractiveNodeEdit(
                out commitError);

            TerrainRegionalElevationMutationDiagnostics diagnostics =
                TerrainRegionalElevationService.LastMutationDiagnostics;

            bool passed =
                runtimeStayedDeferred &&
                committed &&
                node.StableId == stableId &&
                node.PositionXZ == positionBefore &&
                node.Elevation == 175f &&
                data.authoringRevision == revisionBefore + 1 &&
                diagnostics != null &&
                diagnostics.DirtyTileCount == expectedTiles;

            AddResult(
                "Elevation Scene drag preserves XZ and commits once",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "Multiple elevation samples preserved PositionXZ/StableId, deferred runtime invalidation during the hot path, and committed one whole-world transaction."
                    : beginError + " " + updateAError + " " + updateBError + " " + commitError);
        }
        finally
        {
            CancelInteractiveIfNeeded();
            ClearFixture(data);
        }
    }

    private static void ValidateInteractiveCancelAndNoOp()
    {
        TerrainAuthoringData cancelData = CreateData(
            new NodeSpec(new Vector2(15f, 25f), 70f),
            new NodeSpec(new Vector2(100f, 100f), 120f));

        try
        {
            TerrainElevationNode node =
                ((TerrainNodeElevationSource)cancelData.RegionalElevationSource).Nodes[0];
            Vector2 positionBefore = node.PositionXZ;
            float elevationBefore = node.Elevation;
            string stableIdBefore = node.StableId;
            int revisionBefore = cancelData.authoringRevision;
            string overallBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                cancelData);

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                cancelData,
                worldSettings,
                stableIdBefore,
                "Validate Scene Cancel",
                out string beginError);
            string updateError = "";
            bool updated = began && TerrainRegionalElevationService.UpdateInteractiveNode(
                new Vector2(90f, 95f),
                210f,
                out updateError);
            bool cancelHadPreviewClassification =
                TerrainRegionalElevationService.LastInteractivePreviewDirtyTileCount == ExpectedTileCount();
            string cancelError = "";
            bool cancelled = updated && TerrainRegionalElevationService.CancelInteractiveNodeEdit(
                out cancelError);

            TerrainElevationNode restored =
                ((TerrainNodeElevationSource)cancelData.RegionalElevationSource).Nodes[0];

            bool cancelPassed =
                cancelHadPreviewClassification &&
                cancelled &&
                restored.StableId == stableIdBefore &&
                restored.PositionXZ == positionBefore &&
                restored.Elevation == elevationBefore &&
                cancelData.authoringRevision == revisionBefore &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, cancelData) == overallBefore;

            AddResult(
                "Interactive cancel restores the complete starting node state",
                cancelPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                cancelPassed
                    ? "Temporary XZ/elevation changes classified full-world preview work, then Cancel restored identity/output without revision/signature commit."
                    : beginError + " " + updateError + " " + cancelError);
        }
        finally
        {
            CancelInteractiveIfNeeded();
            ClearFixture(cancelData);
        }

        TerrainAuthoringData noOpData = CreateData(
            new NodeSpec(new Vector2(20f, 30f), 80f),
            new NodeSpec(new Vector2(100f, 100f), 120f));

        try
        {
            TerrainElevationNode node =
                ((TerrainNodeElevationSource)noOpData.RegionalElevationSource).Nodes[0];
            Vector2 start = node.PositionXZ;
            int revisionBefore = noOpData.authoringRevision;
            string overallBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                noOpData);

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                noOpData,
                worldSettings,
                node.StableId,
                "Validate Scene Return To Start",
                out string beginError);
            string awayError = "";
            bool away = began && TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                start + new Vector2(40f, 50f),
                out awayError);
            string backError = "";
            bool back = away && TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                start,
                out backError);
            string commitError = "";
            bool committed = back && TerrainRegionalElevationService.CommitInteractiveNodeEdit(
                out commitError);

            TerrainRegionalElevationMutationDiagnostics diagnostics =
                TerrainRegionalElevationService.LastMutationDiagnostics;

            bool passed =
                committed &&
                node.PositionXZ == start &&
                noOpData.authoringRevision == revisionBefore &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, noOpData) == overallBefore &&
                diagnostics != null &&
                !diagnostics.Changed;

            AddResult(
                "Gesture returning to its starting state commits as a no-op",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "A -> temporary positions -> A produced live-change classification but no persistent revision/signature transaction."
                    : beginError + " " + awayError + " " + backError + " " + commitError);
        }
        finally
        {
            CancelInteractiveIfNeeded();
            ClearFixture(noOpData);
        }
    }

    private static void ValidateToolDisableCancellation()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(30f, 40f), 100f),
            new NodeSpec(new Vector2(100f, 100f), 120f));

        try
        {
            TerrainRegionalElevationSceneTool.SetContext(worldSettings, data);
            TerrainRegionalElevationSceneTool.SetEnabled(true);

            TerrainElevationNode node =
                ((TerrainNodeElevationSource)data.RegionalElevationSource).Nodes[0];
            Vector2 start = node.PositionXZ;
            int revisionBefore = data.authoringRevision;

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                data,
                worldSettings,
                node.StableId,
                "Validate Tool Disable",
                out string beginError);
            string updateError = "";
            bool updated = began && TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                start + new Vector2(20f, 20f),
                out updateError);

            TerrainRegionalElevationSceneTool.SetEnabled(false);

            TerrainElevationNode restored =
                ((TerrainNodeElevationSource)data.RegionalElevationSource).Nodes[0];

            bool passed =
                updated &&
                !TerrainRegionalElevationService.HasActiveInteractiveEdit &&
                restored.PositionXZ == start &&
                data.authoringRevision == revisionBefore;

            AddResult(
                "Disabling the Scene tool cancels an active regional edit",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "Tool deactivation restored the starting node state and left no active Package 5 transaction."
                    : beginError + " " + updateError);
        }
        finally
        {
            TerrainRegionalElevationSceneTool.SetEnabled(false);
            CancelInteractiveIfNeeded();
            ClearFixture(data);
        }
    }

    private static void ValidateContextLossCancellation()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(35f, 45f), 110f),
            new NodeSpec(new Vector2(100f, 100f), 120f));

        try
        {
            TerrainRegionalElevationSceneTool.SetContext(worldSettings, data);

            TerrainElevationNode node =
                ((TerrainNodeElevationSource)data.RegionalElevationSource).Nodes[0];
            float elevationBefore = node.Elevation;
            int revisionBefore = data.authoringRevision;

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                data,
                worldSettings,
                node.StableId,
                "Validate Context Loss",
                out string beginError);
            string updateError = "";
            bool updated = began && TerrainRegionalElevationService.UpdateInteractiveNodeElevation(
                elevationBefore + 75f,
                out updateError);

            TerrainRegionalElevationSceneTool.ClearContext();

            TerrainElevationNode restored =
                ((TerrainNodeElevationSource)data.RegionalElevationSource).Nodes[0];

            bool passed =
                updated &&
                !TerrainRegionalElevationService.HasActiveInteractiveEdit &&
                restored.Elevation == elevationBefore &&
                data.authoringRevision == revisionBefore;

            AddResult(
                "Scene authoring context loss cancels the active edit",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "Clearing the Scene tool context cancelled temporary elevation changes instead of leaving a half-finished transaction."
                    : beginError + " " + updateError);
        }
        finally
        {
            CancelInteractiveIfNeeded();
            ClearFixture(data);
        }
    }

    private static void ValidateRealStateUnchanged()
    {
        bool passed =
            realAuthoringData.authoringRevision == realRevisionBefore &&
            ReferenceEquals(realAuthoringData.RegionalElevationSource, realRegionalBefore) &&
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings) == realCommittedBefore &&
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, realAuthoringData) == realOverallBefore;

        AddResult(
            "Real authoring state remains unchanged",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Transient Package 6 selection/interactive/lifecycle validation did not alter the real regional source, revision, committed signature, or overall signature."
                : "The real authoring state changed during Package 6 validation.");
    }

    private static TerrainAuthoringData CreateData(params NodeSpec[] nodes)
    {
        TerrainAuthoringData data = ScriptableObject.CreateInstance<TerrainAuthoringData>();
        data.sourceMode = TerrainHeightSourceMode.Flat;

        TerrainNodeElevationSource source = new TerrainNodeElevationSource();
        if (nodes != null)
        {
            for (int index = 0; index < nodes.Length; index++)
            {
                TerrainElevationNode node = new TerrainElevationNode();
                node.SetPositionXZInternal(nodes[index].Position);
                node.SetElevationInternal(nodes[index].Elevation);
                source.AddNodeInternal(node);
            }
        }

        source.RepairNodeStableIds();
        data.SetRegionalElevationSourceInternal(source);
        return data;
    }

    private static int ExpectedTileCount()
    {
        return worldSettings.HeightTileGridWidth * worldSettings.HeightTileGridHeight;
    }

    private static void CancelInteractiveIfNeeded()
    {
        if (TerrainRegionalElevationService.HasActiveInteractiveEdit)
        {
            TerrainRegionalElevationService.CancelInteractiveNodeEdit(out _);
        }
    }

    private static void ClearFixture(TerrainAuthoringData data)
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

    private static void RestoreEditorState()
    {
        CancelInteractiveIfNeeded();
        TerrainRegionalElevationSceneTool.SetEnabled(false);
        TerrainRegionalElevationSceneTool.SetContext(worldSettings, realAuthoringData);

        TerrainRegionalElevationSelectionState.ForceClearSelection(null);
        if (
            realAuthoringData != null &&
            !string.IsNullOrEmpty(realSelectedStableIdBefore))
        {
            TerrainRegionalElevationSelectionState.TrySelectNode(
                realAuthoringData,
                realSelectedStableIdBefore,
                out _);
        }

        TerrainRegionalElevationSceneTool.SetEnabled(sceneToolEnabledBefore);
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
            Details = details ?? ""
        });
    }

    private static void WriteReport()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("WorldMeshes Regional Elevation Scene Tool Validation");
        builder.AppendLine("=====================================================");
        builder.AppendLine();

        int passed = 0;
        int failed = 0;
        int blocked = 0;

        for (int index = 0; index < results.Count; index++)
        {
            ValidationResult result = results[index];
            string outcome = result.Outcome.ToString().ToUpperInvariant();
            builder.AppendLine(outcome + " - " + result.Name);
            builder.AppendLine("       " + result.Details.Replace("\n", "\n       "));
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

        builder.AppendLine("-----------------------------------------------------");
        builder.AppendLine($"{passed} passed");
        builder.AppendLine($"{failed} failed");
        builder.AppendLine($"{blocked} blocked");
        builder.AppendLine();
        builder.AppendLine(
            failed == 0 && blocked == 0
                ? "Regional elevation Scene tool validation: PASSED"
                : failed > 0
                    ? "Regional elevation Scene tool validation: FAILED"
                    : "Regional elevation Scene tool validation: BLOCKED");

        Debug.Log(builder.ToString());
    }
}
