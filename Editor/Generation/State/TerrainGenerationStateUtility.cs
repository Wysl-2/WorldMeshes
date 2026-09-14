using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static partial class TerrainGenerationStateUtility
{
    // =====================================================
    // VERSIONS
    // =====================================================

    /*
     * Runtime heightmaps bake the current modifier-inclusive composite.
     * Version 10 adds target-surface Replace height-stamp composition.
     */
    public const int RuntimeHeightCompilerVersion =
        10;

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
    // AUTHORING HEIGHTFIELD STATUS
    // =====================================================

    public static GenerationStatus GetAuthoringHeightfieldStatus(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        TerrainGenerationStateEvaluationContext context =
            new TerrainGenerationStateEvaluationContext(
                worldSettings,
                authoringData
            );

        return
            GetAuthoringHeightfieldStatus(
                context
            );
    }

    // =====================================================
    // RUNTIME HEIGHTMAP STATUS
    // =====================================================

    public static GenerationStatus GetHeightmapStatus(
        WorldSettings worldSettings
    )
    {
        TerrainGenerationStateEvaluationContext context =
            new TerrainGenerationStateEvaluationContext(
                worldSettings
            );

        return
            GetHeightmapStatus(
                context
            );
    }

    // =====================================================
    // COLLISION MESH STATUS
    // =====================================================

    public static GenerationStatus GetCollisionMeshStatus(
        WorldSettings worldSettings
    )
    {
        TerrainGenerationStateEvaluationContext context =
            new TerrainGenerationStateEvaluationContext(
                worldSettings
            );

        return
            GetCollisionMeshStatus(
                context
            );
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

        worldSettings.lastGeneratedSurfaceSignature =
            "";

        worldSettings.surfaceMaskGenerationRevision =
            0;

        worldSettings
            .surfaceSourceHeightmapGenerationRevision =
                -1;

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
                worldSettings.heightfieldResolutionPerChunk
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
