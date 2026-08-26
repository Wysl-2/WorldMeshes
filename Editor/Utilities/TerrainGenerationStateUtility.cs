using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TerrainGenerationStateUtility
{
    // =====================================================
    // PATHS
    // =====================================================

    private const string BaseMeshPath =
        WorldMeshesPaths.BaseMeshAssetPath;

    private const string ChunkMeshFolder =
        WorldMeshesPaths.GeneratedChunkMeshes;

    // =====================================================
    // VERSIONS
    // =====================================================


    /*
     * Increment whenever the authoring -> runtime height
     * compilation algorithm changes in a way that requires
     * existing runtime heightmaps to be recompiled.
     */
    public const int RuntimeHeightCompilerVersion =
        2;

    public const int CollisionGeneratorVersion =
        1;

    // =====================================================
    // STATUS
    // =====================================================

    public enum GenerationStatus
    {
        NotGenerated,
        OutOfDate,
        Current
    }

    // =====================================================
    // CHUNK MESH STATUS
    // =====================================================

    public static GenerationStatus GetChunkMeshStatus(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            worldSettings.chunkMeshGenerationRevision <= 0
            ||
            worldSettings.lastSyncedGridWidth < 1
            ||
            worldSettings.lastSyncedGridHeight < 1
            ||
            worldSettings.lastSyncedChunkSize <= 0f
            ||
            worldSettings.lastSyncedLOD0Resolution < 1
            ||
            string.IsNullOrEmpty(
                worldSettings.lastSyncedBaseMeshHash
            )
        )
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            !AssetDatabase.IsValidFolder(
                ChunkMeshFolder
            )
        )
        {
            return GenerationStatus.NotGenerated;
        }

        Mesh baseMesh =
            AssetDatabase.LoadAssetAtPath<Mesh>(
                BaseMeshPath
            );

        if (baseMesh == null)
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            worldSettings.lastSyncedGridWidth !=
                Mathf.Max(1, worldSettings.gridWidth)
            ||
            worldSettings.lastSyncedGridHeight !=
                Mathf.Max(1, worldSettings.gridHeight)
            ||
            !FloatMatches(
                worldSettings.lastSyncedChunkSize,
                Mathf.Max(0.01f, worldSettings.chunkSize)
            )
            ||
            worldSettings.lastSyncedLOD0Resolution !=
                Mathf.Max(1, worldSettings.lod0Resolution)
        )
        {
            return GenerationStatus.OutOfDate;
        }

        string currentBaseHash =
            GetCurrentBaseMeshHash();

        if (
            string.IsNullOrEmpty(currentBaseHash)
            ||
            currentBaseHash !=
                worldSettings.lastSyncedBaseMeshHash
        )
        {
            return GenerationStatus.OutOfDate;
        }

        return GenerationStatus.Current;
    }

    // =====================================================
    // COLLISION MESH STATUS
    // =====================================================

    public static GenerationStatus GetCollisionMeshStatus(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            worldSettings.collisionMeshGenerationRevision <= 0
            ||
            string.IsNullOrEmpty(
                worldSettings.lastGeneratedCollisionSignature
            )
            ||
            worldSettings
                .collisionSourceHeightmapGenerationRevision < 0
        )
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            !AssetDatabase.IsValidFolder(
                TerrainCollisionMeshGenerator
                    .CollisionMeshFolder
            )
        )
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            GetHeightmapStatus(worldSettings) !=
            GenerationStatus.Current
        )
        {
            return GenerationStatus.OutOfDate;
        }

        if (
            worldSettings
                .collisionSourceHeightmapGenerationRevision
            !=
            worldSettings.heightmapGenerationRevision
        )
        {
            return GenerationStatus.OutOfDate;
        }

        string currentSignature =
            GetCurrentCollisionSettingsSignature(
                worldSettings
            );

        if (
            worldSettings.lastGeneratedCollisionSignature !=
            currentSignature
        )
        {
            return GenerationStatus.OutOfDate;
        }

        return GenerationStatus.Current;
    }

    // =====================================================
    // RUNTIME HEIGHTMAP STATUS
    // =====================================================

    public static GenerationStatus GetHeightmapStatus(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            worldSettings.heightmapGenerationRevision <= 0
            ||
            string.IsNullOrEmpty(
                worldSettings.lastGeneratedHeightSignature
            )
        )
        {
            return GenerationStatus.NotGenerated;
        }

        TerrainHeightmapManifest runtimeManifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (runtimeManifest == null)
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            !runtimeManifest.isComplete
            ||
            runtimeManifest.compilerVersion !=
                RuntimeHeightCompilerVersion
        )
        {
            return GenerationStatus.OutOfDate;
        }

        TerrainAuthoringData authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        if (
            authoringData == null
            ||
            authoringData.authoringRevision <= 0
        )
        {
            return GenerationStatus.OutOfDate;
        }

        TerrainAuthoringHeightManifest authoringManifest =
            TerrainAuthoringStateUtility
                .LoadAuthoringHeightManifest();

        if (
            authoringManifest == null
            ||
            !authoringManifest.isComplete
            ||
            authoringManifest.manifestVersion !=
                TerrainAuthoringHeightManifest.CurrentVersion
            ||
            authoringManifest.committedHeightRevision <= 0
            ||
            !TerrainAuthoringStateUtility
                .ManifestMatchesWorldSettings(
                    authoringManifest,
                    worldSettings
                )
        )
        {
            return GenerationStatus.OutOfDate;
        }

        string currentAuthoringSignature =
            TerrainAuthoringStateUtility
                .GetCurrentAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            string.IsNullOrEmpty(
                currentAuthoringSignature
            )
        )
        {
            return GenerationStatus.OutOfDate;
        }

        if (
            runtimeManifest.sourceAuthoringRevision !=
                authoringData.authoringRevision
            ||
            runtimeManifest.sourceAuthoringSignature !=
                currentAuthoringSignature
            ||
            runtimeManifest.sourceAuthoringContentHash !=
                authoringManifest.committedContentHash
            ||
            worldSettings.lastGeneratedHeightSignature !=
                currentAuthoringSignature
        )
        {
            return GenerationStatus.OutOfDate;
        }

        return GenerationStatus.Current;
    }

    // =====================================================
    // HEIGHT APPLICATION STATUS
    // =====================================================

    public static GenerationStatus
        GetHeightApplicationStatus(
            WorldSettings worldSettings
        )
    {
        if (worldSettings == null)
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            worldSettings
                .appliedChunkMeshGenerationRevision < 0
            ||
            worldSettings
                .appliedHeightmapGenerationRevision < 0
        )
        {
            return GenerationStatus.NotGenerated;
        }

        if (
            GetChunkMeshStatus(worldSettings) !=
                GenerationStatus.Current
            ||
            GetHeightmapStatus(worldSettings) !=
                GenerationStatus.Current
        )
        {
            return GenerationStatus.OutOfDate;
        }

        if (
            worldSettings
                .appliedChunkMeshGenerationRevision
            !=
            worldSettings.chunkMeshGenerationRevision
            ||
            worldSettings
                .appliedHeightmapGenerationRevision
            !=
            worldSettings.heightmapGenerationRevision
        )
        {
            return GenerationStatus.OutOfDate;
        }

        return GenerationStatus.Current;
    }

    // =====================================================
    // SUCCESSFUL CHUNK SYNCHRONIZATION
    // =====================================================

    public static void MarkChunkSyncSuccessful(
        WorldSettings worldSettings,
        string currentBaseMeshHash,
        bool chunkAssetsChanged
    )
    {
        if (worldSettings == null)
        {
            return;
        }

        string previousStateSignature =
            GetStoredChunkStateSignature(
                worldSettings
            );

        worldSettings.lastSyncedGridWidth =
            Mathf.Max(1, worldSettings.gridWidth);

        worldSettings.lastSyncedGridHeight =
            Mathf.Max(1, worldSettings.gridHeight);

        worldSettings.lastSyncedChunkSize =
            Mathf.Max(0.01f, worldSettings.chunkSize);

        worldSettings.lastSyncedLOD0Resolution =
            Mathf.Max(1, worldSettings.lod0Resolution);

        worldSettings.lastSyncedBaseMeshHash =
            currentBaseMeshHash;

        string newStateSignature =
            GetStoredChunkStateSignature(
                worldSettings
            );

        bool generationConfigurationChanged =
            previousStateSignature !=
            newStateSignature;

        if (
            worldSettings.chunkMeshGenerationRevision <= 0
        )
        {
            worldSettings.chunkMeshGenerationRevision =
                1;
        }
        else if (
            chunkAssetsChanged
            ||
            generationConfigurationChanged
        )
        {
            worldSettings.chunkMeshGenerationRevision++;
        }

        SaveWorldSettings(
            worldSettings
        );
    }

    // =====================================================
    // SUCCESSFUL COLLISION GENERATION
    // =====================================================

    public static bool MarkCollisionMeshesGenerated(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return false;
        }

        if (
            GetHeightmapStatus(worldSettings) !=
            GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Cannot record collision mesh generation " +
                "state because the heightmap state is not current."
            );

            return false;
        }

        worldSettings.lastGeneratedCollisionSignature =
            GetCurrentCollisionSettingsSignature(
                worldSettings
            );

        worldSettings
            .collisionSourceHeightmapGenerationRevision =
                worldSettings.heightmapGenerationRevision;

        if (
            worldSettings.collisionMeshGenerationRevision < 0
        )
        {
            worldSettings.collisionMeshGenerationRevision =
                0;
        }

        worldSettings.collisionMeshGenerationRevision++;

        SaveWorldSettings(
            worldSettings
        );

        return true;
    }

    // =====================================================
    // SUCCESSFUL RUNTIME HEIGHTMAP COMPILATION
    // =====================================================

    public static void MarkHeightmapsCompiled(
        WorldSettings worldSettings,
        string authoringSignature
    )
    {
        if (
            worldSettings == null
            ||
            string.IsNullOrEmpty(authoringSignature)
        )
        {
            return;
        }

        worldSettings.lastGeneratedHeightSignature =
            authoringSignature;

        if (
            worldSettings.heightmapGenerationRevision < 0
        )
        {
            worldSettings.heightmapGenerationRevision =
                0;
        }

        /*
         * Every successful complete runtime compilation gets
         * a new revision. Existing collision/application state
         * will therefore become stale automatically.
         */
        worldSettings.heightmapGenerationRevision++;

        SaveWorldSettings(
            worldSettings
        );
    }


    // =====================================================
    // SUCCESSFUL HEIGHT APPLICATION
    // =====================================================

    public static bool MarkHeightApplicationSuccessful(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return false;
        }

        if (
            GetChunkMeshStatus(worldSettings) !=
            GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Cannot record height application state.\n\n" +
                "Chunk mesh generation state is not current."
            );

            return false;
        }

        if (
            GetHeightmapStatus(worldSettings) !=
            GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Cannot record height application state.\n\n" +
                "Heightmap generation state is not current."
            );

            return false;
        }

        worldSettings
            .appliedChunkMeshGenerationRevision =
                worldSettings.chunkMeshGenerationRevision;

        worldSettings
            .appliedHeightmapGenerationRevision =
                worldSettings.heightmapGenerationRevision;

        SaveWorldSettings(
            worldSettings
        );

        return true;
    }

    // =====================================================
    // RESET
    // =====================================================

    public static void ResetGeneratedState(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return;
        }

        worldSettings.lastSyncedGridWidth = -1;
        worldSettings.lastSyncedGridHeight = -1;
        worldSettings.lastSyncedChunkSize = -1f;
        worldSettings.lastSyncedLOD0Resolution = -1;
        worldSettings.lastSyncedBaseMeshHash = "";
        worldSettings.chunkMeshGenerationRevision = 0;

        worldSettings.lastGeneratedCollisionSignature = "";
        worldSettings.collisionMeshGenerationRevision = 0;
        worldSettings
            .collisionSourceHeightmapGenerationRevision = -1;

        worldSettings.lastGeneratedHeightSignature = "";
        worldSettings.heightmapGenerationRevision = 0;

        worldSettings
            .appliedChunkMeshGenerationRevision = -1;

        worldSettings
            .appliedHeightmapGenerationRevision = -1;
    }

    // =====================================================
    // COLLISION SETTINGS SIGNATURE
    // =====================================================

    public static string GetCurrentCollisionSettingsSignature(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "TerrainCollisionGenerator"
        );

        AppendValue(builder, CollisionGeneratorVersion);
        AppendValue(builder, Mathf.Max(1, worldSettings.gridWidth));
        AppendValue(builder, Mathf.Max(1, worldSettings.gridHeight));
        AppendValue(builder, Mathf.Max(0.01f, worldSettings.chunkSize));
        AppendValue(builder, Mathf.Max(1, worldSettings.lod0Resolution));
        AppendValue(builder, Mathf.Max(1, worldSettings.heightTileChunkSpan));
        AppendValue(builder, Mathf.Max(1, worldSettings.collisionResolution));

        return ComputeSHA256(
            builder.ToString()
        );
    }



    // =====================================================
    // CURRENT BASE MESH HASH
    // =====================================================

    public static string GetCurrentBaseMeshHash()
    {
        Object baseAsset =
            AssetDatabase.LoadMainAssetAtPath(
                BaseMeshPath
            );

        if (baseAsset == null)
        {
            return "";
        }

        return
            AssetDatabase
                .GetAssetDependencyHash(
                    BaseMeshPath
                )
                .ToString();
    }

    // =====================================================
    // STORED CHUNK STATE SIGNATURE
    // =====================================================

    private static string GetStoredChunkStateSignature(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "TerrainChunkMeshes"
        );

        AppendValue(builder, worldSettings.lastSyncedGridWidth);
        AppendValue(builder, worldSettings.lastSyncedGridHeight);
        AppendValue(builder, worldSettings.lastSyncedChunkSize);
        AppendValue(builder, worldSettings.lastSyncedLOD0Resolution);

        builder.Append('|');

        builder.Append(
            worldSettings.lastSyncedBaseMeshHash ??
            ""
        );

        return ComputeSHA256(
            builder.ToString()
        );
    }

    // =====================================================
    // SIGNATURE BUILDING
    // =====================================================

    private static void AppendValue(
        StringBuilder builder,
        int value
    )
    {
        builder.Append('|');

        builder.Append(
            value.ToString(
                CultureInfo.InvariantCulture
            )
        );
    }

    private static void AppendValue(
        StringBuilder builder,
        float value
    )
    {
        builder.Append('|');

        builder.Append(
            value.ToString(
                "R",
                CultureInfo.InvariantCulture
            )
        );
    }

    private static string ComputeSHA256(
        string value
    )
    {
        byte[] inputBytes =
            Encoding.UTF8.GetBytes(
                value
            );

        byte[] hashBytes;

        using (
            SHA256 sha256 =
                SHA256.Create()
        )
        {
            hashBytes =
                sha256.ComputeHash(
                    inputBytes
                );
        }

        StringBuilder result =
            new StringBuilder(
                hashBytes.Length * 2
            );

        for (
            int index = 0;
            index < hashBytes.Length;
            index++
        )
        {
            result.Append(
                hashBytes[index].ToString(
                    "x2"
                )
            );
        }

        return result.ToString();
    }

    private static bool FloatMatches(
        float a,
        float b
    )
    {
        return
            Mathf.Abs(a - b) <=
            0.0001f;
    }

    private static void SaveWorldSettings(
        WorldSettings worldSettings
    )
    {
        EditorUtility.SetDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );
    }

    // =====================================================
    // LABEL
    // =====================================================

    public static string GetStatusLabel(
        GenerationStatus status
    )
    {
        switch (status)
        {
            case GenerationStatus.Current:
                return "Current";

            case GenerationStatus.OutOfDate:
                return "Out of Date";

            default:
                return "Not Generated";
        }
    }
}
