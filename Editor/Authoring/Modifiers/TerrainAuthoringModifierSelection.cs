using System;
using UnityEditor;
using UnityEngine;

/*
 * Shared editor-only selection state for terrain height modifiers.
 *
 * Selection is identified by TerrainHeightModifier.StableId rather than
 * list index so it remains attached to the same modifier when the ordered
 * stack is rearranged.
 *
 * SessionState intentionally keeps this state outside TerrainAuthoringData:
 * selecting a modifier must never dirty authoring data, change terrain
 * signatures, or affect generated runtime output.
 */
[InitializeOnLoad]
public static class TerrainAuthoringModifierSelection
{
    private const string SelectedStableIdSessionKey =
        "WorldMeshes.TerrainAuthoring.SelectedModifierStableId";

    public static event Action SelectionChanged;

    static TerrainAuthoringModifierSelection()
    {
        Undo.undoRedoPerformed +=
            OnUndoRedoPerformed;

        EditorApplication.projectChanged +=
            OnProjectChanged;

        EditorApplication.delayCall +=
            RepaintEditorViews;
    }

    public static string SelectedStableId
    {
        get
        {
            return
                SessionState.GetString(
                    SelectedStableIdSessionKey,
                    ""
                );
        }
    }

    public static bool HasSelection
    {
        get
        {
            return
                !string.IsNullOrEmpty(
                    SelectedStableId
                );
        }
    }

    public static void Select(
        string stableId
    )
    {
        string safeStableId =
            stableId ??
            "";

        if (
            SelectedStableId ==
            safeStableId
        )
        {
            return;
        }

        SessionState.SetString(
            SelectedStableIdSessionKey,
            safeStableId
        );

        RaiseSelectionChanged();
    }

    public static void Clear()
    {
        Select(
            ""
        );
    }

    public static bool SelectIndex(
        TerrainAuthoringData authoringData,
        int index
    )
    {
        if (
            authoringData == null
            ||
            index < 0
            ||
            index >=
                authoringData.HeightModifierCount
        )
        {
            Clear();
            return false;
        }

        TerrainHeightModifier modifier =
            authoringData.HeightModifiers[
                index
            ];

        if (
            modifier == null
            ||
            string.IsNullOrEmpty(
                modifier.StableId
            )
        )
        {
            Clear();
            return false;
        }

        Select(
            modifier.StableId
        );

        return true;
    }

    public static bool TryFindModifier(
        TerrainAuthoringData authoringData,
        string stableId,
        out TerrainHeightModifier modifier,
        out int index
    )
    {
        modifier = null;
        index = -1;

        if (
            authoringData == null
            ||
            string.IsNullOrEmpty(
                stableId
            )
        )
        {
            return false;
        }

        for (
            int current = 0;
            current <
                authoringData.HeightModifierCount;
            current++
        )
        {
            TerrainHeightModifier candidate =
                authoringData.HeightModifiers[
                    current
                ];

            if (
                candidate == null
                ||
                candidate.StableId !=
                    stableId
            )
            {
                continue;
            }

            modifier =
                candidate;

            index =
                current;

            return true;
        }

        return false;
    }

    public static bool TryGetSelectedModifier(
        TerrainAuthoringData authoringData,
        out TerrainHeightModifier modifier,
        out int index
    )
    {
        return
            TryFindModifier(
                authoringData,
                SelectedStableId,
                out modifier,
                out index
            );
    }

    /*
     * Call while drawing/using a specific TerrainAuthoringData asset.
     * A stale StableId is cleared, but an intentionally empty selection
     * is left empty.
     */
    public static bool EnsureValidSelection(
        TerrainAuthoringData authoringData
    )
    {
        if (!HasSelection)
        {
            return true;
        }

        if (
            TryGetSelectedModifier(
                authoringData,
                out _,
                out _
            )
        )
        {
            return true;
        }

        Clear();
        return false;
    }

    /*
     * Persistent authoring data changed without changing selection.
     * Stage 15C can reuse this repaint boundary for Scene View tooling.
     */
    public static void NotifyModifierDataChanged()
    {
        RepaintEditorViews();
    }

    private static void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke();

        RepaintEditorViews();
    }

    private static void OnUndoRedoPerformed()
    {
        /*
         * The active TerrainAuthoringData may be selected/assigned by a
         * WorldMeshes window, so do not assume one asset here. Each UI
         * consumer revalidates the StableId against its own authoring data.
         */
        RepaintEditorViews();
    }

    private static void OnProjectChanged()
    {
        RepaintEditorViews();
    }

    private static void RepaintEditorViews()
    {
        WorldMeshesEditorWindow[] windows =
            Resources.FindObjectsOfTypeAll<
                WorldMeshesEditorWindow
            >();

        for (
            int index = 0;
            index < windows.Length;
            index++
        )
        {
            if (windows[index] != null)
            {
                windows[index].Repaint();
            }
        }

        SceneView.RepaintAll();
    }
}
