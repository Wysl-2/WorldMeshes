using UnityEditor;

public static class TerrainAuthoringModifierContextUtility
{
    public static bool TryLoadDefault(
        out WorldSettings worldSettings,
        out TerrainAuthoringData authoringData,
        out string errorMessage
    )
    {
        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        authoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        if (worldSettings == null)
        {
            errorMessage =
                "Default WorldSettings could not be loaded from:\n" +
                WorldMeshesPaths.WorldSettingsAssetPath;

            return false;
        }

        if (authoringData == null)
        {
            errorMessage =
                "Default TerrainAuthoringData could not be loaded from:\n" +
                WorldMeshesPaths.TerrainAuthoringDataAssetPath;

            return false;
        }

        errorMessage =
            "";

        return true;
    }
}
