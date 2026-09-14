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
        TerrainGenerationStateEvaluationContext context =
            new TerrainGenerationStateEvaluationContext(
                worldSettings
            );

        return
            GetSurfaceMaskStatus(
                context
            );
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
