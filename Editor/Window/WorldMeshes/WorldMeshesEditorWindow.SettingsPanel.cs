using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private enum Workspace
    {
        World,
        Terrain,
        Runtime,
        Diagnostics
    }

    private const string WorkspaceEditorPrefsKey =
        "WorldMeshes.EditorWindow.Workspace";

    private static readonly string[] WorkspaceLabels =
    {
        "World",
        "Terrain",
        "Runtime",
        "Diagnostics"
    };

    [SerializeField]
    private Workspace selectedWorkspace =
        Workspace.World;

    [SerializeField]
    private Vector2 worldWorkspaceScrollPosition =
        Vector2.zero;

    [SerializeField]
    private Vector2 terrainWorkspaceScrollPosition =
        Vector2.zero;

    [SerializeField]
    private Vector2 runtimeWorkspaceScrollPosition =
        Vector2.zero;

    [SerializeField]
    private Vector2 diagnosticsWorkspaceScrollPosition =
        Vector2.zero;

    private bool workspacePreferenceLoaded;

    private void DrawSettingsPanel(
        Rect settingsArea
    )
    {
        EnsureWorkspacePreferenceLoaded();

        GUILayout.BeginArea(
            settingsArea
        );

        DrawWorkspaceToolbar();

        GUILayout.Space(6f);

        Vector2 scrollPosition =
            GetSelectedWorkspaceScrollPosition();

        scrollPosition =
            EditorGUILayout.BeginScrollView(
                scrollPosition,
                false,
                false,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );

        DrawSelectedWorkspace();

        EditorGUILayout.EndScrollView();

        SetSelectedWorkspaceScrollPosition(
            scrollPosition
        );

        GUILayout.EndArea();
    }

    // =====================================================
    // WORKSPACE NAVIGATION
    // =====================================================

    private void EnsureWorkspacePreferenceLoaded()
    {
        if (workspacePreferenceLoaded)
        {
            return;
        }

        int storedWorkspace =
            EditorPrefs.GetInt(
                WorkspaceEditorPrefsKey,
                (int)Workspace.World
            );

        if (
            storedWorkspace <
                (int)Workspace.World
            ||
            storedWorkspace >
                (int)Workspace.Diagnostics
        )
        {
            storedWorkspace =
                (int)Workspace.World;
        }

        selectedWorkspace =
            (Workspace)storedWorkspace;

        workspacePreferenceLoaded =
            true;
    }

    private void DrawWorkspaceToolbar()
    {
        GUILayout.BeginHorizontal(
            EditorStyles.toolbar
        );

        int selectedIndex =
            GUILayout.Toolbar(
                (int)selectedWorkspace,
                WorkspaceLabels,
                EditorStyles.toolbarButton,
                GUILayout.ExpandWidth(true)
            );

        GUILayout.EndHorizontal();

        if (
            selectedIndex ==
            (int)selectedWorkspace
        )
        {
            return;
        }

        selectedWorkspace =
            (Workspace)selectedIndex;

        EditorPrefs.SetInt(
            WorkspaceEditorPrefsKey,
            selectedIndex
        );

        GUI.FocusControl(
            null
        );

        Repaint();
    }

    private void DrawSelectedWorkspace()
    {
        switch (selectedWorkspace)
        {
            case Workspace.Terrain:
                DrawTerrainWorkspace();
                break;

            case Workspace.Runtime:
                DrawRuntimeWorkspace();
                break;

            case Workspace.Diagnostics:
                DrawDiagnosticsWorkspace();
                break;

            default:
                DrawWorldWorkspace();
                break;
        }
    }

    // =====================================================
    // WORLD
    // =====================================================

    private void DrawWorldWorkspace()
    {
        DrawWorkspaceHeader(
            "World",
            "Configure the world, clipmap layout, committed authoring " +
            "heightfield, and generated scene hierarchy."
        );

        DrawWorldSettings();

        DrawWorkspaceSectionGap();

        DrawClipmapGenerationSettings();

        DrawWorkspaceSectionGap();

        DrawHeightAuthoringSettings();

        DrawWorkspaceSectionGap();

        DrawWorldHierarchySettings();
    }

    // =====================================================
    // TERRAIN
    // =====================================================

    private void DrawTerrainWorkspace()
    {
        DrawWorkspaceHeader(
            "Terrain",
            "Manage non-destructive terrain modifiers and control the " +
            "edit-mode terrain preview and authoring visualization."
        );

        DrawStampAuthoringToolbarSettings();

        DrawWorkspaceSectionGap();

        DrawStampLibraryBrowserSettings();

        DrawWorkspaceSectionGap();

        DrawModifierAuthoringSettings();

        DrawWorkspaceSectionGap();

        DrawHeightPreviewSettings();

        DrawWorkspaceSectionGap();

        DrawSceneViewFollowingSettings();

        DrawWorkspaceSectionGap();

        DrawAuthoringVisualizationSettings();

        DrawWorkspaceSectionGap();

        DrawAuthoringWireframeSettings();
    }

    // =====================================================
    // RUNTIME
    // =====================================================

    private void DrawRuntimeWorkspace()
    {
        DrawWorkspaceHeader(
            "Runtime",
            "Compile derived runtime terrain data, inspect generation " +
            "state, and prepare collision/runtime streaming assets."
        );

        DrawGenerationStateSettings();

        DrawWorkspaceSectionGap();

        DrawCurrentRuntimeHeightmapPipeline();

        DrawWorkspaceSectionGap();

        DrawCollisionGenerationSettings();
    }

    // =====================================================
    // DIAGNOSTICS
    // =====================================================

    private void DrawDiagnosticsWorkspace()
    {
        DrawWorkspaceHeader(
            "Diagnostics",
            "Validation and implementation diagnostics for authoring, " +
            "composition, preview responsiveness, and runtime systems."
        );

        DrawPreviewResponsivenessValidationSettings();

        DrawWorkspaceSectionGap();

        DrawModifierDataFoundationSettings();

        DrawWorkspaceSectionGap();

        DrawAuthoringChangePipelineSettings();

        DrawWorkspaceSectionGap();

        DrawGpuCompositorFoundationSettings();

        DrawWorkspaceSectionGap();

        DrawStampRotationFoundationValidationSettings();

        DrawWorkspaceSectionGap();

        DrawStampSourceOrientationValidationSettings();

        DrawWorkspaceSectionGap();

        DrawStampSourceRemapValidationSettings();

        DrawWorkspaceSectionGap();

        DrawStampFalloffValidationSettings();

        DrawWorkspaceSectionGap();

        DrawStampLibraryFoundationValidationSettings();

        DrawWorkspaceSectionGap();

        DrawStampLibrarySyncValidationSettings();

        DrawWorkspaceSectionGap();

        DrawStampAssetDefaultsValidationSettings();

        DrawWorkspaceSectionGap();

        DrawStampLibraryBrowserValidationSettings();

        DrawWorkspaceSectionGap();

        DrawLiveStampValidationSettings();

        DrawWorkspaceSectionGap();

        DrawRuntimeHeightCompositionValidationSettings();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakeStateDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeBakePlanDiagnostics();

        DrawWorkspaceSectionGap();

        DrawRuntimeValidationSettings();
    }

    // =====================================================
    // SHARED WORKSPACE UI
    // =====================================================

    private static void DrawWorkspaceHeader(
        string title,
        string description
    )
    {
        GUILayout.Label(
            title,
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            description,
            MessageType.None
        );

        GUILayout.Space(8f);
    }

    private static void DrawWorkspaceSectionGap()
    {
        GUILayout.Space(10f);
    }

    // =====================================================
    // PER-WORKSPACE SCROLL STATE
    // =====================================================

    private Vector2 GetSelectedWorkspaceScrollPosition()
    {
        switch (selectedWorkspace)
        {
            case Workspace.Terrain:
                return
                    terrainWorkspaceScrollPosition;

            case Workspace.Runtime:
                return
                    runtimeWorkspaceScrollPosition;

            case Workspace.Diagnostics:
                return
                    diagnosticsWorkspaceScrollPosition;

            default:
                return
                    worldWorkspaceScrollPosition;
        }
    }

    private void SetSelectedWorkspaceScrollPosition(
        Vector2 value
    )
    {
        switch (selectedWorkspace)
        {
            case Workspace.Terrain:
                terrainWorkspaceScrollPosition =
                    value;
                break;

            case Workspace.Runtime:
                runtimeWorkspaceScrollPosition =
                    value;
                break;

            case Workspace.Diagnostics:
                diagnosticsWorkspaceScrollPosition =
                    value;
                break;

            default:
                worldWorkspaceScrollPosition =
                    value;
                break;
        }
    }
}
