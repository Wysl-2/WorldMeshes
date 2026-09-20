using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainAuthoringResidencySizeRecoveryValidationUtility
{
    private sealed class ValidationResult
    {
        public string Name;
        public string Detail;
        public bool Passed;
        public bool Blocked;
    }

    private static readonly List<ValidationResult>
        results =
            new List<ValidationResult>();

    private static bool validationRunning;

    private static bool validationScheduled;

    public static bool IsRunning =>
        validationRunning
        ||
        validationScheduled;

    public static void ValidateResidencySizeRecovery()
    {
        RequestValidation();
    }

    public static void RequestValidation()
    {
        if (IsRunning)
        {
            return;
        }

        validationScheduled =
            true;

        EditorApplication.delayCall +=
            RunScheduledValidation;
    }

    private static void RunScheduledValidation()
    {
        validationScheduled =
            false;

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            RequestValidation();

            return;
        }

        validationRunning =
            true;

        results.Clear();

        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        long beforeRevision =
            authoringData != null
                ? authoringData.authoringRevision
                : long.MinValue;

        int beforeAuthoringInstanceId =
            authoringData != null
                ? authoringData.GetInstanceID()
                : 0;

        string beforeCommittedSignature =
            worldSettings != null
                ? TerrainAuthoringStateUtility
                    .GetCommittedHeightfieldSignature(
                        worldSettings
                    )
                : "";

        string beforeOverallSignature =
            worldSettings != null
            &&
            authoringData != null
                ? TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        worldSettings,
                        authoringData
                    )
                : "";

        try
        {
            ValidateObservedOversizeRecovery();
            ValidateHealthyAndTolerance();
            ValidateMaterialSizeMismatch();
            ValidateCoverageCriticalMove();
            ValidateOriginDifferenceAlone();
            ValidateWorldEdgeFitting();
            ValidateSmallWorldResidency();
            ValidateShrinkClassification();
            ValidateBuildWindowSelection();
            ValidateSizeStability();
            ValidateLiveResidencyInformation();
            ValidatePersistentAuthoringState(
                worldSettings,
                authoringData,
                beforeRevision,
                beforeAuthoringInstanceId,
                beforeCommittedSignature,
                beforeOverallSignature
            );
        }
        catch (Exception exception)
        {
            AddFail(
                "Unexpected validation exception",
                exception.ToString()
            );
        }
        finally
        {
            validationRunning =
                false;

            FinishValidation();
        }
    }

    private static void ValidateObservedOversizeRecovery()
    {
        TerrainHeightCacheWindow required =
            Window(
                7,
                7,
                10,
                10
            );

        TerrainHeightCacheWindow desired =
            Window(
                6,
                6,
                12,
                12
            );

        TerrainHeightCacheWindow active =
            Window(
                0,
                0,
                24,
                24
            );

        if (
            !TerrainAuthoringPreviewResidencyPolicy.TryEvaluate(
                required,
                desired,
                true,
                active,
                out TerrainAuthoringPreviewResidencyDecision decision,
                out string errorMessage
            )
        )
        {
            AddFail(
                "Oversized active residency recovery",
                errorMessage
            );

            return;
        }

        bool valid =
            decision.ActiveCoversRequired
            &&
            decision.SizeHealth ==
                TerrainAuthoringPreviewResidencySizeHealth.Oversized
            &&
            decision.TransitionRequired
            &&
            !decision.TransitionIsCoverageCritical
            &&
            decision.TargetWindow ==
                desired;

        if (valid)
        {
            AddPass(
                "Oversized active residency recovery",
                "Active=Origin=(0, 0), Size=(24, 24); desired=Origin=(6, 6), Size=(12, 12); recovery targets local residency without requiring cache reset."
            );
        }
        else
        {
            AddFail(
                "Oversized active residency recovery",
                $"Unexpected decision: covers={decision.ActiveCoversRequired}, health={decision.SizeHealth}, transition={decision.TransitionRequired}, critical={decision.TransitionIsCoverageCritical}, target={decision.TargetWindow}."
            );
        }
    }

    private static void ValidateHealthyAndTolerance()
    {
        TerrainHeightCacheWindow required =
            Window(7, 7, 10, 10);

        TerrainHeightCacheWindow desired =
            Window(6, 6, 12, 12);

        TerrainHeightCacheWindow[] healthyWindows =
        {
            Window(6, 6, 12, 12),
            Window(5, 6, 13, 12),
            Window(6, 5, 12, 13),
            Window(5, 5, 13, 13)
        };

        foreach (
            TerrainHeightCacheWindow active
            in healthyWindows
        )
        {
            if (
                !TryEvaluate(
                    required,
                    desired,
                    active,
                    out TerrainAuthoringPreviewResidencyDecision decision,
                    out string errorMessage
                )
                ||
                decision.SizeHealth !=
                    TerrainAuthoringPreviewResidencySizeHealth.Healthy
                ||
                decision.TransitionRequired
            )
            {
                AddFail(
                    "One-tile residency size tolerance",
                    string.IsNullOrEmpty(errorMessage)
                        ? $"Active {active} was not treated as healthy."
                        : errorMessage
                );

                return;
            }
        }

        AddPass(
            "One-tile residency size tolerance",
            "12x12, 13x12, 12x13, and 13x13 active dimensions remain healthy for a 12x12 desired window when required coverage is safe."
        );
    }

    private static void ValidateMaterialSizeMismatch()
    {
        TerrainHeightCacheWindow required =
            Window(7, 7, 10, 10);

        TerrainHeightCacheWindow desired =
            Window(6, 6, 12, 12);

        TerrainHeightCacheWindow[] oversized =
        {
            Window(5, 6, 14, 12),
            Window(6, 5, 12, 14),
            Window(5, 5, 14, 14)
        };

        foreach (
            TerrainHeightCacheWindow active
            in oversized
        )
        {
            if (
                !TryEvaluate(
                    required,
                    desired,
                    active,
                    out TerrainAuthoringPreviewResidencyDecision decision,
                    out _
                )
                ||
                decision.SizeHealth !=
                    TerrainAuthoringPreviewResidencySizeHealth.Oversized
                ||
                !decision.TransitionRequired
                ||
                decision.TransitionIsCoverageCritical
            )
            {
                AddFail(
                    "Material residency oversize detection",
                    $"Active {active} was not classified as a safe oversized recovery."
                );

                return;
            }
        }

        TerrainHeightCacheWindow[] undersized =
        {
            Window(7, 6, 10, 12),
            Window(6, 7, 12, 10)
        };

        foreach (
            TerrainHeightCacheWindow active
            in undersized
        )
        {
            TerrainHeightCacheWindow narrowRequired =
                new TerrainHeightCacheWindow(
                    active.OriginTile +
                        Vector2Int.one,
                    new Vector2Int(
                        Mathf.Max(1, active.Width - 2),
                        Mathf.Max(1, active.Height - 2)
                    )
                );

            if (
                !TryEvaluate(
                    narrowRequired,
                    desired,
                    active,
                    out TerrainAuthoringPreviewResidencyDecision decision,
                    out _
                )
                ||
                decision.SizeHealth !=
                    TerrainAuthoringPreviewResidencySizeHealth.Undersized
                ||
                !decision.TransitionRequired
                ||
                decision.TransitionIsCoverageCritical
            )
            {
                AddFail(
                    "Material residency undersize detection",
                    $"Active {active} was not classified as a safe undersized resize."
                );

                return;
            }
        }

        AddPass(
            "Material residency size mismatch",
            "Dimension differences greater than the one-tile tolerance request a non-critical resize when required coverage remains safe."
        );
    }

    private static void ValidateCoverageCriticalMove()
    {
        TerrainHeightCacheWindow active =
            Window(0, 0, 12, 12);

        TerrainHeightCacheWindow required =
            Window(12, 7, 4, 4);

        TerrainHeightCacheWindow desired =
            Window(6, 6, 12, 12);

        if (
            TryEvaluate(
                required,
                desired,
                active,
                out TerrainAuthoringPreviewResidencyDecision decision,
                out _
            )
            &&
            !decision.ActiveCoversRequired
            &&
            decision.TransitionRequired
            &&
            decision.TransitionIsCoverageCritical
            &&
            decision.TargetWindow ==
                desired
        )
        {
            AddPass(
                "Coverage-critical residency move",
                "Required coverage outside the active cache remains a blocking transition to the desired window."
            );
        }
        else
        {
            AddFail(
                "Coverage-critical residency move",
                "The policy did not preserve coverage-critical waiting semantics."
            );
        }
    }

    private static void ValidateOriginDifferenceAlone()
    {
        TerrainHeightCacheWindow active =
            Window(4, 4, 12, 12);

        TerrainHeightCacheWindow desired =
            Window(5, 4, 12, 12);

        TerrainHeightCacheWindow required =
            Window(6, 6, 8, 8);

        if (
            TryEvaluate(
                required,
                desired,
                active,
                out TerrainAuthoringPreviewResidencyDecision decision,
                out _
            )
            &&
            decision.ActiveCoversRequired
            &&
            decision.SizeHealth ==
                TerrainAuthoringPreviewResidencySizeHealth.Healthy
            &&
            !decision.TransitionRequired
        )
        {
            AddPass(
                "Origin difference alone does not shift residency",
                "Healthy active dimensions retain guard headroom even when the exact desired origin moves by one tile."
            );
        }
        else
        {
            AddFail(
                "Origin difference alone does not shift residency",
                "A safe one-tile desired-origin difference incorrectly requested a transition."
            );
        }
    }

    private static void ValidateWorldEdgeFitting()
    {
        Vector2Int worldGrid =
            new Vector2Int(
                24,
                24
            );

        Vector2Int size =
            new Vector2Int(
                12,
                12
            );

        Vector2Int[] origins =
        {
            new Vector2Int(-3, 6),
            new Vector2Int(18, 6),
            new Vector2Int(6, -3),
            new Vector2Int(6, 18),
            new Vector2Int(-3, -3),
            new Vector2Int(18, 18)
        };

        foreach (
            Vector2Int origin
            in origins
        )
        {
            if (
                !TerrainHeightCacheWindow.TryFitToWorld(
                    origin,
                    size,
                    worldGrid,
                    out TerrainHeightCacheWindow fitted
                )
                ||
                fitted.Size !=
                    size
            )
            {
                AddFail(
                    "World-edge resident size preservation",
                    $"Origin {origin} did not preserve the requested 12x12 resident dimensions."
                );

                return;
            }
        }

        AddPass(
            "World-edge resident size preservation",
            "A 12x12 desired cache shifts against all tested edges/corners without shrinking dimensions."
        );
    }

    private static void ValidateSmallWorldResidency()
    {
        TerrainHeightCacheWindow active =
            Window(0, 0, 8, 8);

        TerrainHeightCacheWindow desired =
            active;

        TerrainHeightCacheWindow required =
            Window(0, 0, 8, 8);

        if (
            TryEvaluate(
                required,
                desired,
                active,
                out TerrainAuthoringPreviewResidencyDecision decision,
                out _
            )
            &&
            decision.SizeHealth ==
                TerrainAuthoringPreviewResidencySizeHealth.Healthy
            &&
            !decision.TransitionRequired
        )
        {
            AddPass(
                "Small-world whole residency remains valid",
                "An 8x8 logical world is not falsely classified as oversized when the desired cache legitimately fills the world."
            );
        }
        else
        {
            AddFail(
                "Small-world whole residency remains valid",
                "Legitimate whole-world residency was incorrectly classified for recovery."
            );
        }
    }

    private static void ValidateShrinkClassification()
    {
        TerrainHeightCacheWindow source =
            Window(0, 0, 24, 24);

        TerrainHeightCacheWindow target =
            Window(6, 6, 12, 12);

        if (
            !TerrainAuthoringPreviewCacheTransition.TryCreate(
                true,
                source,
                target,
                "committed",
                "overall",
                out TerrainAuthoringPreviewCacheTransition transition,
                out string errorMessage
            )
        )
        {
            AddFail(
                "Oversized staged shrink classification",
                errorMessage
            );

            return;
        }

        if (
            transition.RetainedTiles.Count ==
                144
            &&
            transition.EnteringTiles.Count ==
                0
            &&
            transition.LeavingTiles.Count ==
                432
        )
        {
            AddPass(
                "Oversized staged shrink classification",
                "24x24 -> 12x12 classifies Retained=144, Entering=0, Leaving=432 without allocating a large GPU cache."
            );
        }
        else
        {
            AddFail(
                "Oversized staged shrink classification",
                $"Retained={transition.RetainedTiles.Count}, Entering={transition.EnteringTiles.Count}, Leaving={transition.LeavingTiles.Count}."
            );
        }
    }

    private static void ValidateBuildWindowSelection()
    {
        Vector2Int worldGrid =
            new Vector2Int(24, 24);

        TerrainHeightCacheWindow oversizedActive =
            Window(0, 0, 24, 24);

        TerrainHeightCacheWindow desired =
            Window(6, 6, 12, 12);

        bool oversizeSelection =
            TerrainAuthoringPreviewResidencyPolicy
                .TrySelectPreferredBuildWindow(
                    worldGrid,
                    false,
                    default,
                    true,
                    oversizedActive,
                    true,
                    desired,
                    out TerrainHeightCacheWindow oversizeBuild
                )
            &&
            oversizeBuild ==
                desired;

        TerrainHeightCacheWindow healthyActive =
            Window(4, 1, 12, 12);

        TerrainHeightCacheWindow shiftedDesired =
            Window(5, 2, 12, 12);

        bool healthySelection =
            TerrainAuthoringPreviewResidencyPolicy
                .TrySelectPreferredBuildWindow(
                    worldGrid,
                    false,
                    default,
                    true,
                    healthyActive,
                    true,
                    shiftedDesired,
                    out TerrainHeightCacheWindow healthyBuild
                )
            &&
            healthyBuild ==
                healthyActive;

        if (
            oversizeSelection
            &&
            healthySelection
        )
        {
            AddPass(
                "Committed rebuild target selection",
                "Oversized active residency selects desired 12x12, while a healthy 12x12 active cache preserves its current window."
            );
        }
        else
        {
            AddFail(
                "Committed rebuild target selection",
                $"Oversized target={oversizeBuild}; healthy target={healthyBuild}."
            );
        }
    }

    private static void ValidateSizeStability()
    {
        TerrainHeightCacheWindow active =
            Window(6, 6, 12, 12);

        TerrainHeightCacheWindow required =
            Window(7, 7, 10, 10);

        TerrainHeightCacheWindow[] stableDesired =
        {
            Window(6, 6, 12, 12),
            Window(6, 6, 13, 12),
            Window(6, 6, 12, 12),
            Window(6, 6, 13, 12)
        };

        foreach (
            TerrainHeightCacheWindow desired
            in stableDesired
        )
        {
            if (
                !TryEvaluate(
                    required,
                    desired,
                    active,
                    out TerrainAuthoringPreviewResidencyDecision decision,
                    out _
                )
                ||
                decision.TransitionRequired
            )
            {
                AddFail(
                    "Residency size stability",
                    $"Desired {desired.Size} unexpectedly requested a resize from active 12x12."
                );

                return;
            }
        }

        TerrainHeightCacheWindow materialDesired =
            Window(6, 6, 14, 12);

        if (
            !TryEvaluate(
                required,
                materialDesired,
                active,
                out TerrainAuthoringPreviewResidencyDecision materialDecision,
                out _
            )
            ||
            !materialDecision.TransitionRequired
        )
        {
            AddFail(
                "Residency size stability",
                "A two-tile desired width increase did not request a resize."
            );

            return;
        }

        AddPass(
            "Residency size stability",
            "12x12/13x12 desired fluctuations remain stable; a 14x12 desired size requests a resize."
        );
    }

    private static void ValidateLiveResidencyInformation()
    {
        if (
            !TerrainAuthoringPreviewService
                .TryGetActiveResidentWindow(
                    out TerrainHeightCacheWindow active
                )
        )
        {
            AddBlocked(
                "Live residency size information",
                "No active Height Preview cache is currently available. Synthetic regression tests remain authoritative."
            );

            return;
        }

        string desiredText =
            TerrainAuthoringPreviewService
                .TryGetDesiredResidentWindow(
                    out TerrainHeightCacheWindow desired
                )
                ? desired.ToString()
                : "(not evaluated)";

        AddPass(
            "Live residency size information",
            $"Active={active}; Desired={desiredText}; Health={TerrainAuthoringPreviewService.ActiveResidencySizeHealthLabel}; Slices={TerrainAuthoringPreviewService.CacheSliceCount:N0}; Memory={TerrainAuthoringPreviewService.ApproximateGpuMemoryBytes:N0} byte(s)."
        );
    }

    private static void ValidatePersistentAuthoringState(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        long beforeRevision,
        int beforeAuthoringInstanceId,
        string beforeCommittedSignature,
        string beforeOverallSignature
    )
    {
        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            AddBlocked(
                "Persistent authoring state unchanged",
                "WorldSettings or TerrainAuthoringData is unavailable."
            );

            return;
        }

        string afterCommittedSignature =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string afterOverallSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        bool unchanged =
            authoringData.authoringRevision ==
                beforeRevision
            &&
            authoringData.GetInstanceID() ==
                beforeAuthoringInstanceId
            &&
            beforeCommittedSignature ==
                afterCommittedSignature
            &&
            beforeOverallSignature ==
                afterOverallSignature;

        if (unchanged)
        {
            AddPass(
                "Persistent authoring state unchanged",
                "Package 03A validation did not mutate persistent terrain authoring identity."
            );
        }
        else
        {
            AddFail(
                "Persistent authoring state unchanged",
                "Persistent revision, object identity, or authoring signatures changed during validation."
            );
        }
    }

    private static bool TryEvaluate(
        TerrainHeightCacheWindow required,
        TerrainHeightCacheWindow desired,
        TerrainHeightCacheWindow active,
        out TerrainAuthoringPreviewResidencyDecision decision,
        out string errorMessage
    )
    {
        return
            TerrainAuthoringPreviewResidencyPolicy.TryEvaluate(
                required,
                desired,
                true,
                active,
                out decision,
                out errorMessage
            );
    }

    private static TerrainHeightCacheWindow Window(
        int x,
        int z,
        int width,
        int height
    )
    {
        return
            new TerrainHeightCacheWindow(
                new Vector2Int(x, z),
                new Vector2Int(width, height)
            );
    }

    private static void AddPass(
        string name,
        string detail
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Detail = detail,
                Passed = true
            }
        );
    }

    private static void AddFail(
        string name,
        string detail
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Detail = detail,
                Passed = false
            }
        );
    }

    private static void AddBlocked(
        string name,
        string detail
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Detail = detail,
                Blocked = true
            }
        );
    }

    private static void FinishValidation()
    {
        int passed = 0;
        int failed = 0;
        int blocked = 0;

        System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

        builder.AppendLine(
            "WorldMeshes Edit-Mode Height Cache Streaming - Package 03A Validation"
        );

        builder.AppendLine(
            "===================================================================="
        );

        builder.AppendLine();

        foreach (
            ValidationResult result
            in results
        )
        {
            string outcome;

            if (result.Blocked)
            {
                outcome = "BLOCKED";
                blocked++;
            }
            else if (result.Passed)
            {
                outcome = "PASS";
                passed++;
            }
            else
            {
                outcome = "FAIL";
                failed++;
            }

            builder.AppendLine(
                $"{outcome} - {result.Name}"
            );

            if (
                !string.IsNullOrEmpty(
                    result.Detail
                )
            )
            {
                builder.AppendLine(
                    $"       {result.Detail}"
                );
            }

            builder.AppendLine();
        }

        builder.AppendLine(
            "----------------------------------------------"
        );

        builder.AppendLine(
            $"{passed} passed"
        );

        builder.AppendLine(
            $"{failed} failed"
        );

        builder.AppendLine(
            $"{blocked} blocked"
        );

        builder.AppendLine();

        builder.AppendLine(
            failed == 0
                ? "Package 03A residency size recovery: PASSED"
                : "Package 03A residency size recovery: FAILED"
        );

        if (failed == 0)
        {
            Debug.Log(
                builder.ToString()
            );
        }
        else
        {
            Debug.LogError(
                builder.ToString()
            );
        }
    }
}
