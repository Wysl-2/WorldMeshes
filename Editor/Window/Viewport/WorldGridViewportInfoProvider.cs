using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

internal interface IWorldGridViewportInfoProvider
{
    string Id
    {
        get;
    }

    bool IsAvailable(
        WorldGridViewportContext context
    );

    void DrawInformation(
        WorldGridViewportContext context,
        WorldGridViewportLattice lattice
    );
}

internal static class WorldGridViewportInfoProviderRegistry
{
    private static readonly IWorldGridViewportInfoProvider[] RegisteredProviders =
    {
        new WorldGridViewportWorldInfoProvider()
    };

    internal static IReadOnlyList<IWorldGridViewportInfoProvider> Providers
    {
        get
        {
            return
                RegisteredProviders;
        }
    }
}

internal sealed class WorldGridViewportWorldInfoProvider :
    IWorldGridViewportInfoProvider
{
    public string Id
    {
        get
        {
            return
                "world";
        }
    }

    public bool IsAvailable(
        WorldGridViewportContext context
    )
    {
        return
            context.WorldSettings != null;
    }

    public void DrawInformation(
        WorldGridViewportContext context,
        WorldGridViewportLattice lattice
    )
    {
        WorldSettings settings =
            context.WorldSettings;

        if (settings == null)
        {
            return;
        }

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    settings
                );

        GUILayout.Label(
            "WORLD",
            EditorStyles.boldLabel
        );

        DrawValue(
            "Context",
            GetExecutionStateDisplayName(
                context
            )
        );

        DrawValue(
            "Grid",
            WorldGridViewportLatticeUtility
                .GetDisplayName(
                    lattice
                )
        );

        DrawValue(
            "World Size",
            worldSizeXZ.x.ToString(
                "0.##",
                CultureInfo.InvariantCulture
            ) +
            " x " +
            worldSizeXZ.y.ToString(
                "0.##",
                CultureInfo.InvariantCulture
            ) +
            " m"
        );

        DrawValue(
            "Chunk Size",
            Mathf.Max(
                0.01f,
                settings.chunkSize
            ).ToString(
                "0.##",
                CultureInfo.InvariantCulture
            ) +
            " m"
        );

        DrawValue(
            "Height Tile Span",
            Mathf.Max(
                1,
                settings.heightTileChunkSpan
            ).ToString(
                CultureInfo.InvariantCulture
            ) +
            " chunks"
        );
    }

    private static string GetExecutionStateDisplayName(
        WorldGridViewportContext context
    )
    {
        switch (context.ExecutionState)
        {
            case WorldGridViewportExecutionState.EnteringPlayMode:
                return
                    "Entering Play Mode";

            case WorldGridViewportExecutionState.PlayMode:
                return
                    context.IsPaused
                        ? "Play Mode (Paused)"
                        : "Play Mode";

            case WorldGridViewportExecutionState.ExitingPlayMode:
                return
                    "Exiting Play Mode";

            default:
                return
                    "Edit Mode";
        }
    }

    private static void DrawValue(
        string label,
        string value
    )
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField(
            label,
            GUILayout.Width(
                92f
            )
        );

        EditorGUILayout.LabelField(
            value
        );

        EditorGUILayout.EndHorizontal();
    }
}
