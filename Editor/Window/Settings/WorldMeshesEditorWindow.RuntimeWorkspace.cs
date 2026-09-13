using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private enum RuntimeWorkspaceOverallState
    {
        InitialBakeRequired,
        ChangesPending,
        Ready,
        Baking,
        ErrorIncomplete
    }

    [SerializeField]
    private bool showRuntimeGenerationDetails;

    [SerializeField]
    private bool showAdvancedRuntimeTools;

    private bool runtimeWorkspaceRepaintScheduled;

    private string lastAdvancedRuntimeMessage =
        "";

    private MessageType lastAdvancedRuntimeMessageType =
        MessageType.None;

    // =====================================================
    // PRODUCTION RUNTIME WORKSPACE
    // =====================================================

    private void DrawRuntimeWorkspace()
    {
        DrawWorkspaceHeader(
            "Runtime",
            "Prepare generated terrain data for runtime streaming with a " +
            "single dependency-aware bake workflow."
        );

        DrawRuntimeBakeWorkspace();
    }

    private void DrawRuntimeBakeWorkspace()
    {
        if (
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            DrawRuntimeWorkspaceMissingInputs();
            return;
        }

        bool pipelineRunning =
            TerrainRuntimeBakePipeline.IsRunning;

        TerrainRuntimeBakePlan plan =
            pipelineRunning
                ? TerrainRuntimeBakePipeline.CurrentPlan
                    ??
                    TerrainRuntimeBakePipeline.InitialPlan
                : TerrainRuntimeBakePlanner.BuildPlan(
                    worldSettings,
                    terrainAuthoringData
                );

        bool hierarchyReady =
            TryGetRuntimeHierarchyReadiness(
                out string hierarchyMessage
            );

        DrawRuntimeBakeSummary(
            plan,
            hierarchyReady,
            hierarchyMessage
        );

        DrawWorkspaceSectionGap();

        DrawRuntimeReadiness(
            plan,
            hierarchyReady
        );

        DrawWorkspaceSectionGap();

        DrawRuntimeConfigurationSettings();

        DrawWorkspaceSectionGap();

        DrawRuntimeGenerationDetails(
            plan
        );

        DrawWorkspaceSectionGap();

        DrawAdvancedRuntimeTools();

        if (pipelineRunning)
        {
            ScheduleRuntimeWorkspaceRepaint();
        }
    }

    private void DrawRuntimeWorkspaceMissingInputs()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Bake",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "WorldSettings and TerrainAuthoringData must be assigned before runtime bake status can be calculated.",
            MessageType.Error
        );

        if (worldSettings == null)
        {
            EditorGUILayout.LabelField(
                "World Settings",
                "Missing"
            );
        }

        if (terrainAuthoringData == null)
        {
            EditorGUILayout.LabelField(
                "Terrain Authoring Data",
                "Missing"
            );
        }

        GUILayout.EndVertical();
    }

    // =====================================================
    // RUNTIME BAKE SUMMARY
    // =====================================================

    private void DrawRuntimeBakeSummary(
        TerrainRuntimeBakePlan plan,
        bool hierarchyReady,
        string hierarchyMessage
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Bake",
            EditorStyles.boldLabel
        );

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            DrawRunningRuntimeBakeSummary();
            GUILayout.EndVertical();
            return;
        }

        RuntimeWorkspaceOverallState state =
            GetRuntimeWorkspaceOverallState(
                plan,
                hierarchyReady
            );

        DrawRuntimeOverallState(
            state,
            plan,
            hierarchyReady,
            hierarchyMessage
        );

        if (
            plan != null
            &&
            plan.HasWork
        )
        {
            GUILayout.Space(7f);

            DrawPendingRuntimeWork(
                plan,
                plan.IsInitialBake,
                false,
                "Pending Runtime Work"
            );
        }

        GUILayout.Space(8f);

        DrawPrimaryRuntimeBakeAction(
            state,
            plan,
            hierarchyReady
        );

        DrawLastRuntimeBakeResultSummary();

        GUILayout.EndVertical();
    }

    private void DrawRunningRuntimeBakeSummary()
    {
        bool rebuildAll =
            TerrainRuntimeBakePipeline.CurrentMode ==
            TerrainRuntimeBakePipelineMode.RebuildAll;

        EditorGUILayout.HelpBox(
            rebuildAll
                ? "Rebuilding Runtime Data"
                : "Baking Runtime Changes",
            MessageType.Info
        );

        EditorGUILayout.LabelField(
            "Current Stage",
            TerrainRuntimeBakePipeline.CurrentStageLabel
        );

        float progress =
            Mathf.Clamp01(
                TerrainRuntimeBakePipeline
                    .CurrentOverallProgress
            );

        Rect progressRect =
            GUILayoutUtility.GetRect(
                18f,
                18f,
                GUILayout.ExpandWidth(true)
            );

        EditorGUI.ProgressBar(
            progressRect,
            progress,
            Mathf.RoundToInt(
                progress * 100f
            ) +
            "%"
        );

        TerrainRuntimeBakePlan initialPlan =
            TerrainRuntimeBakePipeline.InitialPlan;

        if (initialPlan != null)
        {
            GUILayout.Space(8f);

            DrawPendingRuntimeWork(
                initialPlan,
                initialPlan.IsInitialBake,
                rebuildAll,
                rebuildAll
                    ? "Rebuild Scope"
                    : "Initial Work"
            );
        }

        GUILayout.Space(8f);

        EditorGUI.BeginDisabledGroup(
            TerrainRuntimeBakePipeline
                .CancelRequested
        );

        if (
            GUILayout.Button(
                TerrainRuntimeBakePipeline
                    .CancelRequested
                    ? "Cancellation Requested"
                    : "Request Cancel",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeBakePipeline
                .RequestCancel();

            ScheduleRuntimeWorkspaceRepaint();
        }

        EditorGUI.EndDisabledGroup();

        if (
            TerrainRuntimeBakePipeline
                .CancelRequested
        )
        {
            EditorGUILayout.HelpBox(
                "Cancellation requested. The pipeline will stop at the next safe stage boundary.",
                MessageType.Info
            );
        }
    }

    private RuntimeWorkspaceOverallState
        GetRuntimeWorkspaceOverallState(
            TerrainRuntimeBakePlan plan,
            bool hierarchyReady
        )
    {
        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            return
                RuntimeWorkspaceOverallState.Baking;
        }

        if (
            plan == null
            ||
            plan.IsBlocked
            ||
            !hierarchyReady
        )
        {
            return
                RuntimeWorkspaceOverallState.ErrorIncomplete;
        }

        if (plan.IsInitialBake)
        {
            return
                RuntimeWorkspaceOverallState.InitialBakeRequired;
        }

        if (plan.HasWork)
        {
            return
                RuntimeWorkspaceOverallState.ChangesPending;
        }

        return
            RuntimeWorkspaceOverallState.Ready;
    }

    private void DrawRuntimeOverallState(
        RuntimeWorkspaceOverallState state,
        TerrainRuntimeBakePlan plan,
        bool hierarchyReady,
        string hierarchyMessage
    )
    {
        switch (state)
        {
            case RuntimeWorkspaceOverallState.InitialBakeRequired:
                EditorGUILayout.HelpBox(
                    "Initial Bake Required\n\nThe runtime terrain dataset has not been fully prepared yet.",
                    MessageType.Warning
                );
                break;

            case RuntimeWorkspaceOverallState.ChangesPending:
                EditorGUILayout.HelpBox(
                    "Changes Pending\n\nRuntime data is out of date with the current authored world.",
                    MessageType.Warning
                );
                break;

            case RuntimeWorkspaceOverallState.Ready:
                EditorGUILayout.HelpBox(
                    "Ready\n\nRuntime data is current and ready for streaming.",
                    MessageType.Info
                );
                break;

            case RuntimeWorkspaceOverallState.Baking:
                EditorGUILayout.HelpBox(
                    "Baking",
                    MessageType.Info
                );
                break;

            default:
                EditorGUILayout.HelpBox(
                    "Error / Incomplete",
                    MessageType.Error
                );

                if (
                    plan != null
                    &&
                    plan.IsBlocked
                    &&
                    !string.IsNullOrEmpty(
                        plan.BlockReason
                    )
                )
                {
                    EditorGUILayout.HelpBox(
                        plan.BlockReason,
                        MessageType.Warning
                    );
                }

                if (!hierarchyReady)
                {
                    EditorGUILayout.HelpBox(
                        string.IsNullOrEmpty(
                            hierarchyMessage
                        )
                            ? "The generated runtime scene hierarchy requires setup or repair."
                            : hierarchyMessage,
                        MessageType.Warning
                    );

                    DrawSetupRepairWorldHierarchyButton();
                }

                break;
        }
    }

    private void DrawPrimaryRuntimeBakeAction(
        RuntimeWorkspaceOverallState state,
        TerrainRuntimeBakePlan plan,
        bool hierarchyReady
    )
    {
        bool enteringPlayMode =
            EditorApplication
                .isPlayingOrWillChangePlaymode;

        bool independentSurfaceBake =
            TerrainSurfaceMaskCompiler
                .IsGenerating;

        bool canBake =
            plan != null
            &&
            !plan.IsBlocked
            &&
            hierarchyReady
            &&
            plan.HasWork
            &&
            !enteringPlayMode
            &&
            !independentSurfaceBake;

        string buttonLabel;

        switch (state)
        {
            case RuntimeWorkspaceOverallState.InitialBakeRequired:
                buttonLabel =
                    "Bake World For Runtime";
                break;

            case RuntimeWorkspaceOverallState.ChangesPending:
                buttonLabel =
                    "Bake Runtime Changes";
                break;

            case RuntimeWorkspaceOverallState.Ready:
                buttonLabel =
                    "Ready";
                break;

            default:
                buttonLabel =
                    "Resolve Runtime Issues";
                break;
        }

        EditorGUI.BeginDisabledGroup(
            !canBake
        );

        if (
            GUILayout.Button(
                buttonLabel,
                GUILayout.Height(28f),
                GUILayout.ExpandWidth(true)
            )
        )
        {
            StartRuntimeBakePendingChanges();
        }

        EditorGUI.EndDisabledGroup();

        if (enteringPlayMode)
        {
            EditorGUILayout.HelpBox(
                "Runtime bake operations are unavailable while entering or running Play Mode.",
                MessageType.None
            );
        }
        else if (
            independentSurfaceBake
            &&
            !TerrainRuntimeBakePipeline.IsRunning
        )
        {
            EditorGUILayout.HelpBox(
                "A Surface Mask bake is already running. Finish or cancel that operation before starting the unified runtime bake.",
                MessageType.None
            );
        }
    }

    // =====================================================
    // PENDING WORK
    // =====================================================

    private static void DrawPendingRuntimeWork(
        TerrainRuntimeBakePlan plan,
        bool initialBake,
        bool rebuildAll,
        string heading
    )
    {
        if (plan == null)
        {
            return;
        }

        GUILayout.Label(
            heading,
            EditorStyles.boldLabel
        );

        if (rebuildAll)
        {
            EditorGUILayout.LabelField(
                "Heightmaps",
                "Full rebuild"
            );

            EditorGUILayout.LabelField(
                "Surface Masks",
                "Full rebuild"
            );

            EditorGUILayout.LabelField(
                "Collision Meshes",
                "Full rebuild"
            );

            EditorGUILayout.LabelField(
                "Addressables",
                "Rebuild as required"
            );

            EditorGUILayout.LabelField(
                "Runtime Scene",
                "Final synchronization"
            );

            return;
        }

        if (
            plan.HeightWorkMode !=
            TerrainRuntimeBakeWorkMode.None
        )
        {
            EditorGUILayout.LabelField(
                "Heightmaps",
                GetGeneratedWorkLabel(
                    plan.HeightWorkMode,
                    plan.HeightTileCount,
                    "tile",
                    "tiles",
                    initialBake
                )
            );
        }

        if (
            plan.SurfaceWorkMode !=
            TerrainRuntimeBakeWorkMode.None
        )
        {
            EditorGUILayout.LabelField(
                "Surface Masks",
                GetGeneratedWorkLabel(
                    plan.SurfaceWorkMode,
                    plan.SurfaceTileCount,
                    "tile",
                    "tiles",
                    initialBake
                )
            );
        }

        if (
            plan.CollisionWorkMode !=
            TerrainRuntimeBakeWorkMode.None
        )
        {
            EditorGUILayout.LabelField(
                "Collision Meshes",
                GetGeneratedWorkLabel(
                    plan.CollisionWorkMode,
                    plan.CollisionChunkCount,
                    "chunk",
                    "chunks",
                    initialBake
                )
            );
        }

        if (
            plan.AddressablesConfigurationRequired
        )
        {
            EditorGUILayout.LabelField(
                "Addressables",
                initialBake
                    ? "Setup + Build"
                    : "Configuration + Build Required"
            );
        }
        else if (
            plan.AddressablesContentBuildRequired
        )
        {
            EditorGUILayout.LabelField(
                "Addressables",
                "Content Build Required"
            );
        }

        if (
            plan.RuntimeSceneMetadataUpdateRequired
        )
        {
            EditorGUILayout.LabelField(
                "Runtime Scene",
                initialBake
                    ? "Setup / Update"
                    : "Update Required"
            );
        }
    }

    private static string GetGeneratedWorkLabel(
        TerrainRuntimeBakeWorkMode mode,
        int count,
        string singular,
        string plural,
        bool initialBake
    )
    {
        if (
            mode ==
            TerrainRuntimeBakeWorkMode.None
        )
        {
            return "Current";
        }

        if (
            mode ==
            TerrainRuntimeBakeWorkMode.Full
        )
        {
            return
                initialBake
                    ? "Initial build"
                    : "Full rebuild";
        }

        return
            count.ToString("N0") +
            " " +
            (
                count == 1
                    ? singular
                    : plural
            );
    }

    // =====================================================
    // READINESS
    // =====================================================

    private void DrawRuntimeReadiness(
        TerrainRuntimeBakePlan plan,
        bool hierarchyReady
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Readiness",
            EditorStyles.boldLabel
        );

        TerrainGenerationStateUtility.GenerationStatus
            authoringStatus =
                TerrainGenerationStateUtility
                    .GetAuthoringHeightfieldStatus(
                        worldSettings,
                        terrainAuthoringData
                    );

        TerrainGenerationStateUtility.GenerationStatus
            heightStatus =
                TerrainGenerationStateUtility
                    .GetHeightmapStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            surfaceStatus =
                TerrainGenerationStateUtility
                    .GetSurfaceMaskStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        worldSettings
                    );

        EditorGUILayout.LabelField(
            "Authoring Heightfield",
            GetGenerationReadinessLabel(
                authoringStatus
            )
        );

        EditorGUILayout.LabelField(
            "Runtime Heightmaps",
            GetGenerationReadinessLabel(
                heightStatus
            )
        );

        EditorGUILayout.LabelField(
            "Surface Masks",
            GetGenerationReadinessLabel(
                surfaceStatus
            )
        );

        EditorGUILayout.LabelField(
            "Collision Meshes",
            GetGenerationReadinessLabel(
                collisionStatus
            )
        );

        EditorGUILayout.LabelField(
            "Addressables",
            GetAddressablesReadinessLabel(
                plan
            )
        );

        EditorGUILayout.LabelField(
            "Runtime Scene",
            !hierarchyReady
                ? "Needs Repair"
                : plan != null
                    &&
                    plan.RuntimeSceneMetadataUpdateRequired
                        ? "Update Required"
                        : "Current"
        );

        if (
            authoringStatus !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            EditorGUILayout.HelpBox(
                "Runtime baking cannot continue because the committed authoring heightfield is not current. Use the World workspace Height Authoring controls to initialize or repair the committed authoring heightfield.",
                MessageType.Warning
            );
        }

        GUILayout.EndVertical();
    }

    private static string GetGenerationReadinessLabel(
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
                return "Out of Date";

            default:
                return "Not Generated";
        }
    }

    private static string GetAddressablesReadinessLabel(
        TerrainRuntimeBakePlan plan
    )
    {
        if (plan == null)
        {
            return "Unavailable";
        }

        if (
            plan.AddressablesConfigurationRequired
        )
        {
            return "Configuration Required";
        }

        if (
            plan.AddressablesContentBuildRequired
        )
        {
            return "Build Required";
        }

        return "Current";
    }

    // =====================================================
    // RUNTIME SETTINGS
    // =====================================================

    private void DrawRuntimeConfigurationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Settings",
            EditorStyles.boldLabel
        );

        GUILayout.Label(
            "Collision",
            EditorStyles.boldLabel
        );

        int heightfieldResolutionPerChunk =
            Mathf.Max(
                1,
                worldSettings
                    .heightfieldResolutionPerChunk
            );

        inputCollisionResolution =
            Mathf.Max(
                1,
                EditorGUILayout.IntField(
                    "Collision Resolution",
                    inputCollisionResolution
                )
            );

        bool resolutionValid =
            inputCollisionResolution <=
                heightfieldResolutionPerChunk
            &&
            heightfieldResolutionPerChunk %
                inputCollisionResolution ==
                0;

        EditorGUILayout.LabelField(
            "Heightfield Resolution / Chunk",
            heightfieldResolutionPerChunk
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Saved Collision Resolution",
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            ).ToString()
        );

        if (!resolutionValid)
        {
            EditorGUILayout.HelpBox(
                "Heightfield Resolution / Chunk must be evenly divisible by Collision Resolution, and Collision Resolution cannot be larger than the heightfield resolution.",
                MessageType.Warning
            );
        }

        bool mutatingDisabled =
            IsRuntimeMutationDisabled();

        EditorGUI.BeginDisabledGroup(
            mutatingDisabled
            ||
            !resolutionValid
        );

        if (
            GUILayout.Button(
                "Update Collision Settings",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            UpdateCollisionSettings();
        }

        EditorGUI.EndDisabledGroup();

        if (
            GUILayout.Button(
                "Reload Collision Settings",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            inputCollisionResolution =
                Mathf.Max(
                    1,
                    worldSettings.collisionResolution
                );

            Repaint();
        }

        GUILayout.EndVertical();
    }

    // =====================================================
    // GENERATION DETAILS
    // =====================================================

    private void DrawRuntimeGenerationDetails(
        TerrainRuntimeBakePlan plan
    )
    {
        showRuntimeGenerationDetails =
            EditorGUILayout.Foldout(
                showRuntimeGenerationDetails,
                "Generation Details",
                true
            );

        if (!showRuntimeGenerationDetails)
        {
            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        TerrainAuthoringHeightManifest authoringManifest =
            TerrainAuthoringStateUtility
                .LoadAuthoringHeightManifest();

        TerrainHeightmapManifest heightManifest =
            AssetDatabase
                .LoadAssetAtPath<
                    TerrainHeightmapManifest
                >(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase
                .LoadAssetAtPath<
                    TerrainSurfaceMaskManifest
                >(
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .SurfaceMaskManifestPath
                );

        TerrainCollisionManifest collisionManifest =
            AssetDatabase
                .LoadAssetAtPath<
                    TerrainCollisionManifest
                >(
                    WorldMeshesPaths
                        .CollisionManifestAssetPath
                );

        GUILayout.Label(
            "Authoring",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Authoring Revision",
            terrainAuthoringData
                .authoringRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Committed Height Revision",
            authoringManifest != null
                ? authoringManifest
                    .committedHeightRevision
                    .ToString()
                : "-"
        );

        GUILayout.Space(5f);

        GUILayout.Label(
            "Runtime Heightmaps",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Generation Revision",
            worldSettings
                .heightmapGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Manifest",
            GetManifestStatusLabel(
                heightManifest != null,
                heightManifest != null
                &&
                heightManifest.isComplete
            )
        );

        GUILayout.Space(5f);

        GUILayout.Label(
            "Surface Masks",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Generation Revision",
            worldSettings
                .surfaceMaskGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Source Height Revision",
            worldSettings
                .surfaceSourceHeightmapGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Manifest",
            GetManifestStatusLabel(
                surfaceManifest != null,
                surfaceManifest != null
                &&
                surfaceManifest.isComplete
            )
        );

        GUILayout.Space(5f);

        GUILayout.Label(
            "Collision",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Generation Revision",
            worldSettings
                .collisionMeshGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Source Height Revision",
            worldSettings
                .collisionSourceHeightmapGenerationRevision
                .ToString()
        );

        string collisionManifestLabel;

        if (collisionManifest == null)
        {
            collisionManifestLabel =
                "Missing";
        }
        else if (!collisionManifest.isComplete)
        {
            collisionManifestLabel =
                "Incomplete";
        }
        else if (
            TerrainCollisionAddressablesUtility
                .ValidatePreparedManifestStructure(
                    worldSettings,
                    out _
                )
            &&
            collisionManifest
                .collisionMeshGenerationRevision ==
                worldSettings
                    .collisionMeshGenerationRevision
            &&
            collisionManifest
                .collisionSourceHeightmapGenerationRevision ==
                worldSettings
                    .collisionSourceHeightmapGenerationRevision
        )
        {
            collisionManifestLabel =
                "Current";
        }
        else
        {
            collisionManifestLabel =
                "Out of Date";
        }

        EditorGUILayout.LabelField(
            "Runtime Manifest",
            collisionManifestLabel
        );

        GUILayout.Space(5f);

        GUILayout.Label(
            "Signatures",
            EditorStyles.boldLabel
        );

        DrawRuntimeSignatureField(
            "Current Authoring",
            plan != null
                ? plan.CurrentAuthoringSignature
                : ""
        );

        DrawRuntimeSignatureField(
            "Observed Authoring",
            plan != null
                ? plan.ObservedAuthoringSignature
                : ""
        );

        DrawRuntimeSignatureField(
            "Surface Settings",
            plan != null
                ? plan.CurrentSurfaceSettingsSignature
                : ""
        );

        DrawRuntimeSignatureField(
            "Collision Settings",
            plan != null
                ? plan.CurrentCollisionSettingsSignature
                : ""
        );

        GUILayout.EndVertical();
    }

    private static string GetManifestStatusLabel(
        bool exists,
        bool complete
    )
    {
        if (!exists)
        {
            return "Missing";
        }

        return
            complete
                ? "Complete"
                : "Incomplete";
    }

    private static void DrawRuntimeSignatureField(
        string label,
        string value
    )
    {
        EditorGUILayout.LabelField(
            label
        );

        EditorGUILayout.SelectableLabel(
            string.IsNullOrEmpty(value)
                ? "-"
                : value,
            EditorStyles.textField,
            GUILayout.Height(34f)
        );
    }

    // =====================================================
    // ADVANCED RUNTIME TOOLS
    // =====================================================

    private void DrawAdvancedRuntimeTools()
    {
        showAdvancedRuntimeTools =
            EditorGUILayout.Foldout(
                showAdvancedRuntimeTools,
                "Advanced Runtime Tools",
                true
            );

        if (!showAdvancedRuntimeTools)
        {
            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        EditorGUILayout.HelpBox(
            "These operations force individual runtime stages or repair generated runtime infrastructure. They are normally not required during everyday terrain editing.",
            MessageType.None
        );

        bool mutatingDisabled =
            IsRuntimeMutationDisabled();

        TerrainGenerationStateUtility.GenerationStatus
            authoringStatus =
                TerrainGenerationStateUtility
                    .GetAuthoringHeightfieldStatus(
                        worldSettings,
                        terrainAuthoringData
                    );

        TerrainGenerationStateUtility.GenerationStatus
            heightStatus =
                TerrainGenerationStateUtility
                    .GetHeightmapStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            surfaceStatus =
                TerrainGenerationStateUtility
                    .GetSurfaceMaskStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        worldSettings
                    );

        bool authoringCurrent =
            authoringStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current;

        bool heightCurrent =
            heightStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current;

        bool allGeneratedCurrent =
            heightCurrent
            &&
            surfaceStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            collisionStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current;

        EditorGUI.BeginDisabledGroup(
            mutatingDisabled
        );

        EditorGUI.BeginDisabledGroup(
            !authoringCurrent
        );

        if (
            GUILayout.Button(
                "Rebuild All Runtime Data",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool confirmed =
                EditorUtility.DisplayDialog(
                    "Rebuild All Runtime Data?",
                    "This will regenerate all runtime heightmaps, surface masks, and collision meshes even if they are currently up to date.\n\nExisting generated asset GUIDs will be preserved where possible.",
                    "Rebuild",
                    "Cancel"
                );

            if (confirmed)
            {
                StartRebuildAllRuntimeData();
            }
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(8f);

        GUILayout.Label(
            "Generated Data",
            EditorStyles.boldLabel
        );

        EditorGUI.BeginDisabledGroup(
            !authoringCurrent
        );

        if (
            GUILayout.Button(
                "Rebuild Heightmaps",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            if (
                ConfirmAdvancedRebuild(
                    "Rebuild Heightmaps?",
                    "This will regenerate every runtime heightmap tile."
                )
            )
            {
                TerrainRuntimeHeightCompileResult result =
                    TerrainRuntimeHeightCompiler
                        .RebuildAllRuntimeHeightmaps(
                            worldSettings,
                            terrainAuthoringData
                        );

                LogAdvancedHeightResult(
                    result
                );
            }
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(
            !heightCurrent
        );

        if (
            GUILayout.Button(
                "Rebuild Surface Masks",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            if (
                ConfirmAdvancedRebuild(
                    "Rebuild Surface Masks?",
                    "This will regenerate every runtime surface-mask tile."
                )
            )
            {
                bool started =
                    TerrainSurfaceMaskCompiler
                        .RebuildAllSurfaceMasks(
                            worldSettings,
                            OnAdvancedSurfaceRebuildCompleted
                        );

                if (started)
                {
                    SetAdvancedRuntimeMessage(
                        "Full surface-mask rebuild started.",
                        MessageType.Info
                    );
                }
            }
        }

        if (
            GUILayout.Button(
                "Rebuild Collision Meshes",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            if (
                ConfirmAdvancedRebuild(
                    "Rebuild Collision Meshes?",
                    "This will regenerate every runtime collision Mesh."
                )
            )
            {
                TerrainCollisionGenerationResult result =
                    TerrainCollisionMeshGenerator
                        .RebuildAllCollisionMeshes(
                            worldSettings
                        );

                LogAdvancedCollisionResult(
                    result
                );
            }
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(8f);

        GUILayout.Label(
            "Addressables",
            EditorStyles.boldLabel
        );

        EditorGUI.BeginDisabledGroup(
            !allGeneratedCurrent
        );

        if (
            GUILayout.Button(
                "Reconfigure Addressables",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool success =
                TerrainRuntimeAddressablesUtility
                    .ReconfigureAllRuntimeAddressables(
                        worldSettings
                    );

            SetAdvancedRuntimeMessage(
                success
                    ? "Runtime Addressables structural configuration was reconciled."
                    : "Runtime Addressables structural reconfiguration failed. See the Console for details.",
                success
                    ? MessageType.Info
                    : MessageType.Error
            );
        }

        if (
            GUILayout.Button(
                "Rebuild Addressables Content",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool success =
                TerrainRuntimeAddressablesUtility
                    .RebuildAddressablesContentAndAcknowledge(
                        worldSettings
                    );

            SetAdvancedRuntimeMessage(
                success
                    ? "Addressables player content was rebuilt."
                    : "Addressables content rebuild failed. See the Console for details.",
                success
                    ? MessageType.Info
                    : MessageType.Error
            );
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(8f);

        GUILayout.Label(
            "Scene",
            EditorStyles.boldLabel
        );

        DrawSetupRepairWorldHierarchyButton();

        EditorGUI.EndDisabledGroup();

        if (
            !string.IsNullOrEmpty(
                lastAdvancedRuntimeMessage
            )
        )
        {
            EditorGUILayout.HelpBox(
                lastAdvancedRuntimeMessage,
                lastAdvancedRuntimeMessageType
            );
        }

        if (
            mutatingDisabled
        )
        {
            EditorGUILayout.HelpBox(
                GetRuntimeMutationDisabledReason(),
                MessageType.None
            );
        }

        GUILayout.EndVertical();
    }

    private static bool ConfirmAdvancedRebuild(
        string title,
        string message
    )
    {
        return
            EditorUtility.DisplayDialog(
                title,
                message,
                "Rebuild",
                "Cancel"
            );
    }

    private bool IsRuntimeMutationDisabled()
    {
        return
            TerrainRuntimeBakePipeline.IsRunning
            ||
            TerrainSurfaceMaskCompiler.IsGenerating
            ||
            EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private string GetRuntimeMutationDisabledReason()
    {
        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            return
                "Advanced runtime mutations are disabled while the unified runtime bake pipeline is running.";
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            return
                "Advanced runtime mutations are disabled while Surface Mask generation is running.";
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            return
                "Advanced runtime mutations are disabled while entering or running Play Mode.";
        }

        return "";
    }

    // =====================================================
    // HIERARCHY SETUP / READINESS
    // =====================================================

    private void DrawSetupRepairWorldHierarchyButton()
    {
        EditorGUI.BeginDisabledGroup(
            IsRuntimeMutationDisabled()
        );

        if (
            GUILayout.Button(
                "Setup / Repair World Hierarchy",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainWorldHierarchyGenerator
                .SyncWorldHierarchy(
                    worldSettings
                );

            SetAdvancedRuntimeMessage(
                "World hierarchy setup / repair completed. Review the Console for any warnings.",
                MessageType.Info
            );

            Repaint();
        }

        EditorGUI.EndDisabledGroup();
    }

    private bool TryGetRuntimeHierarchyReadiness(
        out string message
    )
    {
        return
            TerrainRuntimeHierarchyReadinessUtility
                .TryGetReadiness(
                    out message
                );
    }

    // =====================================================
    // PIPELINE ACTIONS / CALLBACKS
    // =====================================================

    private void StartRuntimeBakePendingChanges()
    {
        bool started =
            TerrainRuntimeBakePipeline
                .BakePendingChanges(
                    OnRuntimeWorkspacePipelineCompleted
                );

        if (started)
        {
            ScheduleRuntimeWorkspaceRepaint();
        }

        Repaint();
    }

    private void StartRebuildAllRuntimeData()
    {
        bool started =
            TerrainRuntimeBakePipeline
                .RebuildAllRuntimeData(
                    OnRuntimeWorkspacePipelineCompleted
                );

        if (started)
        {
            ScheduleRuntimeWorkspaceRepaint();
        }

        Repaint();
    }

    private void OnRuntimeWorkspacePipelineCompleted(
        TerrainRuntimeBakePipelineResult result
    )
    {
        if (result != null)
        {
            LogRuntimeWorkspacePipelineResult(
                result
            );
        }

        Repaint();
    }

    private static void LogRuntimeWorkspacePipelineResult(
        TerrainRuntimeBakePipelineResult result
    )
    {
        if (result == null)
        {
            return;
        }

        string report =
            result.BuildDiagnosticReport();

        switch (result.Outcome)
        {
            case TerrainRuntimeBakePipelineOutcome.Completed:
            case TerrainRuntimeBakePipelineOutcome.NoWork:
                Debug.Log(
                    report
                );
                break;

            case TerrainRuntimeBakePipelineOutcome.CompletedWithWarnings:
            case TerrainRuntimeBakePipelineOutcome.Cancelled:
            case TerrainRuntimeBakePipelineOutcome.Blocked:
                Debug.LogWarning(
                    report
                );
                break;

            default:
                Debug.LogError(
                    report
                );
                break;
        }
    }

    private void DrawLastRuntimeBakeResultSummary()
    {
        TerrainRuntimeBakePipelineResult result =
            TerrainRuntimeBakePipeline.LastResult;

        if (
            result == null
            ||
            TerrainRuntimeBakePipeline.IsRunning
        )
        {
            return;
        }

        GUILayout.Space(8f);

        MessageType messageType;
        string message;

        switch (result.Outcome)
        {
            case TerrainRuntimeBakePipelineOutcome.Completed:
                messageType =
                    MessageType.Info;

                message =
                    "Last Bake: Completed\n" +
                    result.DurationSeconds
                        .ToString("0.0") +
                    " seconds";
                break;

            case TerrainRuntimeBakePipelineOutcome.NoWork:
                messageType =
                    MessageType.Info;

                message =
                    "Last Bake: No runtime changes were pending.";
                break;

            case TerrainRuntimeBakePipelineOutcome.CompletedWithWarnings:
                messageType =
                    MessageType.Warning;

                message =
                    "Last Bake: Completed with warnings.\n" +
                    result.DurationSeconds
                        .ToString("0.0") +
                    " seconds";
                break;

            case TerrainRuntimeBakePipelineOutcome.Cancelled:
                messageType =
                    MessageType.Warning;

                message =
                    "Last bake was cancelled. Successfully completed work was preserved and remaining work can be resumed.";
                break;

            default:
                messageType =
                    MessageType.Error;

                message =
                    "Last bake did not complete.";

                if (
                    result.FailedStage !=
                    TerrainRuntimeBakePipelineState.Idle
                )
                {
                    message +=
                        "\nFailed Stage: " +
                        GetPipelineStageDisplayName(
                            result.FailedStage
                        );
                }

                if (
                    !string.IsNullOrEmpty(
                        result.ErrorMessage
                    )
                )
                {
                    message +=
                        "\n" +
                        result.ErrorMessage;
                }

                break;
        }

        EditorGUILayout.HelpBox(
            message,
            messageType
        );

        if (
            result.SceneSyncResult != null
            &&
            result.SceneSyncResult.Outcome ==
                TerrainRuntimeSceneSynchronizationOutcome
                    .RepairRequired
        )
        {
            DrawSetupRepairWorldHierarchyButton();
        }
    }

    private static string GetPipelineStageDisplayName(
        TerrainRuntimeBakePipelineState state
    )
    {
        switch (state)
        {
            case TerrainRuntimeBakePipelineState.Heightmaps:
                return "Runtime Heightmaps";

            case TerrainRuntimeBakePipelineState.SurfaceMasks:
                return "Surface Masks";

            case TerrainRuntimeBakePipelineState.Collision:
                return "Collision Meshes";

            case TerrainRuntimeBakePipelineState.Addressables:
                return "Addressables";

            case TerrainRuntimeBakePipelineState.SceneSync:
                return "Runtime Scene";

            case TerrainRuntimeBakePipelineState.Preflight:
                return "Preflight";

            case TerrainRuntimeBakePipelineState.Finalizing:
                return "Final Validation";

            default:
                return state.ToString();
        }
    }

    // =====================================================
    // ADVANCED RESULT LOGGING
    // =====================================================

    private void LogAdvancedHeightResult(
        TerrainRuntimeHeightCompileResult result
    )
    {
        if (result == null)
        {
            SetAdvancedRuntimeMessage(
                "Heightmap rebuild returned no result.",
                MessageType.Error
            );

            return;
        }

        Debug.Log(
            result.BuildDiagnosticReport()
        );

        bool success =
            result.Outcome ==
                TerrainRuntimeHeightCompileOutcome.Completed
            ||
            result.Outcome ==
                TerrainRuntimeHeightCompileOutcome.NoWork;

        SetAdvancedRuntimeMessage(
            success
                ? "Full runtime heightmap rebuild completed."
                : "Full runtime heightmap rebuild did not complete. See the Console for details.",
            success
                ? MessageType.Info
                : MessageType.Warning
        );
    }

    private void OnAdvancedSurfaceRebuildCompleted(
        TerrainSurfaceMaskGenerationResult result
    )
    {
        if (result == null)
        {
            SetAdvancedRuntimeMessage(
                "Surface-mask rebuild returned no result.",
                MessageType.Error
            );

            return;
        }

        Debug.Log(
            result.BuildDiagnosticReport()
        );

        bool success =
            result.Outcome ==
                TerrainSurfaceMaskGenerationOutcome.Completed
            ||
            result.Outcome ==
                TerrainSurfaceMaskGenerationOutcome.NoWork;

        SetAdvancedRuntimeMessage(
            success
                ? "Full runtime surface-mask rebuild completed."
                : "Full runtime surface-mask rebuild did not complete. See the Console for details.",
            success
                ? MessageType.Info
                : MessageType.Warning
        );

        Repaint();
    }

    private void LogAdvancedCollisionResult(
        TerrainCollisionGenerationResult result
    )
    {
        if (result == null)
        {
            SetAdvancedRuntimeMessage(
                "Collision rebuild returned no result.",
                MessageType.Error
            );

            return;
        }

        Debug.Log(
            result.BuildDiagnosticReport()
        );

        bool success =
            result.Outcome ==
                TerrainCollisionGenerationOutcome.Completed
            ||
            result.Outcome ==
                TerrainCollisionGenerationOutcome.NoWork;

        SetAdvancedRuntimeMessage(
            success
                ? "Full runtime collision rebuild completed."
                : "Full runtime collision rebuild did not complete. See the Console for details.",
            success
                ? MessageType.Info
                : MessageType.Warning
        );
    }

    private void SetAdvancedRuntimeMessage(
        string message,
        MessageType messageType
    )
    {
        lastAdvancedRuntimeMessage =
            message ?? "";

        lastAdvancedRuntimeMessageType =
            messageType;

        Repaint();
    }

    // =====================================================
    // ACTIVE PIPELINE REPAINT
    // =====================================================

    private void ScheduleRuntimeWorkspaceRepaint()
    {
        if (runtimeWorkspaceRepaintScheduled)
        {
            return;
        }

        runtimeWorkspaceRepaintScheduled =
            true;

        EditorApplication.delayCall +=
            RuntimeWorkspaceRepaintTick;
    }

    private void RuntimeWorkspaceRepaintTick()
    {
        runtimeWorkspaceRepaintScheduled =
            false;

        if (this == null)
        {
            return;
        }

        Repaint();

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            ScheduleRuntimeWorkspaceRepaint();
        }
    }
}
