using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawHeightPreviewSettings()
    {
        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
        GUILayout.Label("Height Preview", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        bool enabled = EditorGUILayout.Toggle("Enabled", TerrainAuthoringPreviewService.Enabled);
        if (EditorGUI.EndChangeCheck()) TerrainAuthoringPreviewService.Enabled = enabled;
        var snapshot = TerrainAuthoringPreviewService.GetDiagnosticsSnapshot();
        DrawHeightPreviewDiagnostics(snapshot);
        if (snapshot.Enabled)
        {
            EditorGUILayout.LabelField("Modifier Logical Dirty Tiles", TerrainAuthoringPreviewService.PendingGlobalDirtyTileCount.ToString("N0"));
            EditorGUILayout.LabelField("Modifier Resident / Nonresident", $"{TerrainAuthoringPreviewService.LastResidentDirtyTileCount:N0} / {TerrainAuthoringPreviewService.LastNonresidentDirtyTileCount:N0}");
            EditorGUILayout.LabelField("Regional Scope", TerrainAuthoringPreviewService.LastRegionalInvalidationKind);
            EditorGUILayout.LabelField("Regional Logical / Resident / Nonresident", $"{TerrainAuthoringPreviewService.LastRegionalLogicalAffectedTileCount:N0} / {TerrainAuthoringPreviewService.LastRegionalResidentAffectedTileCount:N0} / {TerrainAuthoringPreviewService.LastRegionalNonresidentAffectedTileCount:N0}");
            EditorGUILayout.LabelField("Interactive Edits", $"Modifier={TerrainAuthoringPreviewService.InteractiveModifierEditActive}; Regional={TerrainAuthoringPreviewService.InteractiveRegionalElevationEditActive}");
            EditorGUILayout.LabelField("Deferred Restart", TerrainAuthoringPreviewService.StreamingRestartDeferredForInteractiveEdit ? "Yes" : "No");
        }
        EditorGUI.BeginDisabledGroup(!enabled || EditorApplication.isPlayingOrWillChangePlaymode);
        if (GUILayout.Button("Rebuild Committed Preview", GUILayout.ExpandWidth(true)))
            TerrainAuthoringPreviewService.ForceCommittedRebuildNow();
        EditorGUI.EndDisabledGroup();
        if (snapshot.HasFailedDirtyUpdates || snapshot.BoundsFollowUpPending || snapshot.AnalysisFollowUpPending
            || !string.IsNullOrEmpty(snapshot.LastFollowUpError))
        {
            EditorGUI.BeginDisabledGroup(!snapshot.Enabled || !snapshot.PreviewWorkAllowed);
            if (GUILayout.Button("Retry Height Updates", GUILayout.ExpandWidth(true)))
                TerrainAuthoringPreviewService.RetryFailedDirtyUpdates();
            EditorGUI.EndDisabledGroup();
        }
        EditorGUILayout.HelpBox("The published display set stays bound while a complete replacement is prepared. Source loads count geographic tiles; composition counts representation pages. Dirty updates prepare one scratch candidate per callback, then capture recovery and publish with two copies (three if restoration is required). Work caps apply to the whole editor callback. Height memory is an RFloat array payload estimate, excluding compositor buffers, committed texture assets and driver overhead.", MessageType.Info);
        GUILayout.EndVertical();
    }



    private static string FormatPreviewMemory(
        long byteCount
    )
    {
        if (byteCount <= 0L)
        {
            return
                "0 MiB";
        }

        double mebibytes =
            byteCount
            /
            (1024.0 * 1024.0);

        return
            $"{mebibytes:F1} MiB";
    }
    private bool showHeightPreviewLods;
    private bool showHeightPreviewAnalysis;
    private readonly bool[] showHeightPreviewLodRows = new bool[TerrainClipmapTopologyUtility.MaximumLevelCount];

    private void DrawHeightPreviewDiagnostics(TerrainAuthoringPreviewDiagnosticsSnapshot snapshot)
    {
        EditorGUILayout.LabelField("Status", snapshot.PreviewStatus.ToString());
        EditorGUILayout.LabelField("Published Display", $"Drawable={snapshot.Drawable}; Content Current={snapshot.CacheReady}");
        EditorGUILayout.LabelField("Latest Paired Intent", $"Coverage={snapshot.LatestCoverageCurrent}; Placement={snapshot.PlacementCurrent}; Ready={snapshot.ReadyForLatestIntent}");
        EditorGUILayout.LabelField("Authoring / Request Generation", $"{snapshot.AuthoringGeneration} / {snapshot.StreamingRequestGeneration}");
        if (!string.IsNullOrEmpty(snapshot.PreviewStatusMessage))
            EditorGUILayout.HelpBox(snapshot.PreviewStatusMessage, snapshot.PreviewStatus == TerrainAuthoringPreviewStatus.Error ? MessageType.Error : MessageType.Info);
        EditorGUILayout.LabelField("Streaming State", snapshot.StreamingState.ToString());
        DrawHeightWorker("Running", snapshot.Worker);
        DrawHeightWorker("Queued", snapshot.QueuedWorker);
        EditorGUILayout.LabelField("Staging Caps: Allocate / Load / Copy / Materialize / Compose",
            $"1 / {TerrainAuthoringPreviewService.StreamingCommittedLoadsPerUpdate} / {TerrainAuthoringPreviewService.StreamingRetainedCopiesPerUpdate} / {TerrainAuthoringPreviewService.StreamingMaterializationsPerUpdate} / {TerrainAuthoringPreviewService.StreamingCompositionsPerUpdate}");
        EditorGUILayout.LabelField("Dirty Caps: Allocate / Load / Copy / Materialize / Compose",
            $"1 / {TerrainAuthoringPreviewService.StreamingCommittedLoadsPerUpdate} / {TerrainAuthoringPreviewService.StreamingRetainedCopiesPerUpdate} / {TerrainAuthoringPreviewService.StreamingMaterializationsPerUpdate} / {TerrainAuthoringPreviewService.DirtyCompositionsPerUpdate}");
        EditorGUILayout.LabelField("Soft Callback Budget", $"{TerrainAuthoringPreviewService.StreamingSoftWorkBudgetMilliseconds:R} ms");
        EditorGUILayout.LabelField("Authoring Convergence", snapshot.AuthoringConvergencePending ? "Pending" : "Complete");
        EditorGUILayout.LabelField("Pending Dirty: Resident Geographic / Representation Jobs",
            $"{snapshot.PendingGeographicDirtyCount:N0} / {snapshot.PendingRepresentationCount:N0}");
        EditorGUILayout.LabelField("Incoming Scope / Geographic Tiles",
            $"{snapshot.UnprojectedScopePending} / {snapshot.UnprojectedGeographicDirtyCount:N0}");
        var held = snapshot.HeldDirtySource;
        EditorGUILayout.LabelField("Held Committed Source", held.Present
            ? $"Tile {held.Tile}; stride {held.SourceStride}; texture {held.TextureId}; {(held.Owned ? "Owned derived" : "Borrowed native")}" : "None");
        if (held.Present)
        {
            EditorGUILayout.LabelField("Held Source Committed Identity", held.CommittedSignature);
            EditorGUILayout.LabelField("Held Source Payload Estimate", FormatPreviewMemory(held.ApproximatePayloadBytes));
        }
        var sources = snapshot.SourceCache;
        EditorGUILayout.LabelField("Committed Sources: Native Loads / Derived Hits / Generated",
            $"{sources.NativeLoads:N0} / {sources.CacheHits:N0} / {sources.GeneratedEntries:N0}");
        EditorGUILayout.LabelField("Derived Cache: Native Fallbacks / Rejected / Read Failures / Write Failures",
            $"{sources.NativeFallbacks:N0} / {sources.RejectedEntries:N0} / {sources.ReadFailures:N0} / {sources.WriteFailures:N0}");
        EditorGUILayout.LabelField("Source Counter Lifetime", "Cumulative for this Editor domain; includes native analysis.");
        if (!string.IsNullOrEmpty(sources.Warning)) EditorGUILayout.HelpBox(sources.Warning, MessageType.Warning);
        EditorGUILayout.LabelField("Last Dirty Callback: Allocate / Load / All Copies / Materialize / Compose",
            $"{snapshot.LastDirtyAllocations} / {snapshot.LastDirtyLoads} / {snapshot.LastDirtyCopies} / {snapshot.LastDirtyMaterializations} / {snapshot.LastDirtyCompositions}");
        EditorGUILayout.LabelField("Dirty Copies", "Includes native extraction, live backup/publication and recovery attempts.");
        if (snapshot.HasFailedDirtyUpdates)
        {
            var recent = snapshot.MostRecentDirtyFailure;
            EditorGUILayout.HelpBox($"{snapshot.FailedRepresentationCount} Height updates are suppressed pending a relevant edit or retry. "
                + $"Most recent: LOD {recent.Level}, tile {recent.Tile}, target {recent.AttemptedGeneration}. "
                + (recent.LastGoodAvailable ? "Last-good terrain is retained. " : "Unsafe storage requires replacement. ")
                + recent.Message, MessageType.Warning);
        }
        if (snapshot.BoundsFollowUpPending || snapshot.AnalysisFollowUpPending)
            EditorGUILayout.LabelField("Pending Follow-ups", $"Bounds={snapshot.BoundsFollowUpPending}; Analysis={snapshot.AnalysisFollowUpPending}");
        if (!string.IsNullOrEmpty(snapshot.BoundsFollowUpError)) EditorGUILayout.HelpBox("Bounds repair: " + snapshot.BoundsFollowUpError, MessageType.Warning);
        if (!string.IsNullOrEmpty(snapshot.AnalysisFollowUpError)) EditorGUILayout.HelpBox("Analysis follow-up: " + snapshot.AnalysisFollowUpError, MessageType.Warning);
        if (!string.IsNullOrEmpty(snapshot.LastFollowUpError)) EditorGUILayout.HelpBox("Last follow-up error: " + snapshot.LastFollowUpError, MessageType.Warning);
        if (!string.IsNullOrEmpty(snapshot.StreamingStatusMessage)) EditorGUILayout.HelpBox(snapshot.StreamingStatusMessage, snapshot.StreamingState == TerrainAuthoringPreviewStreamingState.Failed ? MessageType.Error : MessageType.Info);
        if (!string.IsNullOrEmpty(snapshot.CancellationReason)) EditorGUILayout.LabelField("Last Cancellation", snapshot.CancellationReason);
        var memory = snapshot.Ownership;
        EditorGUILayout.LabelField("Display Active Height Payload", FormatPreviewMemory(memory.DisplayActiveBytes));
        EditorGUILayout.LabelField("Analysis Active Height Payload", FormatPreviewMemory(memory.AnalysisActiveBytes));
        EditorGUILayout.LabelField("Display / Analysis Staging Payload", $"{FormatPreviewMemory(memory.DisplayStagingBytes)} / {FormatPreviewMemory(memory.AnalysisStagingBytes)}");
        EditorGUILayout.LabelField("Retiring Display / Analysis Payload", $"{FormatPreviewMemory(memory.RetiringDisplayBytes)} / {FormatPreviewMemory(memory.RetiringAnalysisBytes)}");
        EditorGUILayout.LabelField("Dirty Scratch Payload / Arrays", $"{FormatPreviewMemory(memory.DirtyScratchBytes)} / {memory.DirtyScratchArrayCount}");
        EditorGUILayout.LabelField("Total / Transition Peak Height Payload", $"{FormatPreviewMemory(memory.TotalBytes)} / {FormatPreviewMemory(snapshot.PeakTransitionGpuMemoryBytes)}");
        EditorGUILayout.LabelField("Service Owned Cache Objects / Arrays", $"{memory.OwnedCacheCount} / {memory.AllocatedArrayCount}");
        EditorGUILayout.LabelField("Editor Domain: Created / Disposed / Live", $"{snapshot.CacheCreateCount} / {snapshot.CacheDisposeCount} / {snapshot.CacheLiveCount}");
        EditorGUILayout.LabelField("Aggregate Residency Size Health", TerrainAuthoringPreviewService.AggregateSizeHealth(snapshot.DisplayLods).ToString());
        showHeightPreviewLods = EditorGUILayout.Foldout(showHeightPreviewLods, $"Display LODs ({snapshot.DisplayLods.Count})", true);
        if (showHeightPreviewLods)
        {
            EditorGUI.indentLevel++;
            foreach (var row in snapshot.DisplayLods)
            {
                showHeightPreviewLodRows[row.Level] = EditorGUILayout.Foldout(showHeightPreviewLodRows[row.Level],
                    $"LOD {row.Level}: stride {row.Active.Representation.Stride}; current={row.Active.Current}; dirty={row.PendingDirtyCount}; {row.SizeHealth}; active/staging {FormatPreviewMemory(row.Active.GpuBytes)}/{FormatPreviewMemory(row.Staging.GpuBytes)}", true);
                if (!showHeightPreviewLodRows[row.Level]) continue;
                EditorGUILayout.LabelField("Unsafe Live Storage", row.WriteFailed ? "Yes" : "No");
                EditorGUILayout.LabelField("Suppressed Dirty / Scratch", $"{row.FailedDirtyCount} / {FormatPreviewMemory(row.DirtyScratchBytes)}");
                if (row.DirtyFailure.Present)
                {
                    var failure = row.DirtyFailure;
                    EditorGUILayout.HelpBox($"Tile {failure.Tile}, attempted generation {failure.AttemptedGeneration}; last-good available={failure.LastGoodAvailable}. {failure.Message}",
                        failure.LastGoodAvailable ? MessageType.Warning : MessageType.Error);
                }
                DrawHeightAllocation("Published", row.Active);
                if (row.Active.Present) EditorGUILayout.LabelField("Published Required", row.PublishedRequiredWindow.ToString());
                if (row.HasLatestPlan)
                {
                    EditorGUILayout.LabelField("Latest Representation", FormatHeightRepresentation(row.PlannedRepresentation));
                    EditorGUILayout.LabelField("Latest Required", row.RequiredWindow.ToString());
                    EditorGUILayout.LabelField("Latest Guarded / Desired", $"{row.GuardedWindow} / {row.DesiredWindow}");
                }
                if (row.WorkerRepresentation.IsValid)
                {
                    EditorGUILayout.LabelField("Running Representation", FormatHeightRepresentation(row.WorkerRepresentation));
                    EditorGUILayout.LabelField("Running Required / Frozen Target", $"{row.WorkerRequiredWindow} / {row.WorkerTargetWindow}");
                }
                DrawHeightAllocation("Display Staging", row.Staging);
                if (row.QueuedRepresentation.IsValid)
                    EditorGUILayout.LabelField("Queued Target", $"{FormatHeightRepresentation(row.QueuedRepresentation)}; {row.QueuedTargetWindow}");
                EditorGUILayout.LabelField("Published / Request Generation", $"{row.PublishedGeneration} / {row.RequestGeneration}");
                EditorGUILayout.LabelField("Phase / Retained / Reusable / Entering / Leaving", $"{row.Phase} / {row.RetainedCount} / {row.ReusableCount} / {row.EnteringCount} / {row.LeavingCount}");
                EditorGUILayout.LabelField("Copied / Materialized / Composed / Remaining", $"{row.CopiedCount} / {row.MaterializedCount} / {row.ComposedCount} / {row.CompositionRemainingCount}");
            }
            EditorGUI.indentLevel--;
        }
        showHeightPreviewAnalysis = EditorGUILayout.Foldout(showHeightPreviewAnalysis, $"Native Analysis: {snapshot.Analysis.Kind}; Ready={snapshot.Analysis.Ready}", true);
        if (showHeightPreviewAnalysis)
        {
            EditorGUI.indentLevel++;
            var analysis = snapshot.Analysis;
            EditorGUILayout.LabelField("Native Samples / Spacing", $"{analysis.SamplesPerSide} / {analysis.SampleSpacing:R}");
            EditorGUILayout.LabelField("Derived Output", analysis.OutputWindow.ToString());
            EditorGUILayout.LabelField("Source Dependency / Guard Tiles", $"{analysis.RequiredSourceWindow} / {analysis.GuardTileCount}");
            EditorGUILayout.LabelField("Selected Physical Storage", analysis.PhysicalWindow.ToString());
            EditorGUILayout.LabelField("Whole Cache Acknowledgement / Authoring Target", $"{analysis.AuthoringGeneration} / {analysis.TargetAuthoringGeneration}");
            EditorGUILayout.LabelField("Ownership / Residency / Composite", $"{analysis.OwnershipGeneration} / {analysis.ResidencyGeneration} / {analysis.CompositeGeneration}");
            EditorGUILayout.LabelField("Native Waiting Reason", analysis.WaitingReason.ToString());
            EditorGUILayout.LabelField("Required Tiles: Pending / Failed / Publication",
                $"{analysis.PendingRequiredTileCount} / {analysis.FailedRequiredTileCount} / {analysis.PendingPublicationTileCount}");
            DrawHeightAllocation("Owned Native Active", snapshot.AnalysisActive);
            DrawHeightAllocation("Native Staging", snapshot.AnalysisStaging);
            if (!string.IsNullOrEmpty(analysis.Message)) EditorGUILayout.HelpBox(analysis.Message, analysis.Failed ? MessageType.Error : MessageType.Info);
            EditorGUI.indentLevel--;
        }
        DrawHeightFailure("Display Failure", snapshot.DisplayFailure);
        DrawHeightFailure("Native Analysis Failure", snapshot.AnalysisFailure);
    }

    private static string FormatHeightRepresentation(TerrainAuthoringPreviewRepresentationSnapshot representation) =>
        representation.IsValid ? $"Stride {representation.Stride}; {representation.SamplesPerSide} samples; spacing {representation.SampleSpacing:R}" : "Unplanned";

    private static void DrawHeightAllocation(string label, TerrainAuthoringPreviewCacheSnapshot allocation)
    {
        EditorGUILayout.LabelField(label, !allocation.Present ? "None" : !allocation.HasTexture ? "Unallocated" :
            $"{FormatHeightRepresentation(allocation.Representation)}; {allocation.Window}; {allocation.PageCount} pages; texture {allocation.TextureId}; {FormatPreviewMemory(allocation.GpuBytes)}; complete={allocation.Complete}");
    }

    private static void DrawHeightWorker(string label, TerrainAuthoringPreviewWorkerSnapshot worker)
    {
        EditorGUILayout.LabelField(label + " Worker", worker.Present ? $"{worker.Purpose}; {worker.Phase}; {worker.Progress:P1}" : "Idle");
        if (!worker.Present) return;
        EditorGUILayout.LabelField(label + " Generations: Request / Authoring / Ownership", $"{worker.RequestGeneration} / {worker.AuthoringGeneration} / {worker.OwnershipGeneration}");
        EditorGUILayout.LabelField(label + " Geographic Sources Loaded", $"{worker.LoadedGroupCount:N0} / {worker.SourceGroupCount:N0}");
        EditorGUILayout.LabelField(label + " Source Acquisitions", $"{worker.SourceAcquisitionCount:N0}");
        EditorGUILayout.LabelField(label + " Representation Pages Materialized / Composed", $"{worker.MaterializedCount:N0} / {worker.ComposedCount:N0} of {worker.RepresentationCount:N0}");
        EditorGUILayout.LabelField(label + " Retained Copies", $"{worker.CopiedCount:N0} / {worker.ReusableCount:N0}");
        EditorGUILayout.LabelField(label + " Last Callback: Allocate / Load / Copy / Materialize / Compose", $"{worker.LastAllocations} / {worker.LastLoads} / {worker.LastCopies} / {worker.LastMaterializations} / {worker.LastCompositions}");
    }

    private static void DrawHeightFailure(string label, TerrainAuthoringPreviewFailureSnapshot failure)
    {
        if (!failure.Present) return;
        string scope = failure.Level >= 0 ? $"LOD {failure.Level}; {failure.Window}" : "Whole set";
        if (failure.HasTile) scope += $"; tile {failure.Tile}";
        EditorGUILayout.HelpBox($"{label}: {failure.Purpose}; {scope}; request {failure.RequestGeneration}, placement {failure.PlacementGeneration}. {failure.Message}", MessageType.Error);
    }

}


