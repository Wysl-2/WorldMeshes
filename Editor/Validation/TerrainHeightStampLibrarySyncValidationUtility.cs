using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TerrainHeightStampLibrarySyncValidationUtility
{
    private sealed class Result
    {
        public string Name;
        public bool Passed;
        public string Details;
    }

    private static readonly List<Result>
        results =
            new List<Result>();

    private static bool validationScheduled;
    private static bool validationRunning;

    private static int lastPassedCount;
    private static int lastFailedCount;

    private static string lastSummary =
        "Not run.";

    private static readonly string ValidationRoot =
        WorldMeshesPaths.GeneratedValidation
        +
        "/StampLibraryPackage2";

    public static bool IsScheduled =>
        validationScheduled;

    public static bool IsRunning =>
        validationRunning;

    public static int LastPassedCount =>
        lastPassedCount;

    public static int LastFailedCount =>
        lastFailedCount;

    public static string LastSummary =>
        lastSummary;

    public static void RequestValidation()
    {
        if (
            validationScheduled
            ||
            validationRunning
        )
        {
            return;
        }

        validationScheduled =
            true;

        EditorApplication.delayCall -=
            RunScheduledValidation;

        EditorApplication.delayCall +=
            RunScheduledValidation;
    }

    private static void RunScheduledValidation()
    {
        EditorApplication.delayCall -=
            RunScheduledValidation;

        if (
            !validationScheduled
            ||
            validationRunning
        )
        {
            return;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            EditorApplication.delayCall +=
                RunScheduledValidation;

            return;
        }

        validationScheduled =
            false;

        validationRunning =
            true;

        results.Clear();

        string tempRoot =
            Path.Combine(
                Path.GetTempPath(),
                "WorldMeshes_StampLibraryPackage2_"
                +
                Guid.NewGuid()
                    .ToString(
                        "N"
                    )
            );

        try
        {
            Directory.CreateDirectory(
                tempRoot
            );

            PrepareValidationLibrary(
                out TerrainHeightStampLibraryLocation location
            );

            string verboseA =
                Path.Combine(
                    tempRoot,
                    "heightmap_20260905222247_64S6.png"
                );

            string verboseB =
                Path.Combine(
                    tempRoot,
                    "heightmap_20260905222308_849A.png"
                );

            WriteValidationPng(
                verboseA,
                false
            );

            WriteValidationPng(
                verboseB,
                true
            );

            ValidateImportIdentityAndImporter(
                verboseA,
                location,
                out TerrainHeightStampAsset firstStamp,
                out Texture2D firstTexture
            );

            ValidateSequentialIdAllocation(
                verboseB,
                location,
                out TerrainHeightStampAsset secondStamp,
                out Texture2D secondTexture
            );

            ValidateReferenceRelationshipAndCanonicalRename(
                location,
                secondStamp,
                secondTexture
            );

            ValidateMissingStampCreationWithoutIdReuse(
                location,
                firstStamp,
                firstTexture,
                out TerrainHeightStampAsset recreatedStamp
            );

            ValidateImporterRepair(
                location,
                firstTexture
            );

            ValidateDuplicateAndBrokenDetection(
                location,
                recreatedStamp,
                secondTexture
            );

            ValidateIdempotentSync(
                location
            );
        }
        catch (Exception exception)
        {
            Add(
                "Unexpected validation exception",
                false,
                exception.ToString()
            );
        }
        finally
        {
            CleanupValidationLibrary();

            try
            {
                if (
                    Directory.Exists(
                        tempRoot
                    )
                )
                {
                    Directory.Delete(
                        tempRoot,
                        true
                    );
                }
            }
            catch
            {
            }

            Finish();
        }
    }

    private static void PrepareValidationLibrary(
        out TerrainHeightStampLibraryLocation location
    )
    {
        CleanupValidationLibrary();

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
            "StampLibraryPackage2",
            ValidationRoot
        );

        string heightmaps =
            ValidationRoot
            +
            "/Heightmaps";

        string assets =
            ValidationRoot
            +
            "/Assets";

        EnsureFolder(
            ValidationRoot,
            "Heightmaps",
            heightmaps
        );

        EnsureFolder(
            ValidationRoot,
            "Assets",
            assets
        );

        location =
            new TerrainHeightStampLibraryLocation(
                heightmaps,
                assets
            );
    }

    private static void ValidateImportIdentityAndImporter(
        string externalPath,
        TerrainHeightStampLibraryLocation location,
        out TerrainHeightStampAsset firstStamp,
        out Texture2D firstTexture
    )
    {
        TerrainHeightStampLibraryOperationReport report =
            TerrainHeightStampLibraryUtility
                .ImportHeightmapForLocation(
                    externalPath,
                    location
                );

        firstStamp =
            report.LastCreatedStampAsset;

        firstTexture =
            firstStamp != null
                ? firstStamp.HeightTexture
                : null;

        string texturePath =
            firstTexture != null
                ? AssetDatabase.GetAssetPath(
                    firstTexture
                )
                : "";

        string stampPath =
            firstStamp != null
                ? AssetDatabase.GetAssetPath(
                    firstStamp
                )
                : "";

        bool identityPassed =
            report.ImportedHeightmaps ==
                1
            &&
            report.CreatedStampAssets ==
                1
            &&
            firstStamp !=
                null
            &&
            firstStamp.LibraryId ==
                1
            &&
            firstStamp.DisplayName ==
                "Heightmap_001"
            &&
            stampPath.EndsWith(
                "/Heightmap_001.asset",
                StringComparison.Ordinal
            )
            &&
            texturePath.EndsWith(
                "/heightmap_20260905222247_64S6.png",
                StringComparison.Ordinal
            );

        Add(
            "Verbose source filename uses short stable stamp identity",
            identityPassed,
            identityPassed
                ? "Verbose PNG source remained unchanged while its asset became Heightmap_001."
                : report.BuildSummary()
        );

        string importerDetails =
            "Imported texture path is empty.";

        bool importerPassed =
            false;

        if (
            !string.IsNullOrEmpty(
                texturePath
            )
        )
        {
            importerPassed =
                TerrainHeightStampLibraryUtility
                    .IsCanonicalHeightmapImporter(
                        texturePath,
                        out importerDetails
                    );
        }

        Add(
            "Canonical heightmap TextureImporter settings",
            importerPassed,
            importerDetails
        );
    }

    private static void ValidateSequentialIdAllocation(
        string externalPath,
        TerrainHeightStampLibraryLocation location,
        out TerrainHeightStampAsset secondStamp,
        out Texture2D secondTexture
    )
    {
        TerrainHeightStampLibraryOperationReport report =
            TerrainHeightStampLibraryUtility
                .ImportHeightmapForLocation(
                    externalPath,
                    location
                );

        secondStamp =
            report.LastCreatedStampAsset;

        secondTexture =
            secondStamp != null
                ? secondStamp.HeightTexture
                : null;

        bool passed =
            secondStamp !=
                null
            &&
            secondStamp.LibraryId ==
                2
            &&
            secondStamp.DisplayName ==
                "Heightmap_002";

        Add(
            "Sequential library ID allocation",
            passed,
            passed
                ? "Second imported texture received Heightmap_002."
                : report.BuildSummary()
        );
    }

    private static void ValidateReferenceRelationshipAndCanonicalRename(
        TerrainHeightStampLibraryLocation location,
        TerrainHeightStampAsset secondStamp,
        Texture2D secondTexture
    )
    {
        if (
            secondStamp == null
            ||
            secondTexture == null
        )
        {
            Add(
                "Texture-reference relationship survives filename mismatch",
                false,
                "Second validation stamp was not created."
            );

            return;
        }

        string currentPath =
            AssetDatabase.GetAssetPath(
                secondStamp
            );

        string wrongPath =
            location.StampAssetsPath
            +
            "/CompletelyDifferentName.asset";

        string moveError =
            AssetDatabase.MoveAsset(
                currentPath,
                wrongPath
            );

        if (
            !string.IsNullOrEmpty(
                moveError
            )
        )
        {
            Add(
                "Texture-reference relationship survives filename mismatch",
                false,
                moveError
            );

            return;
        }

        TerrainHeightStampLibraryScanResult beforeSync =
            TerrainHeightStampLibraryUtility
                .ScanLibrary(
                    location
                );

        bool relationshipPassed =
            beforeSync.MissingStampAssetCount ==
                0;

        TerrainHeightStampLibraryOperationReport sync =
            TerrainHeightStampLibraryUtility
                .SyncLibraryForLocation(
                    location
                );

        string expectedPath =
            location.StampAssetsPath
            +
            "/Heightmap_002.asset";

        bool renamePassed =
            relationshipPassed
            &&
            sync.CreatedStampAssets ==
                0
            &&
            sync.CanonicalAssetRenames ==
                1
            &&
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightStampAsset>(
                    expectedPath
                )
                !=
                null;

        Add(
            "Texture-reference relationship survives filename mismatch",
            renamePassed,
            renamePassed
                ? "Relationship detection used HeightTexture reference and Sync safely restored Heightmap_002.asset."
                : sync.BuildSummary()
        );
    }

    private static void ValidateMissingStampCreationWithoutIdReuse(
        TerrainHeightStampLibraryLocation location,
        TerrainHeightStampAsset firstStamp,
        Texture2D firstTexture,
        out TerrainHeightStampAsset recreatedStamp
    )
    {
        recreatedStamp =
            null;

        if (
            firstStamp == null
            ||
            firstTexture == null
        )
        {
            Add(
                "Missing stamp asset creation",
                false,
                "First validation stamp/texture is unavailable."
            );

            return;
        }

        string firstAssetPath =
            AssetDatabase.GetAssetPath(
                firstStamp
            );

        if (
            !AssetDatabase.DeleteAsset(
                firstAssetPath
            )
        )
        {
            Add(
                "Missing stamp asset creation",
                false,
                "Could not delete generated validation stamp asset."
            );

            return;
        }

        TerrainHeightStampLibraryScanResult before =
            TerrainHeightStampLibraryUtility
                .ScanLibrary(
                    location
                );

        TerrainHeightStampLibraryOperationReport sync =
            TerrainHeightStampLibraryUtility
                .SyncLibraryForLocation(
                    location
                );

        for (
            int index = 0;
            index < sync.CreatedAssets.Count;
            index++
        )
        {
            if (
                sync.CreatedAssets[index] !=
                    null
                &&
                sync.CreatedAssets[index].HeightTexture ==
                    firstTexture
            )
            {
                recreatedStamp =
                    sync.CreatedAssets[index];

                break;
            }
        }

        bool passed =
            before.MissingStampAssetCount ==
                1
            &&
            sync.CreatedStampAssets ==
                1
            &&
            recreatedStamp !=
                null
            &&
            recreatedStamp.LibraryId ==
                3
            &&
            recreatedStamp.DisplayName ==
                "Heightmap_003";

        Add(
            "Missing stamp asset creation without ID reuse",
            passed,
            passed
                ? "Deleted Heightmap_001 was not reused; Sync created Heightmap_003 from max+1."
                : sync.BuildSummary()
        );
    }

    private static void ValidateImporterRepair(
        TerrainHeightStampLibraryLocation location,
        Texture2D firstTexture
    )
    {
        string path =
            firstTexture != null
                ? AssetDatabase.GetAssetPath(
                    firstTexture
                )
                : "";

        TextureImporter importer =
            AssetImporter.GetAtPath(
                path
            )
            as TextureImporter;

        if (
            importer == null
        )
        {
            Add(
                "Sync repairs incorrect importer settings",
                false,
                "Validation TextureImporter could not be resolved."
            );

            return;
        }

        importer.sRGBTexture =
            true;

        importer.mipmapEnabled =
            false;

        importer.filterMode =
            FilterMode.Point;

        importer.wrapMode =
            TextureWrapMode.Repeat;

        importer.textureCompression =
            TextureImporterCompression.Compressed;

        importer.isReadable =
            true;

        importer.npotScale =
            TextureImporterNPOTScale.ToNearest;

        importer.maxTextureSize =
            256;

        importer.ignorePngGamma =
            false;

        importer.ignoreMipmapLimit =
            false;

        importer.SaveAndReimport();

        TerrainHeightStampLibraryOperationReport sync =
            TerrainHeightStampLibraryUtility
                .SyncLibraryForLocation(
                    location
                );

        bool canonical =
            TerrainHeightStampLibraryUtility
                .IsCanonicalHeightmapImporter(
                    path,
                    out string details
                );

        bool passed =
            sync.ImporterRepairs ==
                1
            &&
            canonical;

        Add(
            "Sync repairs incorrect importer settings",
            passed,
            passed
                ? "One deliberately corrupted importer was repaired exactly once."
                : sync.BuildSummary()
                    +
                    "\n"
                    +
                    details
        );
    }

    private static void ValidateDuplicateAndBrokenDetection(
        TerrainHeightStampLibraryLocation location,
        TerrainHeightStampAsset recreatedStamp,
        Texture2D secondTexture
    )
    {
        if (
            recreatedStamp == null
            ||
            secondTexture == null
        )
        {
            Add(
                "Duplicate IDs / duplicate assignments / broken assets detected",
                false,
                "Required validation assets are unavailable."
            );

            return;
        }

        TerrainHeightStampAsset duplicateId =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        duplicateId.name =
            "DuplicateId";

        duplicateId.SetLibraryIdInternal(
            recreatedStamp.LibraryId
        );

        duplicateId.SetHeightTextureInternal(
            secondTexture
        );

        AssetDatabase.CreateAsset(
            duplicateId,
            location.StampAssetsPath
            +
            "/DuplicateId.asset"
        );

        TerrainHeightStampAsset broken =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        broken.name =
            "Broken";

        broken.SetLibraryIdInternal(
            4
        );

        AssetDatabase.CreateAsset(
            broken,
            location.StampAssetsPath
            +
            "/Broken.asset"
        );

        AssetDatabase.SaveAssets();

        TerrainHeightStampLibraryScanResult scan =
            TerrainHeightStampLibraryUtility
                .ScanLibrary(
                    location
                );

        bool duplicateIdFound =
            scan.DuplicateIds.Count ==
                1
            &&
            scan.DuplicateIds[0] ==
                recreatedStamp.LibraryId;

        bool duplicateTextureFound =
            scan.DuplicateTextureAssignmentCount >=
                1;

        bool brokenFound =
            scan.BrokenStampAssetCount ==
                1;

        Add(
            "Duplicate IDs detected",
            duplicateIdFound,
            "Duplicate IDs: "
            +
            scan.DuplicateIdCount
        );

        Add(
            "Duplicate Texture2D assignments detected",
            duplicateTextureFound,
            "Duplicate assignments: "
            +
            scan.DuplicateTextureAssignmentCount
        );

        Add(
            "Broken TerrainHeightStampAsset detected",
            brokenFound,
            "Broken assets: "
            +
            scan.BrokenStampAssetCount
        );
    }

    private static void ValidateIdempotentSync(
        TerrainHeightStampLibraryLocation location
    )
    {
        TerrainHeightStampLibraryOperationReport first =
            TerrainHeightStampLibraryUtility
                .SyncLibraryForLocation(
                    location
                );

        TerrainHeightStampLibraryOperationReport second =
            TerrainHeightStampLibraryUtility
                .SyncLibraryForLocation(
                    location
                );

        bool passed =
            second.CreatedStampAssets ==
                0
            &&
            second.ImporterRepairs ==
                0
            &&
            second.CanonicalAssetRenames ==
                0
            &&
            second.FailedItems ==
                0;

        Add(
            "Safe Sync is idempotent",
            passed,
            passed
                ? "Second Sync made no repairs, creations, or renames."
                : "First:\n"
                    +
                    first.BuildSummary()
                    +
                    "\n\nSecond:\n"
                    +
                    second.BuildSummary()
        );
    }

    private static void WriteValidationPng(
        string path,
        bool invert
    )
    {
        Texture2D texture =
            new Texture2D(
                8,
                8,
                TextureFormat.RGBA32,
                false,
                true
            );

        try
        {
            Color[] pixels =
                new Color[
                    64
                ];

            for (
                int y = 0;
                y < 8;
                y++
            )
            {
                for (
                    int x = 0;
                    x < 8;
                    x++
                )
                {
                    float value =
                        (
                            x +
                            y
                        )
                        /
                        14f;

                    if (invert)
                    {
                        value =
                            1f -
                            value;
                    }

                    pixels[
                        y *
                        8 +
                        x
                    ] =
                        new Color(
                            value,
                            value,
                            value,
                            1f
                        );
                }
            }

            texture.SetPixels(
                pixels
            );

            texture.Apply();

            File.WriteAllBytes(
                path,
                texture.EncodeToPNG()
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(
                texture
            );
        }
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

        string guid =
            AssetDatabase.CreateFolder(
                parentPath,
                folderName
            );

        if (
            string.IsNullOrEmpty(
                guid
            )
            ||
            !string.Equals(
                AssetDatabase.GUIDToAssetPath(
                    guid
                ),
                fullPath,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "Could not create validation folder: "
                +
                fullPath
            );
        }
    }

    private static void CleanupValidationLibrary()
    {
        if (
            AssetDatabase.IsValidFolder(
                ValidationRoot
            )
        )
        {
            AssetDatabase.DeleteAsset(
                ValidationRoot
            );
        }
    }

    private static void Add(
        string name,
        bool passed,
        string details
    )
    {
        results.Add(
            new Result
            {
                Name =
                    name,

                Passed =
                    passed,

                Details =
                    details ??
                    ""
            }
        );
    }

    private static void Finish()
    {
        validationRunning =
            false;

        lastPassedCount =
            0;

        lastFailedCount =
            0;

        System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

        for (
            int index = 0;
            index < results.Count;
            index++
        )
        {
            Result result =
                results[index];

            if (result.Passed)
            {
                lastPassedCount++;
            }
            else
            {
                lastFailedCount++;
            }

            builder.Append(
                result.Passed
                    ? "PASS: "
                    : "FAIL: "
            );

            builder.Append(
                result.Name
            );

            if (
                !string.IsNullOrEmpty(
                    result.Details
                )
            )
            {
                builder.Append(
                    "\n"
                );

                builder.Append(
                    result.Details
                );
            }

            if (
                index <
                results.Count -
                1
            )
            {
                builder.Append(
                    "\n\n"
                );
            }
        }

        lastSummary =
            builder.Length > 0
                ? builder.ToString()
                : "No validation results were produced.";

        if (
            lastFailedCount >
            0
        )
        {
            Debug.LogError(
                "Stamp Library Import / Sync validation failed.\n\n"
                +
                lastSummary
            );
        }
        else
        {
            Debug.Log(
                "Stamp Library Import / Sync validation passed.\n\n"
                +
                lastSummary
            );
        }

        SceneView.RepaintAll();
    }
}
