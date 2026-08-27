using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TerrainGenerationStateUtility
{
    // =====================================================
    // VERSIONS
    // =====================================================

    /*
     * Incremented because the runtime manifest now records
     * compiled height range metadata used by clipmap bounds.
     */
    public const int RuntimeHeightCompilerVersion =
        3;

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
    // RUNTIME HEIGHTMAP STATUS
    // =====================================================

    public static GenerationStatus GetHeightmapStatus(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            worldSettings.heightmapGenerationRevision <= 0
            ||
            string.IsNullOrEmpty(
                worldSettings.lastGeneratedHeightSignature
            )
        )
        {
            return
                GenerationStatus.NotGenerated;
        }

        TerrainHeightmapManifest runtimeManifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (runtimeManifest == null)
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            !runtimeManifest.isComplete
            ||
            runtimeManifest.compilerVersion !=
                RuntimeHeightCompilerVersion
            ||
            !runtimeManifest.HasValidHeightRange
        )
        {
            return
                GenerationStatus.OutOfDate;
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
            return
                GenerationStatus.OutOfDate;
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
            !authoringManifest.HasValidCommittedHeightRange
            ||
            !TerrainAuthoringStateUtility
                .ManifestMatchesWorldSettings(
                    authoringManifest,
                    worldSettings
                )
        )
        {
            return
                GenerationStatus.OutOfDate;
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
            return
                GenerationStatus.OutOfDate;
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
            return
                GenerationStatus.OutOfDate;
        }

        return
            GenerationStatus.Current;
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
            return
                GenerationStatus.NotGenerated;
        }

        if (
            worldSettings.collisionMeshGenerationRevision <= 0
            ||
            string.IsNullOrEmpty(
                worldSettings.lastGeneratedCollisionSignature
            )
            ||
            worldSettings
                .collisionSourceHeightmapGenerationRevision <
                0
        )
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            !AssetDatabase.IsValidFolder(
                TerrainCollisionMeshGenerator
                    .CollisionMeshFolder
            )
        )
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            GetHeightmapStatus(
                worldSettings
            )
            !=
            GenerationStatus.Current
        )
        {
            return
                GenerationStatus.OutOfDate;
        }

        if (
            worldSettings
                .collisionSourceHeightmapGenerationRevision
            !=
            worldSettings
                .heightmapGenerationRevision
        )
        {
            return
                GenerationStatus.OutOfDate;
        }

        string currentSignature =
            GetCurrentCollisionSettingsSignature(
                worldSettings
            );

        if (
            worldSettings
                .lastGeneratedCollisionSignature
            !=
            currentSignature
        )
        {
            return
                GenerationStatus.OutOfDate;
        }

        return
            GenerationStatus.Current;
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
            string.IsNullOrEmpty(
                authoringSignature
            )
        )
        {
            return;
        }

        worldSettings.lastGeneratedHeightSignature =
            authoringSignature;

        if (
            worldSettings.heightmapGenerationRevision <
            0
        )
        {
            worldSettings.heightmapGenerationRevision =
                0;
        }

        /*
         * Every successful complete runtime compilation gets
         * a new revision. Collision state therefore becomes
         * stale automatically.
         */
        worldSettings.heightmapGenerationRevision++;

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
                "state because the runtime heightmap state " +
                "is not current."
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
            worldSettings.collisionMeshGenerationRevision <
            0
        )
        {
            worldSettings.collisionMeshGenerationRevision =
                0;
        }

        worldSettings
            .collisionMeshGenerationRevision++;

        SaveWorldSettings(
            worldSettings
        );

        return true;
    }

    // =====================================================
    // RESET GENERATED STATE
    // =====================================================

    public static void ResetGeneratedState(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return;
        }

        worldSettings.lastGeneratedHeightSignature =
            "";

        worldSettings.heightmapGenerationRevision =
            0;

        worldSettings.lastGeneratedCollisionSignature =
            "";

        worldSettings.collisionMeshGenerationRevision =
            0;

        worldSettings
            .collisionSourceHeightmapGenerationRevision =
                -1;

        SaveWorldSettings(
            worldSettings
        );
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

        return
            ComputeSHA256(
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

        return
            result.ToString();
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
                return
                    "Current";

            case GenerationStatus.OutOfDate:
                return
                    "Out of Date";

            default:
                return
                    "Not Generated";
        }
    }
}
