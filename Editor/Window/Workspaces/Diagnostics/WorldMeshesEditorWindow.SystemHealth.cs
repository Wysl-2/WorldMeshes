using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showSystemHealth;

    [System.NonSerialized]
    private TerrainRuntimeReadinessResult
        systemHealthReadiness;

    [System.NonSerialized]
    private bool
        systemHealthReadinessEvaluated;

    private void DrawSystemHealth()
    {
        showSystemHealth =
            EditorGUILayout.Foldout(
                showSystemHealth,
                "System Health",
                true
            );

        if (!showSystemHealth)
        {
            return;
        }

        GUILayout.Space(5f);

        TerrainRuntimeReadinessResult readiness =
            GetSystemHealthReadiness();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Current State",
            EditorStyles.boldLabel
        );

        bool requiredInputsAvailable =
            worldSettings != null
            && terrainAuthoringData != null;

        DrawDiagnosticsStatusRow(
            "Operational Readiness",
            GetOperationalReadinessLabel(
                readiness,
                requiredInputsAvailable
            )
        );

        DrawDiagnosticsStatusRow(
            "Authoring",
            requiredInputsAvailable
                ? GetGenerationStatusLabel(
                    readiness != null
                        ? readiness.AuthoringStatus
                        : TerrainGenerationStateUtility
                            .GenerationStatus.NotGenerated
                )
                : "Unavailable"
        );

        DrawDiagnosticsStatusRow(
            "Runtime Height",
            requiredInputsAvailable
                ? GetGenerationStatusLabel(
                    readiness != null
                        ? readiness.HeightStatus
                        : TerrainGenerationStateUtility
                            .GenerationStatus.NotGenerated
                )
                : "Unavailable"
        );

        DrawDiagnosticsStatusRow(
            "Height Streaming",
            GetHeightStreamingHealthLabel(
                readiness,
                requiredInputsAvailable
            )
        );

        DrawDiagnosticsStatusRow(
            "Surface",
            requiredInputsAvailable
                ? GetGenerationStatusLabel(
                    readiness != null
                        ? readiness.SurfaceStatus
                        : TerrainGenerationStateUtility
                            .GenerationStatus.NotGenerated
                )
                : "Unavailable"
        );

        DrawDiagnosticsStatusRow(
            "Surface Streaming",
            requiredInputsAvailable
                ? GetGenerationStatusLabel(
                    readiness != null
                        ? readiness.SurfaceStreamingStatus
                        : TerrainGenerationStateUtility
                            .GenerationStatus.NotGenerated
                )
                : "Unavailable"
        );

        DrawDiagnosticsStatusRow(
            "Collision",
            requiredInputsAvailable
                ? GetGenerationStatusLabel(
                    readiness != null
                        ? readiness.CollisionStatus
                        : TerrainGenerationStateUtility
                            .GenerationStatus.NotGenerated
                )
                : "Unavailable"
        );

        DrawDiagnosticsStatusRow(
            "Addressables",
            GetAddressablesHealthLabel(
                readiness,
                requiredInputsAvailable
            )
        );

        DrawDiagnosticsStatusRow(
            "Runtime Hierarchy",
            GetRuntimeHierarchyHealthLabel(
                readiness
            )
        );

        DrawDiagnosticsStatusRow(
            "Pending Runtime Work",
            GetPendingRuntimeWorkHealthLabel(
                readiness
            )
        );

        DrawDiagnosticsStatusRow(
            "Physical Integrity",
            GetPhysicalIntegrityHealthLabel(
                readiness,
                requiredInputsAvailable
            )
        );

        if (
            readiness != null
            &&
            !readiness.IsReady
            &&
            !string.IsNullOrEmpty(
                readiness.ErrorMessage
            )
        )
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                readiness.ErrorMessage,
                MessageType.Warning
            );
        }

        GUILayout.Space(6f);

        EditorGUILayout.HelpBox(
            "Routine health display uses operational and cached state only. " +
            "Refresh Full Health Check explicitly verifies generated outputs " +
            "and the runtime Addressables configuration.",
            MessageType.None
        );

        bool refreshBlocked =
            TryGetSystemHealthRefreshBlockReason(
                requiredInputsAvailable,
                out string refreshBlockReason
            );

        EditorGUI.BeginDisabledGroup(
            refreshBlocked
        );

        if (
            GUILayout.Button(
                "Refresh Full Health Check",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            RefreshFullSystemHealth();
        }

        EditorGUI.EndDisabledGroup();

        if (
            refreshBlocked
            &&
            !string.IsNullOrEmpty(
                refreshBlockReason
            )
        )
        {
            EditorGUILayout.HelpBox(
                refreshBlockReason,
                MessageType.None
            );
        }

        GUILayout.EndVertical();
    }

    private TerrainRuntimeReadinessResult
        GetSystemHealthReadiness()
    {
        RefreshRuntimeBakeDiagnosticsInputState();

        if (!systemHealthReadinessEvaluated)
        {
            systemHealthReadiness =
                TerrainRuntimeReadinessUtility
                    .EvaluateOperationalReadiness(
                        worldSettings,
                        terrainAuthoringData
                    );

            systemHealthReadinessEvaluated =
                true;
        }

        return
            systemHealthReadiness;
    }

    private void RefreshFullSystemHealth()
    {
        systemHealthReadiness =
            TerrainRuntimeReadinessUtility
                .Evaluate(
                    worldSettings,
                    terrainAuthoringData,
                    true
                );

        systemHealthReadinessEvaluated =
            true;

        Repaint();
    }

    private void InvalidateSystemHealth()
    {
        systemHealthReadiness =
            null;

        systemHealthReadinessEvaluated =
            false;
    }

    private void OnHierarchyChange()
    {
        InvalidateSystemHealth();
        Repaint();
    }

    private static string GetOperationalReadinessLabel(
        TerrainRuntimeReadinessResult readiness,
        bool requiredInputsAvailable
    )
    {
        if (
            !requiredInputsAvailable
            || readiness == null
        )
        {
            return "Unavailable";
        }

        return
            readiness.IsReady
                ? "Ready"
                : "Attention Required";
    }

    private static string GetGenerationStatusLabel(
        TerrainGenerationStateUtility.GenerationStatus status
    )
    {
        switch (status)
        {
            case TerrainGenerationStateUtility
                .GenerationStatus.Current:
                return "Current";

            case TerrainGenerationStateUtility
                .GenerationStatus.OutOfDate:
                return "Out Of Date";

            default:
                return "Not Generated";
        }
    }

    private static string GetHeightStreamingHealthLabel(
        TerrainRuntimeReadinessResult readiness,
        bool requiredInputsAvailable
    )
    {
        if (
            !requiredInputsAvailable
            || readiness == null
        )
        {
            return "Unavailable";
        }

        if (
            readiness.HeightStreamingStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            !readiness.HeightStreamingClipmapCompatible
        )
        {
            return "Needs Repair";
        }

        return
            GetGenerationStatusLabel(
                readiness.HeightStreamingStatus
            );
    }

    private static string GetAddressablesHealthLabel(
        TerrainRuntimeReadinessResult readiness,
        bool requiredInputsAvailable
    )
    {
        if (
            !requiredInputsAvailable
            || readiness == null
        )
        {
            return "Unavailable";
        }

        if (readiness.AddressablesValidation == null)
        {
            return "Not Checked";
        }

        return
            readiness.AddressablesValidation.IsValid
                ? "Current"
                : "Needs Repair";
    }

    private static string GetRuntimeHierarchyHealthLabel(
        TerrainRuntimeReadinessResult readiness
    )
    {
        if (
            readiness == null
            || readiness.HierarchyReadiness == null
        )
        {
            return "Unavailable";
        }

        return
            readiness.HierarchyReadiness.IsReady
                ? "Ready"
                : "Needs Repair";
    }

    private static string GetPendingRuntimeWorkHealthLabel(
        TerrainRuntimeReadinessResult readiness
    )
    {
        TerrainRuntimeBakePlan plan =
            readiness != null
                ? readiness.Plan
                : null;

        if (plan == null)
        {
            return "Unavailable";
        }

        if (plan.IsBlocked)
        {
            return "Blocked";
        }

        return
            plan.HasWork
                ? "Changes Pending"
                : "Current";
    }

    private static string GetPhysicalIntegrityHealthLabel(
        TerrainRuntimeReadinessResult readiness,
        bool requiredInputsAvailable
    )
    {
        if (
            !requiredInputsAvailable
            || readiness == null
        )
        {
            return "Unavailable";
        }

        if (readiness.IntegrityAudit == null)
        {
            return "Not Checked";
        }

        return
            readiness.IntegrityAudit.GeneratedDataValid
                ? "Current"
                : "Needs Repair";
    }

    private static bool TryGetSystemHealthRefreshBlockReason(
        bool requiredInputsAvailable,
        out string reason
    )
    {
        if (!requiredInputsAvailable)
        {
            reason =
                "WorldSettings and TerrainAuthoringData are required for a full health check.";
            return true;
        }

        if (EditorApplication.isCompiling)
        {
            reason =
                "Full health refresh is unavailable while scripts are compiling.";
            return true;
        }

        if (EditorApplication.isUpdating)
        {
            reason =
                "Full health refresh is unavailable while the AssetDatabase is updating.";
            return true;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            reason =
                "Full health refresh is unavailable while entering or running Play Mode.";
            return true;
        }

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            reason =
                "Full health refresh is unavailable while the runtime bake pipeline is running.";
            return true;
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            reason =
                "Full health refresh is unavailable while surface masks are generating.";
            return true;
        }

        reason = "";
        return false;
    }
}
