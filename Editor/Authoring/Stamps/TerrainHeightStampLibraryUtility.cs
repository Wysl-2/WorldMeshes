using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public enum TerrainHeightStampLibraryIssueSeverity
{
    Info,
    Warning,
    Error
}

public sealed class TerrainHeightStampLibraryIssue
{
    public TerrainHeightStampLibraryIssueSeverity Severity
    {
        get;
        internal set;
    }

    public string Code
    {
        get;
        internal set;
    }

    public string Message
    {
        get;
        internal set;
    }

    public string AssetPath
    {
        get;
        internal set;
    }
}

public sealed class TerrainHeightStampDuplicateTextureAssignment
{
    private readonly List<TerrainHeightStampAsset>
        stampAssets =
            new List<TerrainHeightStampAsset>();

    public Texture2D Heightmap
    {
        get;
        internal set;
    }

    public IReadOnlyList<TerrainHeightStampAsset> StampAssets =>
        stampAssets;

    internal List<TerrainHeightStampAsset> MutableStampAssets =>
        stampAssets;
}

public sealed class TerrainHeightStampAssetPathMismatch
{
    public TerrainHeightStampAsset StampAsset
    {
        get;
        internal set;
    }

    public string CurrentPath
    {
        get;
        internal set;
    }

    public string ExpectedPath
    {
        get;
        internal set;
    }

    public bool CanCanonicalize
    {
        get;
        internal set;
    }
}

public sealed class TerrainHeightStampLibraryScanResult
{
    private readonly List<Texture2D>
        heightmaps =
            new List<Texture2D>();

    private readonly List<TerrainHeightStampAsset>
        stampAssets =
            new List<TerrainHeightStampAsset>();

    private readonly List<Texture2D>
        missingStampAssets =
            new List<Texture2D>();

    private readonly List<TerrainHeightStampAsset>
        brokenStampAssets =
            new List<TerrainHeightStampAsset>();

    private readonly List<TerrainHeightStampAsset>
        invalidIdAssets =
            new List<TerrainHeightStampAsset>();

    private readonly List<int>
        duplicateIds =
            new List<int>();

    private readonly List<TerrainHeightStampDuplicateTextureAssignment>
        duplicateTextureAssignments =
            new List<TerrainHeightStampDuplicateTextureAssignment>();

    private readonly List<TerrainHeightStampAssetPathMismatch>
        assetPathMismatches =
            new List<TerrainHeightStampAssetPathMismatch>();

    private readonly List<string>
        unexpectedAssetPaths =
            new List<string>();

    private readonly List<Texture2D>
        importerSettingsMismatches =
            new List<Texture2D>();

    private readonly List<TerrainHeightStampLibraryIssue>
        issues =
            new List<TerrainHeightStampLibraryIssue>();

    public IReadOnlyList<Texture2D> Heightmaps =>
        heightmaps;

    public IReadOnlyList<TerrainHeightStampAsset> StampAssets =>
        stampAssets;

    public IReadOnlyList<Texture2D> MissingStampAssets =>
        missingStampAssets;

    public IReadOnlyList<TerrainHeightStampAsset> BrokenStampAssets =>
        brokenStampAssets;

    public IReadOnlyList<TerrainHeightStampAsset> InvalidIdAssets =>
        invalidIdAssets;

    public IReadOnlyList<int> DuplicateIds =>
        duplicateIds;

    public IReadOnlyList<TerrainHeightStampDuplicateTextureAssignment>
        DuplicateTextureAssignments =>
            duplicateTextureAssignments;

    public IReadOnlyList<TerrainHeightStampAssetPathMismatch>
        AssetPathMismatches =>
            assetPathMismatches;

    public IReadOnlyList<string> UnexpectedAssetPaths =>
        unexpectedAssetPaths;

    public IReadOnlyList<Texture2D> ImporterSettingsMismatches =>
        importerSettingsMismatches;

    public IReadOnlyList<TerrainHeightStampLibraryIssue> Issues =>
        issues;

    public int HeightmapCount =>
        heightmaps.Count;

    public int StampAssetCount =>
        stampAssets.Count;

    public int MissingStampAssetCount =>
        missingStampAssets.Count;

    public int BrokenStampAssetCount =>
        brokenStampAssets.Count;

    public int InvalidIdCount =>
        invalidIdAssets.Count;

    public int DuplicateIdCount =>
        duplicateIds.Count;

    public int DuplicateTextureAssignmentCount =>
        duplicateTextureAssignments.Count;

    public int AssetPathMismatchCount =>
        assetPathMismatches.Count;

    public int UnexpectedAssetCount =>
        unexpectedAssetPaths.Count;

    public int ImporterSettingsMismatchCount =>
        importerSettingsMismatches.Count;

    public int MaximumValidLibraryId
    {
        get;
        internal set;
    }

    public int NextLibraryId
    {
        get;
        internal set;
    }

    public int WarningCount
    {
        get
        {
            int count =
                0;

            for (
                int index = 0;
                index < issues.Count;
                index++
            )
            {
                if (
                    issues[index].Severity ==
                    TerrainHeightStampLibraryIssueSeverity.Warning
                )
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int ErrorCount
    {
        get
        {
            int count =
                0;

            for (
                int index = 0;
                index < issues.Count;
                index++
            )
            {
                if (
                    issues[index].Severity ==
                    TerrainHeightStampLibraryIssueSeverity.Error
                )
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool IsHealthy =>
        ErrorCount == 0
        &&
        MissingStampAssetCount == 0
        &&
        ImporterSettingsMismatchCount == 0
        &&
        AssetPathMismatchCount == 0
        &&
        UnexpectedAssetCount == 0;

    internal List<Texture2D> MutableHeightmaps =>
        heightmaps;

    internal List<TerrainHeightStampAsset> MutableStampAssets =>
        stampAssets;

    internal List<Texture2D> MutableMissingStampAssets =>
        missingStampAssets;

    internal List<TerrainHeightStampAsset> MutableBrokenStampAssets =>
        brokenStampAssets;

    internal List<TerrainHeightStampAsset> MutableInvalidIdAssets =>
        invalidIdAssets;

    internal List<int> MutableDuplicateIds =>
        duplicateIds;

    internal List<TerrainHeightStampDuplicateTextureAssignment>
        MutableDuplicateTextureAssignments =>
            duplicateTextureAssignments;

    internal List<TerrainHeightStampAssetPathMismatch>
        MutableAssetPathMismatches =>
            assetPathMismatches;

    internal List<string> MutableUnexpectedAssetPaths =>
        unexpectedAssetPaths;

    internal List<Texture2D> MutableImporterSettingsMismatches =>
        importerSettingsMismatches;

    internal List<TerrainHeightStampLibraryIssue> MutableIssues =>
        issues;
}

public sealed class TerrainHeightStampLibraryOperationReport
{
    private readonly List<TerrainHeightStampAsset>
        createdStampAssets =
            new List<TerrainHeightStampAsset>();

    private readonly List<string>
        importedHeightmapPaths =
            new List<string>();

    private readonly List<TerrainHeightStampLibraryIssue>
        issues =
            new List<TerrainHeightStampLibraryIssue>();

    public string Operation
    {
        get;
        internal set;
    }

    public bool Cancelled
    {
        get;
        internal set;
    }

    public int ImportedHeightmaps
    {
        get;
        internal set;
    }

    public int CreatedStampAssets
    {
        get;
        internal set;
    }

    public int ImporterRepairs
    {
        get;
        internal set;
    }

    public int CanonicalAssetRenames
    {
        get;
        internal set;
    }

    public int FailedItems
    {
        get;
        internal set;
    }

    public TerrainHeightStampLibraryScanResult FinalScan
    {
        get;
        internal set;
    }

    public IReadOnlyList<TerrainHeightStampAsset> CreatedAssets =>
        createdStampAssets;

    public IReadOnlyList<string> ImportedHeightmapPaths =>
        importedHeightmapPaths;

    public IReadOnlyList<TerrainHeightStampLibraryIssue> Issues =>
        issues;

    public TerrainHeightStampAsset LastCreatedStampAsset =>
        createdStampAssets.Count > 0
            ? createdStampAssets[
                createdStampAssets.Count - 1
            ]
            : null;

    public int ErrorCount
    {
        get
        {
            int count =
                0;

            for (
                int index = 0;
                index < issues.Count;
                index++
            )
            {
                if (
                    issues[index].Severity ==
                    TerrainHeightStampLibraryIssueSeverity.Error
                )
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int WarningCount
    {
        get
        {
            int count =
                0;

            for (
                int index = 0;
                index < issues.Count;
                index++
            )
            {
                if (
                    issues[index].Severity ==
                    TerrainHeightStampLibraryIssueSeverity.Warning
                )
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool Succeeded =>
        !Cancelled
        &&
        ErrorCount == 0
        &&
        FailedItems == 0
        &&
        (
            FinalScan == null
            ||
            FinalScan.ErrorCount == 0
        );

    public string BuildSummary()
    {
        System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

        builder.AppendLine(
            string.IsNullOrEmpty(
                Operation
            )
                ? "Stamp Library Operation"
                : Operation
        );

        if (Cancelled)
        {
            builder.AppendLine(
                "Cancelled."
            );

            return
                builder.ToString();
        }

        builder.AppendLine(
            "Imported Heightmaps: " +
            ImportedHeightmaps
        );

        builder.AppendLine(
            "Created Stamp Assets: " +
            CreatedStampAssets
        );

        builder.AppendLine(
            "Importer Repairs: " +
            ImporterRepairs
        );

        builder.AppendLine(
            "Canonical Asset Renames: " +
            CanonicalAssetRenames
        );

        builder.AppendLine(
            "Failed Items: " +
            FailedItems
        );

        builder.AppendLine(
            "Warnings: " +
            WarningCount
        );

        builder.AppendLine(
            "Errors: " +
            ErrorCount
        );

        if (FinalScan != null)
        {
            builder.AppendLine();

            builder.AppendLine(
                "Library"
            );

            builder.AppendLine(
                "Heightmaps: " +
                FinalScan.HeightmapCount
            );

            builder.AppendLine(
                "Stamp Assets: " +
                FinalScan.StampAssetCount
            );

            builder.AppendLine(
                "Missing Stamp Assets: " +
                FinalScan.MissingStampAssetCount
            );

            builder.AppendLine(
                "Broken Stamp Assets: " +
                FinalScan.BrokenStampAssetCount
            );

            builder.AppendLine(
                "Duplicate IDs: " +
                FinalScan.DuplicateIdCount
            );

            builder.AppendLine(
                "Duplicate Texture Assignments: " +
                FinalScan.DuplicateTextureAssignmentCount
            );

            builder.AppendLine(
                "Importer Mismatches: " +
                FinalScan.ImporterSettingsMismatchCount
            );
        }

        if (
            issues.Count >
            0
        )
        {
            builder.AppendLine();
            builder.AppendLine(
                "Operation Issues"
            );

            for (
                int index = 0;
                index < issues.Count;
                index++
            )
            {
                TerrainHeightStampLibraryIssue issue =
                    issues[index];

                builder.Append(
                    issue.Severity
                );

                builder.Append(
                    ": "
                );

                builder.Append(
                    issue.Message
                );

                if (
                    !string.IsNullOrEmpty(
                        issue.AssetPath
                    )
                )
                {
                    builder.Append(
                        " ["
                    );

                    builder.Append(
                        issue.AssetPath
                    );

                    builder.Append(
                        "]"
                    );
                }

                builder.AppendLine();
            }
        }

        return
            builder.ToString()
                .TrimEnd();
    }

    internal List<TerrainHeightStampAsset> MutableCreatedStampAssets =>
        createdStampAssets;

    internal List<string> MutableImportedHeightmapPaths =>
        importedHeightmapPaths;

    internal List<TerrainHeightStampLibraryIssue> MutableIssues =>
        issues;
}

internal readonly struct TerrainHeightStampLibraryLocation
{
    public TerrainHeightStampLibraryLocation(
        string heightmapsPath,
        string stampAssetsPath
    )
    {
        HeightmapsPath =
            heightmapsPath;

        StampAssetsPath =
            stampAssetsPath;
    }

    public string HeightmapsPath
    {
        get;
    }

    public string StampAssetsPath
    {
        get;
    }
}

/*
 * Production backend for the terrain height-stamp library.
 *
 * The source Texture2D filename is deliberately independent from the stable
 * TerrainHeightStampAsset identity. Relationship detection always follows the
 * actual serialized HeightTexture reference.
 */
public static class TerrainHeightStampLibraryUtility
{
    public const int CanonicalMaximumTextureSize =
        16384;

    private static readonly string[]
        PlatformOverrideNames =
        {
            "Standalone",
            "Android",
            "iPhone",
            "WebGL",
            "Windows Store Apps",
            "tvOS"
        };

    private static TerrainHeightStampLibraryLocation CanonicalLocation =>
        new TerrainHeightStampLibraryLocation(
            WorldMeshesPaths
                .AuthoringStampHeightmaps,
            WorldMeshesPaths
                .AuthoringStampAssets
        );

    // =====================================================
    // PUBLIC PRODUCTION API
    // =====================================================

    public static bool EnsureLibraryFolders(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths
                    .Authoring
            )
        )
        {
            errorMessage =
                "The WorldMeshes Authoring folder is missing.";

            return false;
        }

        if (
            !EnsureFolder(
                WorldMeshesPaths.Authoring,
                "Stamps",
                WorldMeshesPaths.AuthoringStamps,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !EnsureFolder(
                WorldMeshesPaths.AuthoringStamps,
                "Heightmaps",
                WorldMeshesPaths.AuthoringStampHeightmaps,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !EnsureFolder(
                WorldMeshesPaths.AuthoringStamps,
                "Assets",
                WorldMeshesPaths.AuthoringStampAssets,
                out errorMessage
            )
        )
        {
            return false;
        }

        return true;
    }

    public static TerrainHeightStampLibraryScanResult ScanLibrary()
    {
        if (
            !EnsureLibraryFolders(
                out string errorMessage
            )
        )
        {
            TerrainHeightStampLibraryScanResult failed =
                new TerrainHeightStampLibraryScanResult();

            AddIssue(
                failed.MutableIssues,
                TerrainHeightStampLibraryIssueSeverity.Error,
                "LibraryFoldersUnavailable",
                errorMessage,
                WorldMeshesPaths.AuthoringStamps
            );

            return failed;
        }

        return
            ScanLibrary(
                CanonicalLocation
            );
    }

    public static TerrainHeightStampLibraryOperationReport ImportHeightmap(
        string externalPngPath
    )
    {
        TerrainHeightStampLibraryOperationReport report =
            CreateReport(
                "Import Heightmap"
            );

        if (
            !TryValidateOperationState(
                report
            )
        )
        {
            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        if (
            !EnsureLibraryFolders(
                out string folderError
            )
        )
        {
            AddOperationError(
                report,
                "LibraryFoldersUnavailable",
                folderError,
                WorldMeshesPaths.AuthoringStamps
            );

            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        return
            ImportHeightmap(
                externalPngPath,
                CanonicalLocation,
                report
            );
    }

    public static TerrainHeightStampLibraryOperationReport ImportHeightmapFolder(
        string externalFolderPath
    )
    {
        TerrainHeightStampLibraryOperationReport report =
            CreateReport(
                "Import Heightmap Folder"
            );

        if (
            !TryValidateOperationState(
                report
            )
        )
        {
            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        if (
            !EnsureLibraryFolders(
                out string folderError
            )
        )
        {
            AddOperationError(
                report,
                "LibraryFoldersUnavailable",
                folderError,
                WorldMeshesPaths.AuthoringStamps
            );

            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        if (
            string.IsNullOrWhiteSpace(
                externalFolderPath
            )
        )
        {
            AddOperationError(
                report,
                "InvalidSourceFolder",
                "No source folder was supplied.",
                ""
            );

            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        string sourceFolder;

        try
        {
            sourceFolder =
                Path.GetFullPath(
                    externalFolderPath
                );
        }
        catch (Exception exception)
        {
            AddOperationError(
                report,
                "InvalidSourceFolder",
                exception.Message,
                externalFolderPath
            );

            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        if (
            !Directory.Exists(
                sourceFolder
            )
        )
        {
            AddOperationError(
                report,
                "SourceFolderMissing",
                "The selected source folder does not exist.",
                sourceFolder
            );

            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        string canonicalHeightmapFolder =
            AssetPathToAbsolutePath(
                WorldMeshesPaths
                    .AuthoringStampHeightmaps
            );

        if (
            PathsEqual(
                sourceFolder,
                canonicalHeightmapFolder
            )
        )
        {
            AddOperationError(
                report,
                "SourceIsLibraryFolder",
                "The selected folder is already the canonical Heightmaps "
                +
                "folder. Use Sync Library instead of importing it again.",
                sourceFolder
            );

            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        List<string> sourceFiles =
            new List<string>();

        try
        {
            string[] files =
                Directory.GetFiles(
                    sourceFolder,
                    "*",
                    SearchOption.AllDirectories
                );

            for (
                int index = 0;
                index < files.Length;
                index++
            )
            {
                if (
                    IsSupportedPng(
                        files[index]
                    )
                )
                {
                    sourceFiles.Add(
                        files[index]
                    );
                }
            }
        }
        catch (Exception exception)
        {
            AddOperationError(
                report,
                "SourceEnumerationFailed",
                exception.Message,
                sourceFolder
            );

            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        sourceFiles.Sort(
            StringComparer.OrdinalIgnoreCase
        );

        if (
            sourceFiles.Count ==
            0
        )
        {
            AddIssue(
                report.MutableIssues,
                TerrainHeightStampLibraryIssueSeverity.Warning,
                "NoPngFiles",
                "No PNG heightmaps were found in the selected folder.",
                sourceFolder
            );

            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        List<string> copiedAssetPaths =
            new List<string>();

        HashSet<string> reservedAssetPaths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        try
        {
            for (
                int index = 0;
                index < sourceFiles.Count;
                index++
            )
            {
                EditorUtility.DisplayProgressBar(
                    "WorldMeshes - Import Heightmaps",
                    "Copying PNG files...",
                    sourceFiles.Count <= 0
                        ? 0f
                        : (float)index /
                            sourceFiles.Count
                );

                if (
                    TryCopyExternalHeightmap(
                        sourceFiles[index],
                        CanonicalLocation,
                        reservedAssetPaths,
                        out string copiedAssetPath,
                        out string copyError
                    )
                )
                {
                    copiedAssetPaths.Add(
                        copiedAssetPath
                    );

                    reservedAssetPaths.Add(
                        copiedAssetPath
                    );

                    report.ImportedHeightmaps++;

                    report
                        .MutableImportedHeightmapPaths
                        .Add(
                            copiedAssetPath
                        );
                }
                else
                {
                    report.FailedItems++;

                    AddOperationError(
                        report,
                        "HeightmapCopyFailed",
                        copyError,
                        sourceFiles[index]
                    );
                }
            }

            if (
                copiedAssetPaths.Count >
                0
            )
            {
                /*
                 * Copy every external file first, then perform one synchronous
                 * AssetDatabase refresh. This avoids one automatic refresh per
                 * source file while still ensuring all TextureImporters exist
                 * before canonical configuration.
                 */
                using (WorldMeshesProfiler.AssetDatabaseRefresh.Auto())
                {
                    AssetDatabase.Refresh(
                        ImportAssetOptions.ForceSynchronousImport
                        |
                        ImportAssetOptions.ForceUpdate
                    );
                }

                List<Texture2D> importedTextures =
                    new List<Texture2D>();

                for (
                    int index = 0;
                    index < copiedAssetPaths.Count;
                    index++
                )
                {
                    EditorUtility.DisplayProgressBar(
                        "WorldMeshes - Import Heightmaps",
                        "Configuring heightmap importers...",
                        copiedAssetPaths.Count <= 0
                            ? 0f
                            : (float)index /
                                copiedAssetPaths.Count
                    );

                    string copiedAssetPath =
                        copiedAssetPaths[index];

                    if (
                        TryConfigureHeightmapImporter(
                            copiedAssetPath,
                            out bool importerChanged,
                            out string importerError
                        )
                    )
                    {
                        if (importerChanged)
                        {
                            report.ImporterRepairs++;
                        }
                    }
                    else
                    {
                        report.FailedItems++;

                        AddOperationError(
                            report,
                            "ImporterConfigurationFailed",
                            importerError,
                            copiedAssetPath
                        );

                        continue;
                    }

                    Texture2D texture =
                        AssetDatabase
                            .LoadAssetAtPath<Texture2D>(
                                copiedAssetPath
                            );

                    if (
                        texture == null
                    )
                    {
                        report.FailedItems++;

                        AddOperationError(
                            report,
                            "ImportedTextureUnavailable",
                            "Unity did not produce a Texture2D for the imported PNG.",
                            copiedAssetPath
                        );

                        continue;
                    }

                    importedTextures.Add(
                        texture
                    );
                }

                CreateStampAssetsForImportedTextures(
                    CanonicalLocation,
                    importedTextures,
                    report
                );
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
        {
            AssetDatabase.SaveAssets();
        }

        FinalizeReport(
            report,
            CanonicalLocation
        );

        return report;
    }

    public static TerrainHeightStampLibraryOperationReport SyncLibrary()
    {
        TerrainHeightStampLibraryOperationReport report =
            CreateReport(
                "Sync Stamp Library"
            );

        if (
            !TryValidateOperationState(
                report
            )
        )
        {
            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        if (
            !EnsureLibraryFolders(
                out string folderError
            )
        )
        {
            AddOperationError(
                report,
                "LibraryFoldersUnavailable",
                folderError,
                WorldMeshesPaths.AuthoringStamps
            );

            FinalizeReport(
                report,
                CanonicalLocation
            );

            return report;
        }

        return
            SyncLibrary(
                CanonicalLocation,
                report
            );
    }

    public static bool IsCanonicalHeightmapImporter(
        string assetPath,
        out string details
    )
    {
        return
            IsCanonicalHeightmapImporterInternal(
                assetPath,
                out details
            );
    }

    // =====================================================
    // INTERNAL VALIDATION ENTRY POINTS
    // =====================================================

    internal static TerrainHeightStampLibraryScanResult ScanLibrary(
        TerrainHeightStampLibraryLocation location
    )
    {
        TerrainHeightStampLibraryScanResult result =
            new TerrainHeightStampLibraryScanResult();

        if (
            !AssetDatabase.IsValidFolder(
                location.HeightmapsPath
            )
            ||
            !AssetDatabase.IsValidFolder(
                location.StampAssetsPath
            )
        )
        {
            AddIssue(
                result.MutableIssues,
                TerrainHeightStampLibraryIssueSeverity.Error,
                "LibraryLocationInvalid",
                "One or both stamp-library folders do not exist.",
                location.HeightmapsPath
            );

            result.NextLibraryId =
                1;

            return result;
        }

        List<string> heightmapFolderAssetPaths =
            FindAssetPathsUnderFolder(
                location.HeightmapsPath
            );

        for (
            int index = 0;
            index < heightmapFolderAssetPaths.Count;
            index++
        )
        {
            string path =
                heightmapFolderAssetPaths[index];

            if (
                IsSupportedPng(
                    path
                )
            )
            {
                Texture2D texture =
                    AssetDatabase
                        .LoadAssetAtPath<Texture2D>(
                            path
                        );

                if (
                    texture != null
                )
                {
                    result
                        .MutableHeightmaps
                        .Add(
                            texture
                        );

                    if (
                        !IsCanonicalHeightmapImporterInternal(
                            path,
                            out string importerDetails
                        )
                    )
                    {
                        result
                            .MutableImporterSettingsMismatches
                            .Add(
                                texture
                            );

                        AddIssue(
                            result.MutableIssues,
                            TerrainHeightStampLibraryIssueSeverity.Warning,
                            "ImporterMismatch",
                            importerDetails,
                            path
                        );
                    }

                    continue;
                }
            }

            result
                .MutableUnexpectedAssetPaths
                .Add(
                    path
                );

            AddIssue(
                result.MutableIssues,
                TerrainHeightStampLibraryIssueSeverity.Warning,
                "UnexpectedHeightmapAsset",
                "Only imported PNG Texture2D assets belong in the canonical "
                +
                "Heightmaps folder.",
                path
            );
        }

        List<string> stampFolderAssetPaths =
            FindAssetPathsUnderFolder(
                location.StampAssetsPath
            );

        for (
            int index = 0;
            index < stampFolderAssetPaths.Count;
            index++
        )
        {
            string path =
                stampFolderAssetPaths[index];

            TerrainHeightStampAsset stampAsset =
                AssetDatabase
                    .LoadAssetAtPath<TerrainHeightStampAsset>(
                        path
                    );

            if (
                stampAsset != null
            )
            {
                result
                    .MutableStampAssets
                    .Add(
                        stampAsset
                    );

                continue;
            }

            result
                .MutableUnexpectedAssetPaths
                .Add(
                    path
                );

            AddIssue(
                result.MutableIssues,
                TerrainHeightStampLibraryIssueSeverity.Warning,
                "UnexpectedStampAsset",
                "Only TerrainHeightStampAsset objects belong in the canonical "
                +
                "Assets folder.",
                path
            );
        }

        result
            .MutableHeightmaps
            .Sort(
                CompareUnityAssetPaths
            );

        result
            .MutableStampAssets
            .Sort(
                CompareUnityAssetPaths
            );

        AnalyzeIdentities(
            location,
            result
        );

        AnalyzeTextureRelationships(
            result
        );

        return result;
    }

    internal static TerrainHeightStampLibraryOperationReport ImportHeightmapForLocation(
        string externalPngPath,
        TerrainHeightStampLibraryLocation location
    )
    {
        TerrainHeightStampLibraryOperationReport report =
            CreateReport(
                "Import Heightmap Validation"
            );

        return
            ImportHeightmap(
                externalPngPath,
                location,
                report
            );
    }

    internal static TerrainHeightStampLibraryOperationReport SyncLibraryForLocation(
        TerrainHeightStampLibraryLocation location
    )
    {
        TerrainHeightStampLibraryOperationReport report =
            CreateReport(
                "Sync Stamp Library Validation"
            );

        return
            SyncLibrary(
                location,
                report
            );
    }

    // =====================================================
    // IMPORT IMPLEMENTATION
    // =====================================================

    private static TerrainHeightStampLibraryOperationReport ImportHeightmap(
        string externalPngPath,
        TerrainHeightStampLibraryLocation location,
        TerrainHeightStampLibraryOperationReport report
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                externalPngPath
            )
        )
        {
            AddOperationError(
                report,
                "InvalidSourceFile",
                "No source file was supplied.",
                ""
            );

            FinalizeReport(
                report,
                location
            );

            return report;
        }

        string sourcePath;

        try
        {
            sourcePath =
                Path.GetFullPath(
                    externalPngPath
                );
        }
        catch (Exception exception)
        {
            AddOperationError(
                report,
                "InvalidSourceFile",
                exception.Message,
                externalPngPath
            );

            FinalizeReport(
                report,
                location
            );

            return report;
        }

        if (
            !File.Exists(
                sourcePath
            )
        )
        {
            AddOperationError(
                report,
                "SourceFileMissing",
                "The selected source PNG does not exist.",
                sourcePath
            );

            FinalizeReport(
                report,
                location
            );

            return report;
        }

        if (
            !IsSupportedPng(
                sourcePath
            )
        )
        {
            AddOperationError(
                report,
                "UnsupportedSourceFormat",
                "Package 2 supports PNG heightmaps only.",
                sourcePath
            );

            FinalizeReport(
                report,
                location
            );

            return report;
        }

        string canonicalHeightmapFolder =
            AssetPathToAbsolutePath(
                location.HeightmapsPath
            );

        if (
            IsPathInsideFolder(
                sourcePath,
                canonicalHeightmapFolder
            )
        )
        {
            AddOperationError(
                report,
                "SourceAlreadyInLibrary",
                "The selected PNG is already inside this library's Heightmaps "
                +
                "folder. Use Sync Library instead.",
                sourcePath
            );

            FinalizeReport(
                report,
                location
            );

            return report;
        }

        HashSet<string> reserved =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        if (
            !TryCopyExternalHeightmap(
                sourcePath,
                location,
                reserved,
                out string copiedAssetPath,
                out string copyError
            )
        )
        {
            report.FailedItems++;

            AddOperationError(
                report,
                "HeightmapCopyFailed",
                copyError,
                sourcePath
            );

            FinalizeReport(
                report,
                location
            );

            return report;
        }

        report.ImportedHeightmaps++;

        report
            .MutableImportedHeightmapPaths
            .Add(
                copiedAssetPath
            );

        using (WorldMeshesProfiler.AssetDatabaseImportAsset.Auto())
        {
            AssetDatabase.ImportAsset(
                copiedAssetPath,
                ImportAssetOptions.ForceSynchronousImport
                |
                ImportAssetOptions.ForceUpdate
            );
        }

        ProcessImportedHeightmap(
            copiedAssetPath,
            location,
            report
        );

        using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
        {
            AssetDatabase.SaveAssets();
        }

        FinalizeReport(
            report,
            location
        );

        return report;
    }

    private static void CreateStampAssetsForImportedTextures(
        TerrainHeightStampLibraryLocation location,
        List<Texture2D> importedTextures,
        TerrainHeightStampLibraryOperationReport report
    )
    {
        if (
            importedTextures == null
            ||
            importedTextures.Count == 0
        )
        {
            return;
        }

        /*
         * Folder import resolves identity state once, then allocates IDs in a
         * single deterministic pass. This avoids rescanning the whole library
         * for every imported heightmap.
         */
        TerrainHeightStampLibraryScanResult scan =
            ScanLibrary(
                location
            );

        HashSet<Texture2D> assignedTextures =
            new HashSet<Texture2D>();

        HashSet<int> usedIds =
            new HashSet<int>();

        for (
            int index = 0;
            index < scan.StampAssets.Count;
            index++
        )
        {
            TerrainHeightStampAsset existing =
                scan.StampAssets[index];

            if (existing == null)
            {
                continue;
            }

            if (
                existing.HeightTexture !=
                null
            )
            {
                assignedTextures.Add(
                    existing.HeightTexture
                );
            }

            if (
                existing.HasLibraryId
            )
            {
                usedIds.Add(
                    existing.LibraryId
                );
            }
        }

        int nextId =
            scan.NextLibraryId > 0
                ? scan.NextLibraryId
                : 1;

        for (
            int index = 0;
            index < importedTextures.Count;
            index++
        )
        {
            Texture2D texture =
                importedTextures[index];

            EditorUtility.DisplayProgressBar(
                "WorldMeshes - Import Heightmaps",
                "Creating stamp assets...",
                importedTextures.Count <= 0
                    ? 0f
                    : (float)index /
                        importedTextures.Count
            );

            if (
                texture == null
            )
            {
                continue;
            }

            if (
                assignedTextures.Contains(
                    texture
                )
            )
            {
                AddIssue(
                    report.MutableIssues,
                    TerrainHeightStampLibraryIssueSeverity.Warning,
                    "TextureAlreadyAssigned",
                    "Imported Texture2D is already assigned to a stamp asset; "
                    +
                    "no duplicate asset was created.",
                    AssetDatabase.GetAssetPath(
                        texture
                    )
                );

                continue;
            }

            if (
                !TryAllocateLibraryId(
                    location,
                    usedIds,
                    ref nextId,
                    out int libraryId,
                    out string allocationError
                )
            )
            {
                report.FailedItems++;

                AddOperationError(
                    report,
                    "LibraryIdAllocationFailed",
                    allocationError,
                    AssetDatabase.GetAssetPath(
                        texture
                    )
                );

                continue;
            }

            if (
                TryCreateStampAssetWithId(
                    location,
                    texture,
                    libraryId,
                    out TerrainHeightStampAsset stampAsset,
                    out string createError
                )
            )
            {
                usedIds.Add(
                    libraryId
                );

                assignedTextures.Add(
                    texture
                );

                report.CreatedStampAssets++;

                report
                    .MutableCreatedStampAssets
                    .Add(
                        stampAsset
                    );
            }
            else
            {
                report.FailedItems++;

                AddOperationError(
                    report,
                    "StampAssetCreationFailed",
                    createError,
                    AssetDatabase.GetAssetPath(
                        texture
                    )
                );
            }
        }
    }

    private static void ProcessImportedHeightmap(
        string copiedAssetPath,
        TerrainHeightStampLibraryLocation location,
        TerrainHeightStampLibraryOperationReport report
    )
    {
        if (
            TryConfigureHeightmapImporter(
                copiedAssetPath,
                out bool importerChanged,
                out string importerError
            )
        )
        {
            if (importerChanged)
            {
                report.ImporterRepairs++;
            }
        }
        else
        {
            report.FailedItems++;

            AddOperationError(
                report,
                "ImporterConfigurationFailed",
                importerError,
                copiedAssetPath
            );

            return;
        }

        Texture2D texture =
            AssetDatabase
                .LoadAssetAtPath<Texture2D>(
                    copiedAssetPath
                );

        if (
            texture == null
        )
        {
            report.FailedItems++;

            AddOperationError(
                report,
                "ImportedTextureUnavailable",
                "Unity did not produce a Texture2D for the imported PNG.",
                copiedAssetPath
            );

            return;
        }

        if (
            TryCreateStampAsset(
                location,
                texture,
                out TerrainHeightStampAsset stampAsset,
                out string createError
            )
        )
        {
            report.CreatedStampAssets++;

            report
                .MutableCreatedStampAssets
                .Add(
                    stampAsset
                );
        }
        else
        {
            report.FailedItems++;

            AddOperationError(
                report,
                "StampAssetCreationFailed",
                createError,
                copiedAssetPath
            );
        }
    }

    private static bool TryCopyExternalHeightmap(
        string sourcePath,
        TerrainHeightStampLibraryLocation location,
        HashSet<string> reservedAssetPaths,
        out string copiedAssetPath,
        out string errorMessage
    )
    {
        copiedAssetPath =
            "";

        errorMessage =
            "";

        try
        {
            string fileName =
                Path.GetFileName(
                    sourcePath
                );

            if (
                string.IsNullOrEmpty(
                    fileName
                )
            )
            {
                errorMessage =
                    "Could not determine the PNG filename.";

                return false;
            }

            copiedAssetPath =
                GenerateUniqueHeightmapAssetPath(
                    location,
                    fileName,
                    reservedAssetPaths
                );

            if (
                string.IsNullOrEmpty(
                    copiedAssetPath
                )
            )
            {
                errorMessage =
                    "Could not allocate a unique destination path.";

                return false;
            }

            string absoluteDestination =
                AssetPathToAbsolutePath(
                    copiedAssetPath
                );

            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    absoluteDestination
                )
            );

            File.Copy(
                sourcePath,
                absoluteDestination,
                false
            );

            return true;
        }
        catch (Exception exception)
        {
            copiedAssetPath =
                "";

            errorMessage =
                exception.Message;

            return false;
        }
    }

    private static string GenerateUniqueHeightmapAssetPath(
        TerrainHeightStampLibraryLocation location,
        string sourceFileName,
        HashSet<string> reservedAssetPaths
    )
    {
        string extension =
            Path.GetExtension(
                sourceFileName
            );

        string baseName =
            Path.GetFileNameWithoutExtension(
                sourceFileName
            );

        if (
            string.IsNullOrEmpty(
                extension
            )
        )
        {
            extension =
                ".png";
        }

        for (
            int suffix = 0;
            suffix < 100000;
            suffix++
        )
        {
            string candidateName =
                suffix == 0
                    ? baseName +
                        extension
                    : baseName +
                        " " +
                        suffix +
                        extension;

            string requested =
                location.HeightmapsPath
                +
                "/"
                +
                candidateName;

            string candidate =
                AssetDatabase
                    .GenerateUniqueAssetPath(
                        requested
                    );

            if (
                reservedAssetPaths != null
                &&
                reservedAssetPaths.Contains(
                    candidate
                )
            )
            {
                continue;
            }

            string absolute =
                AssetPathToAbsolutePath(
                    candidate
                );

            if (
                File.Exists(
                    absolute
                )
                ||
                Directory.Exists(
                    absolute
                )
            )
            {
                continue;
            }

            return candidate;
        }

        return "";
    }

    // =====================================================
    // SYNC IMPLEMENTATION
    // =====================================================

    private static TerrainHeightStampLibraryOperationReport SyncLibrary(
        TerrainHeightStampLibraryLocation location,
        TerrainHeightStampLibraryOperationReport report
    )
    {
        TerrainHeightStampLibraryScanResult scan =
            ScanLibrary(
                location
            );

        /*
         * Repair importer settings first. Reimporting can recreate Texture2D
         * object instances, so rescan relationships afterward.
         */
        for (
            int index = 0;
            index < scan.Heightmaps.Count;
            index++
        )
        {
            string path =
                AssetDatabase.GetAssetPath(
                    scan.Heightmaps[index]
                );

            if (
                TryConfigureHeightmapImporter(
                    path,
                    out bool importerChanged,
                    out string importerError
                )
            )
            {
                if (importerChanged)
                {
                    report.ImporterRepairs++;
                }
            }
            else
            {
                report.FailedItems++;

                AddOperationError(
                    report,
                    "ImporterRepairFailed",
                    importerError,
                    path
                );
            }
        }

        scan =
            ScanLibrary(
                location
            );

        CanonicalizeStampAssetPaths(
            location,
            scan,
            report
        );

        scan =
            ScanLibrary(
                location
            );

        CreateMissingStampAssets(
            location,
            scan,
            report
        );

        using (WorldMeshesProfiler.AssetDatabaseSaveAssets.Auto())
        {
            AssetDatabase.SaveAssets();
        }

        FinalizeReport(
            report,
            location
        );

        return report;
    }

    private static void CanonicalizeStampAssetPaths(
        TerrainHeightStampLibraryLocation location,
        TerrainHeightStampLibraryScanResult scan,
        TerrainHeightStampLibraryOperationReport report
    )
    {
        for (
            int index = 0;
            index < scan.AssetPathMismatches.Count;
            index++
        )
        {
            TerrainHeightStampAssetPathMismatch mismatch =
                scan.AssetPathMismatches[index];

            if (
                mismatch == null
                ||
                mismatch.StampAsset == null
            )
            {
                continue;
            }

            if (
                !mismatch.CanCanonicalize
            )
            {
                continue;
            }

            UnityEngine.Object existing =
                AssetDatabase.LoadMainAssetAtPath(
                    mismatch.ExpectedPath
                );

            if (
                existing != null
            )
            {
                AddIssue(
                    report.MutableIssues,
                    TerrainHeightStampLibraryIssueSeverity.Warning,
                    "CanonicalPathOccupied",
                    "Could not canonicalize the stamp asset filename because "
                    +
                    "the expected path is already occupied.",
                    mismatch.ExpectedPath
                );

                continue;
            }

            string moveError =
                AssetDatabase.MoveAsset(
                    mismatch.CurrentPath,
                    mismatch.ExpectedPath
                );

            if (
                string.IsNullOrEmpty(
                    moveError
                )
            )
            {
                report.CanonicalAssetRenames++;
            }
            else
            {
                report.FailedItems++;

                AddOperationError(
                    report,
                    "CanonicalRenameFailed",
                    moveError,
                    mismatch.CurrentPath
                );
            }
        }
    }

    private static void CreateMissingStampAssets(
        TerrainHeightStampLibraryLocation location,
        TerrainHeightStampLibraryScanResult scan,
        TerrainHeightStampLibraryOperationReport report
    )
    {
        List<Texture2D> missing =
            new List<Texture2D>(
                scan.MissingStampAssets
            );

        missing.Sort(
            CompareUnityAssetPaths
        );

        HashSet<int> usedIds =
            new HashSet<int>();

        for (
            int index = 0;
            index < scan.StampAssets.Count;
            index++
        )
        {
            TerrainHeightStampAsset asset =
                scan.StampAssets[index];

            if (
                asset != null
                &&
                asset.HasLibraryId
            )
            {
                usedIds.Add(
                    asset.LibraryId
                );
            }
        }

        int nextId =
            scan.NextLibraryId > 0
                ? scan.NextLibraryId
                : 1;

        for (
            int index = 0;
            index < missing.Count;
            index++
        )
        {
            Texture2D texture =
                missing[index];

            if (
                texture == null
            )
            {
                continue;
            }

            if (
                !TryAllocateLibraryId(
                    location,
                    usedIds,
                    ref nextId,
                    out int libraryId,
                    out string allocationError
                )
            )
            {
                report.FailedItems++;

                AddOperationError(
                    report,
                    "LibraryIdAllocationFailed",
                    allocationError,
                    AssetDatabase.GetAssetPath(
                        texture
                    )
                );

                continue;
            }

            if (
                TryCreateStampAssetWithId(
                    location,
                    texture,
                    libraryId,
                    out TerrainHeightStampAsset stampAsset,
                    out string createError
                )
            )
            {
                usedIds.Add(
                    libraryId
                );

                report.CreatedStampAssets++;

                report
                    .MutableCreatedStampAssets
                    .Add(
                        stampAsset
                    );
            }
            else
            {
                report.FailedItems++;

                AddOperationError(
                    report,
                    "MissingStampCreationFailed",
                    createError,
                    AssetDatabase.GetAssetPath(
                        texture
                    )
                );
            }
        }
    }

    // =====================================================
    // STAMP ASSET CREATION / ID ALLOCATION
    // =====================================================

    private static bool TryCreateStampAsset(
        TerrainHeightStampLibraryLocation location,
        Texture2D texture,
        out TerrainHeightStampAsset stampAsset,
        out string errorMessage
    )
    {
        stampAsset =
            null;

        errorMessage =
            "";

        if (
            texture == null
        )
        {
            errorMessage =
                "HeightTexture is null.";

            return false;
        }

        TerrainHeightStampLibraryScanResult scan =
            ScanLibrary(
                location
            );

        for (
            int index = 0;
            index < scan.StampAssets.Count;
            index++
        )
        {
            TerrainHeightStampAsset existing =
                scan.StampAssets[index];

            if (
                existing != null
                &&
                existing.HeightTexture ==
                    texture
            )
            {
                errorMessage =
                    "This Texture2D is already assigned to stamp asset "
                    +
                    existing.DisplayName
                    +
                    ".";

                return false;
            }
        }

        HashSet<int> usedIds =
            new HashSet<int>();

        for (
            int index = 0;
            index < scan.StampAssets.Count;
            index++
        )
        {
            TerrainHeightStampAsset existing =
                scan.StampAssets[index];

            if (
                existing != null
                &&
                existing.HasLibraryId
            )
            {
                usedIds.Add(
                    existing.LibraryId
                );
            }
        }

        int nextId =
            scan.NextLibraryId > 0
                ? scan.NextLibraryId
                : 1;

        if (
            !TryAllocateLibraryId(
                location,
                usedIds,
                ref nextId,
                out int libraryId,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            TryCreateStampAssetWithId(
                location,
                texture,
                libraryId,
                out stampAsset,
                out errorMessage
            );
    }

    private static bool TryAllocateLibraryId(
        TerrainHeightStampLibraryLocation location,
        HashSet<int> usedIds,
        ref int nextId,
        out int allocatedId,
        out string errorMessage
    )
    {
        allocatedId =
            0;

        errorMessage =
            "";

        int candidate =
            Mathf.Max(
                1,
                nextId
            );

        while (
            candidate >
            0
        )
        {
            if (
                usedIds != null
                &&
                usedIds.Contains(
                    candidate
                )
            )
            {
                if (
                    candidate ==
                    int.MaxValue
                )
                {
                    break;
                }

                candidate++;

                continue;
            }

            string canonicalPath =
                GetCanonicalAssetPath(
                    location,
                    candidate
                );

            if (
                AssetDatabase.LoadMainAssetAtPath(
                    canonicalPath
                )
                ==
                null
            )
            {
                allocatedId =
                    candidate;

                nextId =
                    candidate ==
                    int.MaxValue
                        ? 0
                        : candidate +
                            1;

                return true;
            }

            if (
                candidate ==
                int.MaxValue
            )
            {
                break;
            }

            candidate++;
        }

        errorMessage =
            "No positive library ID could be allocated.";

        return false;
    }

    private static bool TryCreateStampAssetWithId(
        TerrainHeightStampLibraryLocation location,
        Texture2D texture,
        int libraryId,
        out TerrainHeightStampAsset stampAsset,
        out string errorMessage
    )
    {
        stampAsset =
            null;

        errorMessage =
            "";

        if (
            texture == null
        )
        {
            errorMessage =
                "HeightTexture is null.";

            return false;
        }

        if (
            !TerrainHeightStampIdentityUtility
                .IsValidLibraryId(
                    libraryId
                )
        )
        {
            errorMessage =
                "LibraryId must be positive.";

            return false;
        }

        string assetName =
            TerrainHeightStampLibraryIdentityUtility
                .GetCanonicalAssetName(
                    libraryId
                );

        string assetPath =
            GetCanonicalAssetPath(
                location,
                libraryId
            );

        if (
            AssetDatabase.LoadMainAssetAtPath(
                assetPath
            )
            !=
            null
        )
        {
            errorMessage =
                "Canonical stamp asset path is already occupied: "
                +
                assetPath;

            return false;
        }

        try
        {
            stampAsset =
                ScriptableObject
                    .CreateInstance<TerrainHeightStampAsset>();

            stampAsset.name =
                assetName;

            stampAsset
                .SetLibraryIdInternal(
                    libraryId
                );

            stampAsset
                .SetHeightTextureInternal(
                    texture
                );

            AssetDatabase.CreateAsset(
                stampAsset,
                assetPath
            );

            EditorUtility.SetDirty(
                stampAsset
            );

            return true;
        }
        catch (Exception exception)
        {
            if (
                stampAsset !=
                null
                &&
                !AssetDatabase.Contains(
                    stampAsset
                )
            )
            {
                UnityEngine.Object.DestroyImmediate(
                    stampAsset
                );
            }

            stampAsset =
                null;

            errorMessage =
                exception.Message;

            return false;
        }
    }

    // =====================================================
    // CANONICAL TEXTURE IMPORTER
    // =====================================================

    private static bool TryConfigureHeightmapImporter(
        string assetPath,
        out bool changed,
        out string errorMessage
    )
    {
        changed =
            false;

        errorMessage =
            "";

        TextureImporter importer =
            AssetImporter.GetAtPath(
                assetPath
            )
            as TextureImporter;

        if (
            importer == null
        )
        {
            errorMessage =
                "TextureImporter could not be resolved.";

            return false;
        }

        try
        {
            changed =
                ApplyCanonicalHeightmapImporterSettings(
                    importer
                );

            if (changed)
            {
                importer.SaveAndReimport();
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                exception.Message;

            return false;
        }
    }

    private static bool ApplyCanonicalHeightmapImporterSettings(
        TextureImporter importer
    )
    {
        bool changed =
            false;

        changed |=
            SetIfDifferent(
                importer.textureType,
                TextureImporterType.Default,
                value =>
                    importer.textureType =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.textureShape,
                TextureImporterShape.Texture2D,
                value =>
                    importer.textureShape =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.sRGBTexture,
                false,
                value =>
                    importer.sRGBTexture =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.mipmapEnabled,
                true,
                value =>
                    importer.mipmapEnabled =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.filterMode,
                FilterMode.Bilinear,
                value =>
                    importer.filterMode =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.wrapMode,
                TextureWrapMode.Clamp,
                value =>
                    importer.wrapMode =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.textureCompression,
                TextureImporterCompression.Uncompressed,
                value =>
                    importer.textureCompression =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.crunchedCompression,
                false,
                value =>
                    importer.crunchedCompression =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.isReadable,
                false,
                value =>
                    importer.isReadable =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.npotScale,
                TextureImporterNPOTScale.None,
                value =>
                    importer.npotScale =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.maxTextureSize,
                CanonicalMaximumTextureSize,
                value =>
                    importer.maxTextureSize =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.ignorePngGamma,
                true,
                value =>
                    importer.ignorePngGamma =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.streamingMipmaps,
                false,
                value =>
                    importer.streamingMipmaps =
                        value
            );

        changed |=
            SetIfDifferent(
                importer.ignoreMipmapLimit,
                true,
                value =>
                    importer.ignoreMipmapLimit =
                        value
            );

        for (
            int index = 0;
            index < PlatformOverrideNames.Length;
            index++
        )
        {
            string platformName =
                PlatformOverrideNames[index];

            TextureImporterPlatformSettings platformSettings =
                importer.GetPlatformTextureSettings(
                    platformName
                );

            if (
                platformSettings !=
                    null
                &&
                platformSettings.overridden
            )
            {
                importer.ClearPlatformTextureSettings(
                    platformName
                );

                changed =
                    true;
            }
        }

        return changed;
    }

    private static bool IsCanonicalHeightmapImporterInternal(
        string assetPath,
        out string details
    )
    {
        TextureImporter importer =
            AssetImporter.GetAtPath(
                assetPath
            )
            as TextureImporter;

        if (
            importer == null
        )
        {
            details =
                "TextureImporter could not be resolved.";

            return false;
        }

        List<string> mismatches =
            new List<string>();

        if (
            importer.textureType !=
            TextureImporterType.Default
        )
        {
            mismatches.Add(
                "Texture Type"
            );
        }

        if (
            importer.textureShape !=
            TextureImporterShape.Texture2D
        )
        {
            mismatches.Add(
                "Texture Shape"
            );
        }

        if (
            importer.sRGBTexture
        )
        {
            mismatches.Add(
                "sRGB"
            );
        }

        if (
            !importer.mipmapEnabled
        )
        {
            mismatches.Add(
                "Mip Maps"
            );
        }

        if (
            importer.filterMode !=
            FilterMode.Bilinear
        )
        {
            mismatches.Add(
                "Filter Mode"
            );
        }

        if (
            importer.wrapMode !=
            TextureWrapMode.Clamp
        )
        {
            mismatches.Add(
                "Wrap Mode"
            );
        }

        if (
            importer.textureCompression !=
            TextureImporterCompression.Uncompressed
        )
        {
            mismatches.Add(
                "Compression"
            );
        }

        if (
            importer.crunchedCompression
        )
        {
            mismatches.Add(
                "Crunch"
            );
        }

        if (
            importer.isReadable
        )
        {
            mismatches.Add(
                "Read/Write"
            );
        }

        if (
            importer.npotScale !=
            TextureImporterNPOTScale.None
        )
        {
            mismatches.Add(
                "NPOT Scale"
            );
        }

        if (
            importer.maxTextureSize !=
            CanonicalMaximumTextureSize
        )
        {
            mismatches.Add(
                "Max Texture Size"
            );
        }

        if (
            !importer.ignorePngGamma
        )
        {
            mismatches.Add(
                "PNG Gamma"
            );
        }

        if (
            importer.streamingMipmaps
        )
        {
            mismatches.Add(
                "Streaming Mipmaps"
            );
        }

        if (
            !importer.ignoreMipmapLimit
        )
        {
            mismatches.Add(
                "Mipmap Limit"
            );
        }

        for (
            int index = 0;
            index < PlatformOverrideNames.Length;
            index++
        )
        {
            TextureImporterPlatformSettings platformSettings =
                importer.GetPlatformTextureSettings(
                    PlatformOverrideNames[index]
                );

            if (
                platformSettings !=
                    null
                &&
                platformSettings.overridden
            )
            {
                mismatches.Add(
                    "Platform Override: "
                    +
                    PlatformOverrideNames[index]
                );
            }
        }

        details =
            mismatches.Count == 0
                ? "Canonical."
                : "Non-canonical importer settings: "
                    +
                    string.Join(
                        ", ",
                        mismatches
                    );

        return
            mismatches.Count ==
            0;
    }

    private static bool SetIfDifferent<T>(
        T currentValue,
        T desiredValue,
        Action<T> setter
    )
    {
        if (
            EqualityComparer<T>
                .Default
                .Equals(
                    currentValue,
                    desiredValue
                )
        )
        {
            return false;
        }

        setter(
            desiredValue
        );

        return true;
    }

    // =====================================================
    // SCAN ANALYSIS
    // =====================================================

    private static void AnalyzeIdentities(
        TerrainHeightStampLibraryLocation location,
        TerrainHeightStampLibraryScanResult result
    )
    {
        List<int> validIds =
            new List<int>();

        Dictionary<int, int> idCounts =
            new Dictionary<int, int>();

        int maximumValidId =
            0;

        for (
            int index = 0;
            index < result.StampAssets.Count;
            index++
        )
        {
            TerrainHeightStampAsset asset =
                result.StampAssets[index];

            if (
                asset == null
            )
            {
                continue;
            }

            if (
                !asset.HasLibraryId
            )
            {
                result
                    .MutableInvalidIdAssets
                    .Add(
                        asset
                    );

                AddIssue(
                    result.MutableIssues,
                    TerrainHeightStampLibraryIssueSeverity.Error,
                    "InvalidLibraryId",
                    "TerrainHeightStampAsset has a non-positive LibraryId.",
                    AssetDatabase.GetAssetPath(
                        asset
                    )
                );

                continue;
            }

            validIds.Add(
                asset.LibraryId
            );

            maximumValidId =
                Math.Max(
                    maximumValidId,
                    asset.LibraryId
                );

            if (
                idCounts.TryGetValue(
                    asset.LibraryId,
                    out int count
                )
            )
            {
                idCounts[
                    asset.LibraryId
                ] =
                    count +
                    1;
            }
            else
            {
                idCounts.Add(
                    asset.LibraryId,
                    1
                );
            }
        }

        List<int> duplicateIds =
            TerrainHeightStampLibraryIdentityUtility
                .FindDuplicateIds(
                    validIds
                );

        result
            .MutableDuplicateIds
            .AddRange(
                duplicateIds
            );

        HashSet<int> duplicateIdSet =
            new HashSet<int>(
                duplicateIds
            );

        for (
            int index = 0;
            index < duplicateIds.Count;
            index++
        )
        {
            AddIssue(
                result.MutableIssues,
                TerrainHeightStampLibraryIssueSeverity.Error,
                "DuplicateLibraryId",
                "Multiple TerrainHeightStampAsset objects use LibraryId "
                +
                duplicateIds[index]
                +
                ".",
                location.StampAssetsPath
            );
        }

        result.MaximumValidLibraryId =
            maximumValidId;

        if (
            TerrainHeightStampLibraryIdentityUtility
                .TryCalculateNextLibraryId(
                    validIds,
                    out int nextLibraryId
                )
        )
        {
            result.NextLibraryId =
                nextLibraryId;
        }
        else
        {
            result.NextLibraryId =
                0;

            AddIssue(
                result.MutableIssues,
                TerrainHeightStampLibraryIssueSeverity.Error,
                "LibraryIdExhausted",
                "A new positive library ID cannot be allocated.",
                location.StampAssetsPath
            );
        }

        for (
            int index = 0;
            index < result.StampAssets.Count;
            index++
        )
        {
            TerrainHeightStampAsset asset =
                result.StampAssets[index];

            if (
                asset == null
                ||
                !asset.HasLibraryId
            )
            {
                continue;
            }

            string currentPath =
                AssetDatabase.GetAssetPath(
                    asset
                );

            string expectedPath =
                GetCanonicalAssetPath(
                    location,
                    asset.LibraryId
                );

            if (
                string.Equals(
                    currentPath,
                    expectedPath,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            bool canCanonicalize =
                !duplicateIdSet.Contains(
                    asset.LibraryId
                );

            TerrainHeightStampAssetPathMismatch mismatch =
                new TerrainHeightStampAssetPathMismatch
                {
                    StampAsset =
                        asset,

                    CurrentPath =
                        currentPath,

                    ExpectedPath =
                        expectedPath,

                    CanCanonicalize =
                        canCanonicalize
                };

            result
                .MutableAssetPathMismatches
                .Add(
                    mismatch
                );

            AddIssue(
                result.MutableIssues,
                TerrainHeightStampLibraryIssueSeverity.Warning,
                "AssetPathMismatch",
                canCanonicalize
                    ? "Stamp asset filename/path does not match its stable "
                        +
                        "LibraryId and can be safely canonicalized if the "
                        +
                        "target path is free."
                    : "Stamp asset filename/path does not match its LibraryId, "
                        +
                        "but duplicate IDs prevent safe automatic renaming.",
                currentPath
            );
        }
    }

    private static void AnalyzeTextureRelationships(
        TerrainHeightStampLibraryScanResult result
    )
    {
        Dictionary<Texture2D, List<TerrainHeightStampAsset>>
            assignments =
                new Dictionary<Texture2D, List<TerrainHeightStampAsset>>();

        for (
            int index = 0;
            index < result.StampAssets.Count;
            index++
        )
        {
            TerrainHeightStampAsset stampAsset =
                result.StampAssets[index];

            if (
                stampAsset == null
            )
            {
                continue;
            }

            Texture2D texture =
                stampAsset.HeightTexture;

            if (
                texture == null
            )
            {
                result
                    .MutableBrokenStampAssets
                    .Add(
                        stampAsset
                    );

                AddIssue(
                    result.MutableIssues,
                    TerrainHeightStampLibraryIssueSeverity.Error,
                    "MissingHeightTexture",
                    "TerrainHeightStampAsset has no HeightTexture reference.",
                    AssetDatabase.GetAssetPath(
                        stampAsset
                    )
                );

                continue;
            }

            if (
                assignments.TryGetValue(
                    texture,
                    out List<TerrainHeightStampAsset> assignedAssets
                )
            )
            {
                assignedAssets.Add(
                    stampAsset
                );
            }
            else
            {
                assignments.Add(
                    texture,
                    new List<TerrainHeightStampAsset>
                    {
                        stampAsset
                    }
                );
            }
        }

        foreach (
            KeyValuePair<Texture2D, List<TerrainHeightStampAsset>> pair
            in assignments
        )
        {
            if (
                pair.Value.Count <=
                1
            )
            {
                continue;
            }

            TerrainHeightStampDuplicateTextureAssignment duplicate =
                new TerrainHeightStampDuplicateTextureAssignment
                {
                    Heightmap =
                        pair.Key
                };

            duplicate
                .MutableStampAssets
                .AddRange(
                    pair.Value
                );

            result
                .MutableDuplicateTextureAssignments
                .Add(
                    duplicate
                );

            AddIssue(
                result.MutableIssues,
                TerrainHeightStampLibraryIssueSeverity.Error,
                "DuplicateTextureAssignment",
                "Multiple TerrainHeightStampAsset objects reference the same "
                +
                "Texture2D. Sync will not delete or choose between them.",
                AssetDatabase.GetAssetPath(
                    pair.Key
                )
            );
        }

        for (
            int index = 0;
            index < result.Heightmaps.Count;
            index++
        )
        {
            Texture2D heightmap =
                result.Heightmaps[index];

            if (
                !assignments.ContainsKey(
                    heightmap
                )
            )
            {
                result
                    .MutableMissingStampAssets
                    .Add(
                        heightmap
                    );

                AddIssue(
                    result.MutableIssues,
                    TerrainHeightStampLibraryIssueSeverity.Info,
                    "MissingStampAsset",
                    "Heightmap is not referenced by a TerrainHeightStampAsset. "
                    +
                    "Sync can create one.",
                    AssetDatabase.GetAssetPath(
                        heightmap
                    )
                );
            }
        }
    }

    // =====================================================
    // SHARED HELPERS
    // =====================================================

    private static TerrainHeightStampLibraryOperationReport CreateReport(
        string operation
    )
    {
        return
            new TerrainHeightStampLibraryOperationReport
            {
                Operation =
                    operation
            };
    }

    private static void FinalizeReport(
        TerrainHeightStampLibraryOperationReport report,
        TerrainHeightStampLibraryLocation location
    )
    {
        if (
            report == null
        )
        {
            return;
        }

        report.FinalScan =
            ScanLibrary(
                location
            );
    }

    private static bool TryValidateOperationState(
        TerrainHeightStampLibraryOperationReport report
    )
    {
        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            AddOperationError(
                report,
                "PlayModeUnavailable",
                "Stamp library import/sync is available only in Edit Mode.",
                ""
            );

            return false;
        }

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            AddOperationError(
                report,
                "EditorBusy",
                "Wait for Unity to finish compiling/importing before changing "
                +
                "the stamp library.",
                ""
            );

            return false;
        }

        return true;
    }

    private static bool EnsureFolder(
        string parentPath,
        string folderName,
        string fullPath,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            AssetDatabase.IsValidFolder(
                fullPath
            )
        )
        {
            return true;
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
        )
        {
            errorMessage =
                "Could not create folder: "
                +
                fullPath;

            return false;
        }

        string createdPath =
            AssetDatabase.GUIDToAssetPath(
                guid
            );

        if (
            !string.Equals(
                createdPath,
                fullPath,
                StringComparison.Ordinal
            )
        )
        {
            errorMessage =
                "Unity created an unexpected folder path: "
                +
                createdPath;

            return false;
        }

        return true;
    }

    private static List<string> FindAssetPathsUnderFolder(
        string folder
    )
    {
        List<string> paths =
            new List<string>();

        string[] guids =
            AssetDatabase.FindAssets(
                "",
                new[]
                {
                    folder
                }
            );

        HashSet<string> uniquePaths =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        for (
            int index = 0;
            index < guids.Length;
            index++
        )
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guids[index]
                );

            if (
                string.IsNullOrEmpty(
                    path
                )
                ||
                AssetDatabase.IsValidFolder(
                    path
                )
                ||
                !uniquePaths.Add(
                    path
                )
            )
            {
                continue;
            }

            paths.Add(
                path
            );
        }

        paths.Sort(
            StringComparer.Ordinal
        );

        return paths;
    }

    private static string GetCanonicalAssetPath(
        TerrainHeightStampLibraryLocation location,
        int libraryId
    )
    {
        if (
            !TerrainHeightStampIdentityUtility
                .IsValidLibraryId(
                    libraryId
                )
        )
        {
            return "";
        }

        return
            location.StampAssetsPath
            +
            "/"
            +
            TerrainHeightStampLibraryIdentityUtility
                .GetCanonicalAssetName(
                    libraryId
                )
            +
            ".asset";
    }

    private static bool IsSupportedPng(
        string path
    )
    {
        return
            string.Equals(
                Path.GetExtension(
                    path
                ),
                ".png",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static int CompareUnityAssetPaths<T>(
        T a,
        T b
    )
        where T : UnityEngine.Object
    {
        return
            string.Compare(
                a != null
                    ? AssetDatabase.GetAssetPath(
                        a
                    )
                    : "",
                b != null
                    ? AssetDatabase.GetAssetPath(
                        b
                    )
                    : "",
                StringComparison.Ordinal
            );
    }

    private static string AssetPathToAbsolutePath(
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
                    assetPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar
                    )
                )
            );
    }

    private static bool IsPathInsideFolder(
        string path,
        string folder
    )
    {
        string fullPath =
            Path.GetFullPath(
                path
            )
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );

        string fullFolder =
            Path.GetFullPath(
                folder
            )
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );

        if (
            PathsEqual(
                fullPath,
                fullFolder
            )
        )
        {
            return true;
        }

        string prefix =
            fullFolder
            +
            Path.DirectorySeparatorChar;

        return
            fullPath.StartsWith(
                prefix,
                StringComparison.Ordinal
            );
    }

    private static bool PathsEqual(
        string a,
        string b
    )
    {
        return
            string.Equals(
                Path.GetFullPath(
                    a
                )
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ),
                Path.GetFullPath(
                    b
                )
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ),
                StringComparison.Ordinal
            );
    }

    private static void AddOperationError(
        TerrainHeightStampLibraryOperationReport report,
        string code,
        string message,
        string assetPath
    )
    {
        AddIssue(
            report.MutableIssues,
            TerrainHeightStampLibraryIssueSeverity.Error,
            code,
            message,
            assetPath
        );
    }

    private static void AddIssue(
        List<TerrainHeightStampLibraryIssue> issues,
        TerrainHeightStampLibraryIssueSeverity severity,
        string code,
        string message,
        string assetPath
    )
    {
        issues.Add(
            new TerrainHeightStampLibraryIssue
            {
                Severity =
                    severity,

                Code =
                    code ?? "",

                Message =
                    message ?? "",

                AssetPath =
                    assetPath ?? ""
            }
        );
    }
}
