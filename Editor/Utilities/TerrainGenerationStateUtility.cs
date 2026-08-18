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
    // GENERATOR VERSIONS
    // =====================================================

    /*
     * Increment this whenever the actual height-generation
     * algorithm changes in a way that means existing
     * heightmaps should be considered outdated.
     *
     * Examples:
     *
     * - changing the Perlin implementation
     * - changing fBM behavior
     * - changing coordinate interpretation
     * - changing how amplitude/base height are applied
     */

    public const int HeightGeneratorVersion =
        1;
    
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

        // -------------------------------------------------
        // No recorded successful generation
        // -------------------------------------------------

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

        // -------------------------------------------------
        // Generated assets missing
        // -------------------------------------------------

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

        // -------------------------------------------------
        // Current desired settings vs last successful sync
        // -------------------------------------------------

        if (
            worldSettings.lastSyncedGridWidth !=
                Mathf.Max(
                    1,
                    worldSettings.gridWidth
                )
            ||
            worldSettings.lastSyncedGridHeight !=
                Mathf.Max(
                    1,
                    worldSettings.gridHeight
                )
            ||
            !FloatMatches(
                worldSettings.lastSyncedChunkSize,
                Mathf.Max(
                    0.01f,
                    worldSettings.chunkSize
                )
            )
            ||
            worldSettings.lastSyncedLOD0Resolution !=
                Mathf.Max(
                    1,
                    worldSettings.lod0Resolution
                )
        )
        {
            return GenerationStatus.OutOfDate;
        }

        // -------------------------------------------------
        // Base mesh revision
        // -------------------------------------------------

        string currentBaseHash =
            GetCurrentBaseMeshHash();

        if (
            string.IsNullOrEmpty(
                currentBaseHash
            )
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
            GetHeightmapStatus(
                worldSettings
            )
            !=
            GenerationStatus.Current
        )
        {
            return GenerationStatus.OutOfDate;
        }

        if (
            worldSettings
                .collisionSourceHeightmapGenerationRevision
            !=
            worldSettings
                .heightmapGenerationRevision
        )
        {
            return GenerationStatus.OutOfDate;
        }

        string currentSignature =
            GetCurrentCollisionSettingsSignature(
                worldSettings
            );

        if (
            worldSettings.lastGeneratedCollisionSignature
            !=
            currentSignature
        )
        {
            return GenerationStatus.OutOfDate;
        }

        return GenerationStatus.Current;
    }

    // =====================================================
    // HEIGHTMAP STATUS
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

        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainHeightmapGenerator
                        .HeightmapManifestPath
                );

        if (manifest == null)
        {
            return GenerationStatus.NotGenerated;
        }

        if (!manifest.isComplete)
        {
            return GenerationStatus.OutOfDate;
        }

        if (
            manifest.generatorVersion !=
            HeightGeneratorVersion
        )
        {
            return GenerationStatus.OutOfDate;
        }

        string currentSignature =
            GetCurrentHeightSettingsSignature(
                worldSettings
            );

        if (
            worldSettings.lastGeneratedHeightSignature !=
            currentSignature
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

        // -------------------------------------------------
        // Never applied
        // -------------------------------------------------

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

        // -------------------------------------------------
        // Dependencies must themselves be current
        // -------------------------------------------------

        if (
            GetChunkMeshStatus(
                worldSettings
            )
            !=
            GenerationStatus.Current
            ||
            GetHeightmapStatus(
                worldSettings
            )
            !=
            GenerationStatus.Current
        )
        {
            return GenerationStatus.OutOfDate;
        }

        // -------------------------------------------------
        // Applied source revisions
        // -------------------------------------------------

        if (
            worldSettings
                .appliedChunkMeshGenerationRevision
            !=
            worldSettings
                .chunkMeshGenerationRevision
            ||
            worldSettings
                .appliedHeightmapGenerationRevision
            !=
            worldSettings
                .heightmapGenerationRevision
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

        // -------------------------------------------------
        // Previous generation configuration
        // -------------------------------------------------

        string previousStateSignature =
            GetStoredChunkStateSignature(
                worldSettings
            );

        // -------------------------------------------------
        // Record current synchronized configuration
        // -------------------------------------------------

        worldSettings.lastSyncedGridWidth =
            Mathf.Max(
                1,
                worldSettings.gridWidth
            );

        worldSettings.lastSyncedGridHeight =
            Mathf.Max(
                1,
                worldSettings.gridHeight
            );

        worldSettings.lastSyncedChunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        worldSettings.lastSyncedLOD0Resolution =
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            );

        worldSettings.lastSyncedBaseMeshHash =
            currentBaseMeshHash;

        // -------------------------------------------------
        // New synchronized configuration
        // -------------------------------------------------

        string newStateSignature =
            GetStoredChunkStateSignature(
                worldSettings
            );

        bool generationConfigurationChanged =
            previousStateSignature !=
            newStateSignature;

        // -------------------------------------------------
        // Revision
        // -------------------------------------------------

        if (
            worldSettings.chunkMeshGenerationRevision <= 0
        )
        {
            /*
             * First successfully tracked chunk generation.
             */
            worldSettings.chunkMeshGenerationRevision =
                1;
        }
        else if (
            chunkAssetsChanged ||
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
            GetHeightmapStatus(
                worldSettings
            )
            !=
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
            worldSettings
                .heightmapGenerationRevision;

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
    // SUCCESSFUL HEIGHTMAP GENERATION
    // =====================================================

    public static void MarkHeightmapsGenerated(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return;
        }

        worldSettings.lastGeneratedHeightSignature =
            GetCurrentHeightSettingsSignature(
                worldSettings
            );

        if (
            worldSettings.heightmapGenerationRevision < 0
        )
        {
            worldSettings.heightmapGenerationRevision =
                0;
        }

        /*
         * Every complete regeneration receives a new
         * revision, even when the settings are identical.
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
            GetChunkMeshStatus(
                worldSettings
            )
            !=
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
            GetHeightmapStatus(
                worldSettings
            )
            !=
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
                worldSettings
                    .chunkMeshGenerationRevision;

        worldSettings
            .appliedHeightmapGenerationRevision =
                worldSettings
                    .heightmapGenerationRevision;

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

        // -------------------------------------------------
        // Chunk meshes
        // -------------------------------------------------

        worldSettings.lastSyncedGridWidth =
            -1;

        worldSettings.lastSyncedGridHeight =
            -1;

        worldSettings.lastSyncedChunkSize =
            -1f;

        worldSettings.lastSyncedLOD0Resolution =
            -1;

        worldSettings.lastSyncedBaseMeshHash =
            "";

        worldSettings.chunkMeshGenerationRevision =
            0;
        
        // -------------------------------------------------
        // Collision meshes
        // -------------------------------------------------

        worldSettings.lastGeneratedCollisionSignature =
            "";

        worldSettings.collisionMeshGenerationRevision =
            0;

        worldSettings
                .collisionSourceHeightmapGenerationRevision =
            -1;

        // -------------------------------------------------
        // Heightmaps
        // -------------------------------------------------

        worldSettings.lastGeneratedHeightSignature =
            "";

        worldSettings.heightmapGenerationRevision =
            0;

        // -------------------------------------------------
        // Applied height
        // -------------------------------------------------

        worldSettings
            .appliedChunkMeshGenerationRevision =
                -1;

        worldSettings
            .appliedHeightmapGenerationRevision =
                -1;
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

        AppendValue(
            builder,
            CollisionGeneratorVersion
        );

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.gridWidth
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.gridHeight
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.heightTileChunkSpan
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            )
        );

        return ComputeSHA256(
            builder.ToString()
        );
    }

    // =====================================================
    // HEIGHT SETTINGS SIGNATURE
    // =====================================================

    public static string GetCurrentHeightSettingsSignature(
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
            "TerrainHeightGenerator"
        );

        AppendValue(
            builder,
            HeightGeneratorVersion
        );

        // -------------------------------------------------
        // World layout
        // -------------------------------------------------

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.gridWidth
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.gridHeight
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            )
        );

        // -------------------------------------------------
        // Height layout
        // -------------------------------------------------

        AppendValue(
            builder,
            Mathf.Max(
                1,
                worldSettings.heightTileChunkSpan
            )
        );

        // -------------------------------------------------
        // Noise configuration
        // -------------------------------------------------

        AppendValue(
            builder,
            worldSettings.heightSeed
        );

        AppendValue(
            builder,
            Mathf.Max(
                0.0001f,
                worldSettings.heightNoiseScale
            )
        );

        AppendValue(
            builder,
            worldSettings.heightBaseHeight
        );

        AppendValue(
            builder,
            Mathf.Max(
                0f,
                worldSettings.heightAmplitude
            )
        );

        AppendValue(
            builder,
            Mathf.Clamp(
                worldSettings.heightOctaves,
                1,
                12
            )
        );

        AppendValue(
            builder,
            Mathf.Clamp01(
                worldSettings.heightPersistence
            )
        );

        AppendValue(
            builder,
            Mathf.Max(
                1f,
                worldSettings.heightLacunarity
            )
        );

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

        AppendValue(
            builder,
            worldSettings.lastSyncedGridWidth
        );

        AppendValue(
            builder,
            worldSettings.lastSyncedGridHeight
        );

        AppendValue(
            builder,
            worldSettings.lastSyncedChunkSize
        );

        AppendValue(
            builder,
            worldSettings.lastSyncedLOD0Resolution
        );

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
            int i = 0;
            i < hashBytes.Length;
            i++
        )
        {
            result.Append(
                hashBytes[i].ToString(
                    "x2"
                )
            );
        }

        return result.ToString();
    }

    // =====================================================
    // FLOAT COMPARISON
    // =====================================================

    private static bool FloatMatches(
        float a,
        float b
    )
    {
        return
            Mathf.Abs(
                a - b
            )
            <=
            0.0001f;
    }

    // =====================================================
    // SAVE
    // =====================================================

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