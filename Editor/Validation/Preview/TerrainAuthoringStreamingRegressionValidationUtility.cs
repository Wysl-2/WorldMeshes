using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Final integration-level regression coverage for the edit-mode streamed
 * height preview.
 *
 * Synthetic tests exercise the same production residency/window/transition
 * policy used by the editor without allocating full-world GPU resources or
 * moving the Scene View. Live checks are observational only.
 */
public static class TerrainAuthoringStreamingRegressionValidationUtility
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

    public static void ValidateStreamingRegression()
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

        if (
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogWarning(
                "WorldMeshes final streaming regression did not run because Play Mode owns runtime terrain residency."
            );

            return;
        }

        validationRunning =
            true;

        results.Clear();

        WorldSettings liveWorldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData liveAuthoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        long revisionBefore =
            liveAuthoringData != null
                ? liveAuthoringData.authoringRevision
                : long.MinValue;

        int authoringInstanceIdBefore =
            liveAuthoringData != null
                ? liveAuthoringData.GetInstanceID()
                : 0;

        string committedSignatureBefore =
            liveWorldSettings != null
                ? TerrainAuthoringStateUtility
                    .GetCommittedHeightfieldSignature(
                        liveWorldSettings
                    )
                : "";

        string overallSignatureBefore =
            liveWorldSettings != null
            &&
            liveAuthoringData != null
                ? TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        liveWorldSettings,
                        liveAuthoringData
                    )
                : "";

        try
        {
            ValidateSyntheticLargeWorldResidency();
            ValidateSyntheticTransitionMemoryBounds();
            ValidateWorldEdgesAndCorners();

            ValidateOneTileMovementCost();
            ValidateSequentialMovementStress();
            ValidateRapidLatestTargetWins();
            ValidateMovementReversal();
            ValidateDistantJump();

            ValidateAuthoringGenerationIdentity();
            ValidateAnalysisSourceCoherence();

            ValidateLiveResidencyBoundedness(
                liveWorldSettings
            );

            ValidateLiveCacheOwnership();

            ValidatePersistentAuthoringState(
                liveWorldSettings,
                liveAuthoringData,
                revisionBefore,
                authoringInstanceIdBefore,
                committedSignatureBefore,
                overallSignatureBefore
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

    // =====================================================
    // SYNTHETIC LARGE-WORLD BOUNDEDNESS
    // =====================================================

    private static void ValidateSyntheticLargeWorldResidency()
    {
        int[] worldSizes =
        {
            32,
            128,
            512,
            1024
        };

        Vector2Int baselineRequiredSize =
            Vector2Int.zero;

        Vector2Int baselineResidentSize =
            Vector2Int.zero;

        long baselineResidentMemory =
            -1L;

        bool baselineSet =
            false;

        bool allBounded =
            true;

        StringBuilder detail =
            new StringBuilder();

        foreach (int worldSize in worldSizes)
        {
            WorldSettings settings =
                CreateSyntheticSettings(
                    worldSize,
                    worldSize
                );

            try
            {
                if (
                    settings.HeightTileGridWidth !=
                        worldSize
                    ||
                    settings.HeightTileGridHeight !=
                        worldSize
                )
                {
                    AddFail(
                        $"Synthetic {worldSize}x{worldSize} settings",
                        "Synthetic settings did not produce the requested height-tile grid."
                    );

                    allBounded =
                        false;

                    continue;
                }

                int footprintWidth =
                    4;

                int footprintHeight =
                    4;

                int startX =
                    Mathf.Max(
                        0,
                        (worldSize - footprintWidth) / 2
                    );

                int startZ =
                    Mathf.Max(
                        0,
                        (worldSize - footprintHeight) / 2
                    );

                if (
                    !TryCalculateSyntheticWindows(
                        settings,
                        startX,
                        startZ,
                        footprintWidth,
                        footprintHeight,
                        out TerrainHeightCacheWindow required,
                        out TerrainHeightCacheWindow resident,
                        out string errorMessage
                    )
                )
                {
                    AddFail(
                        $"Synthetic {worldSize}x{worldSize} residency",
                        errorMessage
                    );

                    allBounded =
                        false;

                    continue;
                }

                long residentMemory =
                    EstimateCacheMemoryBytes(
                        settings,
                        resident
                    );

                if (!baselineSet)
                {
                    baselineRequiredSize =
                        required.Size;

                    baselineResidentSize =
                        resident.Size;

                    baselineResidentMemory =
                        residentMemory;

                    baselineSet =
                        true;
                }

                bool sameBoundedShape =
                    required.Size ==
                        baselineRequiredSize
                    &&
                    resident.Size ==
                        baselineResidentSize
                    &&
                    residentMemory ==
                        baselineResidentMemory;

                allBounded &=
                    sameBoundedShape;

                double residentFraction =
                    settings.HeightTileCount > 0
                        ? (double)resident.TileCount /
                            settings.HeightTileCount
                        : 0.0;

                detail.AppendLine(
                    $"{worldSize}x{worldSize}: " +
                    $"logical={settings.HeightTileCount:N0}, " +
                    $"required={required.TileCount:N0} ({required.Size.x}x{required.Size.y}), " +
                    $"resident={resident.TileCount:N0} ({resident.Size.x}x{resident.Size.y}), " +
                    $"fraction={residentFraction:P6}, " +
                    $"activeEstimate={FormatBytes(residentMemory)}"
                );

                AddResult(
                    $"Synthetic {worldSize}x{worldSize} residency",
                    sameBoundedShape
                        ? ValidationOutcome.Pass
                        : ValidationOutcome.Fail,
                    $"Required={required}; Resident={resident}; Logical tiles={settings.HeightTileCount:N0}; Estimated active GPU={FormatBytes(residentMemory)}."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(
                    settings
                );
            }
        }

        AddResult(
            "Logical world size does not scale local residency",
            baselineSet
            &&
            allBounded
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            detail.ToString().TrimEnd()
        );
    }

    private static void ValidateSyntheticTransitionMemoryBounds()
    {
        int[] worldSizes =
        {
            32,
            128,
            512,
            1024
        };

        long baselinePeak =
            -1L;

        bool allEqual =
            true;

        StringBuilder detail =
            new StringBuilder();

        foreach (int worldSize in worldSizes)
        {
            WorldSettings settings =
                CreateSyntheticSettings(
                    worldSize,
                    worldSize
                );

            try
            {
                if (
                    !TryCalculateSyntheticWindows(
                        settings,
                        Mathf.Max(
                            0,
                            (worldSize - 4) / 2
                        ),
                        Mathf.Max(
                            0,
                            (worldSize - 4) / 2
                        ),
                        4,
                        4,
                        out _,
                        out TerrainHeightCacheWindow resident,
                        out string errorMessage
                    )
                )
                {
                    AddFail(
                        $"Synthetic {worldSize}x{worldSize} transition memory",
                        errorMessage
                    );

                    allEqual =
                        false;

                    continue;
                }

                long oneCache =
                    EstimateCacheMemoryBytes(
                        settings,
                        resident
                    );

                long peak =
                    SafeAdd(
                        oneCache,
                        oneCache
                    );

                if (baselinePeak < 0L)
                {
                    baselinePeak =
                        peak;
                }

                bool matchesBaseline =
                    peak ==
                        baselinePeak;

                allEqual &=
                    matchesBaseline;

                detail.AppendLine(
                    $"{worldSize}x{worldSize}: " +
                    $"active={FormatBytes(oneCache)}, " +
                    $"active+staging={FormatBytes(peak)}"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(
                    settings
                );
            }
        }

        AddResult(
            "Transition memory remains bounded across logical world sizes",
            allEqual
            &&
            baselinePeak >= 0L
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            detail.ToString().TrimEnd()
        );
    }

    // =====================================================
    // EDGE / CORNER FITTING
    // =====================================================

    private static void ValidateWorldEdgesAndCorners()
    {
        const int worldSize =
            128;

        const int footprint =
            4;

        WorldSettings settings =
            CreateSyntheticSettings(
                worldSize,
                worldSize
            );

        try
        {
            int maximumStart =
                worldSize -
                footprint;

            Vector2Int center =
                new Vector2Int(
                    maximumStart / 2,
                    maximumStart / 2
                );

            string[] names =
            {
                "Center",
                "North edge",
                "South edge",
                "East edge",
                "West edge",
                "North-west corner",
                "North-east corner",
                "South-west corner",
                "South-east corner"
            };

            Vector2Int[] starts =
            {
                center,
                new Vector2Int(
                    center.x,
                    maximumStart
                ),
                new Vector2Int(
                    center.x,
                    0
                ),
                new Vector2Int(
                    maximumStart,
                    center.y
                ),
                new Vector2Int(
                    0,
                    center.y
                ),
                new Vector2Int(
                    0,
                    maximumStart
                ),
                new Vector2Int(
                    maximumStart,
                    maximumStart
                ),
                new Vector2Int(
                    0,
                    0
                ),
                new Vector2Int(
                    maximumStart,
                    0
                )
            };

            if (
                !TryCalculateSyntheticWindows(
                    settings,
                    center.x,
                    center.y,
                    footprint,
                    footprint,
                    out _,
                    out TerrainHeightCacheWindow centerResident,
                    out string centerError
                )
            )
            {
                AddFail(
                    "World edge/corner baseline",
                    centerError
                );

                return;
            }

            Vector2Int worldGridSize =
                new Vector2Int(
                    settings.HeightTileGridWidth,
                    settings.HeightTileGridHeight
                );

            for (
                int i = 0;
                i < names.Length;
                i++
            )
            {
                Vector2Int start =
                    starts[i];

                if (
                    !TryCalculateSyntheticWindows(
                        settings,
                        start.x,
                        start.y,
                        footprint,
                        footprint,
                        out TerrainHeightCacheWindow required,
                        out TerrainHeightCacheWindow resident,
                        out string errorMessage
                    )
                )
                {
                    AddFail(
                        names[i],
                        errorMessage
                    );

                    continue;
                }

                bool insideWorld =
                    resident.OriginTile.x >= 0
                    &&
                    resident.OriginTile.y >= 0
                    &&
                    resident.MaximumExclusive.x <=
                        worldGridSize.x
                    &&
                    resident.MaximumExclusive.y <=
                        worldGridSize.y;

                bool preservesSize =
                    resident.Size ==
                        centerResident.Size;

                bool containsRequired =
                    resident.Contains(
                        required
                    );

                AddResult(
                    names[i],
                    insideWorld
                    &&
                    preservesSize
                    &&
                    containsRequired
                        ? ValidationOutcome.Pass
                        : ValidationOutcome.Fail,
                    $"Required={required}; Resident={resident}; Baseline resident size={centerResident.Size}; World={worldGridSize}."
                );
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                settings
            );
        }
    }

    // =====================================================
    // MOVEMENT / TRANSITION REGRESSION
    // =====================================================

    private static void ValidateOneTileMovementCost()
    {
        TerrainHeightCacheWindow source =
            Window(
                100,
                100,
                12,
                10
            );

        TerrainHeightCacheWindow targetX =
            Window(
                101,
                100,
                12,
                10
            );

        TerrainHeightCacheWindow targetY =
            Window(
                100,
                101,
                12,
                10
            );

        ValidateOneTileTransition(
            "One-tile X translation",
            source,
            targetX,
            (source.Width - 1) *
                source.Height,
            source.Height,
            source.Height
        );

        ValidateOneTileTransition(
            "One-tile Y translation",
            source,
            targetY,
            source.Width *
                (source.Height - 1),
            source.Width,
            source.Width
        );
    }

    private static void ValidateOneTileTransition(
        string name,
        TerrainHeightCacheWindow source,
        TerrainHeightCacheWindow target,
        int expectedRetained,
        int expectedEntering,
        int expectedLeaving
    )
    {
        if (
            !TerrainAuthoringPreviewCacheTransition
                .TryCreate(
                    true,
                    source,
                    target,
                    "synthetic-committed",
                    "synthetic-overall",
                    out TerrainAuthoringPreviewCacheTransition transition,
                    out string errorMessage
                )
        )
        {
            AddFail(
                name,
                errorMessage
            );

            return;
        }

        bool passed =
            transition.RetainedTiles.Count ==
                expectedRetained
            &&
            transition.EnteringTiles.Count ==
                expectedEntering
            &&
            transition.LeavingTiles.Count ==
                expectedLeaving;

        AddResult(
            name,
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Retained={transition.RetainedTiles.Count:N0}/{expectedRetained:N0}; Entering={transition.EnteringTiles.Count:N0}/{expectedEntering:N0}; Leaving={transition.LeavingTiles.Count:N0}/{expectedLeaving:N0}."
        );
    }

    private static void ValidateSequentialMovementStress()
    {
        TerrainHeightCacheWindow b =
            Window(
                120,
                100,
                12,
                12
            );

        TerrainHeightCacheWindow c =
            Window(
                128,
                100,
                12,
                12
            );

        TerrainHeightCacheWindow d =
            Window(
                136,
                100,
                12,
                12
            );

        TerrainHeightCacheWindow requiredB =
            InsetWindow(
                b,
                2
            );

        TerrainHeightCacheWindow requiredC =
            InsetWindow(
                c,
                2
            );

        TerrainHeightCacheWindow requiredD =
            InsetWindow(
                d,
                2
            );

        bool bUsefulForB =
            TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    b,
                    requiredB,
                    b
                );

        bool bObsoleteForC =
            !TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    b,
                    requiredC,
                    c
                );

        bool cUsefulForC =
            TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    c,
                    requiredC,
                    c
                );

        bool cObsoleteForD =
            !TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    c,
                    requiredD,
                    d
                );

        bool dUsefulForD =
            TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    d,
                    requiredD,
                    d
                );

        AddResult(
            "Sequential movement invalidates obsolete staging",
            bUsefulForB
            &&
            bObsoleteForC
            &&
            cUsefulForC
            &&
            cObsoleteForD
            &&
            dUsefulForD
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "A -> B -> C -> D uses current required/desired intent. Earlier same-sized local targets become obsolete instead of forming a catch-up queue."
        );
    }

    private static void ValidateRapidLatestTargetWins()
    {
        TerrainHeightCacheWindow b =
            Window(
                200,
                200,
                12,
                12
            );

        TerrainHeightCacheWindow c =
            Window(
                208,
                200,
                12,
                12
            );

        TerrainHeightCacheWindow d =
            Window(
                216,
                200,
                12,
                12
            );

        TerrainHeightCacheWindow requiredD =
            InsetWindow(
                d,
                2
            );

        bool oldBRejected =
            !TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    b,
                    requiredD,
                    d
                );

        bool oldCRejected =
            !TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    c,
                    requiredD,
                    d
                );

        bool latestAccepted =
            TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    d,
                    requiredD,
                    d
                );

        AddResult(
            "Latest meaningful target wins",
            oldBRejected
            &&
            oldCRejected
            &&
            latestAccepted
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "When D is newest intent, B and C are no longer useful staging destinations while D remains valid."
        );
    }

    private static void ValidateMovementReversal()
    {
        TerrainHeightCacheWindow activeA =
            Window(
                300,
                300,
                12,
                12
            );

        TerrainHeightCacheWindow stagingB =
            Window(
                308,
                300,
                12,
                12
            );

        TerrainHeightCacheWindow requiredA =
            InsetWindow(
                activeA,
                2
            );

        Vector2Int worldGridSize =
            new Vector2Int(
                1024,
                1024
            );

        bool stagingNoLongerUseful =
            !TerrainAuthoringPreviewStreamingPolicy
                .IsStagingTargetUseful(
                    stagingB,
                    requiredA,
                    activeA
                );

        bool activeComfortablySufficient =
            TerrainAuthoringPreviewStreamingPolicy
                .IsActiveComfortablySufficient(
                    activeA,
                    requiredA,
                    activeA,
                    worldGridSize
                );

        AddResult(
            "A -> B -> A reversal",
            stagingNoLongerUseful
            &&
            activeComfortablySufficient
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Returning to A makes the old B staging destination unnecessary while the existing active window remains comfortably sufficient."
        );
    }

    private static void ValidateDistantJump()
    {
        TerrainHeightCacheWindow source =
            Window(
                20,
                20,
                12,
                12
            );

        TerrainHeightCacheWindow target =
            Window(
                800,
                800,
                12,
                12
            );

        if (
            !TerrainAuthoringPreviewCacheTransition
                .TryCreate(
                    true,
                    source,
                    target,
                    "synthetic-committed",
                    "synthetic-overall",
                    out TerrainAuthoringPreviewCacheTransition transition,
                    out string errorMessage
                )
        )
        {
            AddFail(
                "Distant jump bounded transition cost",
                errorMessage
            );

            return;
        }

        bool passed =
            transition.RetainedTiles.Count == 0
            &&
            transition.EnteringTiles.Count ==
                target.TileCount
            &&
            transition.LeavingTiles.Count ==
                source.TileCount;

        AddResult(
            "Distant jump bounded transition cost",
            passed
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Distance is hundreds of tiles, but transition work is local: retained={transition.RetainedTiles.Count:N0}, entering={transition.EnteringTiles.Count:N0}, leaving={transition.LeavingTiles.Count:N0}."
        );
    }

    private static void ValidateAuthoringGenerationIdentity()
    {
        TerrainHeightCacheWindow source =
            Window(
                10,
                10,
                8,
                8
            );

        TerrainHeightCacheWindow target =
            Window(
                12,
                10,
                8,
                8
            );

        if (
            !TerrainAuthoringPreviewCacheTransition
                .TryCreate(
                    true,
                    source,
                    target,
                    "synthetic-committed",
                    "synthetic-overall",
                    out TerrainAuthoringPreviewCacheTransition transition,
                    out string errorMessage
                )
        )
        {
            AddFail(
                "Transition authoring-generation identity",
                errorMessage
            );

            return;
        }

        const long generation =
            42L;

        transition.TargetAuthoringGeneration =
            generation;

        bool preserved =
            transition.TargetAuthoringGeneration ==
                generation;

        AddResult(
            "Transition authoring-generation identity",
            preserved
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Transition metadata retains the authoring generation used by the existing modifier-residency stale-activation checks."
        );
    }

    // =====================================================
    // LIVE INTEGRATION CHECKS
    // =====================================================

    private static void ValidateAnalysisSourceCoherence()
    {
        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot =
            TerrainAuthoringPreviewService
                .GetDiagnosticsSnapshot();

        if (!snapshot.HasActiveWindow)
        {
            AddBlocked(
                "Live Terrain Analysis source coherence",
                "No active Height Preview residency is available. Package 07 synthetic analysis validation remains available separately."
            );

            return;
        }

        if (
            !TerrainAuthoringPreviewService
                .TryGetTerrainAnalysisGpuSource(
                    out TerrainAnalysisGpuSource source
                )
        )
        {
            AddFail(
                "Live Terrain Analysis source coherence",
                "An active preview window exists, but no valid Terrain Analysis GPU source could be acquired."
            );

            return;
        }

        bool identityMatches =
            source.SourceWindow ==
                snapshot.ActiveWindow
            &&
            source.ResidencyGeneration ==
                snapshot.AnalysisResidencyGeneration
            &&
            source.CompositeGeneration ==
                snapshot.AnalysisCompositeGeneration;

        if (
            !TerrainAnalysisWindowUtility
                .TryCalculateInteractiveOutputWindow(
                    source,
                    out TerrainHeightCacheWindow outputWindow,
                    out string outputError
                )
        )
        {
            AddFail(
                "Live Terrain Analysis safe output",
                outputError
            );

            return;
        }

        bool outputContained =
            source.SourceWindow.Contains(
                outputWindow
            );

        AddResult(
            "Live Terrain Analysis source coherence",
            identityMatches
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Analysis source={source.SourceWindow}; Active={snapshot.ActiveWindow}; ResidencyGen={source.ResidencyGeneration:N0}/{snapshot.AnalysisResidencyGeneration:N0}; CompositeGen={source.CompositeGeneration:N0}/{snapshot.AnalysisCompositeGeneration:N0}."
        );

        AddResult(
            "Live Terrain Analysis safe output",
            outputContained
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"Source={source.SourceWindow}; Safe output={outputWindow}. Offline whole-world batching remains covered by TerrainAuthoringAnalysisDecouplingValidationUtility."
        );
    }

    private static void ValidateLiveResidencyBoundedness(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            AddBlocked(
                "Live residency boundedness",
                "WorldSettings is unavailable. Synthetic large-world validation remains authoritative."
            );

            return;
        }

        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot =
            TerrainAuthoringPreviewService
                .GetDiagnosticsSnapshot();

        if (
            !snapshot.HasActiveWindow
            ||
            !snapshot.ActiveWindow.IsValid
        )
        {
            AddBlocked(
                "Live residency boundedness",
                "No active Height Preview cache is currently available. Synthetic scaling remains authoritative."
            );

            return;
        }

        int worldTileCount =
            worldSettings.HeightTileCount;

        if (
            snapshot.ActiveTileCount <
            worldTileCount
        )
        {
            AddPass(
                "Live residency boundedness",
                $"Active={snapshot.ActiveTileCount:N0} tiles; logical world={worldTileCount:N0} tiles; active GPU estimate={FormatBytes(snapshot.ActiveGpuMemoryBytes)}; peak transition estimate={FormatBytes(snapshot.PeakTransitionGpuMemoryBytes)}."
            );

            return;
        }

        AddBlocked(
            "Live residency boundedness",
            $"Active residency currently equals the complete {worldTileCount:N0}-tile logical world. This may be legitimate for a small project; synthetic 32/128/512/1024 scaling is the authoritative boundedness test."
        );
    }

    private static void ValidateLiveCacheOwnership()
    {
        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot =
            TerrainAuthoringPreviewService
                .GetDiagnosticsSnapshot();

        if (snapshot.CacheLiveCount > 2)
        {
            AddFail(
                "Live preview cache ownership",
                $"Observed {snapshot.CacheLiveCount:N0} live TerrainAuthoringPreviewCache objects. Active + staging ownership permits at most two."
            );

            return;
        }

        bool settled =
            !snapshot.IsStreaming
            &&
            !snapshot.HasStagingWindow;

        if (!settled)
        {
            AddBlocked(
                "Settled preview cache ownership",
                $"Streaming is still active or staging still exists. Current live cache count={snapshot.CacheLiveCount:N0}; at most two are permitted during a transition."
            );

            return;
        }

        int expectedLive =
            snapshot.CacheReady
                ? 1
                : 0;

        AddResult(
            "Settled preview cache ownership",
            snapshot.CacheLiveCount ==
                expectedLive
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            $"CacheReady={snapshot.CacheReady}; Created={snapshot.CacheCreateCount:N0}; Disposed={snapshot.CacheDisposeCount:N0}; Live={snapshot.CacheLiveCount:N0}; Expected live={expectedLive:N0}."
        );
    }

    private static void ValidatePersistentAuthoringState(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        long revisionBefore,
        int authoringInstanceIdBefore,
        string committedSignatureBefore,
        string overallSignatureBefore
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

        string committedSignatureAfter =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallSignatureAfter =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        bool unchanged =
            authoringData.authoringRevision ==
                revisionBefore
            &&
            authoringData.GetInstanceID() ==
                authoringInstanceIdBefore
            &&
            committedSignatureBefore ==
                committedSignatureAfter
            &&
            overallSignatureBefore ==
                overallSignatureAfter;

        AddResult(
            "Persistent authoring state unchanged",
            unchanged
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail,
            "Final streaming regression validation must not mutate persistent terrain authoring identity."
        );
    }

    // =====================================================
    // SYNTHETIC HELPERS
    // =====================================================

    private static WorldSettings CreateSyntheticSettings(
        int heightTileWidth,
        int heightTileHeight
    )
    {
        WorldSettings settings =
            ScriptableObject.CreateInstance<WorldSettings>();

        settings.heightTileChunkSpan =
            4;

        settings.gridWidth =
            Mathf.Max(
                1,
                heightTileWidth
            )
            *
            settings.heightTileChunkSpan;

        settings.gridHeight =
            Mathf.Max(
                1,
                heightTileHeight
            )
            *
            settings.heightTileChunkSpan;

        settings.chunkSize =
            128f;

        settings.heightfieldResolutionPerChunk =
            128;

        settings.clipmapCenterResolution =
            512;

        settings.clipmapLevelCount =
            5;

        settings.clipmapBaseSampleStep =
            1;

        settings.clipmapLODOuterResolutions =
            new int[]
            {
                512,
                512,
                512,
                512
            };

        return settings;
    }

    private static bool TryCalculateSyntheticWindows(
        WorldSettings settings,
        int startTileX,
        int startTileZ,
        int footprintWidth,
        int footprintHeight,
        out TerrainHeightCacheWindow required,
        out TerrainHeightCacheWindow resident,
        out string errorMessage
    )
    {
        required =
            default;

        resident =
            default;

        errorMessage =
            "";

        if (
            settings == null
            ||
            footprintWidth <= 0
            ||
            footprintHeight <= 0
        )
        {
            errorMessage =
                "Synthetic residency input is invalid.";

            return false;
        }

        float tileWorldSize =
            settings.HeightTileWorldSize;

        float inset =
            Mathf.Max(
                0.001f,
                tileWorldSize *
                    0.25f
            );

        Vector2 minimumXZ =
            new Vector2(
                startTileX *
                    tileWorldSize +
                    inset,
                startTileZ *
                    tileWorldSize +
                    inset
            );

        Vector2 maximumXZ =
            new Vector2(
                (
                    startTileX +
                    footprintWidth
                )
                *
                tileWorldSize -
                    inset,
                (
                    startTileZ +
                    footprintHeight
                )
                *
                tileWorldSize -
                    inset
            );

        if (
            !TerrainAuthoringPreviewResidencyUtility
                .TryCalculateRequiredWindow(
                    settings,
                    minimumXZ,
                    maximumXZ,
                    TerrainAuthoringPreviewResidencyUtility
                        .DefaultSamplePadding,
                    out required,
                    out errorMessage
                )
        )
        {
            return false;
        }

        return
            TerrainAuthoringPreviewResidencyUtility
                .TryCalculateResidentWindow(
                    settings,
                    minimumXZ,
                    maximumXZ,
                    TerrainAuthoringPreviewResidencyUtility
                        .DefaultSamplePadding,
                    TerrainAuthoringPreviewResidencyUtility
                        .DefaultGuardTileCount,
                    out resident,
                    out errorMessage
                );
    }

    private static long EstimateCacheMemoryBytes(
        WorldSettings settings,
        TerrainHeightCacheWindow window
    )
    {
        if (
            settings == null
            ||
            !window.IsValid
        )
        {
            return 0L;
        }

        try
        {
            long samplesPerSide =
                settings.HeightTileSamplesPerSide;

            return
                checked(
                    samplesPerSide
                    *
                    samplesPerSide
                    *
                    (long)window.TileCount
                    *
                    sizeof(float)
                );
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static long SafeAdd(
        long left,
        long right
    )
    {
        if (
            left == long.MaxValue
            ||
            right == long.MaxValue
        )
        {
            return long.MaxValue;
        }

        if (
            right > 0L
            &&
            left > long.MaxValue - right
        )
        {
            return long.MaxValue;
        }

        return
            left +
            right;
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
                new Vector2Int(
                    x,
                    z
                ),
                new Vector2Int(
                    width,
                    height
                )
            );
    }

    private static TerrainHeightCacheWindow InsetWindow(
        TerrainHeightCacheWindow window,
        int inset
    )
    {
        int safeInset =
            Mathf.Max(
                0,
                inset
            );

        Vector2Int size =
            window.Size -
            new Vector2Int(
                safeInset * 2,
                safeInset * 2
            );

        return
            new TerrainHeightCacheWindow(
                window.OriginTile +
                    new Vector2Int(
                        safeInset,
                        safeInset
                    ),
                size
            );
    }

    private static string FormatBytes(
        long bytes
    )
    {
        double safeBytes =
            Math.Max(
                0L,
                bytes
            );

        if (safeBytes >= 1024d * 1024d * 1024d)
        {
            return
                $"{safeBytes / (1024d * 1024d * 1024d):F2} GB";
        }

        if (safeBytes >= 1024d * 1024d)
        {
            return
                $"{safeBytes / (1024d * 1024d):F2} MB";
        }

        if (safeBytes >= 1024d)
        {
            return
                $"{safeBytes / 1024d:F2} KB";
        }

        return
            $"{safeBytes:F0} B";
    }

    // =====================================================
    // RESULTS
    // =====================================================

    private enum ValidationOutcome
    {
        Pass,
        Fail,
        Blocked
    }

    private static void AddResult(
        string name,
        ValidationOutcome outcome,
        string detail
    )
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Detail = detail,
                Passed =
                    outcome ==
                        ValidationOutcome.Pass,
                Blocked =
                    outcome ==
                        ValidationOutcome.Blocked
            }
        );
    }

    private static void AddPass(
        string name,
        string detail
    )
    {
        AddResult(
            name,
            ValidationOutcome.Pass,
            detail
        );
    }

    private static void AddFail(
        string name,
        string detail
    )
    {
        AddResult(
            name,
            ValidationOutcome.Fail,
            detail
        );
    }

    private static void AddBlocked(
        string name,
        string detail
    )
    {
        AddResult(
            name,
            ValidationOutcome.Blocked,
            detail
        );
    }

    private static void FinishValidation()
    {
        int passed =
            0;

        int failed =
            0;

        int blocked =
            0;

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Final Edit-Mode Streaming Regression"
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
                outcome =
                    "BLOCKED";

                blocked++;
            }
            else if (result.Passed)
            {
                outcome =
                    "PASS";

                passed++;
            }
            else
            {
                outcome =
                    "FAIL";

                failed++;
            }

            builder.AppendLine(
                $"[{outcome}] {result.Name}"
            );

            if (
                !string.IsNullOrEmpty(
                    result.Detail
                )
            )
            {
                builder.AppendLine(
                    $"    {result.Detail}"
                );
            }

            builder.AppendLine();
        }

        builder.AppendLine(
            "Package 07 offline/global Terrain Analysis batching remains " +
            "covered by TerrainAuthoringAnalysisDecouplingValidationUtility."
        );

        builder.AppendLine();

        builder.AppendLine(
            $"Summary: {passed:N0} passed, {failed:N0} failed, {blocked:N0} blocked."
        );

        string report =
            builder.ToString();

        if (failed > 0)
        {
            Debug.LogError(
                report
            );
        }
        else if (blocked > 0)
        {
            Debug.LogWarning(
                report
            );
        }
        else
        {
            Debug.Log(
                report
            );
        }
    }
}
