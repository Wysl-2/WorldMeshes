using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showCapacityDiagnostics;

    [SerializeField]
    private bool showRuntimeTerrainCapacity;

    [SerializeField]
    private bool showAddressablesCapacity;

    [SerializeField]
    [Min(0f)]
    private float runtimeTerrainResidencyBudgetMiB;

    [SerializeField]
    [Min(0f)]
    private float runtimeSurfaceResidencyBudgetMiB;

    private void DrawCapacityDiagnostics()
    {
        showCapacityDiagnostics =
            EditorGUILayout.Foldout(
                showCapacityDiagnostics,
                "Capacity",
                true
            );

        if (!showCapacityDiagnostics)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawRuntimeTerrainCapacity();

        DrawWorkspaceSectionGap();

        DrawAddressablesCapacity();
    }

    private void DrawRuntimeTerrainCapacity()
    {
        showRuntimeTerrainCapacity =
            EditorGUILayout.Foldout(
                showRuntimeTerrainCapacity,
                "Runtime Terrain",
                true
            );

        if (!showRuntimeTerrainCapacity)
        {
            return;
        }

        GUILayout.Space(5f);

        TerrainHeightmapStreamer streamer =
            FindRuntimeHeightmapStreamer();

        if (streamer == null)
        {
            EditorGUILayout.HelpBox(
                "Runtime terrain capacity diagnostics require the Height streamer on WorldRoot/Clipmap.",
                MessageType.None
            );

            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        EditorGUILayout.HelpBox(
            "These are deterministic raw/logical texture payload estimates. " +
            "They do not include graphics-driver allocation overhead, " +
            "Addressables bundle residency, collision memory, or unrelated " +
            "scene resources.",
            MessageType.Info
        );

        runtimeTerrainResidencyBudgetMiB =
            SanitizeRuntimeResidencyBudgetMiB(
                EditorGUILayout.FloatField(
                    "Target Terrain Budget (MiB)",
                    runtimeTerrainResidencyBudgetMiB
                )
            );

        runtimeSurfaceResidencyBudgetMiB =
            SanitizeRuntimeResidencyBudgetMiB(
                EditorGUILayout.FloatField(
                    "Optional Surface Budget (MiB)",
                    runtimeSurfaceResidencyBudgetMiB
                )
            );

        if (
            !streamer.TryGetRuntimeResidencyDiagnostics(
                out TerrainRuntimeResidencyDiagnosticsSnapshot snapshot,
                out string reason
            )
        )
        {
            EditorGUILayout.HelpBox(
                string.IsNullOrEmpty(reason)
                    ? "Runtime residency diagnostics are unavailable."
                    : reason,
                MessageType.Warning
            );

            GUILayout.EndVertical();
            return;
        }

        TerrainRuntimeCapacityResult capacity =
            TerrainRuntimeCapacityUtility.Evaluate(
                snapshot,
                MiBToBytes(
                    runtimeTerrainResidencyBudgetMiB
                ),
                MiBToBytes(
                    runtimeSurfaceResidencyBudgetMiB
                )
            );

        GUILayout.Space(5f);
        DrawRuntimeHeightResidency(
            snapshot,
            capacity
        );

        GUILayout.Space(5f);
        DrawRuntimeLegacyHeightResidency(
            capacity
        );

        GUILayout.Space(5f);
        DrawRuntimeSurfaceResidency(
            snapshot,
            capacity
        );

        GUILayout.Space(5f);
        DrawRuntimeTotalResidency(
            capacity
        );

        GUILayout.Space(6f);

        if (
            GUILayout.Button(
                "Log Capacity Analysis",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            Debug.Log(
                capacity.BuildDiagnosticReport()
            );
        }

        GUILayout.EndVertical();
    }

    private void DrawAddressablesCapacity()
    {
        showAddressablesCapacity =
            EditorGUILayout.Foldout(
                showAddressablesCapacity,
                "Addressables",
                true
            );

        if (!showAddressablesCapacity)
        {
            return;
        }

        GUILayout.Space(5f);

        DrawRuntimeAddressablesDiagnostics();
    }

    private void DrawRuntimeHeightResidency(
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot,
        TerrainRuntimeCapacityResult capacity
    )
    {
        GUILayout.Label(
            "Height Residency",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "LOD States",
            snapshot.HeightLodCount.ToString()
        );

        DrawRuntimeResidencyBytes(
            "Active GPU Cache",
            capacity.HeightActiveGpuBytes
        );

        DrawRuntimeResidencyBytes(
            "Staging GPU Cache",
            capacity.HeightStagingGpuBytes
        );

        DrawRuntimeResidencyBytes(
            "Current Source",
            capacity.HeightCurrentSourceBytes
        );

        DrawRuntimeResidencyBytes(
            "Observed Source Peak",
            capacity.HeightObservedPeakSourceBytes
        );

        DrawRuntimeResidencyBytes(
            "Source Upper Bound",
            capacity.HeightSourceUpperBoundBytes
        );

        DrawRuntimeResidencyBytes(
            "Current Logical Residency",
            capacity.HeightCurrentLogicalBytes
        );

        DrawRuntimeResidencyBytes(
            "Observed Peak Logical Residency",
            capacity.HeightObservedPeakLogicalBytes
        );

        DrawRuntimeResidencyBytes(
            "Conservative Upper Bound",
            capacity.HeightConservativeUpperBoundBytes
        );

        DrawRuntimeCapacityTarget(
            "Target Budget",
            capacity.TerrainBudgetConfigured,
            capacity.TerrainBudgetBytes
        );

        DrawRuntimeCapacityHeadroom(
            "Headroom / Excess",
            capacity.TerrainBudgetConfigured,
            capacity.HeightBudgetHeadroomBytes
        );

        DrawRuntimeCapacityStatus(
            "Height Target",
            capacity.HeightStatus
        );
    }

    private void DrawRuntimeLegacyHeightResidency(
        TerrainRuntimeCapacityResult capacity
    )
    {
        GUILayout.Label(
            "Legacy Height Baseline",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Native Cache Grid",
            $"{capacity.LegacyHeightCacheWidth} x " +
            $"{capacity.LegacyHeightCacheHeight}"
        );

        DrawRuntimeResidencyBytes(
            "GPU Cache Payload",
            capacity.LegacyHeightGpuBytes
        );

        DrawRuntimeResidencyBytes(
            "Steady Source",
            capacity.LegacyHeightSteadySourceBytes
        );

        DrawRuntimeResidencyBytes(
            "Transition Source Upper Bound",
            capacity.LegacyHeightTransitionSourceUpperBoundBytes
        );

        DrawRuntimeResidencyBytes(
            "Conservative Upper Bound",
            capacity.LegacyHeightConservativeUpperBoundBytes
        );

        EditorGUILayout.LabelField(
            "GPU Cache Reduction",
            capacity.HeightGpuSavingsPercent.ToString("N2") + "%"
        );

        EditorGUILayout.LabelField(
            "Conservative Reduction",
            capacity.HeightUpperBoundSavingsPercent.ToString("N2") + "%"
        );
    }

    private void DrawRuntimeSurfaceResidency(
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot,
        TerrainRuntimeCapacityResult capacity
    )
    {
        GUILayout.Label(
            "Surface Residency",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Cache Ready",
            snapshot.Surface.CacheReady
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Transition State",
            snapshot.Surface.TransitionState.ToString()
        );

        EditorGUILayout.LabelField(
            "Cache Grid",
            $"{snapshot.Surface.CacheWidth} x " +
            $"{snapshot.Surface.CacheHeight}"
        );

        DrawRuntimeResidencyBytes(
            "Active GPU Cache",
            capacity.SurfaceActiveGpuBytes
        );

        DrawRuntimeResidencyBytes(
            "Staging GPU Cache",
            capacity.SurfaceStagingGpuBytes
        );

        EditorGUILayout.LabelField(
            "Resident Source Pages",
            capacity.SurfaceResidentSourceCount.ToString("N0")
        );

        DrawRuntimeResidencyBytes(
            "Current Source",
            capacity.SurfaceCurrentSourceBytes
        );

        DrawRuntimeResidencyBytes(
            "Source Upper Bound",
            capacity.SurfaceSourceUpperBoundBytes
        );

        DrawRuntimeResidencyBytes(
            "Current Logical Residency",
            capacity.SurfaceCurrentLogicalBytes
        );

        DrawRuntimeResidencyBytes(
            "Conservative Upper Bound",
            capacity.SurfaceConservativeUpperBoundBytes
        );

        DrawRuntimeCapacityTarget(
            "Surface Target",
            capacity.SurfaceBudgetConfigured,
            capacity.SurfaceBudgetBytes
        );

        DrawRuntimeCapacityHeadroom(
            "Headroom / Excess",
            capacity.SurfaceBudgetConfigured,
            capacity.SurfaceBudgetHeadroomBytes
        );

        DrawRuntimeCapacityStatus(
            "Surface Target Status",
            capacity.SurfaceStatus
        );
    }

    private void DrawRuntimeTotalResidency(
        TerrainRuntimeCapacityResult capacity
    )
    {
        GUILayout.Label(
            "Terrain Capacity",
            EditorStyles.boldLabel
        );

        DrawRuntimeResidencyBytes(
            "Current",
            capacity.CurrentTerrainBytes
        );

        DrawRuntimeResidencyBytes(
            "Conservative Upper Bound",
            capacity.ConservativeTerrainUpperBoundBytes
        );

        DrawRuntimeCapacityTarget(
            "Target",
            capacity.TerrainBudgetConfigured,
            capacity.TerrainBudgetBytes
        );

        DrawRuntimeCapacityHeadroom(
            "Headroom / Excess",
            capacity.TerrainBudgetConfigured,
            capacity.TerrainBudgetHeadroomBytes
        );

        DrawRuntimeCapacityStatus(
            "Combined Terrain",
            capacity.TerrainStatus
        );

        EditorGUILayout.LabelField(
            "Surface Share",
            capacity.SurfaceSharePercent.ToString("N2") + "%"
        );

        EditorGUILayout.LabelField(
            "Dominant Component",
            capacity.SurfaceIsDominant
                ? "Surface"
                : "Height"
        );

        string recommendation =
            TerrainRuntimeCapacityResult
                .FormatRecommendation(
                    capacity.SurfaceScalingRecommendation
                );

        EditorGUILayout.LabelField(
            "Surface Scaling",
            recommendation
        );

        if (!string.IsNullOrEmpty(capacity.SurfaceScalingReason))
        {
            EditorGUILayout.HelpBox(
                capacity.SurfaceScalingReason,
                capacity.SurfaceScalingRecommendation ==
                    TerrainSurfaceScalingRecommendation
                        .RecommendedForTarget
                    ? MessageType.Warning
                    : MessageType.None
            );
        }
    }

    private static void DrawRuntimeCapacityTarget(
        string label,
        bool configured,
        long bytes
    )
    {
        EditorGUILayout.LabelField(
            label,
            configured
                ? FormatDiagnosticsBytes(bytes)
                : "Not Configured"
        );
    }

    private static void DrawRuntimeCapacityHeadroom(
        string label,
        bool configured,
        long bytes
    )
    {
        EditorGUILayout.LabelField(
            label,
            configured
                ? FormatSignedRuntimeResidencyBytes(bytes)
                : "Not Configured"
        );
    }

    private static void DrawRuntimeCapacityStatus(
        string label,
        TerrainRuntimeCapacityStatus status
    )
    {
        string statusText =
            TerrainRuntimeCapacityResult.FormatStatus(
                status
            );

        EditorGUILayout.LabelField(
            label,
            statusText
        );

        if (
            status ==
                TerrainRuntimeCapacityStatus.WithinBudget
        )
        {
            EditorGUILayout.HelpBox(
                label + " is within the configured resource target.",
                MessageType.Info
            );
        }
        else if (
            status ==
                TerrainRuntimeCapacityStatus.ExceedsBudget
        )
        {
            EditorGUILayout.HelpBox(
                label + " exceeds the configured resource target.",
                MessageType.Warning
            );
        }
    }

    private static void DrawRuntimeResidencyBytes(
        string label,
        long bytes
    )
    {
        EditorGUILayout.LabelField(
            label,
            FormatDiagnosticsBytes(bytes)
        );
    }

    private static float SanitizeRuntimeResidencyBudgetMiB(
        float mib
    )
    {
        if (
            float.IsNaN(mib)
            || float.IsInfinity(mib)
            || mib <= 0f
        )
        {
            return 0f;
        }

        return mib;
    }

    private static long MiBToBytes(
        float mib
    )
    {
        mib =
            SanitizeRuntimeResidencyBudgetMiB(
                mib
            );

        if (mib <= 0f)
        {
            return 0L;
        }

        double bytes =
            mib *
            1024d *
            1024d;

        if (bytes >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return
            (long)System.Math.Round(
                bytes
            );
    }

    private static string FormatSignedRuntimeResidencyBytes(
        long bytes
    )
    {
        string sign =
            bytes > 0L
                ? "+"
                : "";

        return
            sign +
            (
                bytes /
                (1024d * 1024d)
            ).ToString("N2") +
            " MiB";
    }

    // =====================================================
    // ADDRESSABLES CAPACITY
    // =====================================================

    private TerrainRuntimeAddressablesValidationResult
        lastRuntimeAddressablesValidationResult;

    private void DrawRuntimeAddressablesDiagnostics()
    {
        TerrainRuntimeBakePlan plan =
            GetRuntimeBakeDiagnosticsPlan();

        TerrainRuntimeBakeStateSummary snapshot =
            GetRuntimeBakeDiagnosticsSummary();

        TerrainRuntimeAddressablesOperationMode operationMode =
            TerrainRuntimeAddressablesUtility.GetOperationMode(
                plan
            );

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Addressables Pipeline",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Addressables Configuration Dirty",
            snapshot.AddressablesConfigurationDirty
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Addressables Content Dirty",
            snapshot.AddressablesContentDirty
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Plan Configuration Required",
            plan.AddressablesConfigurationRequired
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Plan Content Required",
            plan.AddressablesContentBuildRequired
                ? "Yes"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Planned Operation",
            GetRuntimeAddressablesOperationLabel(
                operationMode
            )
        );

        GUILayout.Space(4f);

        EditorGUILayout.LabelField(
            "Height Addressables Configuration",
            GetRuntimeAddressablesValidationLabel(
                lastRuntimeAddressablesValidationResult,
                0
            )
        );

        EditorGUILayout.LabelField(
            "Surface Addressables Configuration",
            GetRuntimeAddressablesValidationLabel(
                lastRuntimeAddressablesValidationResult,
                1
            )
        );

        EditorGUILayout.LabelField(
            "Collision Addressables Configuration",
            GetRuntimeAddressablesValidationLabel(
                lastRuntimeAddressablesValidationResult,
                2
            )
        );

        EditorGUILayout.LabelField(
            "Collision Marker Structure",
            GetRuntimeAddressablesValidationLabel(
                lastRuntimeAddressablesValidationResult,
                3
            )
        );

        EditorGUILayout.LabelField(
            "Collision Prepared Manifest",
            GetCollisionPreparedManifestStatusLabel()
        );

        GUILayout.Space(5f);
        GUILayout.Label(
            "Height Addressables Scale",
            EditorStyles.boldLabel
        );

        TerrainHeightAddressablesScaleReport scaleReport =
            TerrainHeightAddressablesScaleUtility.CreateReport(
                null,
                "",
                0d
            );

        EditorGUILayout.LabelField(
            "Geographic Height Pages",
            scaleReport.GeographicTileCount.ToString("N0")
        );
        EditorGUILayout.LabelField(
            "Height Representations",
            scaleReport.RepresentationLevelCount.ToString("N0")
        );
        EditorGUILayout.LabelField(
            "Derived Height Assets",
            scaleReport.DerivedAssetCount.ToString("N0")
        );
        EditorGUILayout.LabelField(
            "Height Entries Expected / Actual",
            scaleReport.ExpectedHeightEntryCount.ToString("N0") +
            " / " +
            scaleReport.ActualHeightEntryCount.ToString("N0")
        );
        EditorGUILayout.LabelField(
            "Packing Regions",
            scaleReport.PackingRegionCount.ToString("N0")
        );
        EditorGUILayout.LabelField(
            "Expected Height Bundles",
            scaleReport.ExpectedHeightBundleCount.ToString("N0")
        );

        TerrainRuntimeAddressablesResult lastAddressablesResult =
            TerrainRuntimeBakePipeline.LastResult != null
                ? TerrainRuntimeBakePipeline.LastResult.AddressablesResult
                : null;

        TerrainHeightAddressablesScaleReport lastScale =
            lastAddressablesResult != null
                ? lastAddressablesResult.HeightScaleReport
                : null;

        if (lastScale != null)
        {
            EditorGUILayout.LabelField(
                "Last Built Bundle Files",
                lastScale.BuiltBundleFileCount >= 0
                    ? lastScale.BuiltBundleFileCount.ToString("N0")
                    : "Unavailable"
            );
            EditorGUILayout.LabelField(
                "Last Catalog Payload Bytes",
                lastScale.CatalogPayloadBytes >= 0L
                    ? lastScale.CatalogPayloadBytes.ToString("N0")
                    : "Unavailable"
            );
            EditorGUILayout.LabelField(
                "Height Reconciliation",
                lastScale.HeightConfigurationReconciliationSeconds.ToString("0.00") + " s"
            );
            EditorGUILayout.LabelField(
                "Addressables Build",
                lastScale.AddressablesBuildSeconds.ToString("0.00") + " s"
            );
        }

        if (plan.IsBlocked)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                plan.BlockReason,
                MessageType.Warning
            );
        }

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Validate Existing Addressables Configuration",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            lastRuntimeAddressablesValidationResult =
                TerrainRuntimeAddressablesUtility
                    .ValidateExistingRuntimeConfiguration(
                        worldSettings
                    );

            string report =
                lastRuntimeAddressablesValidationResult
                    .BuildDiagnosticReport();

            if (
                lastRuntimeAddressablesValidationResult
                    .IsValid
            )
            {
                Debug.Log(
                    report
                );
            }
            else
            {
                Debug.LogWarning(
                    report
                );
            }

            Repaint();
        }

        EditorGUILayout.HelpBox(
            "Read-only runtime Addressables structural diagnostics. " +
            "Use Runtime > Bake Runtime Changes for normal Addressables work. " +
            "Use Advanced Runtime Tools for explicit Addressables maintenance.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private static string GetRuntimeAddressablesOperationLabel(
        TerrainRuntimeAddressablesOperationMode mode
    )
    {
        switch (mode)
        {
            case TerrainRuntimeAddressablesOperationMode.ContentOnly:
                return "Content Only";

            case TerrainRuntimeAddressablesOperationMode.ConfigureAndBuild:
                return "Configure + Build";

            default:
                return "None";
        }
    }

    private static string GetRuntimeAddressablesValidationLabel(
        TerrainRuntimeAddressablesValidationResult validation,
        int channel
    )
    {
        if (validation == null)
        {
            return "Not Checked";
        }

        bool valid;

        switch (channel)
        {
            case 0:
                valid =
                    validation.HeightConfigurationValid;
                break;

            case 1:
                valid =
                    validation.SurfaceConfigurationValid;
                break;

            case 2:
                valid =
                    validation.CollisionConfigurationValid;
                break;

            default:
                valid =
                    validation.CollisionMarkerStructureValid;
                break;
        }

        return
            valid
                ? "Valid"
                : "Needs Repair";
    }

    private string GetCollisionPreparedManifestStatusLabel()
    {
        TerrainCollisionManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainCollisionManifest>(
                WorldMeshesPaths.CollisionManifestAssetPath
            );

        if (manifest == null)
        {
            return "Missing";
        }

        if (!manifest.isComplete)
        {
            return "Incomplete";
        }

        if (
            !TerrainCollisionAddressablesUtility
                .ValidatePreparedManifestStructure(
                    worldSettings,
                    out _
                )
        )
        {
            return "Needs Repair";
        }

        if (
            worldSettings != null
            &&
            manifest.collisionMeshGenerationRevision
                == worldSettings.collisionMeshGenerationRevision
            &&
            manifest.collisionSourceHeightmapGenerationRevision
                == worldSettings.collisionSourceHeightmapGenerationRevision
        )
        {
            return "Current";
        }

        return "Out Of Date";
    }
}
