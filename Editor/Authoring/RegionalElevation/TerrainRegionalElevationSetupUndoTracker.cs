using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Package 4 bridge for Undo/Redo of the existing Package 2 regional source
 * initialization action.
 *
 * Package 5 will own general node mutation transactions. This tracker is
 * deliberately narrower: it observes regional SOURCE output identity across
 * Undo/Redo and, only when that identity changes, invalidates/recomposes the
 * complete logical world because global IDW affects every height tile.
 */
[InitializeOnLoad]
public static class TerrainRegionalElevationSetupUndoTracker
{
    private static bool snapshotInitialized;

    private static string lastRegionalFingerprint =
        "";

    static TerrainRegionalElevationSetupUndoTracker()
    {
        Undo.undoRedoPerformed +=
            OnUndoRedoPerformed;

        EditorApplication.delayCall +=
            RecordCurrentState;
    }

    public static void RecordCurrentState()
    {
        TerrainAuthoringData authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        lastRegionalFingerprint =
            CaptureFingerprint(
                authoringData
            );

        snapshotInitialized =
            true;
    }

    private static void OnUndoRedoPerformed()
    {
        TerrainAuthoringData authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        string currentFingerprint =
            CaptureFingerprint(
                authoringData
            );

        if (!snapshotInitialized)
        {
            lastRegionalFingerprint =
                currentFingerprint;

            snapshotInitialized =
                true;

            return;
        }

        if (
            currentFingerprint ==
            lastRegionalFingerprint
        )
        {
            return;
        }

        lastRegionalFingerprint =
            currentFingerprint;

        WorldSettings worldSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
                    WorldMeshesPaths
                        .WorldSettingsAssetPath
                );

        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            return;
        }

        if (
            string.IsNullOrEmpty(
                TerrainAuthoringStateUtility
                    .GetCommittedHeightfieldSignature(
                        worldSettings
                    )
            )
        )
        {
            /*
             * No committed base exists yet. Initial heightfield creation is a
             * separate full invalidation/rebuild boundary, so there is no
             * composite cache or runtime height dataset to repair here.
             */
            return;
        }

        HashSet<Vector2Int> allHeightTiles =
            new HashSet<Vector2Int>();

        TerrainRegionalElevationCompositionUtility
            .CollectAllHeightTiles(
                worldSettings,
                allHeightTiles
            );

        if (
            allHeightTiles.Count ==
            0
        )
        {
            return;
        }

        TerrainRuntimeInvalidationService
            .InvalidateAuthoringHeightTiles(
                worldSettings,
                authoringData,
                allHeightTiles
            );

        TerrainAuthoringPreviewService
            .NotifyCompositeAuthoringStateChanged(
                allHeightTiles
            );
    }

    private static string CaptureFingerprint(
        TerrainAuthoringData authoringData
    )
    {
        if (authoringData == null)
        {
            return
                "<missing-authoring-data>";
        }

        if (
            TerrainRegionalElevationCompositionUtility
                .TryGetRegionalOutputFingerprint(
                    authoringData,
                    out string fingerprint,
                    out _
                )
        )
        {
            return
                fingerprint;
        }

        TerrainRegionalElevationSource source =
            authoringData.RegionalElevationSource;

        return
            "<invalid-regional-source>|" +
            (
                source != null
                    ? source.GetType().FullName
                    : "null"
            ) +
            "|revision:" +
            authoringData.authoringRevision;
    }
}
