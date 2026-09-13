using UnityEditor;
using UnityEngine;

public static class TerrainHeightStampLibraryMenu
{
    [MenuItem(
        "Tools/WorldMeshes/Import/Import Heightmap",
        false,
        1
    )]
    private static void ImportHeightmap()
    {
        string path =
            EditorUtility.OpenFilePanel(
                "Import Terrain Heightmap",
                "",
                "png"
            );

        if (
            string.IsNullOrEmpty(
                path
            )
        )
        {
            return;
        }

        TerrainHeightStampLibraryOperationReport report =
            TerrainHeightStampLibraryUtility
                .ImportHeightmap(
                    path
                );

        PresentReport(
            report
        );
    }

    [MenuItem(
        "Tools/WorldMeshes/Import/Import Heightmap Folder",
        false,
        0
    )]
    private static void ImportHeightmapFolder()
    {
        string path =
            EditorUtility.OpenFolderPanel(
                "Import Terrain Heightmap Folder",
                "",
                ""
            );

        if (
            string.IsNullOrEmpty(
                path
            )
        )
        {
            return;
        }

        TerrainHeightStampLibraryOperationReport report =
            TerrainHeightStampLibraryUtility
                .ImportHeightmapFolder(
                    path
                );

        PresentReport(
            report
        );
    }

    [MenuItem(
        "Tools/WorldMeshes/Import/Sync Library",
        false,
        2
    )]
    private static void SyncLibrary()
    {
        TerrainHeightStampLibraryOperationReport report =
            TerrainHeightStampLibraryUtility
                .SyncLibrary();

        PresentReport(
            report
        );
    }

    private static void PresentReport(
        TerrainHeightStampLibraryOperationReport report
    )
    {
        if (
            report == null
        )
        {
            EditorUtility.DisplayDialog(
                "WorldMeshes Stamp Library",
                "The stamp-library operation returned no report.",
                "OK"
            );

            return;
        }

        string summary =
            report.BuildSummary();

        if (
            report.Succeeded
        )
        {
            Debug.Log(
                summary
            );
        }
        else
        {
            Debug.LogError(
                summary
            );
        }

        TerrainHeightStampAsset created =
            report.LastCreatedStampAsset;

        if (
            created != null
        )
        {
            Selection.activeObject =
                created;

            EditorGUIUtility.PingObject(
                created
            );
        }

        EditorUtility.DisplayDialog(
            "WorldMeshes Stamp Library",
            summary,
            "OK"
        );
    }
}
