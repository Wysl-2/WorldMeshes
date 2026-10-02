using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private TerrainRuntimeBakePlan
        runtimeBakeDiagnosticsPlan;

    private bool
        runtimeBakeDiagnosticsPlanEvaluated;

    private TerrainRuntimeBakeStateSummary
        runtimeBakeDiagnosticsSummary;

    private bool
        runtimeBakeDiagnosticsSummaryEvaluated;

    private WorldSettings
        runtimeBakeDiagnosticsTrackedWorldSettings;

    private TerrainAuthoringData
        runtimeBakeDiagnosticsTrackedAuthoringData;

    private int
        runtimeBakeDiagnosticsWorldSettingsDirtyCount =
            int.MinValue;

    private int
        runtimeBakeDiagnosticsAuthoringDataDirtyCount =
            int.MinValue;

    private long
        runtimeBakeDiagnosticsAuthoringRevision =
            long.MinValue;

    private void DrawDiagnosticsWorkspace()
    {
        RefreshRuntimeBakeDiagnosticsInputState();

        DrawWorkspaceHeader(
            "Diagnostics",
            "Inspect WorldMeshes health and live system state, evaluate " +
            "capacity, and run explicit regression validation when needed."
        );

        DrawDiagnosticsToolbar();

        DrawWorkspaceSectionGap();

        DrawSystemHealth();

        DrawWorkspaceSectionGap();

        DrawLiveDiagnostics();

        DrawWorkspaceSectionGap();

        DrawCapacityDiagnostics();

        DrawWorkspaceSectionGap();

        DrawValidationSuites();

        DrawWorkspaceSectionGap();

        DrawAdvancedValidation();
    }

    /*
     * Diagnostics data persists across IMGUI Layout/Repaint/input passes.
     * It is invalidated by authoritative bake-state changes, Undo/Redo,
     * project changes, or inexpensive input revision/dirty checks.
     */
    private void InitializeRuntimeBakeDiagnosticsCache()
    {
        TerrainRuntimeBakeStateService.StateChanged -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        TerrainRuntimeBakeStateService.StateChanged +=
            HandleRuntimeBakeDiagnosticsInvalidated;

        Undo.undoRedoPerformed -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        Undo.undoRedoPerformed +=
            HandleRuntimeBakeDiagnosticsInvalidated;

        EditorApplication.projectChanged -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        EditorApplication.projectChanged +=
            HandleRuntimeBakeDiagnosticsInvalidated;

        ResetRuntimeBakeDiagnosticsInputTracking();
        InvalidateRuntimeBakeDiagnostics();
    }

    private void ShutdownRuntimeBakeDiagnosticsCache()
    {
        TerrainRuntimeBakeStateService.StateChanged -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        Undo.undoRedoPerformed -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        EditorApplication.projectChanged -=
            HandleRuntimeBakeDiagnosticsInvalidated;

        InvalidateRuntimeBakeDiagnostics();
        ResetRuntimeBakeDiagnosticsInputTracking();
    }

    private void HandleRuntimeBakeDiagnosticsInvalidated()
    {
        InvalidateRuntimeBakeDiagnostics();
        Repaint();
    }

    private void InvalidateRuntimeBakeDiagnostics()
    {
        runtimeBakeDiagnosticsPlan =
            null;

        runtimeBakeDiagnosticsPlanEvaluated =
            false;

        runtimeBakeDiagnosticsSummary =
            null;

        runtimeBakeDiagnosticsSummaryEvaluated =
            false;

        InvalidateSystemHealth();
    }

    private void ResetRuntimeBakeDiagnosticsInputTracking()
    {
        runtimeBakeDiagnosticsTrackedWorldSettings =
            null;

        runtimeBakeDiagnosticsTrackedAuthoringData =
            null;

        runtimeBakeDiagnosticsWorldSettingsDirtyCount =
            int.MinValue;

        runtimeBakeDiagnosticsAuthoringDataDirtyCount =
            int.MinValue;

        runtimeBakeDiagnosticsAuthoringRevision =
            long.MinValue;
    }

    private void RefreshRuntimeBakeDiagnosticsInputState()
    {
        int worldSettingsDirtyCount =
            worldSettings != null
                ? EditorUtility.GetDirtyCount(
                    worldSettings
                )
                : -1;

        int authoringDataDirtyCount =
            terrainAuthoringData != null
                ? EditorUtility.GetDirtyCount(
                    terrainAuthoringData
                )
                : -1;

        long authoringRevision =
            terrainAuthoringData != null
                ? terrainAuthoringData.authoringRevision
                : long.MinValue;

        if (
            runtimeBakeDiagnosticsTrackedWorldSettings ==
                worldSettings
            &&
            runtimeBakeDiagnosticsTrackedAuthoringData ==
                terrainAuthoringData
            &&
            runtimeBakeDiagnosticsWorldSettingsDirtyCount ==
                worldSettingsDirtyCount
            &&
            runtimeBakeDiagnosticsAuthoringDataDirtyCount ==
                authoringDataDirtyCount
            &&
            runtimeBakeDiagnosticsAuthoringRevision ==
                authoringRevision
        )
        {
            return;
        }

        runtimeBakeDiagnosticsTrackedWorldSettings =
            worldSettings;

        runtimeBakeDiagnosticsTrackedAuthoringData =
            terrainAuthoringData;

        runtimeBakeDiagnosticsWorldSettingsDirtyCount =
            worldSettingsDirtyCount;

        runtimeBakeDiagnosticsAuthoringDataDirtyCount =
            authoringDataDirtyCount;

        runtimeBakeDiagnosticsAuthoringRevision =
            authoringRevision;

        InvalidateRuntimeBakeDiagnostics();
    }

    private TerrainRuntimeBakePlan
        GetRuntimeBakeDiagnosticsPlan()
    {
        RefreshRuntimeBakeDiagnosticsInputState();

        if (!runtimeBakeDiagnosticsPlanEvaluated)
        {
            runtimeBakeDiagnosticsPlan =
                TerrainRuntimeBakePlanner
                    .BuildPlan(
                        worldSettings,
                        terrainAuthoringData
                    );

            runtimeBakeDiagnosticsPlanEvaluated =
                true;
        }

        return
            runtimeBakeDiagnosticsPlan;
    }

    private TerrainRuntimeBakeStateSummary
        GetRuntimeBakeDiagnosticsSummary()
    {
        RefreshRuntimeBakeDiagnosticsInputState();

        if (!runtimeBakeDiagnosticsSummaryEvaluated)
        {
            runtimeBakeDiagnosticsSummary =
                TerrainRuntimeBakeStateService
                    .GetSummary();

            runtimeBakeDiagnosticsSummaryEvaluated =
                true;
        }

        return
            runtimeBakeDiagnosticsSummary;
    }

}
