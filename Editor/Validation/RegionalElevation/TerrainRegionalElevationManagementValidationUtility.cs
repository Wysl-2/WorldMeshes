using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package 5 validation for the production regional elevation mutation service.
 *
 * Service mutations are performed against transient TerrainAuthoringData.
 * The real WorldSettings is used only as immutable layout/signature context,
 * and the service suppresses live preview/runtime notifications for transient
 * data while still reporting the complete logical dirty-tile set.
 */
public static class TerrainRegionalElevationManagementValidationUtility
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

    public static bool IsRunning => validationRunning || validationScheduled;

    public static void ValidateRegionalElevationManagement()
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

            ValidateAddRemoveAndFinalNodeProtection();
            ValidateNumericEditsAndNoOps();
            ValidateInvalidOutsideAndCoincidentPositions();
            ValidateGridReplacement();
            ValidateRemoveSourceUndoRedo();
            ValidateAddUndoRedoAndChangeTracker();
            ValidateRemoveMoveElevationUndoRedo();
            ValidateProductionUiMutationBoundary();
            ValidateInteractiveCommit();
            ValidateInteractiveCancelAndNoOp();
            ValidateRealStateUnchanged();
        }
        catch (Exception exception)
        {
            AddResult("Unexpected validation exception", ValidationOutcome.Fail, exception.ToString());
        }
        finally
        {
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
        string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, realAuthoringData);

        if (string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        {
            errorMessage = "Initialize the committed authoring heightfield before running Package 5 validation.";
            return false;
        }

        return true;
    }

    private static void CaptureRealBaseline()
    {
        realRevisionBefore = realAuthoringData.authoringRevision;
        realCommittedBefore = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);
        realOverallBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, realAuthoringData);
        realRegionalBefore = realAuthoringData.RegionalElevationSource;
    }

    private static void ValidateAddRemoveAndFinalNodeProtection()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(0f, 0f), 20f),
            new NodeSpec(new Vector2(100f, 0f), 60f));

        try
        {
            TerrainNodeElevationSource source = (TerrainNodeElevationSource)data.RegionalElevationSource;
            string firstId = source.Nodes[0].StableId;
            string secondId = source.Nodes[1].StableId;
            string committedBefore = TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings);
            string overallBefore = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data);
            int expectedTiles = ExpectedTileCount();

            bool added = TerrainRegionalElevationService.AddNode(
                data,
                worldSettings,
                new Vector2(250f, -75f),
                90f,
                out string addedId,
                out string addError);

            TerrainRegionalElevationMutationDiagnostics addDiag =
                TerrainRegionalElevationService.LastMutationDiagnostics;

            bool addPassed =
                added &&
                source.NodeCount == 3 &&
                !string.IsNullOrEmpty(addedId) &&
                addedId != firstId &&
                addedId != secondId &&
                source.Nodes[2].StableId == addedId &&
                source.Nodes[2].PositionXZ == new Vector2(250f, -75f) &&
                source.Nodes[2].Elevation == 90f &&
                data.authoringRevision == 1 &&
                addDiag != null &&
                addDiag.Changed &&
                addDiag.DirtyTileCount == expectedTiles &&
                addDiag.RegionalInvalidationKind == "WholeWorld" &&
                addDiag.DirtyTiles.Count == 0 &&
                committedBefore == TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(worldSettings) &&
                overallBefore != TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data);

            AddResult(
                "AddNode uses StableId identity and a whole-world transaction",
                addPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                addPassed
                    ? "A node was appended with a new unique StableId, revision advanced once, committed identity stayed fixed, and the complete logical world was marked dirty."
                    : addError);

            int revisionBeforeRemove = data.authoringRevision;
            bool removed = TerrainRegionalElevationService.RemoveNode(
                data,
                worldSettings,
                secondId,
                out string removeError);

            bool removePassed =
                removed &&
                source.NodeCount == 2 &&
                source.Nodes[0].StableId == firstId &&
                source.Nodes[1].StableId == addedId &&
                data.authoringRevision == revisionBeforeRemove + 1 &&
                TerrainRegionalElevationService.LastMutationDiagnostics.DirtyTileCount == expectedTiles;

            AddResult(
                "RemoveNode removes exactly one StableId and preserves remaining order",
                removePassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                removePassed
                    ? "Removing the middle node preserved relative ordering/identity of the remaining nodes and invalidated the complete world."
                    : removeError);
        }
        finally
        {
            ClearFixture(data);
        }

        TerrainAuthoringData oneNodeData = CreateData(
            new NodeSpec(Vector2.zero, 50f));

        try
        {
            TerrainNodeElevationSource source = (TerrainNodeElevationSource)oneNodeData.RegionalElevationSource;
            string id = source.Nodes[0].StableId;
            int revision = oneNodeData.authoringRevision;
            string overall = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, oneNodeData);

            bool rejected = !TerrainRegionalElevationService.RemoveNode(
                oneNodeData,
                worldSettings,
                id,
                out string errorMessage);

            bool passed =
                rejected &&
                source.NodeCount == 1 &&
                source.Nodes[0].StableId == id &&
                oneNodeData.authoringRevision == revision &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, oneNodeData) == overall;

            AddResult(
                "Removing the final active node is rejected",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "The one-node source remained evaluable; complete source removal is the required destructive operation."
                    : errorMessage);
        }
        finally
        {
            ClearFixture(oneNodeData);
        }
    }

    private static void ValidateNumericEditsAndNoOps()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(10f, 20f), 30f),
            new NodeSpec(new Vector2(90f, 80f), 100f));

        try
        {
            TerrainNodeElevationSource source = (TerrainNodeElevationSource)data.RegionalElevationSource;
            TerrainElevationNode node = source.Nodes[0];
            string stableId = node.StableId;

            bool moved = TerrainRegionalElevationService.SetNodePosition(
                data,
                worldSettings,
                stableId,
                new Vector2(25f, 35f),
                out string moveError);

            bool movePassed =
                moved &&
                node.StableId == stableId &&
                node.PositionXZ == new Vector2(25f, 35f) &&
                node.Elevation == 30f &&
                data.authoringRevision == 1;

            bool elevated = TerrainRegionalElevationService.SetNodeElevation(
                data,
                worldSettings,
                stableId,
                75f,
                out string elevationError);

            bool elevationPassed =
                elevated &&
                node.StableId == stableId &&
                node.PositionXZ == new Vector2(25f, 35f) &&
                node.Elevation == 75f &&
                data.authoringRevision == 2;

            bool atomic = TerrainRegionalElevationService.SetNode(
                data,
                worldSettings,
                stableId,
                new Vector2(40f, 45f),
                125f,
                out string atomicError);

            bool atomicPassed =
                atomic &&
                node.StableId == stableId &&
                node.PositionXZ == new Vector2(40f, 45f) &&
                node.Elevation == 125f &&
                data.authoringRevision == 3;

            AddResult(
                "Move, elevation, and atomic node edits preserve identity",
                movePassed && elevationPassed && atomicPassed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                movePassed && elevationPassed && atomicPassed
                    ? "Position-only, elevation-only, and atomic position+elevation edits each advanced revision exactly once while preserving StableId/order."
                    : moveError + " " + elevationError + " " + atomicError);

            int revisionBeforeNoOp = data.authoringRevision;
            string signatureBeforeNoOp = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data);

            bool noOp1 = TerrainRegionalElevationService.SetNodePosition(
                data,
                worldSettings,
                stableId,
                node.PositionXZ,
                out string noOpError1);
            bool noOp2 = TerrainRegionalElevationService.SetNodeElevation(
                data,
                worldSettings,
                stableId,
                node.Elevation,
                out string noOpError2);
            bool noOp3 = TerrainRegionalElevationService.SetNode(
                data,
                worldSettings,
                stableId,
                node.PositionXZ,
                node.Elevation,
                out string noOpError3);

            TerrainRegionalElevationMutationDiagnostics noOpDiag =
                TerrainRegionalElevationService.LastMutationDiagnostics;

            bool noOpPassed =
                noOp1 && noOp2 && noOp3 &&
                data.authoringRevision == revisionBeforeNoOp &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data) == signatureBeforeNoOp &&
                noOpDiag != null && !noOpDiag.Changed && noOpDiag.DirtyTileCount == 0;

            AddResult(
                "Identical node edits are true no-ops",
                noOpPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                noOpPassed
                    ? "No-op position/elevation/atomic edits left revision/signature unchanged and emitted no dirty tiles."
                    : noOpError1 + " " + noOpError2 + " " + noOpError3);
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static void ValidateInvalidOutsideAndCoincidentPositions()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(Vector2.zero, 10f),
            new NodeSpec(new Vector2(100f, 100f), 20f));

        try
        {
            TerrainNodeElevationSource source = (TerrainNodeElevationSource)data.RegionalElevationSource;
            string firstId = source.Nodes[0].StableId;
            string secondId = source.Nodes[1].StableId;
            int revisionBeforeInvalid = data.authoringRevision;

            bool invalidRejected =
                !TerrainRegionalElevationService.SetNodePosition(
                    data,
                    worldSettings,
                    firstId,
                    new Vector2(float.NaN, 0f),
                    out _) &&
                !TerrainRegionalElevationService.SetNodePosition(
                    data,
                    worldSettings,
                    firstId,
                    new Vector2(0f, float.PositiveInfinity),
                    out _) &&
                !TerrainRegionalElevationService.SetNodeElevation(
                    data,
                    worldSettings,
                    firstId,
                    float.NegativeInfinity,
                    out _);

            bool invalidPassed =
                invalidRejected && data.authoringRevision == revisionBeforeInvalid;

            AddResult(
                "Non-finite node inputs are rejected without mutation",
                invalidPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                invalidPassed
                    ? "NaN/Infinity position and elevation requests were rejected before authoring state changed."
                    : "A non-finite request changed transient authoring state.");

            Vector2 worldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(worldSettings);
            Vector2 outside = worldSize + new Vector2(500f, 750f);

            bool outsideAccepted = TerrainRegionalElevationService.SetNodePosition(
                data,
                worldSettings,
                firstId,
                outside,
                out string outsideError);

            bool coincidentAccepted = TerrainRegionalElevationService.SetNodePosition(
                data,
                worldSettings,
                secondId,
                outside,
                out string coincidentError);

            bool passed =
                outsideAccepted && coincidentAccepted &&
                source.Nodes[0].PositionXZ == outside &&
                source.Nodes[1].PositionXZ == outside &&
                source.Nodes[0].StableId != source.Nodes[1].StableId;

            AddResult(
                "Finite out-of-world and coincident node positions remain valid",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "The service did not clamp world coordinates or forbid Package 3's supported coincident-node state."
                    : outsideError + " " + coincidentError);
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static void ValidateGridReplacement()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(13f, 17f), 11f),
            new NodeSpec(new Vector2(63f, 81f), 22f),
            new NodeSpec(new Vector2(91f, 44f), 33f));

        try
        {
            TerrainNodeElevationSource oldSource = (TerrainNodeElevationSource)data.RegionalElevationSource;
            List<string> oldIds = new List<string>();
            for (int i = 0; i < oldSource.NodeCount; i++)
            {
                oldIds.Add(oldSource.Nodes[i].StableId);
            }

            int revisionBefore = data.authoringRevision;
            bool replaced = TerrainRegionalElevationService.ReplaceNodeLayoutWithGrid(
                data,
                worldSettings,
                3,
                3,
                123.5f,
                out string replaceError);

            TerrainNodeElevationSource grid = data.RegionalElevationSource as TerrainNodeElevationSource;
            Vector2 worldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(worldSettings);
            bool idsNew = grid != null;
            if (grid != null)
            {
                for (int i = 0; i < grid.NodeCount; i++)
                {
                    if (oldIds.Contains(grid.Nodes[i].StableId))
                    {
                        idsNew = false;
                        break;
                    }
                }
            }

            bool gridPassed =
                replaced &&
                grid != null &&
                grid.NodeCount == 16 &&
                grid.Nodes[0].PositionXZ == Vector2.zero &&
                grid.Nodes[15].PositionXZ == worldSize &&
                AllElevations(grid, 123.5f) &&
                grid.TryValidateNodeStableIds(out _) &&
                idsNew &&
                data.authoringRevision == revisionBefore + 1 &&
                TerrainRegionalElevationService.LastMutationDiagnostics.DirtyTileCount == ExpectedTileCount();

            AddResult(
                "Grid replacement reuses Package 2 division semantics",
                gridPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                gridPassed
                    ? "3 x 3 divisions replaced the irregular source with 4 x 4 = 16 newly identified nodes in Package 2 boundary/order semantics."
                    : replaceError);

            Undo.PerformUndo();
            TerrainNodeElevationSource undone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool undoRestored =
                undone != null &&
                undone.NodeCount == 3 &&
                undone.Nodes[0].StableId == oldIds[0] &&
                undone.Nodes[1].StableId == oldIds[1] &&
                undone.Nodes[2].StableId == oldIds[2] &&
                data.authoringRevision == revisionBefore &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            Undo.PerformRedo();
            TerrainNodeElevationSource redone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool redoRestored =
                redone != null && redone.NodeCount == 16 &&
                data.authoringRevision == revisionBefore + 1 &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            AddResult(
                "Grid replacement Undo/Redo restores complete source state",
                undoRestored && redoRestored ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                undoRestored && redoRestored
                    ? "Undo restored old count/order/StableIds/revision and Redo restored the generated grid; both were recognized as whole-world output changes."
                    : "Grid replacement Undo/Redo did not restore expected transient source state.");
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static void ValidateRemoveSourceUndoRedo()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(Vector2.zero, 25f),
            new NodeSpec(new Vector2(100f, 100f), 125f));

        try
        {
            TerrainNodeElevationSource originalSource = data.RegionalElevationSource as TerrainNodeElevationSource;
            string firstId = originalSource.Nodes[0].StableId;
            int revisionBefore = data.authoringRevision;

            bool removed = TerrainRegionalElevationService.RemoveRegionalElevationSource(
                data,
                worldSettings,
                out string removeError);

            bool removedState =
                removed && data.RegionalElevationSource == null &&
                data.authoringRevision == revisionBefore + 1 &&
                TerrainRegionalElevationService.LastMutationDiagnostics.DirtyTileCount == ExpectedTileCount();

            Undo.PerformUndo();
            TerrainNodeElevationSource undone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool undoState =
                undone != null && undone.NodeCount == 2 &&
                undone.Nodes[0].StableId == firstId &&
                data.authoringRevision == revisionBefore &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            Undo.PerformRedo();
            bool redoState =
                data.RegionalElevationSource == null &&
                data.authoringRevision == revisionBefore + 1 &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            AddResult(
                "Explicit regional source removal is undoable and whole-world",
                removedState && undoState && redoState ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                removedState && undoState && redoState
                    ? "Source removal preserved committed terrain semantics, Undo restored the node source, and Redo removed it again with whole-world change tracking."
                    : removeError);
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static void ValidateAddUndoRedoAndChangeTracker()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(Vector2.zero, 40f),
            new NodeSpec(new Vector2(100f, 100f), 80f));

        try
        {
            TerrainNodeElevationSource source = data.RegionalElevationSource as TerrainNodeElevationSource;
            int initialCount = source.NodeCount;
            int revisionBefore = data.authoringRevision;

            bool added = TerrainRegionalElevationService.AddNode(
                data,
                worldSettings,
                new Vector2(50f, 50f),
                60f,
                out string addedId,
                out string addError);

            Undo.PerformUndo();
            TerrainNodeElevationSource undone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool undoPassed =
                added && undone != null && undone.NodeCount == initialCount &&
                data.authoringRevision == revisionBefore &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            Undo.PerformRedo();
            TerrainNodeElevationSource redone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool redoPassed =
                redone != null && redone.NodeCount == initialCount + 1 &&
                redone.Nodes[redone.NodeCount - 1].StableId == addedId &&
                data.authoringRevision == revisionBefore + 1 &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            AddResult(
                "AddNode Undo/Redo is detected by the generalized regional tracker",
                undoPassed && redoPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                undoPassed && redoPassed
                    ? "Unity restored serialized node/revision state and the Package 5 tracker classified both Undo and Redo as complete-world output changes without a second setup-only listener."
                    : addError);
        }
        finally
        {
            ClearFixture(data);
        }
    }


    private static void ValidateRemoveMoveElevationUndoRedo()
    {
        bool removePassed = ValidateRemoveUndoRedo(out string removeDetails);
        bool movePassed = ValidateMoveUndoRedo(out string moveDetails);
        bool elevationPassed = ValidateElevationUndoRedo(out string elevationDetails);

        AddResult(
            "Remove, move, and elevation edits each support Undo/Redo",
            removePassed && movePassed && elevationPassed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            removePassed && movePassed && elevationPassed
                ? "Each discrete production service operation restored its exact pre-state on Undo, restored its post-state on Redo, restored revision values, and was recognized as a complete-world regional output change."
                : removeDetails + " " + moveDetails + " " + elevationDetails);
    }

    private static bool ValidateRemoveUndoRedo(out string details)
    {
        details = "";
        TerrainAuthoringData data = CreateData(
            new NodeSpec(Vector2.zero, 10f),
            new NodeSpec(new Vector2(50f, 50f), 20f),
            new NodeSpec(new Vector2(100f, 100f), 30f));

        try
        {
            TerrainNodeElevationSource source = data.RegionalElevationSource as TerrainNodeElevationSource;
            string removedId = source.Nodes[1].StableId;
            int revision = data.authoringRevision;

            if (!TerrainRegionalElevationService.RemoveNode(
                data, worldSettings, removedId, out details))
            {
                return false;
            }

            Undo.PerformUndo();
            TerrainNodeElevationSource undone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool undoOk =
                undone != null && undone.NodeCount == 3 &&
                undone.Nodes[1].StableId == removedId &&
                data.authoringRevision == revision &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            Undo.PerformRedo();
            TerrainNodeElevationSource redone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool redoOk =
                redone != null && redone.NodeCount == 2 &&
                redone.Nodes[0].StableId != removedId &&
                redone.Nodes[1].StableId != removedId &&
                data.authoringRevision == revision + 1 &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            return undoOk && redoOk;
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static bool ValidateMoveUndoRedo(out string details)
    {
        details = "";
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(10f, 20f), 30f),
            new NodeSpec(new Vector2(90f, 80f), 100f));

        try
        {
            TerrainNodeElevationSource source = data.RegionalElevationSource as TerrainNodeElevationSource;
            string id = source.Nodes[0].StableId;
            Vector2 before = source.Nodes[0].PositionXZ;
            Vector2 after = new Vector2(44f, 55f);
            int revision = data.authoringRevision;

            if (!TerrainRegionalElevationService.SetNodePosition(
                data, worldSettings, id, after, out details))
            {
                return false;
            }

            Undo.PerformUndo();
            TerrainNodeElevationSource undone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool undoOk =
                undone != null && undone.Nodes[0].PositionXZ == before &&
                undone.Nodes[0].StableId == id &&
                data.authoringRevision == revision &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            Undo.PerformRedo();
            TerrainNodeElevationSource redone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool redoOk =
                redone != null && redone.Nodes[0].PositionXZ == after &&
                redone.Nodes[0].StableId == id &&
                data.authoringRevision == revision + 1 &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            return undoOk && redoOk;
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static bool ValidateElevationUndoRedo(out string details)
    {
        details = "";
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(10f, 20f), 30f),
            new NodeSpec(new Vector2(90f, 80f), 100f));

        try
        {
            TerrainNodeElevationSource source = data.RegionalElevationSource as TerrainNodeElevationSource;
            string id = source.Nodes[0].StableId;
            float before = source.Nodes[0].Elevation;
            float after = 175f;
            int revision = data.authoringRevision;

            if (!TerrainRegionalElevationService.SetNodeElevation(
                data, worldSettings, id, after, out details))
            {
                return false;
            }

            Undo.PerformUndo();
            TerrainNodeElevationSource undone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool undoOk =
                undone != null && undone.Nodes[0].Elevation == before &&
                undone.Nodes[0].StableId == id &&
                data.authoringRevision == revision &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            Undo.PerformRedo();
            TerrainNodeElevationSource redone = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool redoOk =
                redone != null && redone.Nodes[0].Elevation == after &&
                redone.Nodes[0].StableId == id &&
                data.authoringRevision == revision + 1 &&
                TerrainRegionalElevationChangeTracker.LastUndoRedoDirtyTileCount == ExpectedTileCount();

            return undoOk && redoOk;
        }
        finally
        {
            ClearFixture(data);
        }
    }

    private static void ValidateProductionUiMutationBoundary()
    {
        string setupPath = Path.Combine(
            Application.dataPath,
            "WorldMeshes/Editor/Window/Settings/WorldMeshesEditorWindow.RegionalElevationSetup.cs");
        string managementPath = Path.Combine(
            Application.dataPath,
            "WorldMeshes/Editor/Window/Settings/WorldMeshesEditorWindow.RegionalElevationManagement.cs");
        string oldTrackerPath = Path.Combine(
            Application.dataPath,
            "WorldMeshes/Editor/Authoring/RegionalElevation/TerrainRegionalElevationSetupUndoTracker.cs");

        bool filesExist =
            File.Exists(setupPath) && File.Exists(managementPath) && File.Exists(oldTrackerPath);

        string setup = filesExist ? File.ReadAllText(setupPath) : "";
        string management = filesExist ? File.ReadAllText(managementPath) : "";
        string oldTracker = filesExist ? File.ReadAllText(oldTrackerPath) : "";

        bool setupUsesService = setup.Contains("TerrainRegionalElevationService") &&
                                setup.Contains("ReplaceNodeLayoutWithGrid");
        bool setupAvoidsDirectOwnership =
            !setup.Contains(".SetRegionalElevationSourceInternal(") &&
            !setup.Contains("TerrainRuntimeInvalidationService") &&
            !setup.Contains("NotifyCompositeAuthoringStateChanged");
        bool managementUsesService = management.Contains("TerrainRegionalElevationService") &&
                                     !management.Contains(".SetPositionXZInternal(") &&
                                     !management.Contains(".SetElevationInternal(") &&
                                     !management.Contains(".AddNodeInternal(") &&
                                     !management.Contains(".RemoveNodeAtInternal(");
        bool oldTrackerIsShim = oldTracker.Contains("TerrainRegionalElevationChangeTracker") &&
                                !oldTracker.Contains("Undo.undoRedoPerformed");

        bool passed = filesExist && setupUsesService && setupAvoidsDirectOwnership &&
                      managementUsesService && oldTrackerIsShim;

        AddResult(
            "Production UI routes regional mutation through one service boundary",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Package 2 setup and Package 5 management use TerrainRegionalElevationService, while the Package 4 setup tracker is a non-subscribing compatibility shim."
                : "Production regional UI/tracker source did not match the expected Package 5 mutation-boundary architecture.");
    }

    private static void ValidateInteractiveCommit()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(10f, 10f), 20f),
            new NodeSpec(new Vector2(90f, 90f), 180f));

        try
        {
            TerrainNodeElevationSource source = data.RegionalElevationSource as TerrainNodeElevationSource;
            string id = source.Nodes[0].StableId;
            int revisionBefore = data.authoringRevision;

            string beginError = "";
            string updateError1 = "";
            string updateError2 = "";
            string updateError3 = "";
            string commitError = "";

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                data,
                worldSettings,
                id,
                "Interactive Regional Node Validation",
                out beginError);
            bool update1 = began && TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                new Vector2(20f, 25f), out updateError1);
            bool update2 = update1 && TerrainRegionalElevationService.UpdateInteractiveNodePosition(
                new Vector2(30f, 35f), out updateError2);
            bool update3 = update2 && TerrainRegionalElevationService.UpdateInteractiveNode(
                new Vector2(40f, 45f), 75f, out updateError3);
            bool committed = update3 && TerrainRegionalElevationService.CommitInteractiveNodeEdit(
                out commitError);

            TerrainRegionalElevationMutationDiagnostics diagnostics =
                TerrainRegionalElevationService.LastMutationDiagnostics;

            bool passed =
                committed &&
                source.Nodes[0].StableId == id &&
                source.Nodes[0].PositionXZ == new Vector2(40f, 45f) &&
                source.Nodes[0].Elevation == 75f &&
                data.authoringRevision == revisionBefore + 1 &&
                diagnostics != null && diagnostics.Changed &&
                diagnostics.DirtyTileCount == ExpectedTileCount();

            AddResult(
                "Interactive node updates commit as one final authoring transaction",
                passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                passed
                    ? "Multiple temporary updates produced one final revision increment, preserved StableId, and recorded one complete-world commit."
                    : beginError + " " + updateError1 + " " + updateError2 + " " + updateError3 + " " + commitError);
        }
        finally
        {
            if (TerrainRegionalElevationService.HasActiveInteractiveEdit)
            {
                TerrainRegionalElevationService.CancelInteractiveNodeEdit(out _);
            }
            ClearFixture(data);
        }
    }

    private static void ValidateInteractiveCancelAndNoOp()
    {
        TerrainAuthoringData data = CreateData(
            new NodeSpec(new Vector2(15f, 25f), 35f),
            new NodeSpec(new Vector2(85f, 75f), 135f));

        try
        {
            TerrainNodeElevationSource source = data.RegionalElevationSource as TerrainNodeElevationSource;
            string id = source.Nodes[0].StableId;
            Vector2 originalPosition = source.Nodes[0].PositionXZ;
            float originalElevation = source.Nodes[0].Elevation;
            int revision = data.authoringRevision;
            string signature = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data);

            string beginError = "";
            string updateError = "";
            string cancelError = "";

            bool began = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                data, worldSettings, id, "Cancel Regional Node Validation", out beginError);
            bool updated = began && TerrainRegionalElevationService.UpdateInteractiveNode(
                new Vector2(200f, 300f), 400f, out updateError);
            bool cancelled = updated && TerrainRegionalElevationService.CancelInteractiveNodeEdit(
                out cancelError);

            TerrainNodeElevationSource restoredSource = data.RegionalElevationSource as TerrainNodeElevationSource;
            bool cancelPassed =
                cancelled && restoredSource != null &&
                restoredSource.Nodes[0].PositionXZ == originalPosition &&
                restoredSource.Nodes[0].Elevation == originalElevation &&
                data.authoringRevision == revision &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data) == signature;

            AddResult(
                "Interactive cancel restores the complete starting state",
                cancelPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                cancelPassed
                    ? "Temporary position/elevation changes were reverted without a revision or persistent signature change."
                    : beginError + " " + updateError + " " + cancelError);

            restoredSource = data.RegionalElevationSource as TerrainNodeElevationSource;
            id = restoredSource.Nodes[0].StableId;
            originalPosition = restoredSource.Nodes[0].PositionXZ;
            originalElevation = restoredSource.Nodes[0].Elevation;
            revision = data.authoringRevision;
            signature = TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data);

            string noOpBeginError = "";
            string awayError = "";
            string backError = "";
            string noOpCommitError = "";

            bool beganNoOp = TerrainRegionalElevationService.BeginInteractiveNodeEdit(
                data, worldSettings, id, "No-op Regional Node Validation", out noOpBeginError);
            bool away = beganNoOp && TerrainRegionalElevationService.UpdateInteractiveNode(
                originalPosition + new Vector2(10f, 10f), originalElevation + 10f, out awayError);
            bool back = away && TerrainRegionalElevationService.UpdateInteractiveNode(
                originalPosition, originalElevation, out backError);
            bool committedNoOp = back && TerrainRegionalElevationService.CommitInteractiveNodeEdit(
                out noOpCommitError);

            bool noOpPassed =
                committedNoOp &&
                data.authoringRevision == revision &&
                TerrainAuthoringStateUtility.GetOverallAuthoringSignature(worldSettings, data) == signature &&
                TerrainRegionalElevationService.LastMutationDiagnostics != null &&
                !TerrainRegionalElevationService.LastMutationDiagnostics.Changed;

            AddResult(
                "Interactive gesture ending at its start is a no-op",
                noOpPassed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
                noOpPassed
                    ? "A temporary move away and back committed no revision/signature change and no meaningful final transaction."
                    : noOpBeginError + " " + awayError + " " + backError + " " + noOpCommitError);
        }
        finally
        {
            if (TerrainRegionalElevationService.HasActiveInteractiveEdit)
            {
                TerrainRegionalElevationService.CancelInteractiveNodeEdit(out _);
            }
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
                ? "Transient Package 5 service/Undo/interactive validation did not alter the real regional source, revision, committed signature, or overall authoring signature."
                : "The real authoring state changed during Package 5 validation.");
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

    private static void ClearFixture(TerrainAuthoringData data)
    {
        if (data == null)
        {
            return;
        }

        TerrainRegionalElevationChangeTracker.Forget(data);
        Undo.ClearUndo(data);
        UnityEngine.Object.DestroyImmediate(data);
    }

    private static bool AllElevations(TerrainNodeElevationSource source, float expected)
    {
        if (source == null)
        {
            return false;
        }

        for (int index = 0; index < source.NodeCount; index++)
        {
            if (source.Nodes[index] == null || source.Nodes[index].Elevation != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static int ExpectedTileCount()
    {
        return worldSettings.HeightTileGridWidth * worldSettings.HeightTileGridHeight;
    }

    private static bool HasWorldCorners(IReadOnlyList<Vector2Int> tiles)
    {
        if (tiles == null || tiles.Count == 0)
        {
            return false;
        }

        Vector2Int last = new Vector2Int(
            worldSettings.HeightTileGridWidth - 1,
            worldSettings.HeightTileGridHeight - 1);

        bool hasFirst = false;
        bool hasLast = false;
        for (int index = 0; index < tiles.Count; index++)
        {
            hasFirst |= tiles[index] == Vector2Int.zero;
            hasLast |= tiles[index] == last;
        }

        return hasFirst && hasLast;
    }

    private static void AddResult(string name, ValidationOutcome outcome, string details)
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
        builder.AppendLine("WorldMeshes Regional Elevation Node Management Validation");
        builder.AppendLine("=========================================================");
        builder.AppendLine();

        int passed = 0;
        int failed = 0;
        int blocked = 0;

        for (int index = 0; index < results.Count; index++)
        {
            ValidationResult result = results[index];
            string label;
            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    label = "PASS";
                    passed++;
                    break;
                case ValidationOutcome.Blocked:
                    label = "BLOCKED";
                    blocked++;
                    break;
                default:
                    label = "FAIL";
                    failed++;
                    break;
            }

            builder.AppendLine(label + " - " + result.Name);
            if (!string.IsNullOrEmpty(result.Details))
            {
                builder.AppendLine("       " + result.Details.Replace("\n", "\n       "));
            }
            builder.AppendLine();
        }

        builder.AppendLine("---------------------------------------------------------");
        builder.AppendLine($"{passed} passed");
        builder.AppendLine($"{failed} failed");
        builder.AppendLine($"{blocked} blocked");
        builder.AppendLine();

        string finalStatus = failed == 0 && blocked == 0
            ? "PASSED"
            : failed > 0 ? "FAILED" : "BLOCKED";

        builder.AppendLine("Regional elevation node management validation: " + finalStatus);

        if (failed == 0 && blocked == 0)
        {
            Debug.Log(builder.ToString());
        }
        else if (failed > 0)
        {
            Debug.LogError(builder.ToString());
        }
        else
        {
            Debug.LogWarning(builder.ToString());
        }
    }
}
