using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private const float StampLibraryBrowserViewportHeight =
        330f;

    private const float StampLibraryBrowserCellHeight =
        132f;

    private const float StampLibraryBrowserCellSpacing =
        6f;

    private const float StampLibraryBrowserThumbnailPadding =
        7f;

    [SerializeField]
    private string stampLibraryBrowserSearch =
        "";

    [SerializeField]
    private Vector2 stampLibraryBrowserScrollPosition =
        Vector2.zero;

    [SerializeField]
    private int selectedStampLibraryId;

    [SerializeField]
    private bool showStampLibraryHealthDetails;

    [SerializeField]
    private bool showStampLibraryOperationDetails;

    private TerrainHeightStampLibraryScanResult
        stampLibraryBrowserScan;

    private readonly List<TerrainHeightStampAsset>
        stampLibraryBrowserVisibleAssets =
            new List<TerrainHeightStampAsset>();

    private bool stampLibraryBrowserCacheDirty =
        true;

    private string stampLibraryBrowserOperationMessage =
        "";

    private string stampLibraryBrowserOperationDetails =
        "";

    private MessageType stampLibraryBrowserOperationMessageType =
        MessageType.Info;

    // =====================================================
    // LIFETIME / CACHE INVALIDATION
    // =====================================================

    private void InitializeStampLibraryBrowser()
    {
        EditorApplication.projectChanged -=
            OnStampLibraryBrowserProjectChanged;

        EditorApplication.projectChanged +=
            OnStampLibraryBrowserProjectChanged;

        stampLibraryBrowserCacheDirty =
            true;
    }

    private void ShutdownStampLibraryBrowser()
    {
        EditorApplication.projectChanged -=
            OnStampLibraryBrowserProjectChanged;
    }

    private void OnStampLibraryBrowserProjectChanged()
    {
        /*
         * Do not run a full AssetDatabase scan from the projectChanged
         * callback itself. Mark the browser dirty and refresh once when its
         * panel is next drawn.
         */
        stampLibraryBrowserCacheDirty =
            true;

        Repaint();
    }

    private void EnsureStampLibraryBrowserCache()
    {
        if (
            !stampLibraryBrowserCacheDirty
            &&
            stampLibraryBrowserScan != null
        )
        {
            return;
        }

        RefreshStampLibraryBrowserCache();
    }

    private void RefreshStampLibraryBrowserCache()
    {
        stampLibraryBrowserScan =
            TerrainHeightStampLibraryUtility
                .ScanLibrary();

        stampLibraryBrowserCacheDirty =
            false;

        RebuildStampLibraryBrowserVisibleAssets();

        TerrainHeightStampAsset selected =
            TerrainHeightStampLibraryBrowserUtility
                .FindByLibraryId(
                    stampLibraryBrowserVisibleAssets,
                    selectedStampLibraryId
                );

        if (
            selectedStampLibraryId > 0
            &&
            selected == null
        )
        {
            /*
             * Search can intentionally hide the selected asset, so only clear
             * the persisted ID when it no longer exists in the complete scan.
             */
            TerrainHeightStampAsset stillExists =
                stampLibraryBrowserScan != null
                    ? TerrainHeightStampLibraryBrowserUtility
                        .FindByLibraryId(
                            stampLibraryBrowserScan.StampAssets,
                            selectedStampLibraryId
                        )
                    : null;

            if (
                stillExists == null
                ||
                !stillExists.IsConfigured
            )
            {
                selectedStampLibraryId =
                    TerrainHeightStampIdentityUtility
                        .UnassignedLibraryId;
            }
        }
    }

    private void RebuildStampLibraryBrowserVisibleAssets()
    {
        IReadOnlyList<TerrainHeightStampAsset> source =
            stampLibraryBrowserScan != null
                ? stampLibraryBrowserScan.StampAssets
                : null;

        TerrainHeightStampLibraryBrowserUtility
            .BuildVisibleAssets(
                source,
                stampLibraryBrowserSearch,
                stampLibraryBrowserVisibleAssets
            );
    }

    // =====================================================
    // TERRAIN WORKSPACE PANEL
    // =====================================================

    private void DrawStampLibraryBrowserSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Stamp Library",
            EditorStyles.boldLabel
        );

        EnsureStampLibraryBrowserCache();

        DrawStampLibraryBrowserSearch();

        GUILayout.Space(
            4f
        );

        DrawStampLibraryBrowserCommands();

        GUILayout.Space(
            6f
        );

        DrawStampLibraryBrowserHealth();

        GUILayout.Space(
            6f
        );

        DrawStampLibraryBrowserGrid();

        GUILayout.Space(
            7f
        );

        DrawStampLibraryBrowserSelection();

        GUILayout.Space(
            5f
        );

        DrawStampLibraryBrowserAddSelected();

        DrawStampLibraryBrowserOperationMessage();

        GUILayout.EndVertical();
    }

    private void DrawStampLibraryBrowserSearch()
    {
        EditorGUI.BeginChangeCheck();

        string search =
            EditorGUILayout.TextField(
                "Search",
                stampLibraryBrowserSearch
            );

        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        stampLibraryBrowserSearch =
            search ?? "";

        stampLibraryBrowserScrollPosition =
            Vector2.zero;

        RebuildStampLibraryBrowserVisibleAssets();
    }

    private void DrawStampLibraryBrowserCommands()
    {
        bool operationBlocked =
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
            ||
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating;

        EditorGUI.BeginDisabledGroup(
            operationBlocked
        );

        GUILayout.BeginHorizontal();

        if (
            GUILayout.Button(
                "Import Heightmap"
            )
        )
        {
            ImportStampLibraryHeightmap();
        }

        if (
            GUILayout.Button(
                "Import Folder"
            )
        )
        {
            ImportStampLibraryFolder();
        }

        if (
            GUILayout.Button(
                "Sync Library"
            )
        )
        {
            SyncStampLibrary();
        }

        GUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
    }

    private void DrawStampLibraryBrowserHealth()
    {
        if (stampLibraryBrowserScan == null)
        {
            EditorGUILayout.HelpBox(
                "Stamp library scan is unavailable.",
                MessageType.Error
            );

            return;
        }

        EditorGUILayout.LabelField(
            "Library",
            TerrainHeightStampLibraryBrowserUtility
                .BuildCompactHealthSummary(
                    stampLibraryBrowserScan
                )
        );

        bool hasProblems =
            !stampLibraryBrowserScan.IsHealthy
            ||
            stampLibraryBrowserScan.WarningCount >
                0;

        if (!hasProblems)
        {
            return;
        }

        MessageType messageType =
            stampLibraryBrowserScan.ErrorCount >
                0
                ? MessageType.Error
                : MessageType.Warning;

        EditorGUILayout.HelpBox(
            "The stamp library has issues. Sync Library may repair safe " +
            "problems; ambiguous identity problems remain diagnostic-only.",
            messageType
        );

        showStampLibraryHealthDetails =
            EditorGUILayout.Foldout(
                showStampLibraryHealthDetails,
                "Library Issue Details",
                true
            );

        if (
            showStampLibraryHealthDetails
        )
        {
            EditorGUILayout.TextArea(
                TerrainHeightStampLibraryBrowserUtility
                    .BuildIssueDetails(
                        stampLibraryBrowserScan,
                        12
                    ),
                GUILayout.MinHeight(
                    60f
                )
            );
        }
    }

    // =====================================================
    // VIRTUALIZED THUMBNAIL GRID
    // =====================================================

    private void DrawStampLibraryBrowserGrid()
    {
        int visibleCount =
            stampLibraryBrowserVisibleAssets.Count;

        if (visibleCount <= 0)
        {
            EditorGUILayout.HelpBox(
                string.IsNullOrWhiteSpace(
                    stampLibraryBrowserSearch
                )
                    ? "No configured stamp assets are available."
                    : "No stamps match the current search.",
                MessageType.None
            );

            return;
        }

        Rect viewportRect =
            GUILayoutUtility.GetRect(
                0f,
                StampLibraryBrowserViewportHeight,
                GUILayout.ExpandWidth(true)
            );

        float availableWidth =
            Mathf.Max(
                1f,
                viewportRect.width -
                    16f
            );

        int columns =
            TerrainHeightStampLibraryBrowserUtility
                .CalculateColumnCount(
                    availableWidth,
                    TerrainHeightStampLibraryBrowserUtility
                        .DefaultMinimumCellWidth,
                    StampLibraryBrowserCellSpacing
                );

        float cellWidth =
            (
                availableWidth -
                StampLibraryBrowserCellSpacing *
                    Mathf.Max(
                        0,
                        columns - 1
                    )
            )
            /
            columns;

        int rowCount =
            Mathf.CeilToInt(
                (float)visibleCount /
                columns
            );

        float rowStride =
            StampLibraryBrowserCellHeight +
            StampLibraryBrowserCellSpacing;

        float contentHeight =
            Mathf.Max(
                StampLibraryBrowserViewportHeight -
                    1f,
                rowCount *
                    rowStride -
                    StampLibraryBrowserCellSpacing
            );

        Rect contentRect =
            new Rect(
                0f,
                0f,
                availableWidth,
                contentHeight
            );

        stampLibraryBrowserScrollPosition =
            GUI.BeginScrollView(
                viewportRect,
                stampLibraryBrowserScrollPosition,
                contentRect
            );

        int firstVisibleRow =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    stampLibraryBrowserScrollPosition.y /
                    rowStride
                ) -
                1,
                0,
                Mathf.Max(
                    0,
                    rowCount - 1
                )
            );

        int lastVisibleRow =
            Mathf.Clamp(
                Mathf.CeilToInt(
                    (
                        stampLibraryBrowserScrollPosition.y +
                        viewportRect.height
                    )
                    /
                    rowStride
                ) +
                1,
                0,
                Mathf.Max(
                    0,
                    rowCount - 1
                )
            );

        for (
            int row = firstVisibleRow;
            row <= lastVisibleRow;
            row++
        )
        {
            for (
                int column = 0;
                column < columns;
                column++
            )
            {
                int assetIndex =
                    row *
                        columns +
                    column;

                if (
                    assetIndex >=
                    visibleCount
                )
                {
                    break;
                }

                TerrainHeightStampAsset asset =
                    stampLibraryBrowserVisibleAssets[
                        assetIndex
                    ];

                DrawStampLibraryBrowserCell(
                    asset,
                    column *
                        (
                            cellWidth +
                            StampLibraryBrowserCellSpacing
                        ),
                    row *
                        rowStride,
                    cellWidth
                );
            }
        }

        GUI.EndScrollView();
    }

    private void DrawStampLibraryBrowserCell(
        TerrainHeightStampAsset asset,
        float x,
        float y,
        float width
    )
    {
        if (asset == null)
        {
            return;
        }

        Rect cellRect =
            new Rect(
                x,
                y,
                width,
                StampLibraryBrowserCellHeight
            );

        bool selected =
            asset.LibraryId ==
                selectedStampLibraryId;

        GUIStyle cellStyle =
            selected
                ? EditorStyles.miniButton
                : EditorStyles.helpBox;

        if (
            GUI.Button(
                cellRect,
                GUIContent.none,
                cellStyle
            )
        )
        {
            selectedStampLibraryId =
                asset.LibraryId;

            stampLibraryBrowserOperationMessage =
                "";

            stampLibraryBrowserOperationDetails =
                "";

            Repaint();
        }

        float labelHeight =
            EditorGUIUtility.singleLineHeight +
            4f;

        Rect thumbnailRect =
            new Rect(
                cellRect.x +
                    StampLibraryBrowserThumbnailPadding,
                cellRect.y +
                    StampLibraryBrowserThumbnailPadding,
                Mathf.Max(
                    1f,
                    cellRect.width -
                        StampLibraryBrowserThumbnailPadding *
                        2f
                ),
                Mathf.Max(
                    1f,
                    cellRect.height -
                        labelHeight -
                        StampLibraryBrowserThumbnailPadding *
                        2f
                )
            );

        Texture2D thumbnail =
            TerrainHeightStampLibraryBrowserUtility
                .GetThumbnail(
                    asset
                );

        if (thumbnail != null)
        {
            GUI.DrawTexture(
                thumbnailRect,
                thumbnail,
                ScaleMode.ScaleToFit,
                false
            );
        }
        else
        {
            GUI.Label(
                thumbnailRect,
                "No Texture",
                EditorStyles.centeredGreyMiniLabel
            );
        }

        Rect labelRect =
            new Rect(
                cellRect.x +
                    3f,
                cellRect.yMax -
                    labelHeight -
                    2f,
                Mathf.Max(
                    1f,
                    cellRect.width -
                        6f
                ),
                labelHeight
            );

        GUIStyle labelStyle =
            new GUIStyle(
                selected
                    ? EditorStyles.miniBoldLabel
                    : EditorStyles.miniLabel
            );

        labelStyle.alignment =
            TextAnchor.MiddleCenter;

        labelStyle.clipping =
            TextClipping.Clip;

        GUI.Label(
            labelRect,
            new GUIContent(
                asset.DisplayName,
                asset.DisplayName
            ),
            labelStyle
        );
    }

    // =====================================================
    // SELECTION / ADD
    // =====================================================

    private void DrawStampLibraryBrowserSelection()
    {
        TerrainHeightStampAsset selected =
            GetSelectedStampLibraryAsset();

        if (selected == null)
        {
            EditorGUILayout.LabelField(
                "Selected Stamp",
                "None"
            );

            return;
        }

        EditorGUILayout.LabelField(
            "Selected Stamp",
            selected.DisplayName
        );

        Texture2D texture =
            selected.HeightTexture;

        if (texture != null)
        {
            EditorGUILayout.LabelField(
                "Source Resolution",
                texture.width +
                " x " +
                texture.height
            );
        }
    }

    private void DrawStampLibraryBrowserAddSelected()
    {
        TerrainHeightStampAsset selected =
            GetSelectedStampLibraryAsset();

        bool canAdd =
            selected != null
            &&
            worldSettings != null
            &&
            terrainAuthoringData != null
            &&
            IsModifierEditingAllowed();

        EditorGUI.BeginDisabledGroup(
            !canAdd
        );

        if (
            GUILayout.Button(
                "Add Selected Stamp",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            AddSelectedStampLibraryAsset(
                selected
            );
        }

        EditorGUI.EndDisabledGroup();
    }

    private TerrainHeightStampAsset GetSelectedStampLibraryAsset()
    {
        if (
            stampLibraryBrowserScan == null
        )
        {
            return null;
        }

        return
            TerrainHeightStampLibraryBrowserUtility
                .FindByLibraryId(
                    stampLibraryBrowserScan.StampAssets,
                    selectedStampLibraryId
                );
    }

    private void AddSelectedStampLibraryAsset(
        TerrainHeightStampAsset stampAsset
    )
    {
        if (stampAsset == null)
        {
            SetStampLibraryBrowserMessage(
                "Select a stamp before adding it.",
                "",
                MessageType.Warning
            );

            return;
        }

        Vector2 positionXZ =
            GetDefaultNewStampPositionXZ();

        if (
            !TerrainHeightStampLibraryBrowserUtility
                .TryAddSelectedStamp(
                    terrainAuthoringData,
                    worldSettings,
                    stampAsset,
                    positionXZ,
                    out string stableId,
                    out string errorMessage
                )
        )
        {
            SetStampLibraryBrowserMessage(
                "Could not add " +
                    stampAsset.DisplayName +
                    ".",
                errorMessage,
                MessageType.Error
            );

            return;
        }

        SetStampLibraryBrowserMessage(
            "Added " +
                stampAsset.DisplayName +
                " and selected the new terrain modifier.",
            "StableId: " +
                stableId,
            MessageType.Info
        );

        OnModifierMutationSucceeded();
    }

    // =====================================================
    // PACKAGE 2 IMPORT / SYNC COMMANDS
    // =====================================================

    private void ImportStampLibraryHeightmap()
    {
        string path =
            EditorUtility.OpenFilePanel(
                "Import Terrain Heightmap",
                "",
                "png"
            );

        if (
            string.IsNullOrEmpty(
                path
            )
        )
        {
            return;
        }

        HandleStampLibraryBrowserOperation(
            TerrainHeightStampLibraryUtility
                .ImportHeightmap(
                    path
                ),
            true
        );
    }

    private void ImportStampLibraryFolder()
    {
        string path =
            EditorUtility.OpenFolderPanel(
                "Import Terrain Heightmap Folder",
                "",
                ""
            );

        if (
            string.IsNullOrEmpty(
                path
            )
        )
        {
            return;
        }

        HandleStampLibraryBrowserOperation(
            TerrainHeightStampLibraryUtility
                .ImportHeightmapFolder(
                    path
                ),
            true
        );
    }

    private void SyncStampLibrary()
    {
        HandleStampLibraryBrowserOperation(
            TerrainHeightStampLibraryUtility
                .SyncLibrary(),
            true
        );
    }

    private void HandleStampLibraryBrowserOperation(
        TerrainHeightStampLibraryOperationReport report,
        bool selectCreatedAsset
    )
    {
        if (report == null)
        {
            stampLibraryBrowserCacheDirty =
                true;

            SetStampLibraryBrowserMessage(
                "The stamp-library operation returned no report.",
                "",
                MessageType.Error
            );

            return;
        }

        stampLibraryBrowserScan =
            report.FinalScan;

        if (
            stampLibraryBrowserScan == null
        )
        {
            stampLibraryBrowserCacheDirty =
                true;

            EnsureStampLibraryBrowserCache();
        }
        else
        {
            stampLibraryBrowserCacheDirty =
                false;

            RebuildStampLibraryBrowserVisibleAssets();
        }

        TerrainHeightStampAsset created =
            report.LastCreatedStampAsset;

        if (
            selectCreatedAsset
            &&
            created != null
        )
        {
            selectedStampLibraryId =
                created.LibraryId;

            Selection.activeObject =
                created;

            EditorGUIUtility.PingObject(
                created
            );
        }

        string message =
            report.Operation +
            (
                report.Succeeded
                    ? " completed."
                    : " completed with problems."
            );

        if (
            report.ImportedHeightmaps >
            0
            ||
            report.CreatedStampAssets >
            0
        )
        {
            message +=
                " Imported " +
                report.ImportedHeightmaps +
                ", created " +
                report.CreatedStampAssets +
                " stamp asset(s).";
        }

        SetStampLibraryBrowserMessage(
            message,
            report.BuildSummary(),
            report.Succeeded
                ? (
                    report.WarningCount > 0
                        ? MessageType.Warning
                        : MessageType.Info
                )
                : MessageType.Error
        );

        Repaint();
    }

    // =====================================================
    // OPERATION MESSAGE
    // =====================================================

    private void SetStampLibraryBrowserMessage(
        string message,
        string details,
        MessageType messageType
    )
    {
        stampLibraryBrowserOperationMessage =
            message ?? "";

        stampLibraryBrowserOperationDetails =
            details ?? "";

        stampLibraryBrowserOperationMessageType =
            messageType;

        showStampLibraryOperationDetails =
            false;
    }

    private void DrawStampLibraryBrowserOperationMessage()
    {
        if (
            string.IsNullOrEmpty(
                stampLibraryBrowserOperationMessage
            )
        )
        {
            return;
        }

        GUILayout.Space(
            6f
        );

        EditorGUILayout.HelpBox(
            stampLibraryBrowserOperationMessage,
            stampLibraryBrowserOperationMessageType
        );

        if (
            string.IsNullOrEmpty(
                stampLibraryBrowserOperationDetails
            )
        )
        {
            return;
        }

        showStampLibraryOperationDetails =
            EditorGUILayout.Foldout(
                showStampLibraryOperationDetails,
                "Operation Details",
                true
            );

        if (
            showStampLibraryOperationDetails
        )
        {
            EditorGUILayout.TextArea(
                stampLibraryBrowserOperationDetails,
                GUILayout.MinHeight(
                    70f
                )
            );
        }
    }
}
