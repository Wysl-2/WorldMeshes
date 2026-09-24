using UnityEditor;

public sealed class TerrainRuntimeReadinessResult
{
    public TerrainGenerationStateUtility.GenerationStatus AuthoringStatus { get; internal set; }
    public TerrainGenerationStateUtility.GenerationStatus HeightStatus { get; internal set; }
    public TerrainGenerationStateUtility.GenerationStatus HeightStreamingStatus { get; internal set; }
    public TerrainGenerationStateUtility.GenerationStatus SurfaceStatus { get; internal set; }
    public TerrainGenerationStateUtility.GenerationStatus CollisionStatus { get; internal set; }

    public TerrainRuntimeIntegrityAuditResult IntegrityAudit { get; internal set; }
    public TerrainRuntimeAddressablesValidationResult AddressablesValidation { get; internal set; }
    public TerrainRuntimeHierarchyReadinessResult HierarchyReadiness { get; internal set; }
    public TerrainRuntimeBakePlan Plan { get; internal set; }

    public bool HeightStreamingClipmapCompatible { get; internal set; }
    public string HeightStreamingClipmapCompatibilityError { get; internal set; }

    public bool IsReady { get; internal set; }
    public string ErrorMessage { get; internal set; }
}

public static class TerrainRuntimeReadinessUtility
{
    public static TerrainRuntimeReadinessResult Evaluate(
        bool forceFreshAudit
    )
    {
        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        return Evaluate(
            worldSettings,
            authoringData,
            forceFreshAudit
        );
    }

    public static TerrainRuntimeReadinessResult
        EvaluateOperationalReadiness()
    {
        return Evaluate(false);
    }

    public static TerrainRuntimeReadinessResult
        EvaluateOperationalReadiness(
            WorldSettings worldSettings,
            TerrainAuthoringData authoringData
        )
    {
        return Evaluate(
            worldSettings,
            authoringData,
            false
        );
    }

    public static TerrainRuntimeReadinessResult Evaluate(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        bool forceFreshAudit
    )
    {
        TerrainRuntimeReadinessResult result =
            new TerrainRuntimeReadinessResult
            {
                ErrorMessage = ""
            };

        if (worldSettings == null || authoringData == null)
        {
            result.ErrorMessage =
                worldSettings == null
                    ? "WorldSettings is unavailable."
                    : "TerrainAuthoringData is unavailable.";

            return result;
        }

        TerrainRuntimeIntegrityAuditResult audit = null;
        TerrainRuntimeAddressablesValidationResult
            addressablesValidation = null;

        if (forceFreshAudit)
        {
            audit =
                TerrainRuntimeIntegrityAuditUtility
                    .ForceFreshAudit(worldSettings);

            addressablesValidation =
                audit != null
                    ? audit.Addressables
                    : null;
        }
        else
        {
            TerrainRuntimeIntegrityAuditUtility
                .TryGetCachedGeneratedDataAudit(
                    worldSettings,
                    out audit
                );

            TerrainRuntimeIntegrityAuditUtility
                .TryGetCachedAddressablesValidation(
                    worldSettings,
                    out addressablesValidation
                );
        }

        result.IntegrityAudit = audit;

        TerrainGenerationStateEvaluationResult generationState =
            forceFreshAudit
                ? TerrainGenerationStateUtility
                    .EvaluateGenerationState(
                        worldSettings,
                        authoringData
                    )
                : TerrainGenerationStateUtility
                    .EvaluateOperationalGenerationState(
                        worldSettings,
                        authoringData
                    );

        result.AuthoringStatus =
            generationState.AuthoringHeightfieldStatus;

        result.HeightStatus =
            generationState.HeightmapStatus;

        result.HeightStreamingStatus =
            generationState.HeightStreamingStatus;

        result.SurfaceStatus =
            generationState.SurfaceMaskStatus;

        result.CollisionStatus =
            generationState.CollisionMeshStatus;

        result.AddressablesValidation =
            addressablesValidation;

        result.HeightStreamingClipmapCompatible =
            TerrainHeightStreamingPyramidPolicy
                .TryValidateClipmapCompatibility(
                    worldSettings,
                    out string clipmapCompatibilityError
                );

        result.HeightStreamingClipmapCompatibilityError =
            clipmapCompatibilityError ?? "";

        if (
            audit != null
            &&
            addressablesValidation != null
        )
        {
            audit.Addressables =
                addressablesValidation;
        }

        result.HierarchyReadiness =
            TerrainRuntimeHierarchyReadinessUtility
                .Evaluate();

        result.Plan =
            TerrainRuntimeBakePlanner
                .BuildPlan(
                    worldSettings,
                    authoringData
                );

        bool integrityReady =
            forceFreshAudit
                ? audit != null
                    && audit.GeneratedDataValid
                    && result.AddressablesValidation != null
                    && result.AddressablesValidation.IsValid
                : (
                    audit == null
                    || audit.GeneratedDataValid
                )
                &&
                (
                    result.AddressablesValidation == null
                    || result.AddressablesValidation.IsValid
                );

        result.IsReady =
            result.AuthoringStatus ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && result.HeightStatus ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && result.HeightStreamingStatus ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && result.HeightStreamingClipmapCompatible
            && result.SurfaceStatus ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && result.CollisionStatus ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && integrityReady
            && result.HierarchyReadiness != null
            && result.HierarchyReadiness.IsReady
            && result.Plan != null
            && !result.Plan.IsBlocked
            && !result.Plan.HasWork;

        if (!result.IsReady)
        {
            if (
                result.HierarchyReadiness != null
                && !result.HierarchyReadiness.IsReady
            )
            {
                result.ErrorMessage =
                    result.HierarchyReadiness.ErrorMessage;
            }
            else if (!result.HeightStreamingClipmapCompatible)
            {
                result.ErrorMessage =
                    string.IsNullOrEmpty(
                        result.HeightStreamingClipmapCompatibilityError
                    )
                        ? "Height Streaming is incompatible with the current clipmap configuration."
                        : result.HeightStreamingClipmapCompatibilityError;
            }
            else if (
                result.AddressablesValidation != null
                && !result.AddressablesValidation.IsValid
            )
            {
                result.ErrorMessage =
                    "Runtime Addressables configuration requires repair.";
            }
            else if (
                result.Plan != null
                && result.Plan.IsBlocked
            )
            {
                result.ErrorMessage =
                    result.Plan.BlockReason;
            }
            else
            {
                result.ErrorMessage =
                    "Runtime generated data or pending work is not current.";
            }
        }

        return result;
    }
}
