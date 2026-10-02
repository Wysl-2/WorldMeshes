using System;
using System.Collections.Generic;
using UnityEditor;

public sealed class TerrainValidationSuiteRunner
{
    public sealed class Case
    {
        public string Name { get; }

        public Func<bool> TryStart { get; }

        public Func<bool> IsRunning { get; }

        public Func<TerrainValidationRunSummary> GetSummary { get; }

        public Case(
            string name,
            Func<bool> tryStart,
            Func<bool> isRunning,
            Func<TerrainValidationRunSummary> getSummary
        )
        {
            Name =
                string.IsNullOrEmpty(name)
                    ? "Validation Case"
                    : name;

            TryStart =
                tryStart ??
                throw new ArgumentNullException(
                    nameof(tryStart)
                );

            IsRunning =
                isRunning ??
                throw new ArgumentNullException(
                    nameof(isRunning)
                );

            GetSummary =
                getSummary ??
                throw new ArgumentNullException(
                    nameof(getSummary)
                );
        }
    }

    private readonly List<Case> cases;

    private readonly TerrainValidationRunSummary[]
        caseSummaries;

    private int currentCaseIndex =
        -1;

    private TerrainValidationRunSummary
        currentCasePreviousSummary;

    private bool isRunning;

    private TerrainValidationRunSummary summary =
        TerrainValidationRunSummary.CreateNotRun();

    public bool IsRunning =>
        isRunning;

    public TerrainValidationRunSummary Summary =>
        summary;

    public int CaseCount =>
        cases.Count;

    public TerrainValidationSuiteRunner(
        IEnumerable<Case> cases
    )
    {
        if (cases == null)
        {
            throw new ArgumentNullException(
                nameof(cases)
            );
        }

        this.cases =
            new List<Case>(
                cases
            );

        caseSummaries =
            new TerrainValidationRunSummary[
                this.cases.Count
            ];

        ResetCaseSummaries();
    }

    public string GetCaseName(
        int index
    )
    {
        return
            index >= 0
            && index < cases.Count
                ? cases[index].Name
                : "";
    }

    public TerrainValidationRunSummary GetCaseSummary(
        int index
    )
    {
        return
            index >= 0
            && index < caseSummaries.Length
                ? caseSummaries[index]
                : TerrainValidationRunSummary
                    .CreateNotRun();
    }

    public bool Start()
    {
        if (isRunning)
        {
            return false;
        }

        ResetCaseSummaries();

        if (cases.Count == 0)
        {
            summary =
                TerrainValidationRunSummary
                    .CreateCompleted(
                        0,
                        0,
                        1,
                        "No validation cases are configured."
                    );

            return false;
        }

        for (
            int index = 0;
            index < cases.Count;
            index++
        )
        {
            bool caseRunning;

            try
            {
                caseRunning =
                    cases[index]
                        .IsRunning();
            }
            catch (Exception exception)
            {
                TerrainValidationRunSummary failed =
                    CreateExceptionSummary(
                        cases[index].Name,
                        exception
                    );

                caseSummaries[index] =
                    failed;

                summary =
                    failed;

                return false;
            }

            if (caseRunning)
            {
                TerrainValidationRunSummary blocked =
                    TerrainValidationRunSummary
                        .CreateCompleted(
                            0,
                            0,
                            1,
                            cases[index].Name +
                            " is already running."
                        );

                caseSummaries[index] =
                    blocked;

                summary =
                    blocked;

                return false;
            }
        }

        currentCaseIndex =
            0;

        currentCasePreviousSummary =
            null;

        isRunning =
            true;

        summary =
            TerrainValidationRunSummary
                .CreateRunning(
                    "Validation suite is running."
                );

        EditorApplication.update -=
            HandleEditorUpdate;

        EditorApplication.update +=
            HandleEditorUpdate;

        StartCurrentCaseOrFinish();

        return true;
    }

    private void HandleEditorUpdate()
    {
        if (!isRunning)
        {
            StopObserving();
            return;
        }

        if (
            currentCaseIndex < 0
            || currentCaseIndex >= cases.Count
        )
        {
            FinishSuite();
            return;
        }

        Case validationCase =
            cases[currentCaseIndex];

        bool caseRunning;

        try
        {
            caseRunning =
                validationCase.IsRunning();
        }
        catch (Exception exception)
        {
            caseSummaries[currentCaseIndex] =
                CreateExceptionSummary(
                    validationCase.Name,
                    exception
                );

            AdvanceCase();
            return;
        }

        if (caseRunning)
        {
            return;
        }

        CaptureCompletedCase(
            validationCase
        );

        AdvanceCase();
    }

    private void StartCurrentCaseOrFinish()
    {
        while (isRunning)
        {
            if (currentCaseIndex >= cases.Count)
            {
                FinishSuite();
                return;
            }

            Case validationCase =
                cases[currentCaseIndex];

            try
            {
                currentCasePreviousSummary =
                    validationCase.GetSummary();
            }
            catch (Exception exception)
            {
                caseSummaries[currentCaseIndex] =
                    CreateExceptionSummary(
                        validationCase.Name,
                        exception
                    );

                currentCaseIndex++;
                continue;
            }

            caseSummaries[currentCaseIndex] =
                TerrainValidationRunSummary
                    .CreateRunning(
                        validationCase.Name +
                        " is running."
                    );

            bool started;

            try
            {
                started =
                    validationCase.TryStart();
            }
            catch (Exception exception)
            {
                caseSummaries[currentCaseIndex] =
                    CreateExceptionSummary(
                        validationCase.Name,
                        exception
                    );

                currentCaseIndex++;
                continue;
            }

            if (!started)
            {
                caseSummaries[currentCaseIndex] =
                    TerrainValidationRunSummary
                        .CreateCompleted(
                            0,
                            0,
                            1,
                            validationCase.Name +
                            " could not be started."
                        );

                currentCaseIndex++;
                continue;
            }

            bool caseRunning;

            try
            {
                caseRunning =
                    validationCase.IsRunning();
            }
            catch (Exception exception)
            {
                caseSummaries[currentCaseIndex] =
                    CreateExceptionSummary(
                        validationCase.Name,
                        exception
                    );

                currentCaseIndex++;
                continue;
            }

            if (caseRunning)
            {
                return;
            }

            CaptureCompletedCase(
                validationCase
            );

            currentCaseIndex++;
        }
    }

    private void CaptureCompletedCase(
        Case validationCase
    )
    {
        TerrainValidationRunSummary completed;

        try
        {
            completed =
                validationCase.GetSummary();
        }
        catch (Exception exception)
        {
            caseSummaries[currentCaseIndex] =
                CreateExceptionSummary(
                    validationCase.Name,
                    exception
                );

            return;
        }

        if (
            completed == null
            || !completed.IsTerminal
            || object.ReferenceEquals(
                completed,
                currentCasePreviousSummary
            )
        )
        {
            caseSummaries[currentCaseIndex] =
                TerrainValidationRunSummary
                    .CreateCompleted(
                        0,
                        0,
                        1,
                        validationCase.Name +
                        " completed without publishing a new terminal summary."
                    );

            return;
        }

        caseSummaries[currentCaseIndex] =
            completed;
    }

    private void AdvanceCase()
    {
        currentCaseIndex++;
        StartCurrentCaseOrFinish();
    }

    private void FinishSuite()
    {
        int passed =
            0;

        int failed =
            0;

        int blocked =
            0;

        for (
            int index = 0;
            index < caseSummaries.Length;
            index++
        )
        {
            TerrainValidationRunSummary caseSummary =
                caseSummaries[index];

            if (caseSummary == null)
            {
                continue;
            }

            passed +=
                caseSummary.PassedCount;

            failed +=
                caseSummary.FailedCount;

            blocked +=
                caseSummary.BlockedCount;
        }

        summary =
            TerrainValidationRunSummary
                .CreateCompleted(
                    passed,
                    failed,
                    blocked,
                    passed.ToString() +
                    " passed, " +
                    failed.ToString() +
                    " failed, " +
                    blocked.ToString() +
                    " blocked across " +
                    cases.Count.ToString() +
                    " cases."
                );

        isRunning =
            false;

        currentCaseIndex =
            -1;

        currentCasePreviousSummary =
            null;

        StopObserving();
    }

    private void ResetCaseSummaries()
    {
        for (
            int index = 0;
            index < caseSummaries.Length;
            index++
        )
        {
            caseSummaries[index] =
                TerrainValidationRunSummary
                    .CreateNotRun();
        }

        summary =
            TerrainValidationRunSummary
                .CreateNotRun();
    }

    private void StopObserving()
    {
        EditorApplication.update -=
            HandleEditorUpdate;
    }

    private static TerrainValidationRunSummary
        CreateExceptionSummary(
            string caseName,
            Exception exception
        )
    {
        return
            TerrainValidationRunSummary
                .CreateCompleted(
                    0,
                    1,
                    0,
                    caseName +
                    " failed while being orchestrated.\n\n" +
                    exception
                );
    }
}
