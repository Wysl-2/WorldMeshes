using UnityEditor;
using UnityEngine;

/*
 * Editor ownership/lifecycle for the default TerrainSurfaceSettings asset.
 *
 * The first time Stage 7 is imported, existing Scree suitability values are
 * copied from MAT_ClipmapTerrain so the migration preserves the user's
 * current tuning. After the asset exists, the material is never read as the
 * authoritative source again.
 */
[InitializeOnLoad]
public static class TerrainSurfaceSettingsEditorUtility
{
    private const string ResourcesFolderName =
        "Resources";

    static TerrainSurfaceSettingsEditorUtility()
    {
        EditorApplication.delayCall +=
            EnsureDefaultAssetAfterReload;
    }

    public static TerrainSurfaceSettings LoadOrCreate(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        TerrainSurfaceSettings existing =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                WorldMeshesPaths
                    .TerrainSurfaceSettingsAssetPath
            );

        if (existing != null)
        {
            return existing;
        }

        if (!EnsureResourcesFolder(out errorMessage))
        {
            return null;
        }

        TerrainSurfaceSettings created =
            ScriptableObject.CreateInstance<TerrainSurfaceSettings>();

        if (created == null)
        {
            errorMessage =
                "Could not create TerrainSurfaceSettings.";

            return null;
        }

        /*
         * One-time migration only. If the old material is unavailable or a
         * property is missing, the ScriptableObject defaults remain intact.
         */
        CopyScreeSuitabilityFromLegacyMaterial(
            created
        );

        created.Scree.Sanitize();

        AssetDatabase.CreateAsset(
            created,
            WorldMeshesPaths
                .TerrainSurfaceSettingsAssetPath
        );

        EditorUtility.SetDirty(
            created
        );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                WorldMeshesPaths
                    .TerrainSurfaceSettingsAssetPath
            );
    }

    private static void EnsureDefaultAssetAfterReload()
    {
        EditorApplication.delayCall -=
            EnsureDefaultAssetAfterReload;

        TerrainSurfaceSettings settings =
            LoadOrCreate(
                out string errorMessage
            );

        if (
            settings == null
            &&
            !string.IsNullOrEmpty(
                errorMessage
            )
        )
        {
            Debug.LogError(
                "WorldMeshes could not create TerrainSurfaceSettings.\n\n" +
                errorMessage
            );
        }
    }

    private static bool EnsureResourcesFolder(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.Configuration
            )
        )
        {
            errorMessage =
                "WorldMeshes Configuration folder is missing:\n" +
                WorldMeshesPaths.Configuration;

            return false;
        }

        if (
            AssetDatabase.IsValidFolder(
                WorldMeshesPaths
                    .ConfigurationResources
            )
        )
        {
            return true;
        }

        string guid =
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Configuration,
                ResourcesFolderName
            );

        if (string.IsNullOrEmpty(guid))
        {
            errorMessage =
                "Could not create the WorldMeshes Configuration/Resources folder.";

            return false;
        }

        return true;
    }

    private static void CopyScreeSuitabilityFromLegacyMaterial(
        TerrainSurfaceSettings settings
    )
    {
        if (settings == null)
        {
            return;
        }

        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(
                WorldMeshesPaths
                    .ClipmapTerrainMaterialPath
            );

        if (material == null)
        {
            return;
        }

        ScreeSettings scree =
            settings.Scree;

        TryCopyFloat(
            material,
            "_ScreeSlopeMin",
            value =>
                scree.slopeMin =
                    value
        );

        TryCopyFloat(
            material,
            "_ScreeSlopePreferredMin",
            value =>
                scree.slopePreferredMin =
                    value
        );

        TryCopyFloat(
            material,
            "_ScreeSlopePreferredMax",
            value =>
                scree.slopePreferredMax =
                    value
        );

        TryCopyFloat(
            material,
            "_ScreeSlopeMax",
            value =>
                scree.slopeMax =
                    value
        );

        TryCopyFloat(
            material,
            "_ScreeCurvatureScale",
            value =>
                scree.curvatureScale =
                    value
        );

        TryCopyFloat(
            material,
            "_ScreeConvexRejectStart",
            value =>
                scree.convexRejectStart =
                    value
        );

        TryCopyFloat(
            material,
            "_ScreeConvexRejectEnd",
            value =>
                scree.convexRejectEnd =
                    value
        );

        TryCopyFloat(
            material,
            "_ScreeGeologyScale",
            value =>
                scree.geologyScale =
                    value
        );

        TryCopyFloat(
            material,
            "_ScreeGeologyStrength",
            value =>
                scree.geologyStrength =
                    value
        );
    }

    private static void TryCopyFloat(
        Material material,
        string propertyName,
        System.Action<float> setter
    )
    {
        if (
            material == null
            ||
            string.IsNullOrEmpty(
                propertyName
            )
            ||
            setter == null
            ||
            !material.HasProperty(
                propertyName
            )
        )
        {
            return;
        }

        setter(
            material.GetFloat(
                propertyName
            )
        );
    }
}
