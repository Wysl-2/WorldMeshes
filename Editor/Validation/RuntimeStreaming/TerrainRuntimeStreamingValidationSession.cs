using System;
using UnityEditor;
using UnityEngine;

public sealed class TerrainRuntimeStreamingValidationSession
{
    private enum Operation
    {
        Idle,
        FocusedValidation,
        StressTest
    }

    private enum FocusedPhase
    {
        None,
        HeightCache,
        CrossResolution,
        Multiresolution,
        IndependentAnchor,
        SchedulerStress
    }

    private readonly Action repaint;

    private Operation operation;
    private FocusedPhase focusedPhase;

    private GameObject clipmapObject;
    private TerrainHeightmapCacheValidator cacheValidator;
    private TerrainClipmapDisplacementValidator displacementValidator;
    private bool ownsCacheValidator;
    private bool ownsDisplacementValidator;
    private bool callbacksSubscribed;

    public bool IsRunning =>
        operation != Operation.Idle;

    public TerrainRuntimeValidationStatus ValidationStatus { get; private set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public string ValidationSummary { get; private set; } =
        "Runtime Streaming validation has not been run.";

    public TerrainRuntimeValidationStatus HeightCacheStatus { get; private set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public string HeightCacheSummary { get; private set; } =
        "Height cache validation has not been run.";

    public TerrainRuntimeValidationStatus CrossResolutionStatus { get; private set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public string CrossResolutionSummary { get; private set; } =
        "Cross-resolution validation has not been run.";

    public TerrainRuntimeValidationStatus MultiresolutionStatus { get; private set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public string MultiresolutionSummary { get; private set; } =
        "Renderer / displacement / stitch validation has not been run.";

    public TerrainRuntimeValidationStatus IndependentAnchorStatus { get; private set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public string IndependentAnchorSummary { get; private set; } =
        "Independent-anchor stress validation has not been run.";

    public TerrainRuntimeValidationStatus SchedulerStressStatus { get; private set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public string SchedulerStressSummary { get; private set; } =
        "Height scheduler stress validation has not been run.";

    public TerrainRuntimeValidationStatus StressStatus { get; private set; } =
        TerrainRuntimeValidationStatus.NotRun;

    public string StressSummary { get; private set; } =
        "Runtime Streaming stress test has not been run.";

    public TerrainRuntimeStreamingStressResult StressResult { get; private set; }

    public TerrainRuntimeStreamingValidationSession(
        Action repaint
    )
    {
        this.repaint = repaint;
    }

    public void BeginValidation()
    {
        if (IsRunning)
        {
            return;
        }

        ResetFocusedResults();

        ValidationStatus =
            TerrainRuntimeValidationStatus.Running;

        ValidationSummary =
            "Running focused Runtime Streaming validation.";

        operation =
            Operation.FocusedValidation;

        if (
            !PrepareTemporaryValidators(
                true,
                true,
                out string reason
            )
        )
        {
            ValidationStatus =
                TerrainRuntimeValidationStatus.Inconclusive;

            ValidationSummary = reason;
            operation = Operation.Idle;
            repaint?.Invoke();
            return;
        }

        SubscribeCallbacks();

        focusedPhase =
            FocusedPhase.HeightCache;

        StartFocusedPhase();
    }

    public void BeginStressTest()
    {
        if (IsRunning)
        {
            return;
        }

        StressStatus =
            TerrainRuntimeValidationStatus.Running;

        StressSummary =
            "Running Runtime Streaming stress test.";

        StressResult = null;
        operation = Operation.StressTest;

        if (
            !PrepareTemporaryValidators(
                false,
                true,
                out string reason
            )
        )
        {
            StressStatus =
                TerrainRuntimeValidationStatus.Inconclusive;

            StressSummary = reason;
            operation = Operation.Idle;
            repaint?.Invoke();
            return;
        }

        SubscribeCallbacks();

        displacementValidator
            .BeginRuntimeStreamingStressTest();

        if (
            !displacementValidator.IsValidating
            && displacementValidator.RuntimeStreamingStressStatus !=
                TerrainRuntimeValidationStatus.Running
        )
        {
            StressStatus =
                TerrainRuntimeValidationStatus.Inconclusive;

            StressSummary =
                "The Runtime Streaming stress test could not start. See the Unity Console for its prerequisite error.";

            FinishOperation();
            return;
        }

        repaint?.Invoke();
    }

    public void Shutdown()
    {
        if (operation == Operation.FocusedValidation)
        {
            MarkFocusedPhaseInconclusive(
                "The Runtime Streaming validation session was shut down before completion."
            );

            ValidationStatus =
                TerrainRuntimeValidationStatus.Inconclusive;

            ValidationSummary =
                "The Runtime Streaming validation session was shut down before completion.";
        }
        else if (operation == Operation.StressTest)
        {
            StressStatus =
                TerrainRuntimeValidationStatus.Inconclusive;

            StressSummary =
                "The Runtime Streaming stress test was stopped before completion.";
        }

        FinishOperation();
    }

    private bool PrepareTemporaryValidators(
        bool requireCacheValidator,
        bool requireDisplacementValidator,
        out string reason
    )
    {
        reason = null;

        if (!EditorApplication.isPlaying)
        {
            reason =
                "Enter Play Mode to run Runtime Streaming validation.";

            return false;
        }

        GameObject worldRoot =
            GameObject.Find(
                TerrainWorldHierarchyGenerator.WorldRootName
            );

        if (worldRoot == null)
        {
            reason =
                "The generated WorldRoot was not found in the active scene.";

            return false;
        }

        Transform clipmapRoot =
            worldRoot.transform.Find(
                TerrainWorldHierarchyGenerator.ClipmapRootName
            );

        if (clipmapRoot == null)
        {
            reason =
                "The generated WorldRoot/Clipmap hierarchy was not found.";

            return false;
        }

        clipmapObject =
            clipmapRoot.gameObject;

        TerrainHeightmapStreamer streamer =
            clipmapObject.GetComponent<TerrainHeightmapStreamer>();

        TerrainClipmapController controller =
            clipmapObject.GetComponent<TerrainClipmapController>();

        if (
            streamer == null
            || controller == null
        )
        {
            reason =
                "The generated Clipmap is missing required production streaming components. Exit Play Mode and run Setup / Repair World Hierarchy.";

            clipmapObject = null;
            return false;
        }

        if (
            clipmapObject.GetComponent<TerrainHeightmapCacheValidator>() != null
            || clipmapObject.GetComponent<TerrainClipmapDisplacementValidator>() != null
        )
        {
            reason =
                "Persistent runtime validation components are still attached to WorldRoot/Clipmap.\n\nExit Play Mode and run Setup / Repair World Hierarchy to migrate the scene to Editor-owned runtime validation.";

            clipmapObject = null;
            return false;
        }

        try
        {
            if (requireCacheValidator)
            {
                cacheValidator =
                    clipmapObject
                        .AddComponent<TerrainHeightmapCacheValidator>();

                cacheValidator.hideFlags =
                    HideFlags.DontSave;

                ownsCacheValidator = true;
            }

            if (requireDisplacementValidator)
            {
                displacementValidator =
                    clipmapObject
                        .AddComponent<TerrainClipmapDisplacementValidator>();

                displacementValidator.hideFlags =
                    HideFlags.DontSave;

                ownsDisplacementValidator = true;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);

            reason =
                "Could not create temporary Runtime Streaming validation components. See the Unity Console for details.";

            DestroyTemporaryValidators();
            return false;
        }

        return true;
    }

    private void StartFocusedPhase()
    {
        if (
            operation != Operation.FocusedValidation
            || !EditorApplication.isPlaying
        )
        {
            return;
        }

        switch (focusedPhase)
        {
            case FocusedPhase.HeightCache:
                HeightCacheStatus =
                    TerrainRuntimeValidationStatus.Running;

                HeightCacheSummary =
                    "Validating active multiresolution Height caches.";

                cacheValidator.BeginValidation();

                if (
                    !cacheValidator.IsValidating
                    && cacheValidator.CacheValidationStatus !=
                        TerrainRuntimeValidationStatus.Running
                )
                {
                    HeightCacheStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    HeightCacheSummary =
                        "Height cache validation could not start. See the Unity Console for its prerequisite error.";

                    AdvanceFocusedPhase();
                }
                break;

            case FocusedPhase.CrossResolution:
                CrossResolutionStatus =
                    TerrainRuntimeValidationStatus.Running;

                CrossResolutionSummary =
                    "Validating cross-resolution Height consistency.";

                cacheValidator.BeginCrossResolutionValidation();

                if (
                    !cacheValidator.IsValidating
                    && cacheValidator.CrossResolutionValidationStatus !=
                        TerrainRuntimeValidationStatus.Running
                )
                {
                    CrossResolutionStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    CrossResolutionSummary =
                        "Cross-resolution validation could not start. See the Unity Console for its prerequisite error.";

                    AdvanceFocusedPhase();
                }
                break;

            case FocusedPhase.Multiresolution:
                MultiresolutionStatus =
                    TerrainRuntimeValidationStatus.Running;

                MultiresolutionSummary =
                    "Validating renderer bindings, displacement, and stitch semantics.";

                displacementValidator.BeginMultiresolutionValidation();

                if (
                    !displacementValidator.IsValidating
                    && displacementValidator.MultiresolutionValidationStatus !=
                        TerrainRuntimeValidationStatus.Running
                )
                {
                    MultiresolutionStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    MultiresolutionSummary =
                        "Renderer / displacement / stitch validation could not start. See the Unity Console for its prerequisite error.";

                    AdvanceFocusedPhase();
                }
                break;

            case FocusedPhase.IndependentAnchor:
                IndependentAnchorStatus =
                    TerrainRuntimeValidationStatus.Running;

                IndependentAnchorSummary =
                    "Running independent-anchor stress validation.";

                displacementValidator.BeginIndependentAnchorStressValidation();

                if (
                    !displacementValidator.IsValidating
                    && displacementValidator.IndependentAnchorValidationStatus !=
                        TerrainRuntimeValidationStatus.Running
                )
                {
                    IndependentAnchorStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    IndependentAnchorSummary =
                        "Independent-anchor stress validation could not start. See the Unity Console for its prerequisite error.";

                    AdvanceFocusedPhase();
                }
                break;

            case FocusedPhase.SchedulerStress:
                SchedulerStressStatus =
                    TerrainRuntimeValidationStatus.Running;

                SchedulerStressSummary =
                    "Running Height scheduler stress validation.";

                displacementValidator.BeginSchedulerStressValidation();

                if (
                    !displacementValidator.IsValidating
                    && displacementValidator.SchedulerStressValidationStatus !=
                        TerrainRuntimeValidationStatus.Running
                )
                {
                    SchedulerStressStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    SchedulerStressSummary =
                        "Height scheduler stress validation could not start. See the Unity Console for its prerequisite error.";

                    CompleteFocusedValidation();
                }
                break;
        }

        repaint?.Invoke();
    }

    private void OnEditorUpdate()
    {
        if (!EditorApplication.isPlaying)
        {
            AbortForPlayModeExit();
            return;
        }

        if (operation == Operation.FocusedValidation)
        {
            ObserveFocusedValidation();
        }
        else if (operation == Operation.StressTest)
        {
            ObserveStressTest();
        }
    }

    private void ObserveFocusedValidation()
    {
        switch (focusedPhase)
        {
            case FocusedPhase.HeightCache:
                if (cacheValidator != null && cacheValidator.IsValidating)
                {
                    return;
                }

                if (cacheValidator != null)
                {
                    HeightCacheStatus =
                        cacheValidator.CacheValidationStatus;

                    HeightCacheSummary =
                        cacheValidator.CacheValidationSummary;
                }
                else
                {
                    HeightCacheStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    HeightCacheSummary =
                        "The temporary Height cache validator became unavailable before completion.";
                }

                AdvanceFocusedPhase();
                break;

            case FocusedPhase.CrossResolution:
                if (cacheValidator != null && cacheValidator.IsValidating)
                {
                    return;
                }

                if (cacheValidator != null)
                {
                    CrossResolutionStatus =
                        cacheValidator.CrossResolutionValidationStatus;

                    CrossResolutionSummary =
                        cacheValidator.CrossResolutionValidationSummary;
                }
                else
                {
                    CrossResolutionStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    CrossResolutionSummary =
                        "The temporary Height cache validator became unavailable before completion.";
                }

                AdvanceFocusedPhase();
                break;

            case FocusedPhase.Multiresolution:
                if (displacementValidator != null && displacementValidator.IsValidating)
                {
                    return;
                }

                if (displacementValidator != null)
                {
                    MultiresolutionStatus =
                        displacementValidator.MultiresolutionValidationStatus;

                    MultiresolutionSummary =
                        displacementValidator.MultiresolutionValidationSummary;
                }
                else
                {
                    MultiresolutionStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    MultiresolutionSummary =
                        "The temporary displacement validator became unavailable before completion.";
                }

                AdvanceFocusedPhase();
                break;

            case FocusedPhase.IndependentAnchor:
                if (displacementValidator != null && displacementValidator.IsValidating)
                {
                    return;
                }

                if (displacementValidator != null)
                {
                    IndependentAnchorStatus =
                        displacementValidator.IndependentAnchorValidationStatus;

                    IndependentAnchorSummary =
                        displacementValidator.IndependentAnchorValidationSummary;
                }
                else
                {
                    IndependentAnchorStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    IndependentAnchorSummary =
                        "The temporary displacement validator became unavailable before completion.";
                }

                AdvanceFocusedPhase();
                break;

            case FocusedPhase.SchedulerStress:
                if (displacementValidator != null && displacementValidator.IsValidating)
                {
                    return;
                }

                if (displacementValidator != null)
                {
                    SchedulerStressStatus =
                        displacementValidator.SchedulerStressValidationStatus;

                    SchedulerStressSummary =
                        displacementValidator.SchedulerStressValidationSummary;
                }
                else
                {
                    SchedulerStressStatus =
                        TerrainRuntimeValidationStatus.Inconclusive;

                    SchedulerStressSummary =
                        "The temporary displacement validator became unavailable before completion.";
                }

                CompleteFocusedValidation();
                break;
        }
    }

    private void AdvanceFocusedPhase()
    {
        focusedPhase =
            focusedPhase == FocusedPhase.HeightCache
                ? FocusedPhase.CrossResolution
                : focusedPhase == FocusedPhase.CrossResolution
                    ? FocusedPhase.Multiresolution
                    : focusedPhase == FocusedPhase.Multiresolution
                        ? FocusedPhase.IndependentAnchor
                        : FocusedPhase.SchedulerStress;

        StartFocusedPhase();
    }

    private void CompleteFocusedValidation()
    {
        TerrainRuntimeValidationStatus[] statuses =
        {
            HeightCacheStatus,
            CrossResolutionStatus,
            MultiresolutionStatus,
            IndependentAnchorStatus,
            SchedulerStressStatus
        };

        int passed = 0;
        int failed = 0;
        int inconclusive = 0;

        for (int index = 0; index < statuses.Length; index++)
        {
            switch (statuses[index])
            {
                case TerrainRuntimeValidationStatus.Passed:
                    passed++;
                    break;

                case TerrainRuntimeValidationStatus.Failed:
                    failed++;
                    break;

                default:
                    inconclusive++;
                    break;
            }
        }

        ValidationStatus =
            failed > 0
                ? TerrainRuntimeValidationStatus.Failed
                : inconclusive > 0
                    ? TerrainRuntimeValidationStatus.Inconclusive
                    : TerrainRuntimeValidationStatus.Passed;

        ValidationSummary =
            $"5 checks completed: {passed} passed, {failed} failed, {inconclusive} inconclusive.";

        FinishOperation();
    }

    private void ObserveStressTest()
    {
        if (
            displacementValidator != null
            && displacementValidator.IsValidating
        )
        {
            return;
        }

        if (displacementValidator != null)
        {
            StressStatus =
                displacementValidator.RuntimeStreamingStressStatus;

            StressSummary =
                displacementValidator.RuntimeStreamingStressSummary;

            StressResult =
                displacementValidator.RuntimeStreamingStressResult;
        }
        else
        {
            StressStatus =
                TerrainRuntimeValidationStatus.Inconclusive;

            StressSummary =
                "The temporary Runtime Streaming stress validator became unavailable before completion.";
        }

        FinishOperation();
    }

    private void OnPlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        if (
            state != PlayModeStateChange.ExitingPlayMode
            || !IsRunning
        )
        {
            return;
        }

        AbortForPlayModeExit();
    }

    private void AbortForPlayModeExit()
    {
        if (operation == Operation.FocusedValidation)
        {
            MarkFocusedPhaseInconclusive(
                "Play Mode ended before the current Runtime Streaming validation check completed."
            );

            ValidationStatus =
                TerrainRuntimeValidationStatus.Inconclusive;

            ValidationSummary =
                "Play Mode ended before Runtime Streaming validation completed.";
        }
        else if (operation == Operation.StressTest)
        {
            StressStatus =
                TerrainRuntimeValidationStatus.Inconclusive;

            StressSummary =
                "Play Mode ended before the Runtime Streaming stress test completed.";
        }

        FinishOperation();
    }

    private void MarkFocusedPhaseInconclusive(
        string summary
    )
    {
        switch (focusedPhase)
        {
            case FocusedPhase.HeightCache:
                HeightCacheStatus = TerrainRuntimeValidationStatus.Inconclusive;
                HeightCacheSummary = summary;
                break;

            case FocusedPhase.CrossResolution:
                CrossResolutionStatus = TerrainRuntimeValidationStatus.Inconclusive;
                CrossResolutionSummary = summary;
                break;

            case FocusedPhase.Multiresolution:
                MultiresolutionStatus = TerrainRuntimeValidationStatus.Inconclusive;
                MultiresolutionSummary = summary;
                break;

            case FocusedPhase.IndependentAnchor:
                IndependentAnchorStatus = TerrainRuntimeValidationStatus.Inconclusive;
                IndependentAnchorSummary = summary;
                break;

            case FocusedPhase.SchedulerStress:
                SchedulerStressStatus = TerrainRuntimeValidationStatus.Inconclusive;
                SchedulerStressSummary = summary;
                break;
        }
    }

    private void ResetFocusedResults()
    {
        HeightCacheStatus = TerrainRuntimeValidationStatus.NotRun;
        HeightCacheSummary = "Height cache validation has not been run.";
        CrossResolutionStatus = TerrainRuntimeValidationStatus.NotRun;
        CrossResolutionSummary = "Cross-resolution validation has not been run.";
        MultiresolutionStatus = TerrainRuntimeValidationStatus.NotRun;
        MultiresolutionSummary = "Renderer / displacement / stitch validation has not been run.";
        IndependentAnchorStatus = TerrainRuntimeValidationStatus.NotRun;
        IndependentAnchorSummary = "Independent-anchor stress validation has not been run.";
        SchedulerStressStatus = TerrainRuntimeValidationStatus.NotRun;
        SchedulerStressSummary = "Height scheduler stress validation has not been run.";
    }

    private void SubscribeCallbacks()
    {
        if (callbacksSubscribed)
        {
            return;
        }

        EditorApplication.update +=
            OnEditorUpdate;

        EditorApplication.playModeStateChanged +=
            OnPlayModeStateChanged;

        callbacksSubscribed = true;
    }

    private void UnsubscribeCallbacks()
    {
        if (!callbacksSubscribed)
        {
            return;
        }

        EditorApplication.update -=
            OnEditorUpdate;

        EditorApplication.playModeStateChanged -=
            OnPlayModeStateChanged;

        callbacksSubscribed = false;
    }

    private void FinishOperation()
    {
        UnsubscribeCallbacks();
        DestroyTemporaryValidators();
        focusedPhase = FocusedPhase.None;
        operation = Operation.Idle;
        repaint?.Invoke();
    }

    private void DestroyTemporaryValidators()
    {
        if (
            ownsCacheValidator
            && cacheValidator != null
        )
        {
            cacheValidator.enabled = false;
            DestroyComponent(cacheValidator);
        }

        if (
            ownsDisplacementValidator
            && displacementValidator != null
        )
        {
            displacementValidator.enabled = false;
            DestroyComponent(displacementValidator);
        }

        cacheValidator = null;
        displacementValidator = null;
        ownsCacheValidator = false;
        ownsDisplacementValidator = false;
        clipmapObject = null;
    }

    private static void DestroyComponent(
        Component component
    )
    {
        if (component == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(component);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(component);
        }
    }
}
