using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeInvalidationValidationUtility
{
    public static bool CanStartGuidedScenario(out string errorMessage)
    {
        errorMessage = "";

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage = "Invalidation validation must run outside Play Mode.";
            return false;
        }

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            errorMessage = "The unified runtime bake pipeline is already running.";
            return false;
        }

        if (TerrainRuntimeBakeValidationUtility.IsValidationBakeRunning)
        {
            errorMessage = "A Package 10.1 validation bake is already running.";
            return false;
        }

        if (TerrainRuntimeBakeResumeValidationUtility.IsRunning)
        {
            errorMessage = "A Package 10.2 resume validation scenario is already running.";
            return false;
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            errorMessage = "Independent Surface generation is already running.";
            return false;
        }

        TerrainRuntimeBakePlan plan = TerrainRuntimeBakePlanner.BuildCurrentPlan();

        if (plan == null)
        {
            errorMessage = "The current runtime bake plan is unavailable.";
            return false;
        }

        if (plan.IsBlocked)
        {
            errorMessage =
                string.IsNullOrEmpty(plan.BlockReason)
                    ? "The current runtime bake plan is blocked."
                    : plan.BlockReason;
            return false;
        }

        if (plan.HasWork)
        {
            errorMessage =
                "Start an invalidation scenario from Runtime Ready / no pending work.";
            return false;
        }

        return true;
    }

    public static TerrainRuntimeInvalidationModifierBaseline
        CaptureSelectedModifierBaseline(out string errorMessage)
    {
        errorMessage = "";

        if (!CanStartGuidedScenario(out errorMessage))
        {
            return null;
        }

        if (
            !TryLoadContext(
                out _,
                out TerrainAuthoringData authoringData,
                out _,
                out errorMessage
            )
        )
        {
            return null;
        }

        if (
            !TerrainAuthoringModifierSelection.TryGetSelectedModifier(
                authoringData,
                out TerrainHeightModifier modifier,
                out int index
            )
        )
        {
            errorMessage =
                "Select a terrain height modifier before capturing a baseline.";
            return null;
        }

        return CaptureModifier(modifier, index);
    }

    public static TerrainRuntimeInvalidationSettingsBaseline
        CaptureSettingsBaseline(out string errorMessage)
    {
        errorMessage = "";

        if (!CanStartGuidedScenario(out errorMessage))
        {
            return null;
        }

        if (
            !TryLoadContext(
                out WorldSettings worldSettings,
                out _,
                out TerrainSurfaceSettings surfaceSettings,
                out errorMessage
            )
        )
        {
            return null;
        }

        return new TerrainRuntimeInvalidationSettingsBaseline
        {
            GridWidth = worldSettings.gridWidth,
            GridHeight = worldSettings.gridHeight,
            ChunkSize = worldSettings.chunkSize,
            HeightfieldResolutionPerChunk =
                worldSettings.heightfieldResolutionPerChunk,
            HeightTileChunkSpan =
                worldSettings.heightTileChunkSpan,
            CollisionResolution =
                worldSettings.collisionResolution,
            SurfaceSettingsSignature =
                TerrainSurfaceSignatureUtility.GetSettingsSignature(
                    surfaceSettings
                ),
            CollisionSettingsSignature =
                TerrainGenerationStateUtility
                    .GetCurrentCollisionSettingsSignature(
                        worldSettings
                    )
        };
    }

    public static bool TryCaptureCurrentModifier(
        TerrainRuntimeInvalidationModifierBaseline baseline,
        out TerrainRuntimeInvalidationModifierBaseline current,
        out string errorMessage
    )
    {
        current = null;
        errorMessage = "";

        if (baseline == null)
        {
            errorMessage = "No modifier baseline has been captured.";
            return false;
        }

        if (
            !TryLoadContext(
                out _,
                out TerrainAuthoringData authoringData,
                out _,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TerrainAuthoringModifierSelection.TryFindModifier(
                authoringData,
                baseline.StableId,
                out TerrainHeightModifier modifier,
                out int index
            )
        )
        {
            current = new TerrainRuntimeInvalidationModifierBaseline
            {
                StableId = baseline.StableId,
                ModifierType = baseline.ModifierType,
                Exists = false
            };

            return true;
        }

        current = CaptureModifier(modifier, index);
        return true;
    }

    public static TerrainRuntimeInvalidationValidationResult
        ValidateModifierMutation(
            TerrainRuntimeInvalidationScenario scenario,
            TerrainRuntimeInvalidationModifierBaseline baseline,
            IEnumerable<Vector2Int> accumulatedHeightCoordinates = null
        )
    {
        if (baseline == null)
        {
            return Blocked(
                scenario,
                "Capture a selected modifier baseline before validating a modifier mutation."
            );
        }

        if (
            !TryLoadContext(
                out WorldSettings worldSettings,
                out _,
                out TerrainSurfaceSettings surfaceSettings,
                out string contextError
            )
        )
        {
            return Blocked(scenario, contextError);
        }

        if (
            !TryCaptureCurrentModifier(
                baseline,
                out TerrainRuntimeInvalidationModifierBaseline current,
                out string currentError
            )
        )
        {
            return Blocked(scenario, currentError);
        }

        if (
            !TryBuildModifierExpectation(
                scenario,
                worldSettings,
                surfaceSettings,
                baseline,
                current,
                accumulatedHeightCoordinates,
                out TerrainRuntimeInvalidationExpectation expectation,
                out string expectationError,
                out string expectationWarning
            )
        )
        {
            return Blocked(scenario, expectationError);
        }

        return ValidateExpectation(
            scenario,
            expectation,
            expectationWarning
        );
    }

    public static TerrainRuntimeInvalidationValidationResult
        ValidateSettingsMutation(
            TerrainRuntimeInvalidationScenario scenario,
            TerrainRuntimeInvalidationSettingsBaseline baseline
        )
    {
        if (baseline == null)
        {
            return Blocked(
                scenario,
                "Capture a settings baseline before validating settings invalidation."
            );
        }

        if (
            !TryLoadContext(
                out WorldSettings worldSettings,
                out _,
                out TerrainSurfaceSettings surfaceSettings,
                out string contextError
            )
        )
        {
            return Blocked(scenario, contextError);
        }

        if (
            !TryBuildSettingsExpectation(
                scenario,
                worldSettings,
                surfaceSettings,
                baseline,
                out TerrainRuntimeInvalidationExpectation expectation,
                out string expectationError
            )
        )
        {
            return Blocked(scenario, expectationError);
        }

        return ValidateExpectation(scenario, expectation, "");
    }

    public static TerrainRuntimeInvalidationValidationResult
        ValidateAggregatedModifierMutations(
            TerrainRuntimeInvalidationScenario scenario,
            IEnumerable<Vector2Int> expectedHeightCoordinates
        )
    {
        if (
            !TryLoadContext(
                out WorldSettings worldSettings,
                out _,
                out TerrainSurfaceSettings surfaceSettings,
                out string contextError
            )
        )
        {
            return Blocked(scenario, contextError);
        }

        HashSet<Vector2Int> height =
            new HashSet<Vector2Int>(
                expectedHeightCoordinates ??
                Array.Empty<Vector2Int>()
            );

        if (height.Count == 0)
        {
            return Blocked(
                scenario,
                "No accumulated modifier invalidation coordinates have been recorded."
            );
        }

        TerrainRuntimeInvalidationExpectation expectation =
            BuildLocalAuthoringExpectation(
                worldSettings,
                surfaceSettings,
                height,
                "Union of all recorded modifier mutation footprints."
            );

        return ValidateExpectation(scenario, expectation, "");
    }

    public static bool TryCollectModifierTransitionHeightTiles(
        TerrainRuntimeInvalidationModifierBaseline before,
        TerrainRuntimeInvalidationModifierBaseline after,
        WorldSettings worldSettings,
        ISet<Vector2Int> output,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            before == null
            || after == null
            || worldSettings == null
            || output == null
        )
        {
            errorMessage =
                "Modifier transition footprint received an invalid context.";
            return false;
        }

        if (before.Exists && before.Enabled)
        {
            TerrainAuthoringPreviewDirtyRegionUtility
                .CollectTilesOverlappingBounds(
                    worldSettings,
                    before.AffectedWorldBounds,
                    output,
                    1
                );
        }

        if (after.Exists && after.Enabled)
        {
            TerrainAuthoringPreviewDirtyRegionUtility
                .CollectTilesOverlappingBounds(
                    worldSettings,
                    after.AffectedWorldBounds,
                    output,
                    1
                );
        }

        return true;
    }

    public static bool TryLoadWorldSettings(out WorldSettings worldSettings)
    {
        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        return worldSettings != null;
    }

    private static bool TryBuildModifierExpectation(
        TerrainRuntimeInvalidationScenario scenario,
        WorldSettings worldSettings,
        TerrainSurfaceSettings surfaceSettings,
        TerrainRuntimeInvalidationModifierBaseline before,
        TerrainRuntimeInvalidationModifierBaseline after,
        IEnumerable<Vector2Int> accumulatedHeightCoordinates,
        out TerrainRuntimeInvalidationExpectation expectation,
        out string errorMessage,
        out string warningMessage
    )
    {
        expectation = null;
        errorMessage = "";
        warningMessage = "";

        if (
            !ValidateModifierScenarioTransition(
                scenario,
                before,
                after,
                out errorMessage,
                out warningMessage
            )
        )
        {
            return false;
        }

        HashSet<Vector2Int> height =
            accumulatedHeightCoordinates != null
                ? new HashSet<Vector2Int>(accumulatedHeightCoordinates)
                : new HashSet<Vector2Int>();

        if (
            !TryCollectModifierTransitionHeightTiles(
                before,
                after,
                worldSettings,
                height,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (height.Count == 0)
        {
            errorMessage =
                "The modifier transition does not intersect any current runtime Height tiles.";
            return false;
        }

        if (
            scenario == TerrainRuntimeInvalidationScenario.SingleTileEdit
            && height.Count != 1
        )
        {
            errorMessage =
                "SingleTileEdit requires an expected Height footprint of exactly one tile, but this transition touches " +
                height.Count +
                ".";
            return false;
        }

        if (
            scenario == TerrainRuntimeInvalidationScenario.MultiTileEdit
            && height.Count <= 1
        )
        {
            errorMessage =
                "MultiTileEdit requires a transition that touches more than one Height tile.";
            return false;
        }

        if (scenario == TerrainRuntimeInvalidationScenario.ModifierDistantMove)
        {
            HashSet<Vector2Int> oldTiles = new HashSet<Vector2Int>();
            HashSet<Vector2Int> newTiles = new HashSet<Vector2Int>();

            if (before.Exists && before.Enabled)
            {
                TerrainAuthoringPreviewDirtyRegionUtility
                    .CollectTilesOverlappingBounds(
                        worldSettings,
                        before.AffectedWorldBounds,
                        oldTiles,
                        1
                    );
            }

            if (after.Exists && after.Enabled)
            {
                TerrainAuthoringPreviewDirtyRegionUtility
                    .CollectTilesOverlappingBounds(
                        worldSettings,
                        after.AffectedWorldBounds,
                        newTiles,
                        1
                    );
            }

            oldTiles.IntersectWith(newTiles);

            if (oldTiles.Count > 0)
            {
                errorMessage =
                    "ModifierDistantMove requires old and new Height footprints to be spatially disjoint.";
                return false;
            }
        }

        expectation =
            BuildLocalAuthoringExpectation(
                worldSettings,
                surfaceSettings,
                height,
                "Old enabled footprint UNION new enabled footprint, with Package 02 downstream dependency expansion."
            );

        return true;
    }

    private static bool ValidateModifierScenarioTransition(
        TerrainRuntimeInvalidationScenario scenario,
        TerrainRuntimeInvalidationModifierBaseline before,
        TerrainRuntimeInvalidationModifierBaseline after,
        out string errorMessage,
        out string warningMessage
    )
    {
        errorMessage = "";
        warningMessage = "";

        switch (scenario)
        {
            case TerrainRuntimeInvalidationScenario.ModifierDelete:
                if (!before.Exists || !before.Enabled || after.Exists)
                {
                    errorMessage =
                        "ModifierDelete requires an enabled baseline modifier that no longer exists.";
                    return false;
                }
                return true;

            case TerrainRuntimeInvalidationScenario.ModifierEnable:
                if (
                    !before.Exists
                    || before.Enabled
                    || !after.Exists
                    || !after.Enabled
                )
                {
                    errorMessage =
                        "ModifierEnable requires a disabled baseline modifier that is now enabled.";
                    return false;
                }
                return true;

            case TerrainRuntimeInvalidationScenario.ModifierDisable:
                if (
                    !before.Exists
                    || !before.Enabled
                    || !after.Exists
                    || after.Enabled
                )
                {
                    errorMessage =
                        "ModifierDisable requires an enabled baseline modifier that is now disabled.";
                    return false;
                }
                return true;

            case TerrainRuntimeInvalidationScenario.ModifierMove:
            case TerrainRuntimeInvalidationScenario.ModifierDistantMove:
                if (
                    !before.IsStamp
                    || !after.IsStamp
                    || before.PositionXZ == after.PositionXZ
                )
                {
                    errorMessage =
                        "The selected stamp position did not change from the captured baseline.";
                    return false;
                }
                return true;

            case TerrainRuntimeInvalidationScenario.ModifierScale:
                if (
                    !before.IsStamp
                    || !after.IsStamp
                    || before.SizeXZ == after.SizeXZ
                )
                {
                    errorMessage =
                        "The selected stamp size did not change from the captured baseline.";
                    return false;
                }
                return true;

            case TerrainRuntimeInvalidationScenario.ModifierRotation:
                if (
                    !before.IsStamp
                    || !after.IsStamp
                    || Mathf.Approximately(
                        before.RotationDegrees,
                        after.RotationDegrees
                    )
                )
                {
                    errorMessage =
                        "The selected stamp rotation did not change from the captured baseline.";
                    return false;
                }

                if (before.AffectedWorldBounds == after.AffectedWorldBounds)
                {
                    warningMessage =
                        "Rotation changed but affected bounds are unchanged; recomposition of the current footprint is still valid.";
                }

                return true;

            case TerrainRuntimeInvalidationScenario.ModifierStrength:
                if (
                    !before.IsStamp
                    || !after.IsStamp
                    || Mathf.Approximately(
                        before.HeightDelta,
                        after.HeightDelta
                    )
                )
                {
                    errorMessage =
                        "The selected stamp Height Delta did not change from the captured baseline.";
                    return false;
                }
                return true;

            case TerrainRuntimeInvalidationScenario.SingleTileEdit:
            case TerrainRuntimeInvalidationScenario.MultiTileEdit:
            case TerrainRuntimeInvalidationScenario.Undo:
            case TerrainRuntimeInvalidationScenario.Redo:
            case TerrainRuntimeInvalidationScenario.MultipleEdits:
            case TerrainRuntimeInvalidationScenario.RepeatedRegionEdits:
            case TerrainRuntimeInvalidationScenario.DisconnectedRegionEdits:
                if (
                    before.Exists
                    && after.Exists
                    && before.ContentSignature == after.ContentSignature
                )
                {
                    errorMessage =
                        "The selected modifier content signature did not change from the captured baseline.";
                    return false;
                }
                return true;

            default:
                errorMessage =
                    "The selected scenario is not a modifier mutation scenario.";
                return false;
        }
    }

    private static TerrainRuntimeInvalidationExpectation
        BuildLocalAuthoringExpectation(
            WorldSettings worldSettings,
            TerrainSurfaceSettings surfaceSettings,
            IEnumerable<Vector2Int> heightCoordinates,
            string description
        )
    {
        HashSet<Vector2Int> height =
            new HashSet<Vector2Int>(heightCoordinates);

        HashSet<Vector2Int> surface =
            new HashSet<Vector2Int>();

        HashSet<Vector2Int> collision =
            new HashSet<Vector2Int>();

        bool surfaceMapped =
            surfaceSettings != null
            && TerrainRuntimeBakeDependencyUtility
                .TryCollectDependentSurfaceTiles(
                    worldSettings,
                    surfaceSettings,
                    height,
                    surface,
                    out _
                );

        bool collisionMapped =
            TerrainRuntimeBakeDependencyUtility
                .TryCollectDependentCollisionChunks(
                    worldSettings,
                    height,
                    collision,
                    out _
                );

        if (!surfaceMapped || !collisionMapped)
        {
            return null;
        }

        return new TerrainRuntimeInvalidationExpectation(
            TerrainRuntimeBakeWorkMode.Incremental,
            TerrainRuntimeBakeWorkMode.Incremental,
            TerrainRuntimeBakeWorkMode.Incremental,
            height,
            surface,
            collision,
            height,
            surface,
            collision,
            false,
            false,
            false,
            false,
            true,
            true,
            false,
            true,
            true,
            description
        );
    }

    private static bool TryBuildSettingsExpectation(
        TerrainRuntimeInvalidationScenario scenario,
        WorldSettings worldSettings,
        TerrainSurfaceSettings surfaceSettings,
        TerrainRuntimeInvalidationSettingsBaseline baseline,
        out TerrainRuntimeInvalidationExpectation expectation,
        out string errorMessage
    )
    {
        expectation = null;
        errorMessage = "";

        HashSet<Vector2Int> allHeight = new HashSet<Vector2Int>();
        HashSet<Vector2Int> allCollision = new HashSet<Vector2Int>();

        TerrainRuntimeBakeDependencyUtility
            .CollectAllHeightTiles(worldSettings, allHeight);

        TerrainRuntimeBakeDependencyUtility
            .CollectAllCollisionChunks(worldSettings, allCollision);

        switch (scenario)
        {
            case TerrainRuntimeInvalidationScenario.SurfaceSettingsChange:
            {
                string currentSurfaceSignature =
                    TerrainSurfaceSignatureUtility
                        .GetSettingsSignature(surfaceSettings);

                if (
                    string.Equals(
                        currentSurfaceSignature,
                        baseline.SurfaceSettingsSignature,
                        StringComparison.Ordinal
                    )
                )
                {
                    errorMessage =
                        "Terrain Surface settings signature did not change from the captured baseline.";
                    return false;
                }

                expectation = new TerrainRuntimeInvalidationExpectation(
                    TerrainRuntimeBakeWorkMode.None,
                    TerrainRuntimeBakeWorkMode.Full,
                    TerrainRuntimeBakeWorkMode.None,
                    null,
                    null,
                    null,
                    null,
                    allHeight,
                    null,
                    false,
                    true,
                    false,
                    false,
                    true,
                    false,
                    false,
                    true,
                    false,
                    "Surface-only settings change: Full Surface, no unnecessary Height or Collision invalidation."
                );

                return true;
            }

            case TerrainRuntimeInvalidationScenario.CollisionSettingsChange:
            {
                if (
                    baseline.CollisionResolution ==
                    worldSettings.collisionResolution
                )
                {
                    errorMessage =
                        "Collision Resolution did not change from the captured baseline.";
                    return false;
                }

                expectation = new TerrainRuntimeInvalidationExpectation(
                    TerrainRuntimeBakeWorkMode.None,
                    TerrainRuntimeBakeWorkMode.None,
                    TerrainRuntimeBakeWorkMode.Full,
                    null,
                    null,
                    null,
                    null,
                    null,
                    allCollision,
                    false,
                    false,
                    true,
                    false,
                    true,
                    false,
                    false,
                    true,
                    false,
                    "Collision-only settings change: Full Collision, no unnecessary Height or Surface invalidation."
                );

                return true;
            }

            case TerrainRuntimeInvalidationScenario.WorldLayoutChange:
            {
                bool gridChanged =
                    baseline.GridWidth != worldSettings.gridWidth
                    || baseline.GridHeight != worldSettings.gridHeight;

                bool metricChanged =
                    !Mathf.Approximately(
                        baseline.ChunkSize,
                        worldSettings.chunkSize
                    )
                    || baseline.HeightfieldResolutionPerChunk !=
                        worldSettings.heightfieldResolutionPerChunk;

                if (!gridChanged && !metricChanged)
                {
                    errorMessage =
                        "Grid Width/Height, Chunk Size, and Heightfield Resolution are unchanged from the captured baseline.";
                    return false;
                }

                expectation = new TerrainRuntimeInvalidationExpectation(
                    TerrainRuntimeBakeWorkMode.Full,
                    TerrainRuntimeBakeWorkMode.Full,
                    TerrainRuntimeBakeWorkMode.Full,
                    null,
                    null,
                    null,
                    allHeight,
                    allHeight,
                    allCollision,
                    true,
                    true,
                    true,
                    gridChanged,
                    true,
                    true,
                    gridChanged,
                    true,
                    true,
                    "Structural world layout change: Full Height/Surface/Collision dependency chain."
                );

                return true;
            }

            case TerrainRuntimeInvalidationScenario.HeightTileSpanChange:
            {
                if (
                    baseline.HeightTileChunkSpan ==
                    worldSettings.heightTileChunkSpan
                )
                {
                    errorMessage =
                        "Height Tile / Chunk Span did not change from the captured baseline.";
                    return false;
                }

                expectation = new TerrainRuntimeInvalidationExpectation(
                    TerrainRuntimeBakeWorkMode.Full,
                    TerrainRuntimeBakeWorkMode.Full,
                    TerrainRuntimeBakeWorkMode.Full,
                    null,
                    null,
                    null,
                    allHeight,
                    allHeight,
                    allCollision,
                    true,
                    true,
                    true,
                    true,
                    true,
                    true,
                    true,
                    true,
                    true,
                    "Height tile topology changed: Full dependency chain and Addressables configuration reconciliation required."
                );

                return true;
            }

            default:
                errorMessage =
                    "The selected scenario is not a supported settings invalidation scenario.";
                return false;
        }
    }

    private static TerrainRuntimeInvalidationValidationResult
        ValidateExpectation(
            TerrainRuntimeInvalidationScenario scenario,
            TerrainRuntimeInvalidationExpectation expectation,
            string warningMessage
        )
    {
        if (expectation == null)
        {
            return Blocked(
                scenario,
                "Expected dependency expansion could not be calculated."
            );
        }

        TerrainRuntimeBakeStateSnapshot state =
            TerrainRuntimeBakeStateService.GetSnapshot();

        TerrainRuntimeBakePlan plan =
            TerrainRuntimeBakePlanner.BuildCurrentPlan();

        if (state == null || plan == null)
        {
            return Failed(
                scenario,
                expectation,
                state,
                plan,
                "Persistent runtime bake state or planner output is unavailable."
            );
        }

        CompareSets(
            expectation.PlanHeightCoordinates,
            plan.HeightTiles,
            out List<Vector2Int> missingHeight,
            out List<Vector2Int> unexpectedHeight
        );

        CompareSets(
            expectation.PlanSurfaceCoordinates,
            plan.SurfaceTiles,
            out List<Vector2Int> missingSurface,
            out List<Vector2Int> unexpectedSurface
        );

        CompareSets(
            expectation.PlanCollisionCoordinates,
            plan.CollisionChunks,
            out List<Vector2Int> missingCollision,
            out List<Vector2Int> unexpectedCollision
        );

        bool persistentStateMatched =
            SetEquals(
                expectation.StateHeightCoordinates,
                state.PendingHeightTiles
            )
            && SetEquals(
                expectation.StateSurfaceCoordinates,
                state.PendingSurfaceTiles
            )
            && SetEquals(
                expectation.StateCollisionCoordinates,
                state.PendingCollisionChunks
            )
            && state.FullHeightRebuildRequired ==
                expectation.ExpectedFullHeight
            && state.FullSurfaceRebuildRequired ==
                expectation.ExpectedFullSurface
            && state.FullCollisionRebuildRequired ==
                expectation.ExpectedFullCollision
            && state.AddressablesConfigurationDirty ==
                expectation.ExpectedStateAddressablesConfigurationDirty
            && state.AddressablesContentDirty ==
                expectation.ExpectedStateAddressablesContentDirty
            && state.RuntimeSceneMetadataDirty ==
                expectation.ExpectedStateRuntimeSceneDirty;

        bool plannerMatched =
            !plan.IsBlocked
            && plan.HeightWorkMode ==
                expectation.ExpectedHeightMode
            && plan.SurfaceWorkMode ==
                expectation.ExpectedSurfaceMode
            && plan.CollisionWorkMode ==
                expectation.ExpectedCollisionMode
            && missingHeight.Count == 0
            && unexpectedHeight.Count == 0
            && missingSurface.Count == 0
            && unexpectedSurface.Count == 0
            && missingCollision.Count == 0
            && unexpectedCollision.Count == 0
            && plan.AddressablesConfigurationRequired ==
                expectation.ExpectedPlanAddressablesConfigurationRequired
            && plan.AddressablesContentBuildRequired ==
                expectation.ExpectedPlanAddressablesContentRequired
            && plan.RuntimeSceneMetadataUpdateRequired ==
                expectation.ExpectedPlanRuntimeSceneRequired;

        bool unexpectedFullEscalation =
            (
                expectation.ExpectedHeightMode !=
                    TerrainRuntimeBakeWorkMode.Full
                && plan.HeightWorkMode ==
                    TerrainRuntimeBakeWorkMode.Full
            )
            || (
                expectation.ExpectedSurfaceMode !=
                    TerrainRuntimeBakeWorkMode.Full
                && plan.SurfaceWorkMode ==
                    TerrainRuntimeBakeWorkMode.Full
            )
            || (
                expectation.ExpectedCollisionMode !=
                    TerrainRuntimeBakeWorkMode.Full
                && plan.CollisionWorkMode ==
                    TerrainRuntimeBakeWorkMode.Full
            );

        bool missingRequiredFullEscalation =
            (
                expectation.ExpectedHeightMode ==
                    TerrainRuntimeBakeWorkMode.Full
                && plan.HeightWorkMode !=
                    TerrainRuntimeBakeWorkMode.Full
            )
            || (
                expectation.ExpectedSurfaceMode ==
                    TerrainRuntimeBakeWorkMode.Full
                && plan.SurfaceWorkMode !=
                    TerrainRuntimeBakeWorkMode.Full
            )
            || (
                expectation.ExpectedCollisionMode ==
                    TerrainRuntimeBakeWorkMode.Full
                && plan.CollisionWorkMode !=
                    TerrainRuntimeBakeWorkMode.Full
            );

        bool passed =
            persistentStateMatched
            && plannerMatched
            && !unexpectedFullEscalation
            && !missingRequiredFullEscalation;

        TerrainRuntimeInvalidationValidationOutcome outcome =
            passed
                ? (
                    string.IsNullOrEmpty(warningMessage)
                        ? TerrainRuntimeInvalidationValidationOutcome.Passed
                        : TerrainRuntimeInvalidationValidationOutcome.PassedWithWarnings
                )
                : TerrainRuntimeInvalidationValidationOutcome.Failed;

        return new TerrainRuntimeInvalidationValidationResult(
            scenario,
            outcome,
            expectation,
            state,
            plan,
            missingHeight,
            unexpectedHeight,
            missingSurface,
            unexpectedSurface,
            missingCollision,
            unexpectedCollision,
            persistentStateMatched,
            plannerMatched,
            unexpectedFullEscalation,
            missingRequiredFullEscalation,
            warningMessage,
            passed
                ? ""
                : "Expected invalidation did not match persistent state and/or TerrainRuntimeBakePlan.",
            passed
                ? "The mutation produced the expected persistent dirty state and planner workset."
                : "Invalidation scenario validation failed."
        );
    }

    private static TerrainRuntimeInvalidationModifierBaseline CaptureModifier(
        TerrainHeightModifier modifier,
        int index
    )
    {
        TerrainStampModifier stamp = modifier as TerrainStampModifier;

        return new TerrainRuntimeInvalidationModifierBaseline
        {
            StableId = modifier.StableId,
            Index = index,
            ModifierType = modifier.GetType().Name,
            Exists = true,
            Enabled = modifier.Enabled,
            AffectedWorldBounds =
                modifier.GetAffectedWorldBounds(),
            ContentSignature =
                TerrainAuthoringStateUtility
                    .GetModifierContentSignature(modifier),
            IsStamp = stamp != null,
            PositionXZ =
                stamp != null
                    ? stamp.PositionXZ
                    : Vector2.zero,
            SizeXZ =
                stamp != null
                    ? stamp.SizeXZ
                    : Vector2.zero,
            RotationDegrees =
                stamp != null
                    ? stamp.RotationDegrees
                    : 0f,
            HeightDelta =
                stamp != null
                    ? stamp.HeightDelta
                    : 0f
        };
    }

    private static bool TryLoadContext(
        out WorldSettings worldSettings,
        out TerrainAuthoringData authoringData,
        out TerrainSurfaceSettings surfaceSettings,
        out string errorMessage
    )
    {
        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        surfaceSettings =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                WorldMeshesPaths.TerrainSurfaceSettingsAssetPath
            );

        errorMessage = "";

        if (worldSettings == null)
        {
            errorMessage = "WorldSettings is unavailable.";
            return false;
        }

        if (authoringData == null)
        {
            errorMessage = "TerrainAuthoringData is unavailable.";
            return false;
        }

        if (surfaceSettings == null)
        {
            errorMessage = "TerrainSurfaceSettings is unavailable.";
            return false;
        }

        return true;
    }

    private static bool SetEquals(
        IEnumerable<Vector2Int> left,
        IEnumerable<Vector2Int> right
    )
    {
        HashSet<Vector2Int> set =
            new HashSet<Vector2Int>(
                left ?? Array.Empty<Vector2Int>()
            );

        return set.SetEquals(
            right ?? Array.Empty<Vector2Int>()
        );
    }

    private static void CompareSets(
        IEnumerable<Vector2Int> expected,
        IEnumerable<Vector2Int> actual,
        out List<Vector2Int> missing,
        out List<Vector2Int> unexpected
    )
    {
        HashSet<Vector2Int> expectedSet =
            new HashSet<Vector2Int>(
                expected ?? Array.Empty<Vector2Int>()
            );

        HashSet<Vector2Int> actualSet =
            new HashSet<Vector2Int>(
                actual ?? Array.Empty<Vector2Int>()
            );

        missing = new List<Vector2Int>(expectedSet);
        missing.RemoveAll(
            coordinate => actualSet.Contains(coordinate)
        );

        unexpected = new List<Vector2Int>(actualSet);
        unexpected.RemoveAll(
            coordinate => expectedSet.Contains(coordinate)
        );

        missing.Sort(
            TerrainRuntimeInvalidationExpectation.CompareCoordinates
        );

        unexpected.Sort(
            TerrainRuntimeInvalidationExpectation.CompareCoordinates
        );
    }

    private static TerrainRuntimeInvalidationValidationResult Blocked(
        TerrainRuntimeInvalidationScenario scenario,
        string errorMessage
    )
    {
        return new TerrainRuntimeInvalidationValidationResult(
            scenario,
            TerrainRuntimeInvalidationValidationOutcome.Blocked,
            null,
            TerrainRuntimeBakeStateService.GetSnapshot(),
            TerrainRuntimeBakePlanner.BuildCurrentPlan(),
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            false,
            false,
            false,
            "",
            errorMessage,
            "Invalidation validation did not run."
        );
    }

    private static TerrainRuntimeInvalidationValidationResult Failed(
        TerrainRuntimeInvalidationScenario scenario,
        TerrainRuntimeInvalidationExpectation expectation,
        TerrainRuntimeBakeStateSnapshot state,
        TerrainRuntimeBakePlan plan,
        string errorMessage
    )
    {
        return new TerrainRuntimeInvalidationValidationResult(
            scenario,
            TerrainRuntimeInvalidationValidationOutcome.Failed,
            expectation,
            state,
            plan,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            false,
            false,
            false,
            "",
            errorMessage,
            "Invalidation validation failed."
        );
    }
}
