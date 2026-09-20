using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainAuthoringRegionalElevationResidencyValidationUtility
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

    private static bool validationScheduled;
    private static bool validationRunning;

    public static bool IsRunning =>
        validationScheduled
        ||
        validationRunning;

    public static void ValidateRegionalElevationResidency()
    {
        if (IsRunning)
        {
            return;
        }

        validationScheduled =
            true;

        EditorApplication.delayCall -=
            RunScheduledValidation;

        EditorApplication.delayCall +=
            RunScheduledValidation;
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
            ValidateWholeWorldCompactScope();
            ValidateWholeWorldResidentIntersection();
            ValidateNoActiveWindow();
            ValidateBoundedIntersection();
            ValidateScopeMerging();
            ValidateRegionalModifierUnion();
            ValidateNoResidencyExpansion();
            ValidateInteractiveAuthoringPredicate();
            ValidateGenerationReuse();
            ValidateResidentOnlyPublicationPolicy();
            ValidatePackage03ARegression();
            ValidateLiveInformation();
        }
        catch (Exception exception)
        {
            AddResult(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString()
            );
        }
        finally
        {
            validationRunning =
                false;

            WriteReport();
        }
    }

    private static WorldSettings CreateSyntheticWorld(
        int heightTileGridWidth,
        int heightTileGridHeight
    )
    {
        WorldSettings world =
            ScriptableObject
                .CreateInstance<WorldSettings>();

        world.heightTileChunkSpan =
            1;

        world.gridWidth =
            Mathf.Max(
                1,
                heightTileGridWidth
            );

        world.gridHeight =
            Mathf.Max(
                1,
                heightTileGridHeight
            );

        world.chunkSize =
            100f;

        world.heightfieldResolutionPerChunk =
            100;

        return world;
    }

    private static void ValidateWholeWorldCompactScope()
    {
        WorldSettings world =
            CreateSyntheticWorld(
                32,
                32
            );

        try
        {
            long logicalCount =
                TerrainRegionalElevationResidencyPolicy
                    .GetLogicalAffectedTileCount(
                        world,
                        TerrainRegionalElevationInvalidationScope.WholeWorld
                    );

            AddResult(
                "Whole-world regional invalidation is compact",
                logicalCount == 1024L
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                $"Logical affected count={logicalCount:N0}; no 1,024-entry preview dirty set is required."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                world
            );
        }
    }

    private static void ValidateWholeWorldResidentIntersection()
    {
        WorldSettings world =
            CreateSyntheticWorld(
                32,
                32
            );

        List<Vector2Int> tiles =
            new List<Vector2Int>();

        try
        {
            TerrainHeightCacheWindow active =
                new TerrainHeightCacheWindow(
                    new Vector2Int(
                        7,
                        9
                    ),
                    new Vector2Int(
                        12,
                        12
                    )
                );

            bool collected =
                TerrainRegionalElevationResidencyPolicy
                    .TryCollectResidentTiles(
                        world,
                        TerrainRegionalElevationInvalidationScope.WholeWorld,
                        true,
                        active,
                        tiles,
                        out string errorMessage
                    );

            bool ordered =
                collected
                &&
                tiles.Count == 144
                &&
                tiles[0] ==
                    new Vector2Int(
                        7,
                        9
                    )
                &&
                tiles[tiles.Count - 1] ==
                    new Vector2Int(
                        18,
                        20
                    );

            AddResult(
                "Whole-world regional invalidation intersects active residency",
                ordered
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                ordered
                    ? "32x32 logical influence produced exactly 12x12 = 144 deterministic resident tiles."
                    : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                world
            );
        }
    }

    private static void ValidateNoActiveWindow()
    {
        WorldSettings world =
            CreateSyntheticWorld(
                32,
                32
            );

        List<Vector2Int> tiles =
            new List<Vector2Int>();

        try
        {
            bool collected =
                TerrainRegionalElevationResidencyPolicy
                    .TryCollectResidentTiles(
                        world,
                        TerrainRegionalElevationInvalidationScope.WholeWorld,
                        false,
                        default,
                        tiles,
                        out string errorMessage
                    );

            bool passed =
                collected
                &&
                tiles.Count == 0
                &&
                TerrainRegionalElevationResidencyPolicy
                    .GetLogicalAffectedTileCount(
                        world,
                        TerrainRegionalElevationInvalidationScope.WholeWorld
                    )
                    == 1024L;

            AddResult(
                "Regional authoring without active residency performs no immediate GPU work",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "Logical influence remains 1,024 tiles while immediate resident work is zero."
                    : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                world
            );
        }
    }

    private static void ValidateBoundedIntersection()
    {
        WorldSettings world =
            CreateSyntheticWorld(
                20,
                20
            );

        List<Vector2Int> tiles =
            new List<Vector2Int>();

        try
        {
            TerrainHeightCacheWindow active =
                new TerrainHeightCacheWindow(
                    new Vector2Int(
                        5,
                        5
                    ),
                    new Vector2Int(
                        6,
                        6
                    )
                );

            Bounds bounds =
                new Bounds(
                    new Vector3(
                        950f,
                        0f,
                        950f
                    ),
                    new Vector3(
                        500f,
                        10f,
                        500f
                    )
                );

            TerrainRegionalElevationInvalidationScope scope =
                TerrainRegionalElevationInvalidationScope
                    .FromWorldBounds(
                        bounds
                    );

            bool collected =
                TerrainRegionalElevationResidencyPolicy
                    .TryCollectResidentTiles(
                        world,
                        scope,
                        true,
                        active,
                        tiles,
                        out string errorMessage
                    );

            bool passed =
                collected
                &&
                tiles.Count > 0
                &&
                tiles.Count <
                    active.TileCount;

            AddResult(
                "Bounded regional invalidation intersects only resident overlap",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? $"Bounded scope produced {tiles.Count:N0} resident tile(s) inside a {active.TileCount:N0}-tile active window."
                    : errorMessage
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                world
            );
        }
    }

    private static void ValidateScopeMerging()
    {
        TerrainRegionalElevationInvalidationScope a =
            TerrainRegionalElevationInvalidationScope
                .FromWorldBounds(
                    new Bounds(
                        new Vector3(
                            100f,
                            0f,
                            100f
                        ),
                        new Vector3(
                            100f,
                            1f,
                            100f
                        )
                    )
                );

        TerrainRegionalElevationInvalidationScope b =
            TerrainRegionalElevationInvalidationScope
                .FromWorldBounds(
                    new Bounds(
                        new Vector3(
                            250f,
                            0f,
                            100f
                        ),
                        new Vector3(
                            100f,
                            1f,
                            100f
                        )
                    )
                );

        TerrainRegionalElevationInvalidationScope mergedBounds =
            TerrainRegionalElevationResidencyPolicy
                .Merge(
                    a,
                    b
                );

        TerrainRegionalElevationInvalidationScope whole =
            TerrainRegionalElevationResidencyPolicy
                .Merge(
                    mergedBounds,
                    TerrainRegionalElevationInvalidationScope.WholeWorld
                );

        bool passed =
            mergedBounds.Kind ==
                TerrainRegionalElevationInvalidationKind.WorldBounds
            &&
            whole.Kind ==
                TerrainRegionalElevationInvalidationKind.WholeWorld
            &&
            TerrainRegionalElevationResidencyPolicy
                .Merge(
                    TerrainRegionalElevationInvalidationScope.None,
                    a
                )
                .Kind ==
                TerrainRegionalElevationInvalidationKind.WorldBounds;

        AddResult(
            "Regional invalidation scopes merge conservatively",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "None, bounded unions, and WholeWorld dominance remain compact and deterministic."
                : "Regional invalidation merge policy produced an unexpected scope."
        );
    }

    private static void ValidateRegionalModifierUnion()
    {
        List<Vector2Int> modifier =
            new List<Vector2Int>
            {
                new Vector2Int(2, 2),
                new Vector2Int(3, 2),
                new Vector2Int(4, 2),
                new Vector2Int(5, 2)
            };

        List<Vector2Int> regional =
            new List<Vector2Int>();

        for (int x = 0; x < 12; x++)
        {
            regional.Add(
                new Vector2Int(
                    x,
                    2
                )
            );
        }

        List<Vector2Int> union =
            new List<Vector2Int>();

        TerrainAuthoringPreviewService
            .BuildResidentAuthoringDirtyUnion(
                modifier,
                regional,
                union
            );

        bool passed =
            union.Count == 12;

        AddResult(
            "Regional and modifier dirty work is unioned",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            passed
                ? "12 regional tiles plus four modifier tiles with four overlaps produced 12 unique recomposition tiles."
                : $"Unexpected union count: {union.Count:N0}."
        );
    }

    private static void ValidateNoResidencyExpansion()
    {
        WorldSettings world =
            CreateSyntheticWorld(
                32,
                32
            );

        List<Vector2Int> tiles =
            new List<Vector2Int>();

        try
        {
            TerrainHeightCacheWindow active =
                new TerrainHeightCacheWindow(
                    new Vector2Int(
                        10,
                        10
                    ),
                    new Vector2Int(
                        12,
                        12
                    )
                );

            TerrainRegionalElevationResidencyPolicy
                .TryCollectResidentTiles(
                    world,
                    TerrainRegionalElevationInvalidationScope.WholeWorld,
                    true,
                    active,
                    tiles,
                    out _
                );

            bool passed =
                tiles.Count ==
                    active.TileCount
                &&
                active.Size ==
                    new Vector2Int(
                        12,
                        12
                    );

            AddResult(
                "Whole-world regional invalidation does not expand residency",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "WholeWorld influence remained bounded to the existing 12x12 active window."
                    : "Regional invalidation changed or exceeded the active residency shape."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                world
            );
        }
    }

    private static void ValidateInteractiveAuthoringPredicate()
    {
        bool modifierDefers =
            TerrainAuthoringPreviewService
                .ShouldDeferStreamingRestart(
                    true
                );

        bool idleDoesNotDefer =
            !TerrainAuthoringPreviewService
                .ShouldDeferStreamingRestart(
                    false
                );

        AddResult(
            "Interactive terrain-authoring restart deferral remains reusable",
            modifierDefers && idleDoesNotDefer
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            modifierDefers && idleDoesNotDefer
                ? "Package 04 restart deferral still distinguishes an active interactive authoring gesture from idle authoring."
                : "Streaming restart deferral policy returned an unexpected result."
        );
    }

    private static void ValidateGenerationReuse()
    {
        long before =
            500L;

        long after =
            TerrainAuthoringPreviewService
                .CalculateNextAuthoringGeneration(
                    before
                );

        AddResult(
            "Regional elevation reuses the Package 05 authoring generation",
            after == 501L
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            after == 501L
                ? "Regional notifications use the existing monotonic preview authoring-generation boundary."
                : $"Expected generation 501, got {after}."
        );
    }

    private static void ValidateResidentOnlyPublicationPolicy()
    {
        WorldSettings world =
            CreateSyntheticWorld(
                32,
                32
            );

        List<Vector2Int> resident =
            new List<Vector2Int>();

        try
        {
            TerrainHeightCacheWindow active =
                new TerrainHeightCacheWindow(
                    new Vector2Int(
                        3,
                        4
                    ),
                    new Vector2Int(
                        12,
                        12
                    )
                );

            TerrainRegionalElevationResidencyPolicy
                .TryCollectResidentTiles(
                    world,
                    TerrainRegionalElevationInvalidationScope.WholeWorld,
                    true,
                    active,
                    resident,
                    out _
                );

            long logical =
                TerrainRegionalElevationResidencyPolicy
                    .GetLogicalAffectedTileCount(
                        world,
                        TerrainRegionalElevationInvalidationScope.WholeWorld
                    );

            bool passed =
                logical == 1024L
                &&
                resident.Count == 144;

            AddResult(
                "Regional composite publication remains resident-only",
                passed
                    ? ValidationOutcome.Pass
                    : ValidationOutcome.Fail,
                passed
                    ? "1,024 logical affected tiles map to exactly 144 immediate resident publication candidates."
                    : $"Logical={logical:N0}, resident={resident.Count:N0}."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                world
            );
        }
    }

    private static void ValidatePackage03ARegression()
    {
        TerrainHeightCacheWindow active =
            new TerrainHeightCacheWindow(
                Vector2Int.zero,
                new Vector2Int(
                    24,
                    24
                )
            );

        TerrainHeightCacheWindow desired =
            new TerrainHeightCacheWindow(
                new Vector2Int(
                    6,
                    6
                ),
                new Vector2Int(
                    12,
                    12
                )
            );

        TerrainAuthoringPreviewResidencySizeHealth health =
            TerrainAuthoringPreviewResidencyPolicy
                .EvaluateSizeHealth(
                    true,
                    active,
                    desired,
                    TerrainAuthoringPreviewResidencyPolicy
                        .DefaultResidentSizeToleranceTiles
                );

        AddResult(
            "Package 03A bounded-residency regression",
            health ==
                TerrainAuthoringPreviewResidencySizeHealth.Oversized
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            health ==
                TerrainAuthoringPreviewResidencySizeHealth.Oversized
                ? "24x24 active versus 12x12 desired remains Oversized; regional invalidation does not become residency policy."
                : $"Unexpected size health: {health}."
        );
    }

    private static void ValidateLiveInformation()
    {
        string details =
            $"AuthoringGeneration={TerrainAuthoringPreviewService.AuthoringGeneration:N0}; " +
            $"RegionalScope={TerrainAuthoringPreviewService.LastRegionalInvalidationKind}; " +
            $"Logical/Resident/Nonresident=" +
            $"{TerrainAuthoringPreviewService.LastRegionalLogicalAffectedTileCount:N0}/" +
            $"{TerrainAuthoringPreviewService.LastRegionalResidentAffectedTileCount:N0}/" +
            $"{TerrainAuthoringPreviewService.LastRegionalNonresidentAffectedTileCount:N0}; " +
            $"Pending={TerrainAuthoringPreviewService.HasPendingRegionalElevationInvalidation}; " +
            $"InteractiveRegionalEdit={TerrainAuthoringPreviewService.InteractiveRegionalElevationEditActive}.";

        AddResult(
            "Live regional-elevation residency information",
            ValidationOutcome.Pass,
            details
        );
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
                Name =
                    name,

                Outcome =
                    outcome,

                Details =
                    details ?? ""
            }
        );
    }

    private static void WriteReport()
    {
        int passed =
            0;

        int failed =
            0;

        int blocked =
            0;

        System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

        builder.AppendLine(
            "WorldMeshes Edit-Mode Height Cache Streaming - Package 06 Validation"
        );

        builder.AppendLine(
            "===================================================================="
        );

        builder.AppendLine();

        for (
            int index = 0;
            index < results.Count;
            index++
        )
        {
            ValidationResult result =
                results[index];

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

            builder.AppendLine(
                $"{result.Outcome.ToString().ToUpperInvariant()} - {result.Name}"
            );

            builder.AppendLine(
                "       " +
                result.Details
            );

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
                ? "Package 06 regional elevation residency: PASSED"
                : "Package 06 regional elevation residency: FAILED"
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
