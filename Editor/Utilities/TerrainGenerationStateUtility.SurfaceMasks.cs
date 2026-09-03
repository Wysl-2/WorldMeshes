using UnityEditor;
using UnityEngine;

/*
 * Stage 8 surface-mask generation state.
 *
 * The existing TerrainGenerationStateUtility remains the single generation
 * status facade; this partial adds the new runtime surface dependency.
 */
public static partial class TerrainGenerationStateUtility
{
    public const int SurfaceMaskCompilerVersion =
        TerrainSurfaceMaskManifest
            .CurrentCompilerVersion;

    // =====================================================
    // SURFACE MASK STATUS
    // =====================================================

    public static GenerationStatus GetSurfaceMaskStatus(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            worldSettings
                .surfaceMaskGenerationRevision <=
                0
            ||
            worldSettings
                .surfaceSourceHeightmapGenerationRevision <=
                0
            ||
            string.IsNullOrEmpty(
                worldSettings
                    .lastGeneratedSurfaceSignature
            )
        )
        {
            return
                GenerationStatus.NotGenerated;
        }

        TerrainSurfaceMaskManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .SurfaceMaskManifestPath
                );

        if (manifest == null)
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            !manifest.isComplete
            ||
            manifest.compilerVersion !=
                SurfaceMaskCompilerVersion
            ||
            manifest.channelLayoutVersion !=
                TerrainSurfaceMaskManifest
                    .CurrentChannelLayoutVersion
        )
        {
            return
                GenerationStatus.OutOfDate;
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

        TerrainHeightmapManifest heightManifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        if (
            heightManifest == null
            ||
            !manifest
                .MatchesHeightLayout(
                    heightManifest
                )
        )
        {
            return
                GenerationStatus.OutOfDate;
        }

        TerrainSurfaceSettings settings =
            AssetDatabase
                .LoadAssetAtPath<TerrainSurfaceSettings>(
                    WorldMeshesPaths
                        .TerrainSurfaceSettingsAssetPath
                );

        if (settings == null)
        {
            return
                GenerationStatus.OutOfDate;
        }

        string settingsSignature =
            TerrainSurfaceSignatureUtility
                .GetSettingsSignature(
                    settings
                );

        string generationSignature =
            GetCurrentSurfaceMaskSignature(
                worldSettings,
                settings
            );

        if (
            string.IsNullOrEmpty(
                settingsSignature
            )
            ||
            string.IsNullOrEmpty(
                generationSignature
            )
            ||
            manifest
                .sourceHeightmapGenerationRevision !=
                worldSettings
                    .heightmapGenerationRevision
            ||
            manifest
                .sourceHeightmapSignature !=
                worldSettings
                    .lastGeneratedHeightSignature
            ||
            manifest
                .surfaceSettingsSignature !=
                settingsSignature
            ||
            manifest
                .surfaceGenerationSignature !=
                generationSignature
            ||
            manifest
                .surfaceMaskGenerationRevision !=
                worldSettings
                    .surfaceMaskGenerationRevision
            ||
            worldSettings
                .surfaceSourceHeightmapGenerationRevision !=
                worldSettings
                    .heightmapGenerationRevision
            ||
            worldSettings
                .lastGeneratedSurfaceSignature !=
                generationSignature
        )
        {
            return
                GenerationStatus.OutOfDate;
        }

        return
            GenerationStatus.Current;
    }

    // =====================================================
    // CURRENT SURFACE SIGNATURE
    // =====================================================

    public static string GetCurrentSurfaceMaskSignature(
        WorldSettings worldSettings,
        TerrainSurfaceSettings settings
    )
    {
        if (
            worldSettings == null
            ||
            settings == null
            ||
            worldSettings
                .heightmapGenerationRevision <=
                0
            ||
            string.IsNullOrEmpty(
                worldSettings
                    .lastGeneratedHeightSignature
            )
        )
        {
            return "";
        }

        string settingsSignature =
            TerrainSurfaceSignatureUtility
                .GetSettingsSignature(
                    settings
                );

        return
            TerrainSurfaceSignatureUtility
                .GetGenerationSignature(
                    SurfaceMaskCompilerVersion,
                    TerrainSurfaceMaskManifest
                        .CurrentChannelLayoutVersion,
                    worldSettings
                        .heightmapGenerationRevision,
                    worldSettings
                        .lastGeneratedHeightSignature,
                    settingsSignature
                );
    }

    // =====================================================
    // SUCCESSFUL SURFACE GENERATION
    // =====================================================

    public static bool MarkSurfaceMasksGenerated(
        WorldSettings worldSettings,
        string generationSignature
    )
    {
        if (
            worldSettings == null
            ||
            string.IsNullOrEmpty(
                generationSignature
            )
        )
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
                "Cannot record surface-mask generation state because the runtime heightmaps are not current."
            );

            return false;
        }

        worldSettings
            .lastGeneratedSurfaceSignature =
                generationSignature;

        worldSettings
            .surfaceSourceHeightmapGenerationRevision =
                worldSettings
                    .heightmapGenerationRevision;

        if (
            worldSettings
                .surfaceMaskGenerationRevision <
                0
        )
        {
            worldSettings
                .surfaceMaskGenerationRevision =
                    0;
        }

        worldSettings
            .surfaceMaskGenerationRevision++;

        SaveWorldSettings(
            worldSettings
        );

        return true;
    }
}
