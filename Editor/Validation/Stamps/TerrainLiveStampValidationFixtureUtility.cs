using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TerrainLiveStampValidationFixtureUtility
{
    public const string ValidationTextureAssetPath =
        WorldMeshesPaths
            .GeneratedLiveStampValidation
        +
        "/TerrainLiveStampValidationHeight.asset";

    public const string ValidationStampAssetPath =
        WorldMeshesPaths
            .GeneratedLiveStampValidation
        +
        "/TerrainLiveStampValidation.asset";

    private const int ValidationTextureSize =
        64;

    public static TerrainHeightStampAsset GetOrCreateStampAsset()
    {
        if (
            TryGetOrCreateStampAsset(
                out TerrainHeightStampAsset stampAsset,
                out string errorMessage
            )
        )
        {
            return stampAsset;
        }

        Debug.LogError(
            "Could not create the generated live-stamp validation fixture.\n\n"
            +
            errorMessage
        );

        return null;
    }

    public static bool TryGetOrCreateStampAsset(
        out TerrainHeightStampAsset stampAsset,
        out string errorMessage
    )
    {
        stampAsset =
            null;

        errorMessage =
            "";

        try
        {
            EnsureGeneratedFolders();

            Texture2D texture =
                AssetDatabase
                    .LoadAssetAtPath<Texture2D>(
                        ValidationTextureAssetPath
                    );

            if (texture == null)
            {
                UnityEngine.Object existingTexturePathAsset =
                    AssetDatabase
                        .LoadMainAssetAtPath(
                            ValidationTextureAssetPath
                        );

                if (
                    existingTexturePathAsset != null
                )
                {
                    errorMessage =
                        "The generated validation texture path is occupied by "
                        +
                        "an unexpected asset type.";

                    return false;
                }

                texture =
                    CreateValidationTexture();

                AssetDatabase.CreateAsset(
                    texture,
                    ValidationTextureAssetPath
                );
            }

            stampAsset =
                AssetDatabase
                    .LoadAssetAtPath<TerrainHeightStampAsset>(
                        ValidationStampAssetPath
                    );

            if (stampAsset == null)
            {
                UnityEngine.Object existingStampPathAsset =
                    AssetDatabase
                        .LoadMainAssetAtPath(
                            ValidationStampAssetPath
                        );

                if (
                    existingStampPathAsset != null
                )
                {
                    errorMessage =
                        "The generated validation stamp path is occupied by "
                        +
                        "an unexpected asset type.";

                    return false;
                }

                stampAsset =
                    ScriptableObject
                        .CreateInstance<TerrainHeightStampAsset>();

                stampAsset.name =
                    "TerrainLiveStampValidation";

                stampAsset
                    .SetLibraryIdInternal(
                        TerrainHeightStampIdentityUtility
                            .UnassignedLibraryId
                    );

                stampAsset
                    .SetHeightTextureInternal(
                        texture
                    );

                AssetDatabase.CreateAsset(
                    stampAsset,
                    ValidationStampAssetPath
                );
            }
            else
            {
                bool changed =
                    false;

                if (
                    stampAsset.LibraryId !=
                        TerrainHeightStampIdentityUtility
                            .UnassignedLibraryId
                )
                {
                    stampAsset
                        .SetLibraryIdInternal(
                            TerrainHeightStampIdentityUtility
                                .UnassignedLibraryId
                        );

                    changed =
                        true;
                }

                if (
                    stampAsset.HeightTexture !=
                        texture
                )
                {
                    stampAsset
                        .SetHeightTextureInternal(
                            texture
                        );

                    changed =
                        true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(
                        stampAsset
                    );
                }
            }

            AssetDatabase.SaveAssets();

            return
                stampAsset != null
                &&
                stampAsset.HeightTexture !=
                    null;
        }
        catch (Exception exception)
        {
            errorMessage =
                exception.Message;

            stampAsset =
                null;

            return false;
        }
    }

    private static Texture2D CreateValidationTexture()
    {
        Texture2D texture =
            new Texture2D(
                ValidationTextureSize,
                ValidationTextureSize,
                TextureFormat.RFloat,
                true,
                true
            );

        texture.name =
            "TerrainLiveStampValidationHeight";

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Bilinear;

        float[] values =
            new float[
                ValidationTextureSize *
                ValidationTextureSize
            ];

        for (
            int y = 0;
            y < ValidationTextureSize;
            y++
        )
        {
            float v =
                y /
                (
                    (float)(
                        ValidationTextureSize -
                        1
                    )
                );

            for (
                int x = 0;
                x < ValidationTextureSize;
                x++
            )
            {
                float u =
                    x /
                    (
                        (float)(
                            ValidationTextureSize -
                            1
                        )
                    );

                float dx =
                    (
                        u -
                        0.5f
                    )
                    *
                    2f;

                float dz =
                    (
                        v -
                        0.5f
                    )
                    *
                    2f;

                float radial =
                    Mathf.Clamp01(
                        1f -
                        Mathf.Sqrt(
                            dx *
                            dx +
                            dz *
                            dz
                        )
                    );

                values[
                    y *
                    ValidationTextureSize +
                    x
                ] =
                    radial;
            }
        }

        texture.SetPixelData(
            values,
            0
        );

        texture.Apply(
            true,
            false
        );

        return texture;
    }

    private static void EnsureGeneratedFolders()
    {
        EnsureFolder(
            WorldMeshesPaths.Root,
            "Generated",
            WorldMeshesPaths.Generated
        );

        EnsureFolder(
            WorldMeshesPaths.Generated,
            "Validation",
            WorldMeshesPaths.GeneratedValidation
        );

        EnsureFolder(
            WorldMeshesPaths.GeneratedValidation,
            "LiveStamp",
            WorldMeshesPaths.GeneratedLiveStampValidation
        );
    }

    private static void EnsureFolder(
        string parentPath,
        string folderName,
        string fullPath
    )
    {
        if (
            AssetDatabase.IsValidFolder(
                fullPath
            )
        )
        {
            return;
        }

        /*
         * A failed/aborted import can leave the physical directory present
         * before AssetDatabase has registered it. This can happen after the
         * Package 1 startup bug. Refresh only when such a stale physical
         * directory actually exists.
         */
        string absolutePath =
            GetAbsoluteAssetPath(
                fullPath
            );

        if (
            Directory.Exists(
                absolutePath
            )
        )
        {
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport
            );

            if (
                AssetDatabase.IsValidFolder(
                    fullPath
                )
            )
            {
                return;
            }
        }

        if (
            !AssetDatabase.IsValidFolder(
                parentPath
            )
        )
        {
            throw new InvalidOperationException(
                "Cannot create generated validation folder because its "
                +
                "parent is not available to AssetDatabase: "
                +
                parentPath
            );
        }

        string guid =
            AssetDatabase.CreateFolder(
                parentPath,
                folderName
            );

        string createdPath =
            string.IsNullOrEmpty(
                guid
            )
                ? ""
                : AssetDatabase.GUIDToAssetPath(
                    guid
                );

        if (
            string.IsNullOrEmpty(
                guid
            )
            ||
            createdPath !=
                fullPath
        )
        {
            throw new InvalidOperationException(
                "Could not create generated validation folder at the "
                +
                "expected path: "
                +
                fullPath
            );
        }
    }

    private static string GetAbsoluteAssetPath(
        string assetPath
    )
    {
        string projectRoot =
            Directory
                .GetParent(
                    Application.dataPath
                )
                .FullName;

        return
            Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    assetPath
                )
            );
    }
}
