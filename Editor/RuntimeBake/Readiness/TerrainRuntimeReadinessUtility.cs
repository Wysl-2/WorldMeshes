using UnityEditor;

public sealed class TerrainRuntimeReadinessResult
{
    public TerrainGenerationStateUtility.GenerationStatus AuthoringStatus { get; internal set; }
    public TerrainGenerationStateUtility.GenerationStatus HeightStatus { get; internal set; }
    public TerrainGenerationStateUtility.GenerationStatus SurfaceStatus { get; internal set; }
    public TerrainGenerationStateUtility.GenerationStatus CollisionStatus { get; internal set; }

    public TerrainRuntimeIntegrityAuditResult IntegrityAudit { get; internal set; }
    public TerrainRuntimeAddressablesValidationResult AddressablesValidation { get; internal set; }
    public TerrainRuntimeHierarchyReadinessResult HierarchyReadiness { get; internal set; }
    public TerrainRuntimeBakePlan Plan { get; internal set; }

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

        TerrainRuntimeIntegrityAuditResult audit =
            forceFreshAudit
                ? TerrainRuntimeIntegrityAuditUtility
                    .ForceFreshAudit(worldSettings)
                : TerrainRuntimeIntegrityAuditUtility
                    .GetCachedAudit(worldSettings);

        result.IntegrityAudit = audit;

        result.AuthoringStatus =
            TerrainGenerationStateUtility
                .GetAuthoringHeightfieldStatus(
                    worldSettings,
                    authoringData
                );

        result.HeightStatus =
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                );

        result.SurfaceStatus =
            TerrainGenerationStateUtility
                .GetSurfaceMaskStatus(
                    worldSettings
                );

        result.CollisionStatus =
            TerrainGenerationStateUtility
                .GetCollisionMeshStatus(
                    worldSettings
                );

        result.AddressablesValidation =
            audit != null
                ? audit.Addressables
                : null;

        result.HierarchyReadiness =
            TerrainRuntimeHierarchyReadinessUtility
                .Evaluate();

        result.Plan =
            TerrainRuntimeBakePlanner
                .BuildPlan(
                    worldSettings,
                    authoringData
                );

        result.IsReady =
            result.AuthoringStatus ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && result.HeightStatus ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && result.SurfaceStatus ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && result.CollisionStatus ==
                TerrainGenerationStateUtility.GenerationStatus.Current
            && audit != null
            && audit.GeneratedDataValid
            && result.AddressablesValidation != null
            && result.AddressablesValidation.IsValid
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
