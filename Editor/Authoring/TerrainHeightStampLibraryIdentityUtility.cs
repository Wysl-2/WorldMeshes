using System;
using System.Collections.Generic;
using UnityEditor;

public sealed class TerrainHeightStampLibraryIdentityScanResult
{
    private readonly List<TerrainHeightStampAsset>
        assets =
            new List<TerrainHeightStampAsset>();

    private readonly List<TerrainHeightStampAsset>
        invalidIdAssets =
            new List<TerrainHeightStampAsset>();

    private readonly List<int>
        duplicateIds =
            new List<int>();

    public IReadOnlyList<TerrainHeightStampAsset> Assets =>
        assets;

    public IReadOnlyList<TerrainHeightStampAsset> InvalidIdAssets =>
        invalidIdAssets;

    public IReadOnlyList<int> DuplicateIds =>
        duplicateIds;

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

    public bool HasIdentityProblems =>
        invalidIdAssets.Count > 0
        ||
        duplicateIds.Count > 0;

    internal List<TerrainHeightStampAsset> MutableAssets =>
        assets;

    internal List<TerrainHeightStampAsset> MutableInvalidIdAssets =>
        invalidIdAssets;

    internal List<int> MutableDuplicateIds =>
        duplicateIds;
}

public static class TerrainHeightStampLibraryIdentityUtility
{
    public static string GetCanonicalAssetName(
        int libraryId
    )
    {
        return
            TerrainHeightStampIdentityUtility
                .FormatDisplayName(
                    libraryId
                );
    }

    public static string GetCanonicalAssetPath(
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
            WorldMeshesPaths
                .AuthoringStampAssets
            +
            "/"
            +
            GetCanonicalAssetName(
                libraryId
            )
            +
            ".asset";
    }

    public static TerrainHeightStampLibraryIdentityScanResult ScanLibrary()
    {
        TerrainHeightStampLibraryIdentityScanResult result =
            new TerrainHeightStampLibraryIdentityScanResult();

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths
                    .AuthoringStampAssets
            )
        )
        {
            result.NextLibraryId =
                1;

            return result;
        }

        string[] guids =
            AssetDatabase.FindAssets(
                "t:TerrainHeightStampAsset",
                new[]
                {
                    WorldMeshesPaths
                        .AuthoringStampAssets
                }
            );

        Array.Sort(
            guids,
            StringComparer.Ordinal
        );

        Dictionary<int, int> counts =
            new Dictionary<int, int>();

        int maximumValidId =
            0;

        for (
            int index = 0;
            index < guids.Length;
            index++
        )
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guids[
                        index
                    ]
                );

            TerrainHeightStampAsset asset =
                AssetDatabase
                    .LoadAssetAtPath<TerrainHeightStampAsset>(
                        path
                    );

            if (asset == null)
            {
                continue;
            }

            result
                .MutableAssets
                .Add(
                    asset
                );

            int libraryId =
                asset.LibraryId;

            if (
                !TerrainHeightStampIdentityUtility
                    .IsValidLibraryId(
                        libraryId
                    )
            )
            {
                result
                    .MutableInvalidIdAssets
                    .Add(
                        asset
                    );

                continue;
            }

            maximumValidId =
                Math.Max(
                    maximumValidId,
                    libraryId
                );

            if (
                counts.TryGetValue(
                    libraryId,
                    out int existingCount
                )
            )
            {
                counts[
                    libraryId
                ] =
                    existingCount +
                    1;
            }
            else
            {
                counts.Add(
                    libraryId,
                    1
                );
            }
        }

        result
            .MutableDuplicateIds
            .AddRange(
                FindDuplicateIds(
                    counts
                )
            );

        result.MaximumValidLibraryId =
            maximumValidId;

        result.NextLibraryId =
            CalculateNextLibraryId(
                maximumValidId
            );

        return result;
    }

    internal static List<int> FindDuplicateIds(
        IEnumerable<int> libraryIds
    )
    {
        Dictionary<int, int> counts =
            new Dictionary<int, int>();

        if (libraryIds != null)
        {
            foreach (
                int libraryId
                in libraryIds
            )
            {
                if (
                    !TerrainHeightStampIdentityUtility
                        .IsValidLibraryId(
                            libraryId
                        )
                )
                {
                    continue;
                }

                if (
                    counts.TryGetValue(
                        libraryId,
                        out int existingCount
                    )
                )
                {
                    counts[
                        libraryId
                    ] =
                        existingCount +
                        1;
                }
                else
                {
                    counts.Add(
                        libraryId,
                        1
                    );
                }
            }
        }

        return
            FindDuplicateIds(
                counts
            );
    }

    internal static bool TryCalculateNextLibraryId(
        IEnumerable<int> libraryIds,
        out int nextLibraryId
    )
    {
        nextLibraryId =
            1;

        int maximum =
            0;

        if (libraryIds != null)
        {
            foreach (
                int libraryId
                in libraryIds
            )
            {
                if (
                    !TerrainHeightStampIdentityUtility
                        .IsValidLibraryId(
                            libraryId
                        )
                )
                {
                    continue;
                }

                maximum =
                    Math.Max(
                        maximum,
                        libraryId
                    );
            }
        }

        if (maximum == int.MaxValue)
        {
            nextLibraryId =
                0;

            return false;
        }

        nextLibraryId =
            maximum +
            1;

        return true;
    }

    private static int CalculateNextLibraryId(
        int maximumValidLibraryId
    )
    {
        if (
            maximumValidLibraryId < 1
        )
        {
            return 1;
        }

        if (
            maximumValidLibraryId ==
                int.MaxValue
        )
        {
            return 0;
        }

        return
            maximumValidLibraryId +
            1;
    }

    private static List<int> FindDuplicateIds(
        Dictionary<int, int> counts
    )
    {
        List<int> duplicateIds =
            new List<int>();

        foreach (
            KeyValuePair<int, int> pair
            in counts
        )
        {
            if (
                pair.Value > 1
            )
            {
                duplicateIds.Add(
                    pair.Key
                );
            }
        }

        duplicateIds.Sort();

        return duplicateIds;
    }
}
