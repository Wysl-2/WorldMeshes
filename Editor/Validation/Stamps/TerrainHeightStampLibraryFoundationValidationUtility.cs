using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class TerrainHeightStampLibraryFoundationValidationUtility
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

        /*
         * The validation creates its generated live-stamp fixture. Never run
         * AssetDatabase mutations while Unity is compiling/importing. Keep the
         * request scheduled and retry on a later editor tick instead.
         */
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

        try
        {
            ValidatePathsAndFolders();
            ValidateRootCleanup();
            ValidateFormatting();
            ValidateIdentityHelpers();
            ValidateCanonicalLibraryScan();
            ValidateLiveStampFixtureIsolation();
            ValidateTerrainAuthoringReferences();
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
            Finish();
        }
    }

    private static void ValidatePathsAndFolders()
    {
        bool pathsPassed =
            WorldMeshesPaths.AuthoringStamps ==
                "Assets/WorldMeshes/Authoring/Stamps"
            &&
            WorldMeshesPaths.AuthoringStampHeightmaps ==
                "Assets/WorldMeshes/Authoring/Stamps/Heightmaps"
            &&
            WorldMeshesPaths.AuthoringStampAssets ==
                "Assets/WorldMeshes/Authoring/Stamps/Assets";

        bool foldersPassed =
            AssetDatabase.IsValidFolder(
                WorldMeshesPaths
                    .AuthoringStamps
            )
            &&
            AssetDatabase.IsValidFolder(
                WorldMeshesPaths
                    .AuthoringStampHeightmaps
            )
            &&
            AssetDatabase.IsValidFolder(
                WorldMeshesPaths
                    .AuthoringStampAssets
            );

        Add(
            "Canonical stamp-library paths and folders",
            pathsPassed
            &&
            foldersPassed,
            pathsPassed
            &&
            foldersPassed
                ? "Canonical Stamps/Heightmaps and Stamps/Assets paths exist."
                : "A canonical path or library folder is missing/incorrect."
        );
    }

    private static void ValidateRootCleanup()
    {
        string root =
            WorldMeshesPaths
                .AuthoringStamps;

        string heightmaps =
            WorldMeshesPaths
                .AuthoringStampHeightmaps;

        string assets =
            WorldMeshesPaths
                .AuthoringStampAssets;

        List<string> unexpectedRootAssets =
            new List<string>();

        string[] allPaths =
            AssetDatabase.GetAllAssetPaths();

        for (
            int index = 0;
            index < allPaths.Length;
            index++
        )
        {
            string path =
                allPaths[
                    index
                ];

            if (
                path == heightmaps
                ||
                path == assets
            )
            {
                continue;
            }

            string directory =
                Path.GetDirectoryName(
                    path
                );

            if (
                string.IsNullOrEmpty(
                    directory
                )
            )
            {
                continue;
            }

            directory =
                directory.Replace(
                    '\\',
                    '/'
                );

            if (
                directory ==
                    root
            )
            {
                unexpectedRootAssets.Add(
                    path
                );
            }
        }

        bool passed =
            unexpectedRootAssets.Count ==
                0;

        Add(
            "Legacy root-level stamp content removed",
            passed,
            passed
                ? "Authoring/Stamps contains only the canonical library subfolders."
                : "Unexpected legacy/root content remains: " +
                    string.Join(
                        ", ",
                        unexpectedRootAssets
                    )
        );
    }

    private static void ValidateFormatting()
    {
        bool passed =
            TerrainHeightStampIdentityUtility
                .FormatDisplayName(
                    1
                )
            ==
            "Heightmap_001"
            &&
            TerrainHeightStampIdentityUtility
                .FormatDisplayName(
                    27
                )
            ==
            "Heightmap_027"
            &&
            TerrainHeightStampIdentityUtility
                .FormatDisplayName(
                    100
                )
            ==
            "Heightmap_100"
            &&
            TerrainHeightStampIdentityUtility
                .FormatDisplayName(
                    1000
                )
            ==
            "Heightmap_1000"
            &&
            !TerrainHeightStampIdentityUtility
                .IsValidLibraryId(
                    0
                )
            &&
            !TerrainHeightStampIdentityUtility
                .IsValidLibraryId(
                    -1
                )
            &&
            TerrainHeightStampIdentityUtility
                .IsValidLibraryId(
                    1
                );

        Add(
            "Stable library-ID formatting",
            passed,
            passed
                ? "D3 formatting, IDs above 999, and non-positive invalid IDs match the identity contract."
                : "Library ID formatting/validity does not match the contract."
        );
    }

    private static void ValidateIdentityHelpers()
    {
        List<int> duplicateIds =
            TerrainHeightStampLibraryIdentityUtility
                .FindDuplicateIds(
                    new[]
                    {
                        1,
                        1,
                        2,
                        2,
                        2,
                        3,
                        0,
                        -4
                    }
                );

        bool duplicatePassed =
            duplicateIds.Count ==
                2
            &&
            duplicateIds[
                0
            ]
            ==
            1
            &&
            duplicateIds[
                1
            ]
            ==
            2;

        bool nextPassed =
            TerrainHeightStampLibraryIdentityUtility
                .TryCalculateNextLibraryId(
                    new[]
                    {
                        1,
                        2,
                        27,
                        0,
                        -5
                    },
                    out int nextLibraryId
                )
            &&
            nextLibraryId ==
                28
            &&
            TerrainHeightStampLibraryIdentityUtility
                .TryCalculateNextLibraryId(
                    Array.Empty<int>(),
                    out int firstLibraryId
                )
            &&
            firstLibraryId ==
                1;

        bool pathPassed =
            TerrainHeightStampLibraryIdentityUtility
                .GetCanonicalAssetPath(
                    27
                )
            ==
            WorldMeshesPaths
                .AuthoringStampAssets
            +
            "/Heightmap_027.asset";

        Add(
            "Library duplicate/next-ID/path helpers",
            duplicatePassed
            &&
            nextPassed
            &&
            pathPassed,
            duplicatePassed
            &&
            nextPassed
            &&
            pathPassed
                ? "Duplicate detection, max+1 allocation, and canonical asset paths match the foundation contract."
                : "An identity helper produced an unexpected result."
        );
    }

    private static void ValidateCanonicalLibraryScan()
    {
        TerrainHeightStampLibraryIdentityScanResult scan =
            TerrainHeightStampLibraryIdentityUtility
                .ScanLibrary();

        bool passed =
            scan != null
            &&
            scan.InvalidIdAssets.Count ==
                0
            &&
            scan.DuplicateIds.Count ==
                0
            &&
            scan.NextLibraryId >
                0;

        Add(
            "Canonical library identity scan",
            passed,
            passed
                ? "The canonical Assets folder contains no invalid or duplicate library IDs."
                : "The canonical Assets folder contains invalid/duplicate IDs or next-ID allocation failed."
        );
    }

    private static void ValidateLiveStampFixtureIsolation()
    {
        bool created =
            TerrainLiveStampValidationFixtureUtility
                .TryGetOrCreateStampAsset(
                    out TerrainHeightStampAsset fixture,
                    out string errorMessage
                );

        string fixturePath =
            fixture != null
                ? AssetDatabase.GetAssetPath(
                    fixture
                )
                : "";

        bool outsideLibrary =
            !string.IsNullOrEmpty(
                fixturePath
            )
            &&
            fixturePath.StartsWith(
                WorldMeshesPaths
                    .GeneratedLiveStampValidation
                +
                "/",
                StringComparison.Ordinal
            )
            &&
            !fixturePath.StartsWith(
                WorldMeshesPaths
                    .AuthoringStamps
                +
                "/",
                StringComparison.Ordinal
            );

        bool passed =
            created
            &&
            fixture != null
            &&
            fixture.HeightTexture !=
                null
            &&
            fixture.LibraryId ==
                TerrainHeightStampIdentityUtility
                    .UnassignedLibraryId
            &&
            !fixture.HasLibraryId
            &&
            outsideLibrary;

        Add(
            "Live-stamp validation fixture isolation",
            passed,
            passed
                ? "The default live-stamp fixture is generated outside the user library and consumes no LibraryId."
                : "Generated validation fixture isolation failed. " +
                    errorMessage
        );
    }

    private static void ValidateTerrainAuthoringReferences()
    {
        TerrainAuthoringData authoringData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    WorldMeshesPaths
                        .TerrainAuthoringDataAssetPath
                );

        if (authoringData == null)
        {
            Add(
                "TerrainAuthoringData legacy-reference cleanup",
                false,
                "TerrainAuthoringData could not be loaded."
            );

            return;
        }

        bool noLegacyRootAssetReference =
            true;

        IReadOnlyList<TerrainHeightModifier> modifiers =
            authoringData.HeightModifiers;

        for (
            int index = 0;
            index < modifiers.Count;
            index++
        )
        {
            if (
                !(modifiers[index] is
                    TerrainStampModifier stamp)
                ||
                stamp.StampAsset ==
                    null
            )
            {
                continue;
            }

            string path =
                AssetDatabase.GetAssetPath(
                    stamp.StampAsset
                );

            if (
                IsDirectChild(
                    path,
                    WorldMeshesPaths
                        .AuthoringStamps
                )
            )
            {
                noLegacyRootAssetReference =
                    false;

                break;
            }
        }

        bool noMissingSerializedStampGuids =
            TryValidateSerializedStampGuids(
                out string serializedDetails
            );

        bool passed =
            noLegacyRootAssetReference
            &&
            noMissingSerializedStampGuids;

        Add(
            "TerrainAuthoringData legacy-reference cleanup",
            passed,
            passed
                ? "No TerrainStampModifier references a deleted legacy root asset or unresolved serialized stamp GUID."
                : serializedDetails
        );
    }

    private static bool TryValidateSerializedStampGuids(
        out string details
    )
    {
        details =
            "";

        DirectoryInfo projectDirectory =
            Directory.GetParent(
                Application.dataPath
            );

        if (projectDirectory == null)
        {
            details =
                "Could not resolve the Unity project directory.";

            return false;
        }

        string assetFilePath =
            Path.Combine(
                projectDirectory.FullName,
                WorldMeshesPaths
                    .TerrainAuthoringDataAssetPath
            );

        if (
            !File.Exists(
                assetFilePath
            )
        )
        {
            details =
                "TerrainAuthoringData asset file was not found on disk.";

            return false;
        }

        string text =
            File.ReadAllText(
                assetFilePath
            );

        MatchCollection matches =
            Regex.Matches(
                text,
                @"stampAsset:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-fA-F]{32}),"
            );

        List<string> unresolvedGuids =
            new List<string>();

        for (
            int index = 0;
            index < matches.Count;
            index++
        )
        {
            string guid =
                matches[
                    index
                ]
                .Groups[
                    1
                ]
                .Value;

            string path =
                AssetDatabase.GUIDToAssetPath(
                    guid
                );

            if (
                string.IsNullOrEmpty(
                    path
                )
            )
            {
                unresolvedGuids.Add(
                    guid
                );
            }
        }

        if (
            unresolvedGuids.Count >
                0
        )
        {
            details =
                "Unresolved serialized stamp GUID(s): " +
                string.Join(
                    ", ",
                    unresolvedGuids
                );

            return false;
        }

        return true;
    }

    private static bool IsDirectChild(
        string assetPath,
        string parentAssetPath
    )
    {
        if (
            string.IsNullOrEmpty(
                assetPath
            )
        )
        {
            return false;
        }

        string directory =
            Path.GetDirectoryName(
                assetPath
            );

        if (
            string.IsNullOrEmpty(
                directory
            )
        )
        {
            return false;
        }

        directory =
            directory.Replace(
                '\\',
                '/'
            );

        return
            directory ==
            parentAssetPath;
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
                results[
                    index
                ];

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
                "Stamp library foundation validation failed.\n\n"
                +
                lastSummary
            );
        }
        else
        {
            Debug.Log(
                "Stamp library foundation validation passed.\n\n"
                +
                lastSummary
            );
        }

        SceneView.RepaintAll();
    }
}
