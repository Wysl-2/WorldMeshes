using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/*
 * Editor-only presentation/helper layer for the Package 4 stamp browser.
 *
 * This class deliberately does not scan the AssetDatabase. The editor window
 * supplies cached Package 2 scan data so search, sorting, selection resolution,
 * thumbnail access, and layout calculations remain repaint-cheap.
 */
public static class TerrainHeightStampLibraryBrowserUtility
{
    public const float DefaultMinimumCellWidth =
        112f;

    public const float DefaultCellSpacing =
        6f;

    public static void BuildVisibleAssets(
        IReadOnlyList<TerrainHeightStampAsset> source,
        string searchText,
        List<TerrainHeightStampAsset> destination
    )
    {
        if (destination == null)
        {
            throw new ArgumentNullException(
                nameof(destination)
            );
        }

        destination.Clear();

        if (source == null)
        {
            return;
        }

        string query =
            string.IsNullOrWhiteSpace(
                searchText
            )
                ? ""
                : searchText.Trim();

        for (
            int index = 0;
            index < source.Count;
            index++
        )
        {
            TerrainHeightStampAsset asset =
                source[index];

            if (
                asset == null
                ||
                !asset.HasLibraryId
                ||
                !asset.IsConfigured
            )
            {
                continue;
            }

            if (
                !MatchesSearch(
                    asset,
                    query
                )
            )
            {
                continue;
            }

            destination.Add(
                asset
            );
        }

        destination.Sort(
            CompareAssets
        );
    }

    public static bool MatchesSearch(
        TerrainHeightStampAsset asset,
        string searchText
    )
    {
        if (
            asset == null
            ||
            !asset.HasLibraryId
        )
        {
            return false;
        }

        if (
            string.IsNullOrWhiteSpace(
                searchText
            )
        )
        {
            return true;
        }

        string query =
            searchText.Trim();

        if (
            int.TryParse(
                query,
                out int numericId
            )
        )
        {
            return
                asset.LibraryId ==
                numericId;
        }

        return
            asset.DisplayName
                .IndexOf(
                    query,
                    StringComparison.OrdinalIgnoreCase
                )
            >=
            0;
    }

    public static TerrainHeightStampAsset FindByLibraryId(
        IReadOnlyList<TerrainHeightStampAsset> assets,
        int libraryId
    )
    {
        if (
            assets == null
            ||
            !TerrainHeightStampIdentityUtility
                .IsValidLibraryId(
                    libraryId
                )
        )
        {
            return null;
        }

        for (
            int index = 0;
            index < assets.Count;
            index++
        )
        {
            TerrainHeightStampAsset asset =
                assets[index];

            if (
                asset != null
                &&
                asset.LibraryId ==
                    libraryId
            )
            {
                return asset;
            }
        }

        return null;
    }

    public static Texture2D GetThumbnail(
        TerrainHeightStampAsset asset
    )
    {
        return
            asset != null
                ? asset.HeightTexture
                : null;
    }

    public static int CalculateColumnCount(
        float availableWidth,
        float minimumCellWidth =
            DefaultMinimumCellWidth,
        float spacing =
            DefaultCellSpacing
    )
    {
        float safeWidth =
            Mathf.Max(
                1f,
                availableWidth
            );

        float safeCellWidth =
            Mathf.Max(
                32f,
                minimumCellWidth
            );

        float safeSpacing =
            Mathf.Max(
                0f,
                spacing
            );

        return
            Mathf.Max(
                1,
                Mathf.FloorToInt(
                    (
                        safeWidth +
                        safeSpacing
                    )
                    /
                    (
                        safeCellWidth +
                        safeSpacing
                    )
                )
            );
    }

    public static string BuildCompactHealthSummary(
        TerrainHeightStampLibraryScanResult scan
    )
    {
        if (scan == null)
        {
            return
                "Library scan unavailable.";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            scan.HeightmapCount
        );

        builder.Append(
            " Heightmaps  |  "
        );

        builder.Append(
            scan.StampAssetCount
        );

        builder.Append(
            " Stamp Assets"
        );

        if (
            scan.MissingStampAssetCount >
            0
        )
        {
            builder.Append(
                "  |  "
            );

            builder.Append(
                scan.MissingStampAssetCount
            );

            builder.Append(
                " Missing"
            );
        }

        if (
            scan.WarningCount >
            0
        )
        {
            builder.Append(
                "  |  "
            );

            builder.Append(
                scan.WarningCount
            );

            builder.Append(
                scan.WarningCount == 1
                    ? " Warning"
                    : " Warnings"
            );
        }

        if (
            scan.ErrorCount >
            0
        )
        {
            builder.Append(
                "  |  "
            );

            builder.Append(
                scan.ErrorCount
            );

            builder.Append(
                scan.ErrorCount == 1
                    ? " Error"
                    : " Errors"
            );
        }

        if (
            scan.IsHealthy
            &&
            scan.WarningCount == 0
        )
        {
            builder.Append(
                "  |  Healthy"
            );
        }

        return
            builder.ToString();
    }

    public static string BuildIssueDetails(
        TerrainHeightStampLibraryScanResult scan,
        int maximumIssues
    )
    {
        if (
            scan == null
            ||
            scan.Issues == null
            ||
            scan.Issues.Count <= 0
        )
        {
            return
                "No library issues.";
        }

        int safeMaximum =
            Mathf.Max(
                1,
                maximumIssues
            );

        int count =
            Mathf.Min(
                safeMaximum,
                scan.Issues.Count
            );

        StringBuilder builder =
            new StringBuilder();

        for (
            int index = 0;
            index < count;
            index++
        )
        {
            TerrainHeightStampLibraryIssue issue =
                scan.Issues[index];

            if (issue == null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(
                issue.Severity
            );

            builder.Append(
                ": "
            );

            builder.Append(
                issue.Message
            );

            /*
             * Asset paths are intentionally restricted to this explicit
             * advanced-details view. Normal browser labels use DisplayName.
             */
            if (
                !string.IsNullOrEmpty(
                    issue.AssetPath
                )
            )
            {
                builder.AppendLine();

                builder.Append(
                    issue.AssetPath
                );
            }
        }

        if (
            scan.Issues.Count >
            count
        )
        {
            builder.AppendLine();
            builder.AppendLine();

            builder.Append(
                "+"
            );

            builder.Append(
                scan.Issues.Count -
                count
            );

            builder.Append(
                " more issue(s)."
            );
        }

        return
            builder.ToString();
    }

    /*
     * Production Add Selected Stamp boundary.
     *
     * TerrainAuthoringData is mutated only through the Package 3 service.
     * Successful creation also updates the existing modifier selection model.
     */
    public static bool TryAddSelectedStamp(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainHeightStampAsset stampAsset,
        Vector2 positionXZ,
        out string stableId,
        out string errorMessage
    )
    {
        stableId =
            "";

        errorMessage =
            "";

        if (
            stampAsset == null
            ||
            !stampAsset.HasLibraryId
            ||
            !stampAsset.IsConfigured
        )
        {
            errorMessage =
                "Select a configured terrain height stamp before adding it.";

            return false;
        }

        if (
            !TerrainAuthoringModifierService
                .AddStampModifierFromAssetDefaults(
                    authoringData,
                    worldSettings,
                    stampAsset,
                    positionXZ,
                    out stableId,
                    out errorMessage
                )
        )
        {
            return false;
        }

        TerrainAuthoringModifierSelection
            .Select(
                stableId
            );

        return true;
    }

    private static int CompareAssets(
        TerrainHeightStampAsset left,
        TerrainHeightStampAsset right
    )
    {
        if (ReferenceEquals(
            left,
            right
        ))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int idComparison =
            left.LibraryId.CompareTo(
                right.LibraryId
            );

        if (idComparison != 0)
        {
            return
                idComparison;
        }

        return
            string.CompareOrdinal(
                left.DisplayName,
                right.DisplayName
            );
    }
}
