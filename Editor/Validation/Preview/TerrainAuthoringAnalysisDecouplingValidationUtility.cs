using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package 07 deterministic validation for bounded interactive/offline Terrain
 * Analysis. Tests here deliberately avoid moving Scene View residency or
 * mutating authoring state; live source information is reported separately.
 */
public static class TerrainAuthoringAnalysisDecouplingValidationUtility
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

    private static readonly List<ValidationResult> results =
        new List<ValidationResult>();

    [MenuItem(
        "Tools/WorldMeshes/Validation/Package 07 - Analysis Decoupling"
    )]
    public static void ValidateAnalysisDecoupling()
    {
        results.Clear();

        try
        {
            ValidateNonZeroOriginSafeWindow();
            ValidateArtificialBoundaryInset();
            ValidateWorldMinimumBoundaryPreservation();
            ValidateWorldMaximumBoundaryPreservation();
            ValidateFourTileDependencyGuard();
            ValidateCurrentScaleOneTileGuard();
            ValidateBoundedExpansion();
            ValidateSparseBatchPlanning();
            ValidateContiguousBatchMaximum();
            ValidateLiveAnalysisSource();
        }
        catch (Exception exception)
        {
            Add(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );
        }

        WriteReport();
    }

    private static void ValidateNonZeroOriginSafeWindow()
    {
        TerrainHeightCacheWindow source =
            new TerrainHeightCacheWindow(
                new Vector2Int(10, 12),
                new Vector2Int(7, 7)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(
                source,
                new Vector2Int(64, 64),
                1,
                out TerrainHeightCacheWindow output,
                out string error
            );

        bool correct =
            success
            &&
            output.OriginTile == new Vector2Int(11, 13)
            &&
            output.Size == new Vector2Int(5, 5);

        Add(
            "Non-zero resident origin maps to a safe local output window",
            correct ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success
                ? "Source=" + source + ", Output=" + output
                : error
        );
    }

    private static void ValidateArtificialBoundaryInset()
    {
        TerrainHeightCacheWindow source =
            new TerrainHeightCacheWindow(
                new Vector2Int(20, 20),
                new Vector2Int(9, 9)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(
                source,
                new Vector2Int(128, 128),
                2,
                out TerrainHeightCacheWindow output,
                out string error
            );

        bool correct =
            success
            &&
            output.OriginTile == new Vector2Int(22, 22)
            &&
            output.Size == new Vector2Int(5, 5);

        Add(
            "Artificial resident boundaries are excluded from analysis output",
            correct ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success ? output.ToString() : error
        );
    }

    private static void ValidateWorldMinimumBoundaryPreservation()
    {
        TerrainHeightCacheWindow source =
            new TerrainHeightCacheWindow(
                Vector2Int.zero,
                new Vector2Int(7, 7)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(
                source,
                new Vector2Int(64, 64),
                1,
                out TerrainHeightCacheWindow output,
                out string error
            );

        bool correct =
            success
            &&
            output.OriginTile == Vector2Int.zero
            &&
            output.Size == new Vector2Int(6, 6);

        Add(
            "Logical minimum world edges are preserved",
            correct ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success ? output.ToString() : error
        );
    }

    private static void ValidateWorldMaximumBoundaryPreservation()
    {
        TerrainHeightCacheWindow source =
            new TerrainHeightCacheWindow(
                new Vector2Int(57, 57),
                new Vector2Int(7, 7)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryCalculateSafeOutputWindow(
                source,
                new Vector2Int(64, 64),
                1,
                out TerrainHeightCacheWindow output,
                out string error
            );

        bool correct =
            success
            &&
            output.OriginTile == new Vector2Int(58, 58)
            &&
            output.Size == new Vector2Int(6, 6)
            &&
            output.MaximumExclusive == new Vector2Int(64, 64);

        Add(
            "Logical maximum world edges are preserved",
            correct ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success ? output.ToString() : error
        );
    }

    private static void ValidateFourTileDependencyGuard()
    {
        int guard =
            TerrainAnalysisWindowUtility.CalculateRequiredGuardTileCount(
                65,
                1f,
                256f
            );

        Add(
            "256 m dependency on 64 m height tiles requires four guard tiles",
            guard == 4
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Guard tiles=" + guard
        );
    }

    private static void ValidateCurrentScaleOneTileGuard()
    {
        int guard =
            TerrainAnalysisWindowUtility.CalculateRequiredGuardTileCount(
                1025,
                1f,
                256f
            );

        Add(
            "256 m dependency on 1024 m height tiles remains one guard tile",
            guard == 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Guard tiles=" + guard
        );
    }

    private static void ValidateBoundedExpansion()
    {
        TerrainHeightCacheWindow output =
            new TerrainHeightCacheWindow(
                new Vector2Int(100, 50),
                new Vector2Int(8, 1)
            );

        bool success =
            TerrainAnalysisWindowUtility.TryExpandOutputWindow(
                output,
                new Vector2Int(256, 256),
                2,
                out TerrainHeightCacheWindow source,
                out string error
            );

        bool bounded =
            success
            &&
            source.Size == new Vector2Int(12, 5)
            &&
            source.TileCount == 60;

        Add(
            "Offline source expansion scales with batch plus guard",
            bounded ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            success
                ? "Output=" + output + ", Source=" + source
                : error
        );
    }

    private static void ValidateSparseBatchPlanning()
    {
        List<Vector2Int> sparse =
            new List<Vector2Int>
            {
                new Vector2Int(2, 2),
                new Vector2Int(100, 50)
            };

        List<Vector2Int> firstRun =
            TerrainAnalysisRuntimeHeightBatchService.CollectContiguousRun(
                sparse,
                0,
                8
            );

        Add(
            "Sparse surface tiles are split into independent bounded batches",
            firstRun.Count == 1
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "First run tile count=" + firstRun.Count
        );
    }

    private static void ValidateContiguousBatchMaximum()
    {
        List<Vector2Int> row =
            new List<Vector2Int>();

        for (int x = 0; x < 20; x++)
        {
            row.Add(new Vector2Int(x, 3));
        }

        List<Vector2Int> run =
            TerrainAnalysisRuntimeHeightBatchService.CollectContiguousRun(
                row,
                0,
                20
            );

        Add(
            "Whole-world output batches remain capped",
            run.Count ==
                TerrainAnalysisRuntimeHeightBatchService.MaximumOutputTilesPerBatch
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Planned tile count=" + run.Count
        );
    }

    private static void ValidateLiveAnalysisSource()
    {
        if (
            !TerrainAuthoringPreviewService.TryGetTerrainAnalysisGpuSource(
                out TerrainAnalysisGpuSource source
            )
        )
        {
            Add(
                "Live resident analysis source",
                ValidationOutcome.Blocked,
                "Height Preview is not currently ready; deterministic Package 07 policy checks still ran."
            );

            return;
        }

        bool outputReady =
            TerrainAnalysisWindowUtility.TryCalculateInteractiveOutputWindow(
                source,
                out TerrainHeightCacheWindow output,
                out string error
            );

        Add(
            "Live resident analysis source",
            outputReady
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            outputReady
                ? "Source=" + source.SourceWindow +
                    ", Analysis=" + output +
                    ", ResidencyGeneration=" + source.ResidencyGeneration +
                    ", CompositeGeneration=" + source.CompositeGeneration
                : error
        );
    }

    private static void Add(
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

    private static void WriteReport()
    {
        int passed = 0;
        int failed = 0;
        int blocked = 0;

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Package 07 - Terrain Analysis Decoupling Validation"
        );

        builder.AppendLine();

        foreach (ValidationResult result in results)
        {
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

            builder.Append("[");
            builder.Append(result.Outcome.ToString().ToUpperInvariant());
            builder.Append("] ");
            builder.AppendLine(result.Name);

            if (!string.IsNullOrEmpty(result.Details))
            {
                builder.AppendLine("    " + result.Details);
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "Summary: " +
            passed +
            " passed, " +
            failed +
            " failed, " +
            blocked +
            " blocked."
        );

        if (failed > 0)
        {
            Debug.LogError(builder.ToString());
        }
        else if (blocked > 0)
        {
            Debug.LogWarning(builder.ToString());
        }
        else
        {
            Debug.Log(builder.ToString());
        }
    }
}
