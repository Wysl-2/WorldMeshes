using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeBakePersistenceValidationUtility
{
    public const string RelativeCheckpointPath =
        "Library/WorldMeshes/Validation/RuntimeBakePersistenceCheckpoint.json";

    public static string CheckpointPath
    {
        get
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..")
            );

            return Path.Combine(
                projectRoot,
                "Library",
                "WorldMeshes",
                "Validation",
                "RuntimeBakePersistenceCheckpoint.json"
            );
        }
    }

    public static bool CheckpointExists => File.Exists(CheckpointPath);

    public static bool CaptureCheckpoint(
        out TerrainRuntimeBakePersistenceCheckpoint checkpoint,
        out string errorMessage
    )
    {
        checkpoint = null;
        errorMessage = "";

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage =
                "Persistence checkpoints must be captured outside Play Mode.";
            return false;
        }

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        if (worldSettings == null || authoringData == null)
        {
            errorMessage =
                "WorldSettings and TerrainAuthoringData are required to capture a persistence checkpoint.";
            return false;
        }

        TerrainRuntimeBakeStateSnapshot snapshot =
            TerrainRuntimeBakeStateService.GetSnapshot();

        string currentAuthoringSignature =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                authoringData
            ) ?? "";

        TerrainSurfaceSettings surfaceSettings =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                WorldMeshesPaths.TerrainSurfaceSettingsAssetPath
            );

        string surfaceSignature = surfaceSettings != null
            ? TerrainSurfaceSignatureUtility.GetSettingsSignature(surfaceSettings) ?? ""
            : "";

        string collisionSignature =
            TerrainGenerationStateUtility.GetCurrentCollisionSettingsSignature(
                worldSettings
            ) ?? "";

        checkpoint = TerrainRuntimeBakePersistenceCheckpoint.Create(
            snapshot,
            currentAuthoringSignature,
            surfaceSignature,
            collisionSignature
        );

        if (checkpoint == null)
        {
            errorMessage = "Could not create persistence checkpoint data.";
            return false;
        }

        try
        {
            string path = CheckpointPath;
            string directory = Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(directory))
            {
                errorMessage = "Could not resolve checkpoint directory.";
                return false;
            }

            Directory.CreateDirectory(directory);

            string temporaryPath = path + ".tmp";
            string json = JsonUtility.ToJson(checkpoint, true);

            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(false)
            );

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, null);
                }
                catch
                {
                    File.Copy(temporaryPath, path, true);
                    File.Delete(temporaryPath);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not save runtime bake persistence checkpoint.\n\n" +
                exception.Message;
            return false;
        }
    }

    public static bool TryLoadCheckpoint(
        out TerrainRuntimeBakePersistenceCheckpoint checkpoint,
        out string errorMessage
    )
    {
        checkpoint = null;
        errorMessage = "";

        string path = CheckpointPath;

        if (!File.Exists(path))
        {
            errorMessage = "No persistence checkpoint has been captured.";
            return false;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(json))
            {
                errorMessage = "The persistence checkpoint file is empty.";
                return false;
            }

            checkpoint =
                JsonUtility.FromJson<TerrainRuntimeBakePersistenceCheckpoint>(json);

            if (checkpoint == null)
            {
                errorMessage = "The persistence checkpoint could not be parsed.";
                return false;
            }

            if (
                checkpoint.formatVersion !=
                TerrainRuntimeBakePersistenceCheckpoint.CurrentFormatVersion
            )
            {
                errorMessage =
                    "Persistence checkpoint format is incompatible. Expected " +
                    TerrainRuntimeBakePersistenceCheckpoint.CurrentFormatVersion +
                    ", found " + checkpoint.formatVersion + ".";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not read runtime bake persistence checkpoint.\n\n" +
                exception.Message;
            return false;
        }
    }

    public static TerrainRuntimeBakePersistenceValidationResult CompareCurrentStateToCheckpoint()
    {
        bool found = File.Exists(CheckpointPath);

        if (!TryLoadCheckpoint(
            out TerrainRuntimeBakePersistenceCheckpoint checkpoint,
            out string loadError
        ))
        {
            return new TerrainRuntimeBakePersistenceValidationResult(
                TerrainRuntimeBakePersistenceValidationOutcome.Blocked,
                found,
                false,
                false,
                0,
                TerrainRuntimeBakeStateService.GetSnapshot().StateRevision,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                "",
                loadError,
                "Persistence validation could not compare the current state."
            );
        }

        TerrainRuntimeBakeStateSnapshot current =
            TerrainRuntimeBakeStateService.GetSnapshot();

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        string currentAuthoringSignature =
            worldSettings != null && authoringData != null
                ? TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                ) ?? ""
                : "";

        TerrainSurfaceSettings surfaceSettings =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                WorldMeshesPaths.TerrainSurfaceSettingsAssetPath
            );

        string currentSurfaceSignature = surfaceSettings != null
            ? TerrainSurfaceSignatureUtility.GetSettingsSignature(surfaceSettings) ?? ""
            : "";

        string currentCollisionSignature = worldSettings != null
            ? TerrainGenerationStateUtility.GetCurrentCollisionSettingsSignature(
                worldSettings
            ) ?? ""
            : "";

        List<Vector2Int> expectedHeight = checkpoint.GetPendingHeightTiles();
        List<Vector2Int> expectedHeightStreaming =
            checkpoint.GetPendingHeightStreamingTiles();
        List<Vector2Int> expectedSurface = checkpoint.GetPendingSurfaceTiles();
        List<Vector2Int> expectedCollision = checkpoint.GetPendingCollisionChunks();

        CompareCoordinates(
            expectedHeight,
            current.PendingHeightTiles,
            out List<Vector2Int> missingHeight,
            out List<Vector2Int> unexpectedHeight
        );

        CompareCoordinates(
            expectedHeightStreaming,
            current.PendingHeightStreamingTiles,
            out List<Vector2Int> missingHeightStreaming,
            out List<Vector2Int> unexpectedHeightStreaming
        );

        CompareCoordinates(
            expectedSurface,
            current.PendingSurfaceTiles,
            out List<Vector2Int> missingSurface,
            out List<Vector2Int> unexpectedSurface
        );

        CompareCoordinates(
            expectedCollision,
            current.PendingCollisionChunks,
            out List<Vector2Int> missingCollision,
            out List<Vector2Int> unexpectedCollision
        );

        bool revisionMatches = checkpoint.stateRevision == current.StateRevision;

        bool fullFlagsMatch =
            checkpoint.fullHeightRebuildRequired == current.FullHeightRebuildRequired
            && checkpoint.fullHeightStreamingRebuildRequired == current.FullHeightStreamingRebuildRequired
            && checkpoint.fullSurfaceRebuildRequired == current.FullSurfaceRebuildRequired
            && checkpoint.fullCollisionRebuildRequired == current.FullCollisionRebuildRequired;

        bool addressablesFlagsMatch =
            checkpoint.addressablesConfigurationDirty == current.AddressablesConfigurationDirty
            && checkpoint.addressablesContentDirty == current.AddressablesContentDirty;

        bool runtimeSceneMatches =
            checkpoint.runtimeSceneMetadataDirty == current.RuntimeSceneMetadataDirty;

        bool observedAuthoringMatches = string.Equals(
            checkpoint.observedAuthoringSignature ?? "",
            current.LastObservedAuthoringSignature ?? "",
            StringComparison.Ordinal
        );

        bool currentAuthoringMatches = string.Equals(
            checkpoint.currentAuthoringSignature ?? "",
            currentAuthoringSignature,
            StringComparison.Ordinal
        );

        bool surfaceMatches = string.Equals(
            checkpoint.surfaceSettingsSignature ?? "",
            currentSurfaceSignature,
            StringComparison.Ordinal
        );

        bool collisionMatches = string.Equals(
            checkpoint.collisionSettingsSignature ?? "",
            currentCollisionSignature,
            StringComparison.Ordinal
        );

        bool coordinatesMatch =
            missingHeight.Count == 0
            && unexpectedHeight.Count == 0
            && missingHeightStreaming.Count == 0
            && unexpectedHeightStreaming.Count == 0
            && missingSurface.Count == 0
            && unexpectedSurface.Count == 0
            && missingCollision.Count == 0
            && unexpectedCollision.Count == 0;

        bool passed =
            revisionMatches
            && coordinatesMatch
            && fullFlagsMatch
            && addressablesFlagsMatch
            && runtimeSceneMatches
            && observedAuthoringMatches
            && currentAuthoringMatches
            && surfaceMatches
            && collisionMatches;

        return new TerrainRuntimeBakePersistenceValidationResult(
            passed
                ? TerrainRuntimeBakePersistenceValidationOutcome.Passed
                : TerrainRuntimeBakePersistenceValidationOutcome.Failed,
            true,
            true,
            true,
            checkpoint.stateRevision,
            current.StateRevision,
            expectedHeight.Count,
            current.PendingHeightTileCount,
            expectedHeightStreaming.Count,
            current.PendingHeightStreamingTileCount,
            expectedSurface.Count,
            current.PendingSurfaceTileCount,
            expectedCollision.Count,
            current.PendingCollisionChunkCount,
            revisionMatches,
            missingHeight,
            unexpectedHeight,
            missingHeightStreaming,
            unexpectedHeightStreaming,
            missingSurface,
            unexpectedSurface,
            missingCollision,
            unexpectedCollision,
            fullFlagsMatch,
            addressablesFlagsMatch,
            runtimeSceneMatches,
            observedAuthoringMatches,
            currentAuthoringMatches,
            surfaceMatches,
            collisionMatches,
            "",
            passed ? "" : "The current persistent runtime bake state differs from the captured checkpoint.",
            passed
                ? "Pending runtime bake state survived unchanged."
                : "Persistence validation found one or more mismatches."
        );
    }

    public static bool ClearCheckpoint(out string errorMessage)
    {
        errorMessage = "";

        try
        {
            string path = CheckpointPath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            string temporaryPath = path + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                "Could not clear persistence checkpoint.\n\n" +
                exception.Message;
            return false;
        }
    }

    private static void CompareCoordinates(
        IEnumerable<Vector2Int> expected,
        IEnumerable<Vector2Int> current,
        out List<Vector2Int> missing,
        out List<Vector2Int> unexpected
    )
    {
        HashSet<Vector2Int> expectedSet = expected != null
            ? new HashSet<Vector2Int>(expected)
            : new HashSet<Vector2Int>();

        HashSet<Vector2Int> currentSet = current != null
            ? new HashSet<Vector2Int>(current)
            : new HashSet<Vector2Int>();

        missing = new List<Vector2Int>(expectedSet);
        missing.RemoveAll(currentSet.Contains);

        unexpected = new List<Vector2Int>(currentSet);
        unexpected.RemoveAll(expectedSet.Contains);

        missing.Sort(TerrainRuntimeBakeStageValidationResult.CompareCoordinates);
        unexpected.Sort(TerrainRuntimeBakeStageValidationResult.CompareCoordinates);
    }
}
