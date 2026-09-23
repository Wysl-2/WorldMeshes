using UnityEditor;

/*
 * Short-lived cache for one logical generation-state evaluation.
 *
 * The context deliberately does not survive across unrelated operations.
 * Expensive shared inputs are evaluated lazily, reused by nested status
 * checks, and then discarded with the owning operation.
 */
internal enum TerrainGenerationStateEvaluationMode
{
    IntegrityVerified,
    Operational
}

internal sealed class TerrainGenerationStateEvaluationContext
{
    private readonly WorldSettings worldSettings;
    private readonly TerrainGenerationStateEvaluationMode evaluationMode;

    private TerrainAuthoringData authoringData;
    private bool authoringDataEvaluated;

    private TerrainAuthoringHeightManifest authoringHeightManifest;
    private bool authoringHeightManifestEvaluated;

    private TerrainHeightmapManifest heightmapManifest;
    private bool heightmapManifestEvaluated;

    private TerrainSurfaceMaskManifest surfaceMaskManifest;
    private bool surfaceMaskManifestEvaluated;

    private TerrainSurfaceSettings surfaceSettings;
    private bool surfaceSettingsEvaluated;

    private string authoringSignature = "";
    private bool authoringSignatureEvaluated;

    private TerrainRuntimeIntegrityAuditResult integrityAudit;
    private bool integrityAuditEvaluated;

    private TerrainGenerationStateUtility.GenerationStatus
        authoringHeightfieldStatus;
    private bool authoringHeightfieldStatusEvaluated;

    private TerrainGenerationStateUtility.GenerationStatus
        heightmapStatus;
    private bool heightmapStatusEvaluated;

    private TerrainGenerationStateUtility.GenerationStatus
        heightStreamingStatus;
    private bool heightStreamingStatusEvaluated;

    private TerrainGenerationStateUtility.GenerationStatus
        surfaceMaskStatus;
    private bool surfaceMaskStatusEvaluated;

    private TerrainGenerationStateUtility.GenerationStatus
        collisionMeshStatus;
    private bool collisionMeshStatusEvaluated;

    internal TerrainGenerationStateEvaluationContext(
        WorldSettings worldSettings
    )
        : this(
            worldSettings,
            TerrainGenerationStateEvaluationMode.IntegrityVerified
        )
    {
    }

    internal TerrainGenerationStateEvaluationContext(
        WorldSettings worldSettings,
        TerrainGenerationStateEvaluationMode evaluationMode
    )
    {
        this.worldSettings =
            worldSettings;

        this.evaluationMode =
            evaluationMode;
    }

    internal TerrainGenerationStateEvaluationContext(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
        : this(
            worldSettings,
            authoringData,
            TerrainGenerationStateEvaluationMode.IntegrityVerified
        )
    {
    }

    internal TerrainGenerationStateEvaluationContext(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        TerrainGenerationStateEvaluationMode evaluationMode
    )
    {
        this.worldSettings =
            worldSettings;

        this.authoringData =
            authoringData;

        this.evaluationMode =
            evaluationMode;

        authoringDataEvaluated =
            true;
    }

    internal WorldSettings WorldSettings
    {
        get
        {
            return worldSettings;
        }
    }

    internal bool RequiresDeepIntegrityVerification =>
        evaluationMode ==
        TerrainGenerationStateEvaluationMode.IntegrityVerified;

    internal TerrainAuthoringData AuthoringData
    {
        get
        {
            if (!authoringDataEvaluated)
            {
                authoringData =
                    AssetDatabase
                        .LoadAssetAtPath<TerrainAuthoringData>(
                            WorldMeshesPaths
                                .TerrainAuthoringDataAssetPath
                        );

                authoringDataEvaluated =
                    true;
            }

            return authoringData;
        }
    }

    internal TerrainAuthoringHeightManifest AuthoringHeightManifest
    {
        get
        {
            if (!authoringHeightManifestEvaluated)
            {
                authoringHeightManifest =
                    TerrainAuthoringStateUtility
                        .LoadAuthoringHeightManifest();

                authoringHeightManifestEvaluated =
                    true;
            }

            return authoringHeightManifest;
        }
    }

    internal TerrainHeightmapManifest HeightmapManifest
    {
        get
        {
            if (!heightmapManifestEvaluated)
            {
                heightmapManifest =
                    AssetDatabase
                        .LoadAssetAtPath<TerrainHeightmapManifest>(
                            TerrainRuntimeHeightAssetUtility
                                .HeightmapManifestPath
                        );

                heightmapManifestEvaluated =
                    true;
            }

            return heightmapManifest;
        }
    }

    internal TerrainSurfaceMaskManifest SurfaceMaskManifest
    {
        get
        {
            if (!surfaceMaskManifestEvaluated)
            {
                surfaceMaskManifest =
                    AssetDatabase
                        .LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                            TerrainRuntimeSurfaceMaskAssetUtility
                                .SurfaceMaskManifestPath
                        );

                surfaceMaskManifestEvaluated =
                    true;
            }

            return surfaceMaskManifest;
        }
    }

    internal TerrainSurfaceSettings SurfaceSettings
    {
        get
        {
            if (!surfaceSettingsEvaluated)
            {
                surfaceSettings =
                    AssetDatabase
                        .LoadAssetAtPath<TerrainSurfaceSettings>(
                            WorldMeshesPaths
                                .TerrainSurfaceSettingsAssetPath
                        );

                surfaceSettingsEvaluated =
                    true;
            }

            return surfaceSettings;
        }
    }

    internal string AuthoringSignature
    {
        get
        {
            if (!authoringSignatureEvaluated)
            {
                TerrainAuthoringData currentAuthoringData =
                    AuthoringData;

                authoringSignature =
                    worldSettings != null
                    &&
                    currentAuthoringData != null
                        ? TerrainAuthoringStateUtility
                            .GetOverallAuthoringSignature(
                                worldSettings,
                                currentAuthoringData
                            )
                        : "";

                authoringSignatureEvaluated =
                    true;
            }

            return authoringSignature;
        }
    }

    internal TerrainRuntimeIntegrityAuditResult IntegrityAudit
    {
        get
        {
            if (!integrityAuditEvaluated)
            {
                integrityAudit =
                    worldSettings != null
                        ? TerrainRuntimeIntegrityAuditUtility
                            .GetCachedGeneratedDataAudit(
                                worldSettings
                            )
                        : null;

                integrityAuditEvaluated =
                    true;
            }

            return integrityAudit;
        }
    }

    internal bool TryGetAuthoringHeightfieldStatus(
        out TerrainGenerationStateUtility.GenerationStatus status
    )
    {
        status =
            authoringHeightfieldStatus;

        return
            authoringHeightfieldStatusEvaluated;
    }

    internal TerrainGenerationStateUtility.GenerationStatus
        CacheAuthoringHeightfieldStatus(
            TerrainGenerationStateUtility.GenerationStatus status
        )
    {
        authoringHeightfieldStatus =
            status;

        authoringHeightfieldStatusEvaluated =
            true;

        return status;
    }

    internal bool TryGetHeightmapStatus(
        out TerrainGenerationStateUtility.GenerationStatus status
    )
    {
        status =
            heightmapStatus;

        return
            heightmapStatusEvaluated;
    }

    internal TerrainGenerationStateUtility.GenerationStatus
        CacheHeightmapStatus(
            TerrainGenerationStateUtility.GenerationStatus status
        )
    {
        heightmapStatus =
            status;

        heightmapStatusEvaluated =
            true;

        return status;
    }

    internal bool TryGetHeightStreamingStatus(
        out TerrainGenerationStateUtility.GenerationStatus status
    )
    {
        status =
            heightStreamingStatus;

        return
            heightStreamingStatusEvaluated;
    }

    internal TerrainGenerationStateUtility.GenerationStatus
        CacheHeightStreamingStatus(
            TerrainGenerationStateUtility.GenerationStatus status
        )
    {
        heightStreamingStatus =
            status;

        heightStreamingStatusEvaluated =
            true;

        return status;
    }

    internal bool TryGetSurfaceMaskStatus(
        out TerrainGenerationStateUtility.GenerationStatus status
    )
    {
        status =
            surfaceMaskStatus;

        return
            surfaceMaskStatusEvaluated;
    }

    internal TerrainGenerationStateUtility.GenerationStatus
        CacheSurfaceMaskStatus(
            TerrainGenerationStateUtility.GenerationStatus status
        )
    {
        surfaceMaskStatus =
            status;

        surfaceMaskStatusEvaluated =
            true;

        return status;
    }

    internal bool TryGetCollisionMeshStatus(
        out TerrainGenerationStateUtility.GenerationStatus status
    )
    {
        status =
            collisionMeshStatus;

        return
            collisionMeshStatusEvaluated;
    }

    internal TerrainGenerationStateUtility.GenerationStatus
        CacheCollisionMeshStatus(
            TerrainGenerationStateUtility.GenerationStatus status
        )
    {
        collisionMeshStatus =
            status;

        collisionMeshStatusEvaluated =
            true;

        return status;
    }
}

/*
 * Immutable values retained by Diagnostics after its short-lived evaluation
 * context has been discarded. This contains no mutable context state and no
 * long-lived authoring-signature cache.
 */
internal readonly struct TerrainGenerationStateEvaluationResult
{
    internal TerrainGenerationStateUtility.GenerationStatus
        AuthoringHeightfieldStatus
    {
        get;
    }

    internal TerrainGenerationStateUtility.GenerationStatus
        HeightmapStatus
    {
        get;
    }

    internal TerrainGenerationStateUtility.GenerationStatus
        HeightStreamingStatus
    {
        get;
    }

    internal TerrainGenerationStateUtility.GenerationStatus
        SurfaceMaskStatus
    {
        get;
    }

    internal TerrainGenerationStateUtility.GenerationStatus
        CollisionMeshStatus
    {
        get;
    }

    internal TerrainGenerationStateEvaluationResult(
        TerrainGenerationStateUtility.GenerationStatus
            authoringHeightfieldStatus,
        TerrainGenerationStateUtility.GenerationStatus
            heightmapStatus,
        TerrainGenerationStateUtility.GenerationStatus
            heightStreamingStatus,
        TerrainGenerationStateUtility.GenerationStatus
            surfaceMaskStatus,
        TerrainGenerationStateUtility.GenerationStatus
            collisionMeshStatus
    )
    {
        AuthoringHeightfieldStatus =
            authoringHeightfieldStatus;

        HeightmapStatus =
            heightmapStatus;

        HeightStreamingStatus =
            heightStreamingStatus;

        SurfaceMaskStatus =
            surfaceMaskStatus;

        CollisionMeshStatus =
            collisionMeshStatus;
    }
}

public static partial class TerrainGenerationStateUtility
{
    internal static TerrainGenerationStateEvaluationResult
        EvaluateGenerationState(
            WorldSettings worldSettings,
            TerrainAuthoringData authoringData
        )
    {
        return EvaluateGenerationState(
            worldSettings,
            authoringData,
            TerrainGenerationStateEvaluationMode.IntegrityVerified
        );
    }

    internal static TerrainGenerationStateEvaluationResult
        EvaluateOperationalGenerationState(
            WorldSettings worldSettings,
            TerrainAuthoringData authoringData
        )
    {
        return EvaluateGenerationState(
            worldSettings,
            authoringData,
            TerrainGenerationStateEvaluationMode.Operational
        );
    }

    private static TerrainGenerationStateEvaluationResult
        EvaluateGenerationState(
            WorldSettings worldSettings,
            TerrainAuthoringData authoringData,
            TerrainGenerationStateEvaluationMode evaluationMode
        )
    {
        TerrainGenerationStateEvaluationContext context =
            authoringData != null
                ? new TerrainGenerationStateEvaluationContext(
                    worldSettings,
                    authoringData,
                    evaluationMode
                )
                : new TerrainGenerationStateEvaluationContext(
                    worldSettings,
                    evaluationMode
                );

        return
            new TerrainGenerationStateEvaluationResult(
                GetAuthoringHeightfieldStatus(
                    context
                ),
                GetHeightmapStatus(
                    context
                ),
                GetHeightStreamingStatus(
                    context
                ),
                GetSurfaceMaskStatus(
                    context
                ),
                GetCollisionMeshStatus(
                    context
                )
            );
    }

    internal static GenerationStatus GetAuthoringHeightfieldStatus(
        TerrainGenerationStateEvaluationContext context
    )
    {
        if (context == null)
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            context.TryGetAuthoringHeightfieldStatus(
                out GenerationStatus cachedStatus
            )
        )
        {
            return cachedStatus;
        }

        WorldSettings worldSettings =
            context.WorldSettings;

        TerrainAuthoringData authoringData =
            context.AuthoringData;

        if (
            worldSettings == null
            ||
            authoringData == null
            ||
            authoringData.authoringRevision <= 0
        )
        {
            return
                context.CacheAuthoringHeightfieldStatus(
                    GenerationStatus.NotGenerated
                );
        }

        TerrainAuthoringHeightManifest manifest =
            context.AuthoringHeightManifest;

        if (manifest == null)
        {
            return
                context.CacheAuthoringHeightfieldStatus(
                    GenerationStatus.NotGenerated
                );
        }

        if (
            !manifest.isComplete
            ||
            manifest.manifestVersion !=
                TerrainAuthoringHeightManifest.CurrentVersion
            ||
            manifest.committedHeightRevision <= 0
            ||
            !manifest.HasValidCommittedHeightRange
            ||
            !TerrainAuthoringStateUtility
                .ManifestMatchesWorldSettings(
                    manifest,
                    worldSettings
                )
        )
        {
            return
                context.CacheAuthoringHeightfieldStatus(
                    GenerationStatus.OutOfDate
                );
        }

        string signature =
            context.AuthoringSignature;

        if (
            string.IsNullOrEmpty(
                signature
            )
        )
        {
            return
                context.CacheAuthoringHeightfieldStatus(
                    GenerationStatus.OutOfDate
                );
        }

        return
            context.CacheAuthoringHeightfieldStatus(
                GenerationStatus.Current
            );
    }

    internal static GenerationStatus GetHeightmapStatus(
        TerrainGenerationStateEvaluationContext context
    )
    {
        if (context == null)
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            context.TryGetHeightmapStatus(
                out GenerationStatus cachedStatus
            )
        )
        {
            return cachedStatus;
        }

        WorldSettings worldSettings =
            context.WorldSettings;

        if (worldSettings == null)
        {
            return
                context.CacheHeightmapStatus(
                    GenerationStatus.NotGenerated
                );
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
                context.CacheHeightmapStatus(
                    GenerationStatus.NotGenerated
                );
        }

        TerrainHeightmapManifest runtimeManifest =
            context.HeightmapManifest;

        if (runtimeManifest == null)
        {
            return
                context.CacheHeightmapStatus(
                    GenerationStatus.NotGenerated
                );
        }

        if (
            !runtimeManifest.isComplete
            ||
            runtimeManifest.compilerVersion !=
                RuntimeHeightCompilerVersion
            ||
            !runtimeManifest.HasCompleteTileHeightRanges
            ||
            !runtimeManifest.HasValidHeightRange
        )
        {
            return
                context.CacheHeightmapStatus(
                    GenerationStatus.OutOfDate
                );
        }

        TerrainAuthoringData authoringData =
            context.AuthoringData;

        if (
            GetAuthoringHeightfieldStatus(
                context
            )
            !=
            GenerationStatus.Current
        )
        {
            return
                context.CacheHeightmapStatus(
                    GenerationStatus.OutOfDate
                );
        }

        TerrainAuthoringHeightManifest authoringManifest =
            context.AuthoringHeightManifest;

        string currentAuthoringSignature =
            context.AuthoringSignature;

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
                context.CacheHeightmapStatus(
                    GenerationStatus.OutOfDate
                );
        }

        if (context.RequiresDeepIntegrityVerification)
        {
            TerrainRuntimeGeneratedDataIntegrityResult heightIntegrity =
                context.IntegrityAudit != null
                    ? context.IntegrityAudit.Height
                    : null;

            if (
                heightIntegrity == null
                ||
                !heightIntegrity.IsValid
            )
            {
                return
                    context.CacheHeightmapStatus(
                        GenerationStatus.OutOfDate
                    );
            }
        }

        return
            context.CacheHeightmapStatus(
                GenerationStatus.Current
            );
    }

    internal static GenerationStatus GetSurfaceMaskStatus(
        TerrainGenerationStateEvaluationContext context
    )
    {
        if (context == null)
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            context.TryGetSurfaceMaskStatus(
                out GenerationStatus cachedStatus
            )
        )
        {
            return cachedStatus;
        }

        WorldSettings worldSettings =
            context.WorldSettings;

        if (worldSettings == null)
        {
            return
                context.CacheSurfaceMaskStatus(
                    GenerationStatus.NotGenerated
                );
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
                context.CacheSurfaceMaskStatus(
                    GenerationStatus.NotGenerated
                );
        }

        TerrainSurfaceMaskManifest manifest =
            context.SurfaceMaskManifest;

        if (manifest == null)
        {
            return
                context.CacheSurfaceMaskStatus(
                    GenerationStatus.NotGenerated
                );
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
                context.CacheSurfaceMaskStatus(
                    GenerationStatus.OutOfDate
                );
        }

        if (
            GetHeightmapStatus(
                context
            )
            !=
            GenerationStatus.Current
        )
        {
            return
                context.CacheSurfaceMaskStatus(
                    GenerationStatus.OutOfDate
                );
        }

        TerrainHeightmapManifest heightManifest =
            context.HeightmapManifest;

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
                context.CacheSurfaceMaskStatus(
                    GenerationStatus.OutOfDate
                );
        }

        TerrainSurfaceSettings settings =
            context.SurfaceSettings;

        if (settings == null)
        {
            return
                context.CacheSurfaceMaskStatus(
                    GenerationStatus.OutOfDate
                );
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
                context.CacheSurfaceMaskStatus(
                    GenerationStatus.OutOfDate
                );
        }

        if (context.RequiresDeepIntegrityVerification)
        {
            TerrainRuntimeGeneratedDataIntegrityResult surfaceIntegrity =
                context.IntegrityAudit != null
                    ? context.IntegrityAudit.Surface
                    : null;

            if (
                surfaceIntegrity == null
                ||
                !surfaceIntegrity.IsValid
            )
            {
                return
                    context.CacheSurfaceMaskStatus(
                        GenerationStatus.OutOfDate
                    );
            }
        }

        return
            context.CacheSurfaceMaskStatus(
                GenerationStatus.Current
            );
    }

    internal static GenerationStatus GetCollisionMeshStatus(
        TerrainGenerationStateEvaluationContext context
    )
    {
        if (context == null)
        {
            return
                GenerationStatus.NotGenerated;
        }

        if (
            context.TryGetCollisionMeshStatus(
                out GenerationStatus cachedStatus
            )
        )
        {
            return cachedStatus;
        }

        WorldSettings worldSettings =
            context.WorldSettings;

        if (worldSettings == null)
        {
            return
                context.CacheCollisionMeshStatus(
                    GenerationStatus.NotGenerated
                );
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
                context.CacheCollisionMeshStatus(
                    GenerationStatus.NotGenerated
                );
        }

        if (
            !AssetDatabase.IsValidFolder(
                TerrainCollisionMeshGenerator
                    .CollisionMeshFolder
            )
        )
        {
            return
                context.CacheCollisionMeshStatus(
                    GenerationStatus.NotGenerated
                );
        }

        if (
            GetHeightmapStatus(
                context
            )
            !=
            GenerationStatus.Current
        )
        {
            return
                context.CacheCollisionMeshStatus(
                    GenerationStatus.OutOfDate
                );
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
                context.CacheCollisionMeshStatus(
                    GenerationStatus.OutOfDate
                );
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
                context.CacheCollisionMeshStatus(
                    GenerationStatus.OutOfDate
                );
        }

        if (context.RequiresDeepIntegrityVerification)
        {
            TerrainRuntimeGeneratedDataIntegrityResult collisionIntegrity =
                context.IntegrityAudit != null
                    ? context.IntegrityAudit.Collision
                    : null;

            if (
                collisionIntegrity == null
                ||
                !collisionIntegrity.IsValid
            )
            {
                return
                    context.CacheCollisionMeshStatus(
                        GenerationStatus.OutOfDate
                    );
            }
        }

        return
            context.CacheCollisionMeshStatus(
                GenerationStatus.Current
            );
    }
}
