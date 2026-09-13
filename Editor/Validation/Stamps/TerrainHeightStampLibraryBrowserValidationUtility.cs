using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TerrainHeightStampLibraryBrowserValidationUtility
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
        "/StampLibraryBrowser";

    private static readonly string ValidationDataPath =
        ValidationRoot
        +
        "/TerrainAuthoringData.asset";

    private static readonly string ValidationStampPath =
        ValidationRoot
        +
        "/Heightmap_317.asset";

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

        string previousModifierSelection =
            TerrainAuthoringModifierSelection
                .SelectedStableId;

        try
        {
            RunValidation();
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
            TerrainAuthoringModifierSelection
                .Select(
                    previousModifierSelection
                );

            CleanupValidationAssets();

            Finish();
        }
    }

    private static void RunValidation()
    {
        if (
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Add(
                "Validation prerequisites",
                false,
                "Run stamp library browser validation while the editor is idle in Edit Mode."
            );

            return;
        }

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        TerrainAuthoringData realData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath
            );

        if (
            worldSettings == null
            ||
            realData == null
        )
        {
            Add(
                "Validation prerequisites",
                false,
                "WorldSettings or TerrainAuthoringData could not be loaded."
            );

            return;
        }

        Add(
            "Validation prerequisites",
            true,
            "WorldSettings and TerrainAuthoringData are available."
        );

        ValidateCachedBrowserHelpers();

        CleanupValidationAssets();
        PrepareValidationFolder();

        TerrainAuthoringData tempData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        tempData.authoringRevision =
            realData.authoringRevision;

        AssetDatabase.CreateAsset(
            tempData,
            ValidationDataPath
        );

        TerrainHeightStampAsset stamp =
            ScriptableObject
                .CreateInstance<TerrainHeightStampAsset>();

        stamp.name =
            "Heightmap_317";

        stamp.SetLibraryIdInternal(
            317
        );

        Texture2D thumbnail =
            new Texture2D(
                8,
                8,
                TextureFormat.RGBA32,
                false
            );

        thumbnail.name =
            "heightmap_20260905222247_64S6";

        stamp.SetHeightTextureInternal(
            thumbnail
        );

        stamp.SetCreationDefaultsInternal(
            new Vector2(
                260f,
                180f
            ),
            36f,
            0.15f,
            0.85f,
            1.4f,
            TerrainStampFalloffShape.Ellipse,
            TerrainStampFalloffProfile.Sharp,
            0.35f,
            4.5f,
            0.7f
        );

        AssetDatabase.CreateAsset(
            stamp,
            ValidationStampPath
        );

        AssetDatabase.AddObjectToAsset(
            thumbnail,
            stamp
        );

        AssetDatabase.SaveAssets();

        Vector2 requestedPosition =
            new Vector2(
                125f,
                225f
            );

        bool added =
            TerrainHeightStampLibraryBrowserUtility
                .TryAddSelectedStamp(
                    tempData,
                    worldSettings,
                    stamp,
                    requestedPosition,
                    out string stableId,
                    out string addError
                );

        TerrainStampModifier placed =
            FindStampModifier(
                tempData,
                stableId
            );

        bool copiedDefaults =
            added
            &&
            placed != null
            &&
            placed.StampAsset ==
                stamp
            &&
            Approximately(
                placed.PositionXZ,
                requestedPosition
            )
            &&
            Approximately(
                placed.SizeXZ,
                new Vector2(
                    260f,
                    180f
                )
            )
            &&
            Mathf.Approximately(
                placed.HeightDelta,
                36f
            )
            &&
            Mathf.Approximately(
                placed.SourceInputMin,
                0.15f
            )
            &&
            Mathf.Approximately(
                placed.SourceInputMax,
                0.85f
            )
            &&
            Mathf.Approximately(
                placed.SourceGamma,
                1.4f
            )
            &&
            placed.FalloffShape ==
                TerrainStampFalloffShape.Ellipse
            &&
            placed.FalloffProfile ==
                TerrainStampFalloffProfile.Sharp
            &&
            Mathf.Approximately(
                placed.Falloff,
                0.35f
            )
            &&
            Mathf.Approximately(
                placed.SmoothingRadius,
                4.5f
            )
            &&
            Mathf.Approximately(
                placed.SmoothingStrength,
                0.7f
            );

        Add(
            "Add Selected Stamp copies Package 3 defaults",
            copiedDefaults,
            copiedDefaults
                ? "The browser creation boundary routed through AddStampModifierFromAssetDefaults."
                : addError
        );

        bool selectedNewModifier =
            added
            &&
            !string.IsNullOrEmpty(
                stableId
            )
            &&
            TerrainAuthoringModifierSelection
                .SelectedStableId ==
            stableId;

        Add(
            "Add Selected Stamp selects the new modifier",
            selectedNewModifier,
            selectedNewModifier
                ? "TerrainAuthoringModifierSelection points at the newly-created StableId."
                : "The new modifier did not become the active modifier selection."
        );
    }

    private static void ValidateCachedBrowserHelpers()
    {
        List<TerrainHeightStampAsset> assets =
            new List<TerrainHeightStampAsset>();

        Texture2D texture =
            new Texture2D(
                4,
                4,
                TextureFormat.RGBA32,
                false
            );

        texture.name =
            "heightmap_20260905222247_64S6";

        string modifierSelectionBefore =
            TerrainAuthoringModifierSelection
                .SelectedStableId;

        try
        {
            for (
                int id = 500;
                id >= 1;
                id--
            )
            {
                TerrainHeightStampAsset asset =
                    ScriptableObject
                        .CreateInstance<TerrainHeightStampAsset>();

                asset.name =
                    "SourceFile_" +
                    id;

                asset.SetLibraryIdInternal(
                    id
                );

                asset.SetHeightTextureInternal(
                    texture
                );

                assets.Add(
                    asset
                );
            }

            List<TerrainHeightStampAsset> visible =
                new List<TerrainHeightStampAsset>();

            TerrainHeightStampLibraryBrowserUtility
                .BuildVisibleAssets(
                    assets,
                    "",
                    visible
                );

            bool largeLibraryPassed =
                visible.Count ==
                    500
                &&
                visible[0].LibraryId ==
                    1
                &&
                visible[
                    visible.Count - 1
                ].LibraryId ==
                    500;

            Add(
                "500-stamp cached list sorts without AssetDatabase scanning",
                largeLibraryPassed,
                largeLibraryPassed
                    ? "The cached helper returned 500 valid assets in stable LibraryId order."
                    : "The cached helper did not return the expected 500-asset order."
            );

            List<TerrainHeightStampAsset> canonicalSearch =
                new List<TerrainHeightStampAsset>();

            TerrainHeightStampLibraryBrowserUtility
                .BuildVisibleAssets(
                    assets,
                    "HEIGHTMAP_017",
                    canonicalSearch
                );

            List<TerrainHeightStampAsset> numericSearch =
                new List<TerrainHeightStampAsset>();

            TerrainHeightStampLibraryBrowserUtility
                .BuildVisibleAssets(
                    assets,
                    "017",
                    numericSearch
                );

            bool searchPassed =
                canonicalSearch.Count ==
                    1
                &&
                canonicalSearch[0].LibraryId ==
                    17
                &&
                numericSearch.Count ==
                    1
                &&
                numericSearch[0].LibraryId ==
                    17;

            Add(
                "Search matches canonical name and numeric ID",
                searchPassed,
                "Expected HEIGHTMAP_017 and 017 to resolve only Heightmap_017."
            );

            TerrainHeightStampAsset seventeenth =
                numericSearch.Count >
                    0
                    ? numericSearch[0]
                    : null;

            bool labelPassed =
                seventeenth != null
                &&
                seventeenth.DisplayName ==
                    "Heightmap_017"
                &&
                seventeenth.DisplayName !=
                    texture.name
                &&
                TerrainHeightStampLibraryBrowserUtility
                    .GetThumbnail(
                        seventeenth
                    ) ==
                    texture;

            Add(
                "Thumbnail cells use short identity and direct HeightTexture",
                labelPassed,
                labelPassed
                    ? "DisplayName is Heightmap_017 while the direct thumbnail texture keeps its verbose source name."
                    : "Browser identity or direct HeightTexture thumbnail behavior was incorrect."
            );

            List<TerrainHeightStampAsset> refreshed =
                new List<TerrainHeightStampAsset>();

            TerrainHeightStampLibraryBrowserUtility
                .BuildVisibleAssets(
                    assets,
                    "",
                    refreshed
                );

            TerrainHeightStampAsset resolved =
                TerrainHeightStampLibraryBrowserUtility
                    .FindByLibraryId(
                        refreshed,
                        317
                    );

            bool selectionPersistencePassed =
                resolved != null
                &&
                resolved.LibraryId ==
                    317;

            Add(
                "Library selection survives cache rebuild by stable LibraryId",
                selectionPersistencePassed,
                selectionPersistencePassed
                    ? "Heightmap_317 resolved after rebuilding the cached list."
                    : "The persisted LibraryId did not resolve after a cache rebuild."
            );

            bool selectionIndependent =
                TerrainAuthoringModifierSelection
                    .SelectedStableId ==
                modifierSelectionBefore;

            Add(
                "Library browsing does not alter modifier selection",
                selectionIndependent,
                selectionIndependent
                    ? "Search, filtering, and library selection resolution left TerrainAuthoringModifierSelection unchanged."
                    : "Browser-only operations unexpectedly changed terrain modifier selection."
            );

            int narrowColumns =
                TerrainHeightStampLibraryBrowserUtility
                    .CalculateColumnCount(
                        300f
                    );

            int wideColumns =
                TerrainHeightStampLibraryBrowserUtility
                    .CalculateColumnCount(
                        700f
                    );

            Add(
                "Thumbnail grid adapts to available width",
                narrowColumns >= 1
                &&
                wideColumns >
                    narrowColumns,
                "300 px columns=" +
                    narrowColumns +
                    ", 700 px columns=" +
                    wideColumns
            );
        }
        finally
        {
            for (
                int index = 0;
                index < assets.Count;
                index++
            )
            {
                if (assets[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        assets[index]
                    );
                }
            }

            UnityEngine.Object.DestroyImmediate(
                texture
            );
        }
    }

    private static TerrainStampModifier FindStampModifier(
        TerrainAuthoringData data,
        string stableId
    )
    {
        if (
            data == null
            ||
            string.IsNullOrEmpty(
                stableId
            )
        )
        {
            return null;
        }

        IReadOnlyList<TerrainHeightModifier> modifiers =
            data.HeightModifiers;

        for (
            int index = 0;
            index < modifiers.Count;
            index++
        )
        {
            if (
                modifiers[index] is
                    TerrainStampModifier stamp
                &&
                stamp.StableId ==
                    stableId
            )
            {
                return stamp;
            }
        }

        return null;
    }

    private static void PrepareValidationFolder()
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
            "StampLibraryBrowser",
            ValidationRoot
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
                "Could not create validation folder: " +
                fullPath
            );
        }
    }

    private static void CleanupValidationAssets()
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

    private static bool Approximately(
        Vector2 left,
        Vector2 right
    )
    {
        return
            Mathf.Approximately(
                left.x,
                right.x
            )
            &&
            Mathf.Approximately(
                left.y,
                right.y
            );
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

        StringBuilder builder =
            new StringBuilder();

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
            builder.Length >
                0
                ? builder.ToString()
                : "No validation results were produced.";

        if (
            lastFailedCount >
            0
        )
        {
            Debug.LogError(
                "Stamp Library Browser validation failed.\n\n"
                +
                lastSummary
            );
        }
        else
        {
            Debug.Log(
                "Stamp Library Browser validation passed.\n\n"
                +
                lastSummary
            );
        }

        WorldMeshesEditorWindow[] windows =
            Resources.FindObjectsOfTypeAll<
                WorldMeshesEditorWindow
            >();

        for (
            int index = 0;
            index < windows.Length;
            index++
        )
        {
            if (windows[index] != null)
            {
                windows[index].Repaint();
            }
        }
    }
}
