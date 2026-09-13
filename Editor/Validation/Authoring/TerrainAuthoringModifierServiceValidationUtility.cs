using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TerrainAuthoringModifierServiceValidationUtility
{
    private const string TempFolder =
        "Assets/WorldMeshes/Editor/Validation/Stage12Temp";

    private const string TempDataPath =
        TempFolder +
        "/TerrainAuthoringData_Stage12Validation.asset";

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

    private static readonly List<ValidationResult>
        results =
            new List<ValidationResult>();

    private static bool validationRunning;
    private static bool validationScheduled;

    private static TerrainAuthoringData tempData;
    private static WorldSettings worldSettings;
    private static string modifierId;

    private static int undoExpectedRevision;
    private static int redoExpectedRevision;

    private static string undoExpectedOverallSignature;
    private static string redoExpectedOverallSignature;
    private static string committedSignatureBaseline;

    public static bool IsRunning =>
        validationRunning
        ||
        validationScheduled;

    public static bool IsScheduled =>
        validationScheduled;

    /*
     * Safe public entry point.
     *
     * Always defer the validation until after the current IMGUI event.
     * The validation performs AssetDatabase and Undo operations which
     * must not run while the WorldMeshes settings panel still owns an
     * active GUILayout / ScrollView / Area stack.
     */
    public static void RequestValidation()
    {
        if (
            validationRunning
            ||
            validationScheduled
        )
        {
            return;
        }

        validationScheduled =
            true;

        /*
         * Defensive remove-before-add prevents accidental duplicate
         * queued callbacks if this method is invoked through multiple
         * editor paths in the same frame.
         */
        EditorApplication.delayCall -=
            RunScheduledValidation;

        EditorApplication.delayCall +=
            RunScheduledValidation;
    }

    /*
     * Compatibility entry point retained for any existing callers.
     * It is intentionally no longer an immediate execution path.
     */
    public static void ValidateAuthoringChangePipeline()
    {
        RequestValidation();
    }

    private static void RunScheduledValidation()
    {
        EditorApplication.delayCall -=
            RunScheduledValidation;

        if (!validationScheduled)
        {
            return;
        }

        validationScheduled =
            false;

        if (validationRunning)
        {
            return;
        }

        validationRunning =
            true;

        results.Clear();

        try
        {
            if (!RunSynchronousValidation())
            {
                FinishValidation();
                return;
            }

            Undo.PerformUndo();

            ScheduleUndoVerification();
        }
        catch (Exception exception)
        {
            AddResult(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );

            FinishValidation();
        }
    }

    private static void ScheduleUndoVerification()
    {
        EditorApplication.delayCall -=
            VerifyUndoAndRequestRedo;

        EditorApplication.delayCall +=
            VerifyUndoAndRequestRedo;
    }

    private static void ScheduleRedoVerification()
    {
        EditorApplication.delayCall -=
            VerifyRedoAndFinish;

        EditorApplication.delayCall +=
            VerifyRedoAndFinish;
    }

    private static bool RunSynchronousValidation()
    {
        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
            ||
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                "Run Stage 12 validation while the editor is idle in Edit Mode."
            );

            return false;
        }

        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData realData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        if (
            worldSettings == null
            ||
            realData == null
        )
        {
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                "WorldSettings or TerrainAuthoringData could not be loaded."
            );

            return false;
        }

        committedSignatureBaseline =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        if (
            string.IsNullOrEmpty(
                committedSignatureBaseline
            )
        )
        {
            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Blocked,
                "Committed authoring signature is unavailable."
            );

            return false;
        }

        AddResult(
            "Validation prerequisites",
            ValidationOutcome.Pass,
            "Current authoring assets and committed signature are available."
        );

        CleanupTemporaryAssets();
        EnsureTempFolder();

        tempData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        tempData.authoringRevision =
            realData.authoringRevision;

        AssetDatabase.CreateAsset(
            tempData,
            TempDataPath
        );

        int revision0 =
            tempData.authoringRevision;

        string overall0 =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        float tileSize =
            worldSettings.HeightTileWorldSize;

        Vector2 startPosition =
            new Vector2(
                tileSize * 1.5f,
                tileSize * 1.5f
            );

        Vector2 modifierSize =
            new Vector2(
                tileSize * 0.2f,
                tileSize * 0.2f
            );

        Require(
            TerrainAuthoringModifierService
                .AddStampModifier(
                    tempData,
                    worldSettings,
                    null,
                    startPosition,
                    modifierSize,
                    12f,
                    0.25f,
                    out modifierId,
                    out string addError
                ),
            "Add modifier",
            addError
        );

        TerrainAuthoringModifierMutationDiagnostics addDiagnostics =
            TerrainAuthoringModifierService
                .LastMutationDiagnostics;

        AddResult(
            "Add increments revision exactly once",
            tempData.authoringRevision ==
                revision0 + 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Revision: {revision0} -> {tempData.authoringRevision}"
        );

        AddResult(
            "Add generates stable ID",
            !string.IsNullOrEmpty(
                modifierId
            )
            &&
            tempData.HeightModifierCount ==
                1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Modifier count={tempData.HeightModifierCount}, ID={modifierId}"
        );

        ValidateSignatureTransition(
            "Add signature behavior",
            overall0,
            addDiagnostics
        );

        // -------------------------------------------------
        // FAR MOVE: old tile set UNION new tile set only
        // -------------------------------------------------

        TerrainStampModifier stamp =
            (TerrainStampModifier)
                tempData.HeightModifiers[0];

        Bounds oldBounds =
            stamp.GetAffectedWorldBounds();

        int farTileX =
            Mathf.Clamp(
                10,
                3,
                Mathf.Max(
                    3,
                    worldSettings.HeightTileGridWidth -
                    2
                )
            );

        Vector2 farPosition =
            new Vector2(
                tileSize * (
                    farTileX +
                    0.5f
                ),
                startPosition.y
            );

        int revisionBeforeMove =
            tempData.authoringRevision;

        string overallBeforeMove =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        Require(
            TerrainAuthoringModifierService
                .SetStampPositionXZ(
                    tempData,
                    worldSettings,
                    modifierId,
                    farPosition,
                    out string moveError
                ),
            "Far modifier move",
            moveError
        );

        Bounds newBounds =
            ((TerrainStampModifier)
                tempData.HeightModifiers[0])
                .GetAffectedWorldBounds();

        HashSet<Vector2Int> expectedMoveTiles =
            new HashSet<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                oldBounds,
                expectedMoveTiles,
                1
            );

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                newBounds,
                expectedMoveTiles,
                1
            );

        HashSet<Vector2Int> actualMoveTiles =
            new HashSet<Vector2Int>(
                TerrainAuthoringModifierService
                    .LastMutationDiagnostics
                    .DirtyTiles
            );

        AddResult(
            "Far move dirties old UNION new tile sets",
            actualMoveTiles
                .SetEquals(
                    expectedMoveTiles
                )
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Expected {expectedMoveTiles.Count} tiles; actual {actualMoveTiles.Count}."
        );

        AddResult(
            "Far move increments revision exactly once",
            tempData.authoringRevision ==
                revisionBeforeMove + 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Revision: {revisionBeforeMove} -> {tempData.authoringRevision}"
        );

        ValidateSignatureTransition(
            "Far move signature behavior",
            overallBeforeMove,
            TerrainAuthoringModifierService
                .LastMutationDiagnostics
        );

        ValidateNoEnclosingCorridor(
            oldBounds,
            newBounds,
            actualMoveTiles
        );

        // -------------------------------------------------
        // PARAMETER CHANGE
        // -------------------------------------------------

        int revisionBeforeHeight =
            tempData.authoringRevision;

        string overallBeforeHeight =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        Require(
            TerrainAuthoringModifierService
                .SetStampHeightDelta(
                    tempData,
                    worldSettings,
                    modifierId,
                    24f,
                    out string heightError
                ),
            "Height parameter change",
            heightError
        );

        AddResult(
            "Parameter change increments revision exactly once",
            tempData.authoringRevision ==
                revisionBeforeHeight + 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Revision: {revisionBeforeHeight} -> {tempData.authoringRevision}"
        );

        ValidateSignatureTransition(
            "Parameter signature behavior",
            overallBeforeHeight,
            TerrainAuthoringModifierService
                .LastMutationDiagnostics
        );

        // -------------------------------------------------
        // DISABLE + ZERO-DIRTY DISABLED EDIT + ENABLE
        // -------------------------------------------------

        int revisionBeforeDisable =
            tempData.authoringRevision;

        Require(
            TerrainAuthoringModifierService
                .SetModifierEnabled(
                    tempData,
                    worldSettings,
                    modifierId,
                    false,
                    out string disableError
                ),
            "Disable modifier",
            disableError
        );

        AddResult(
            "Disable dirties previous contribution",
            TerrainAuthoringModifierService
                .LastMutationDiagnostics
                .DirtyTileCount >
            0
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Dirty tiles={TerrainAuthoringModifierService.LastMutationDiagnostics.DirtyTileCount}"
        );

        AddResult(
            "Disable increments revision exactly once",
            tempData.authoringRevision ==
                revisionBeforeDisable + 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Revision: {revisionBeforeDisable} -> {tempData.authoringRevision}"
        );

        int revisionBeforeDisabledMove =
            tempData.authoringRevision;

        string overallBeforeDisabledMove =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        Require(
            TerrainAuthoringModifierService
                .SetStampPositionXZ(
                    tempData,
                    worldSettings,
                    modifierId,
                    farPosition +
                        new Vector2(
                            tileSize,
                            0f
                        ),
                    out string disabledMoveError
                ),
            "Disabled modifier move",
            disabledMoveError
        );

        TerrainAuthoringModifierMutationDiagnostics
            disabledMoveDiagnostics =
                TerrainAuthoringModifierService
                    .LastMutationDiagnostics;

        AddResult(
            "Disabled parameter edit produces zero dirty tiles",
            disabledMoveDiagnostics
                .DirtyTileCount ==
            0
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Dirty tiles={disabledMoveDiagnostics.DirtyTileCount}"
        );

        AddResult(
            "Disabled edit still changes overall signature",
            disabledMoveDiagnostics
                .OverallSignatureAfter
            !=
            overallBeforeDisabledMove
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Overall authoring state tracks disabled modifier parameters."
        );

        AddResult(
            "Disabled edit increments revision exactly once",
            tempData.authoringRevision ==
                revisionBeforeDisabledMove + 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Revision: {revisionBeforeDisabledMove} -> {tempData.authoringRevision}"
        );

        Require(
            TerrainAuthoringModifierService
                .SetModifierEnabled(
                    tempData,
                    worldSettings,
                    modifierId,
                    true,
                    out string enableError
                ),
            "Enable modifier",
            enableError
        );

        AddResult(
            "Enable dirties restored contribution",
            TerrainAuthoringModifierService
                .LastMutationDiagnostics
                .DirtyTileCount >
            0
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Dirty tiles={TerrainAuthoringModifierService.LastMutationDiagnostics.DirtyTileCount}"
        );

        // -------------------------------------------------
        // DUPLICATE / REORDER / REMOVE
        // -------------------------------------------------

        int revisionBeforeDuplicate =
            tempData.authoringRevision;

        Require(
            TerrainAuthoringModifierService
                .DuplicateModifier(
                    tempData,
                    worldSettings,
                    modifierId,
                    out string duplicateId,
                    out string duplicateError
                ),
            "Duplicate modifier",
            duplicateError
        );

        AddResult(
            "Duplicate gets fresh stable ID",
            !string.IsNullOrEmpty(
                duplicateId
            )
            &&
            duplicateId !=
                modifierId
            &&
            tempData.HeightModifierCount ==
                2
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Original={modifierId}, duplicate={duplicateId}"
        );

        AddResult(
            "Duplicate increments revision exactly once",
            tempData.authoringRevision ==
                revisionBeforeDuplicate + 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Revision: {revisionBeforeDuplicate} -> {tempData.authoringRevision}"
        );

        Require(
            TerrainAuthoringModifierService
                .SetStampPositionXZ(
                    tempData,
                    worldSettings,
                    duplicateId,
                    new Vector2(
                        tileSize * 2.5f,
                        tileSize * 3.5f
                    ),
                    out string duplicateMoveError
                ),
            "Move duplicate",
            duplicateMoveError
        );

        int revisionBeforeReorder =
            tempData.authoringRevision;

        Require(
            TerrainAuthoringModifierService
                .ReorderModifier(
                    tempData,
                    worldSettings,
                    duplicateId,
                    0,
                    out string reorderError
                ),
            "Reorder modifiers",
            reorderError
        );

        AddResult(
            "Reorder increments revision exactly once",
            tempData.authoringRevision ==
                revisionBeforeReorder + 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Revision: {revisionBeforeReorder} -> {tempData.authoringRevision}"
        );

        AddResult(
            "Reorder dirties changed composition positions",
            TerrainAuthoringModifierService
                .LastMutationDiagnostics
                .DirtyTileCount >
            0
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Dirty tiles={TerrainAuthoringModifierService.LastMutationDiagnostics.DirtyTileCount}"
        );

        int revisionBeforeRemove =
            tempData.authoringRevision;

        Require(
            TerrainAuthoringModifierService
                .RemoveModifier(
                    tempData,
                    worldSettings,
                    duplicateId,
                    out string removeError
                ),
            "Remove modifier",
            removeError
        );

        AddResult(
            "Remove increments revision exactly once",
            tempData.authoringRevision ==
                revisionBeforeRemove + 1
                &&
            tempData.HeightModifierCount ==
                1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Revision: {revisionBeforeRemove} -> {tempData.authoringRevision}; count={tempData.HeightModifierCount}"
        );

        // -------------------------------------------------
        // FINAL MUTATION FOR UNDO / REDO
        // -------------------------------------------------

        undoExpectedRevision =
            tempData.authoringRevision;

        undoExpectedOverallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        TerrainStampModifier finalStamp =
            (TerrainStampModifier)
                tempData.HeightModifiers[0];

        float newFalloff =
            Mathf.Clamp01(
                finalStamp.Falloff +
                0.2f
            );

        if (
            Mathf.Approximately(
                newFalloff,
                finalStamp.Falloff
            )
        )
        {
            newFalloff =
                Mathf.Clamp01(
                    finalStamp.Falloff -
                    0.2f
                );
        }

        Require(
            TerrainAuthoringModifierService
                .SetStampFalloff(
                    tempData,
                    worldSettings,
                    modifierId,
                    newFalloff,
                    out string finalError
                ),
            "Final mutation for Undo/Redo",
            finalError
        );

        redoExpectedRevision =
            tempData.authoringRevision;

        redoExpectedOverallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    tempData
                );

        AddResult(
            "Final edit increments revision exactly once",
            redoExpectedRevision ==
                undoExpectedRevision + 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Revision: {undoExpectedRevision} -> {redoExpectedRevision}"
        );

        return true;
    }

    private static void VerifyUndoAndRequestRedo()
    {
        EditorApplication.delayCall -=
            VerifyUndoAndRequestRedo;

        if (!validationRunning)
        {
            return;
        }

        try
        {
            string currentOverall =
                TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        worldSettings,
                        tempData
                    );

            string currentCommitted =
                TerrainAuthoringStateUtility
                    .GetCommittedHeightfieldSignature(
                        worldSettings
                    );

            bool passed =
                tempData != null
                &&
                tempData.authoringRevision ==
                    undoExpectedRevision
                &&
                currentOverall ==
                    undoExpectedOverallSignature
                &&
                currentCommitted ==
                    committedSignatureBaseline;

            AddResult(
                "Undo restores revision and authoring signature",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "Undo restored the previous serialized modifier state and authoringRevision."
                    : $"Revision expected/actual: {undoExpectedRevision}/{(tempData != null ? tempData.authoringRevision : -1)}"
            );

            AddResult(
                "Undo change tracker dirties affected tiles",
                TerrainAuthoringModifierChangeTracker
                    .LastUndoRedoDirtyTileCount >
                0
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                $"Dirty tiles={TerrainAuthoringModifierChangeTracker.LastUndoRedoDirtyTileCount}"
            );

            Undo.PerformRedo();

            ScheduleRedoVerification();
        }
        catch (Exception exception)
        {
            AddResult(
                "Undo validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );

            FinishValidation();
        }
    }

    private static void VerifyRedoAndFinish()
    {
        EditorApplication.delayCall -=
            VerifyRedoAndFinish;

        if (!validationRunning)
        {
            return;
        }

        try
        {
            string currentOverall =
                TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        worldSettings,
                        tempData
                    );

            string currentCommitted =
                TerrainAuthoringStateUtility
                    .GetCommittedHeightfieldSignature(
                        worldSettings
                    );

            bool passed =
                tempData != null
                &&
                tempData.authoringRevision ==
                    redoExpectedRevision
                &&
                currentOverall ==
                    redoExpectedOverallSignature
                &&
                currentCommitted ==
                    committedSignatureBaseline;

            AddResult(
                "Redo restores changed revision and signature",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "Redo restored the changed modifier state without an extra revision increment."
                    : $"Revision expected/actual: {redoExpectedRevision}/{(tempData != null ? tempData.authoringRevision : -1)}"
            );

            AddResult(
                "Redo change tracker dirties affected tiles",
                TerrainAuthoringModifierChangeTracker
                    .LastUndoRedoDirtyTileCount >
                0
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                $"Dirty tiles={TerrainAuthoringModifierChangeTracker.LastUndoRedoDirtyTileCount}"
            );
        }
        catch (Exception exception)
        {
            AddResult(
                "Redo validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );
        }

        FinishValidation();
    }

    private static void ValidateSignatureTransition(
        string name,
        string overallBefore,
        TerrainAuthoringModifierMutationDiagnostics diagnostics
    )
    {
        bool committedStable =
            diagnostics != null
            &&
            diagnostics.CommittedSignatureBefore ==
                diagnostics.CommittedSignatureAfter
            &&
            diagnostics.CommittedSignatureAfter ==
                committedSignatureBaseline;

        bool overallChanged =
            diagnostics != null
            &&
            diagnostics.OverallSignatureBefore ==
                overallBefore
            &&
            diagnostics.OverallSignatureAfter !=
                overallBefore;

        AddResult(
            name,
            committedStable
            &&
            overallChanged
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            committedStable
            &&
            overallChanged
                ? "Committed signature stayed fixed while OverallAuthoringSignature changed."
                : "Signature transition did not match Stage 12 invariants."
        );
    }

    private static void ValidateNoEnclosingCorridor(
        Bounds oldBounds,
        Bounds newBounds,
        HashSet<Vector2Int> actualTiles
    )
    {
        Bounds enclosing =
            oldBounds;

        enclosing.Encapsulate(
            newBounds
        );

        HashSet<Vector2Int> enclosingTiles =
            new HashSet<Vector2Int>();

        TerrainAuthoringPreviewDirtyRegionUtility
            .CollectTilesOverlappingBounds(
                worldSettings,
                enclosing,
                enclosingTiles,
                1
            );

        bool meaningfulComparison =
            enclosingTiles.Count >
            actualTiles.Count;

        AddResult(
            "Far move avoids enclosing dirty corridor",
            meaningfulComparison
                ? ValidationOutcome.Pass
                : ValidationOutcome.Blocked,
            meaningfulComparison
                ? $"Old/new union dirtied {actualTiles.Count} tiles versus {enclosingTiles.Count} for one enclosing rectangle."
                : "Current world/test geometry was too small to demonstrate a larger enclosing corridor."
        );
    }

    private static void Require(
        bool result,
        string operation,
        string errorMessage
    )
    {
        if (result)
        {
            return;
        }

        throw new InvalidOperationException(
            operation +
            " failed.\n\n" +
            errorMessage
        );
    }

    private static void EnsureTempFolder()
    {
        const string parent =
            "Assets/WorldMeshes/Editor/Validation";

        if (!AssetDatabase.IsValidFolder(parent))
        {
            throw new InvalidOperationException(
                "Expected validation folder does not exist:\n" +
                parent
            );
        }

        if (!AssetDatabase.IsValidFolder(TempFolder))
        {
            AssetDatabase.CreateFolder(
                parent,
                "Stage12Temp"
            );
        }
    }

    private static void CleanupTemporaryAssets()
    {
        if (tempData != null)
        {
            TerrainAuthoringModifierChangeTracker
                .Forget(
                    tempData
                );
        }

        if (AssetDatabase.IsValidFolder(TempFolder))
        {
            AssetDatabase.DeleteAsset(
                TempFolder
            );

            AssetDatabase.Refresh();
        }

        tempData = null;
    }

    private static void AddResult(
        string name,
        ValidationOutcome outcome,
        string details
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Outcome = outcome,
                Details = details ?? ""
            }
        );
    }

    private static void FinishValidation()
    {
        /*
         * Clear every callback owned by this validation before touching
         * temporary assets or publishing the final report. This keeps
         * stale delayCall delegates from firing after an exception or
         * early/blocking validation exit.
         */
        EditorApplication.delayCall -=
            RunScheduledValidation;

        EditorApplication.delayCall -=
            VerifyUndoAndRequestRedo;

        EditorApplication.delayCall -=
            VerifyRedoAndFinish;

        validationScheduled =
            false;

        CleanupTemporaryAssets();

        validationRunning = false;

        int passCount = 0;
        int failCount = 0;
        int blockedCount = 0;

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Authoring Change + Dirty Region Validation"
        );

        builder.AppendLine(
            "===================================================="
        );

        builder.AppendLine();

        foreach (ValidationResult result in results)
        {
            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    passCount++;
                    break;

                case ValidationOutcome.Fail:
                    failCount++;
                    break;

                default:
                    blockedCount++;
                    break;
            }

            string label =
                result.Outcome ==
                    ValidationOutcome.Pass
                    ? "PASS"
                    :
                    result.Outcome ==
                        ValidationOutcome.Fail
                        ? "FAIL"
                        : "BLOCKED";

            builder.AppendLine(
                $"{label} - {result.Name}"
            );

            if (!string.IsNullOrEmpty(result.Details))
            {
                foreach (
                    string line
                    in result.Details
                        .Replace("\r\n", "\n")
                        .Split('\n')
                )
                {
                    builder.AppendLine(
                        "       " + line
                    );
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine(
            "----------------------------------------------"
        );

        builder.AppendLine(
            $"{passCount} passed"
        );

        builder.AppendLine(
            $"{failCount} failed"
        );

        builder.AppendLine(
            $"{blockedCount} blocked"
        );

        builder.AppendLine();

        if (
            failCount == 0
            &&
            blockedCount == 0
        )
        {
            builder.AppendLine(
                "Stage 12 authoring change + dirty region validation: PASSED"
            );

            Debug.Log(
                builder.ToString()
            );
        }
        else if (failCount > 0)
        {
            builder.AppendLine(
                "Stage 12 authoring change + dirty region validation: FAILED"
            );

            Debug.LogError(
                builder.ToString()
            );
        }
        else
        {
            builder.AppendLine(
                "Stage 12 authoring change + dirty region validation: BLOCKED"
            );

            Debug.LogWarning(
                builder.ToString()
            );
        }

        worldSettings = null;
        modifierId = "";
    }
}
