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

        EditorGUI.BeginChangeCheck();

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

        if (EditorGUI.EndChangeCheck())
        {
            lastRuntimeResidencyBudgetResult =
                null;

            lastRuntimeResidencyConfiguration =
                null;

            lastRuntimeFinalCertificationReport =
                null;
        }

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

        GUILayout.Space(5f);
        DrawRuntimeHeightResidency(snapshot);

        GUILayout.Space(5f);
        DrawRuntimeLegacyHeightResidency(snapshot);

        GUILayout.Space(5f);
        DrawRuntimeSurfaceResidency(snapshot);

        GUILayout.Space(5f);
        DrawRuntimeTotalResidency(snapshot);

        DrawRuntimeResidencyBudgetEvaluation(
            streamer,
            snapshot
        );

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
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot
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
            snapshot.HeightActiveGpuBytes
        );

        DrawRuntimeResidencyBytes(
            "Staging GPU Cache",
            snapshot.HeightStagingGpuBytes
        );

        DrawRuntimeResidencyBytes(
            "Current Source",
            snapshot.HeightCurrentSourceBytes
        );

        DrawRuntimeResidencyBytes(
            "Observed Source Peak",
            snapshot.HeightObservedPeakSourceBytes
        );

        DrawRuntimeResidencyBytes(
            "Source Upper Bound",
            snapshot.HeightSourceUpperBoundBytes
        );

        DrawRuntimeResidencyBytes(
            "Current Logical Residency",
            snapshot.HeightCurrentLogicalBytes
        );

        DrawRuntimeResidencyBytes(
            "Observed Peak Logical Residency",
            snapshot.HeightObservedPeakLogicalBytes
        );

        DrawRuntimeResidencyBytes(
            "Conservative Upper Bound",
            snapshot.HeightConservativeUpperBoundBytes
        );
    }

    private void DrawRuntimeLegacyHeightResidency(
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot
    )
    {
        GUILayout.Label(
            "Legacy Height Baseline",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Native Cache Grid",
            $"{snapshot.LegacyHeight.CacheWidth} x " +
            $"{snapshot.LegacyHeight.CacheHeight}"
        );

        DrawRuntimeResidencyBytes(
            "GPU Cache Payload",
            snapshot.LegacyHeight.EstimatedGpuCacheBytes
        );

        DrawRuntimeResidencyBytes(
            "Steady Source",
            snapshot.LegacyHeight.EstimatedSteadySourceBytes
        );

        DrawRuntimeResidencyBytes(
            "Transition Source Upper Bound",
            snapshot.LegacyHeight
                .EstimatedTransitionSourceUpperBoundBytes
        );

        DrawRuntimeResidencyBytes(
            "Conservative Upper Bound",
            snapshot.LegacyHeight
                .EstimatedConservativeUpperBoundBytes
        );

        float gpuReduction =
            CalculateResidencyReductionPercent(
                snapshot.LegacyHeight.EstimatedGpuCacheBytes,
                snapshot.HeightGpuCacheBytes
            );

        float upperBoundReduction =
            CalculateResidencyReductionPercent(
                snapshot.LegacyHeight
                    .EstimatedConservativeUpperBoundBytes,
                snapshot.HeightConservativeUpperBoundBytes
            );

        EditorGUILayout.LabelField(
            "GPU Cache Reduction",
            gpuReduction.ToString("N2") + "%"
        );

        EditorGUILayout.LabelField(
            "Conservative Reduction",
            upperBoundReduction.ToString("N2") + "%"
        );
    }

    private void DrawRuntimeSurfaceResidency(
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot
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
            snapshot.Surface.EstimatedActiveGpuBytes
        );

        DrawRuntimeResidencyBytes(
            "Staging GPU Cache",
            snapshot.Surface.EstimatedStagingGpuBytes
        );

        EditorGUILayout.LabelField(
            "Resident Source Pages",
            snapshot.Surface.ResidentSourceCount.ToString("N0")
        );

        DrawRuntimeResidencyBytes(
            "Current Source",
            snapshot.Surface.EstimatedCurrentSourceBytes
        );

        DrawRuntimeResidencyBytes(
            "Source Upper Bound",
            snapshot.Surface.EstimatedSourceUpperBoundBytes
        );

        DrawRuntimeResidencyBytes(
            "Current Logical Residency",
            snapshot.Surface.EstimatedCurrentLogicalBytes
        );

        DrawRuntimeResidencyBytes(
            "Conservative Upper Bound",
            snapshot.Surface.EstimatedConservativeUpperBoundBytes
        );
    }

    private void DrawRuntimeTotalResidency(
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot
    )
    {
        GUILayout.Label(
            "Terrain Streaming Texture Residency",
            EditorStyles.boldLabel
        );

        DrawRuntimeResidencyBytes(
            "Current",
            snapshot.CurrentTerrainLogicalBytes
        );

        DrawRuntimeResidencyBytes(
            "Conservative Upper Bound",
            snapshot.ConservativeTerrainUpperBoundBytes
        );

        float surfaceShare =
            snapshot.ConservativeTerrainUpperBoundBytes > 0L
                ? (float)(
                    snapshot.Surface
                        .EstimatedConservativeUpperBoundBytes /
                    (double)snapshot
                        .ConservativeTerrainUpperBoundBytes *
                    100d
                )
                : 0f;

        EditorGUILayout.LabelField(
            "Surface Share",
            surfaceShare.ToString("N2") + "%"
        );

        EditorGUILayout.LabelField(
            "Dominant Domain",
            snapshot.Surface
                .EstimatedConservativeUpperBoundBytes >
            snapshot.HeightConservativeUpperBoundBytes
                ? "Surface"
                : "Height"
        );

        if (runtimeTerrainResidencyBudgetMiB > 0f)
        {
            long budgetBytes =
                MiBToBytes(
                    runtimeTerrainResidencyBudgetMiB
                );

            long headroom =
                budgetBytes -
                snapshot.ConservativeTerrainUpperBoundBytes;

            DrawRuntimeResidencyBytes(
                "Target Budget",
                budgetBytes
            );

            EditorGUILayout.LabelField(
                "Headroom / Excess",
                FormatSignedRuntimeResidencyBytes(
                    headroom
                )
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

    private static float CalculateResidencyReductionPercent(
        long legacyBytes,
        long currentBytes
    )
    {
        if (legacyBytes <= 0L)
        {
            return 0f;
        }

        return
            (float)(
                (
                    legacyBytes -
                    currentBytes
                ) /
                (double)legacyBytes *
                100d
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
}
