using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class WorldGridViewportInfoPanel
{
    private const float ContentInset =
        8f;

    internal static void Draw(
        Rect panelRect,
        WorldGridViewportContext context,
        WorldGridViewportLattice lattice
    )
    {
        if (
            panelRect.width <= 0f
            ||
            panelRect.height <= 0f
        )
        {
            return;
        }

        GUI.Box(
            panelRect,
            GUIContent.none,
            EditorStyles.helpBox
        );

        Rect contentRect =
            new Rect(
                panelRect.x +
                    ContentInset,
                panelRect.y +
                    ContentInset,
                Mathf.Max(
                    0f,
                    panelRect.width -
                        ContentInset *
                        2f
                ),
                Mathf.Max(
                    0f,
                    panelRect.height -
                        ContentInset *
                        2f
                )
            );

        GUILayout.BeginArea(
            contentRect
        );

        IReadOnlyList<IWorldGridViewportInfoProvider> providers =
            WorldGridViewportInfoProviderRegistry
                .Providers;

        bool drewProvider =
            false;

        for (
            int i = 0;
            i < providers.Count;
            i++
        )
        {
            IWorldGridViewportInfoProvider provider =
                providers[i];

            if (
                provider == null
                ||
                !provider.IsAvailable(
                    context
                )
            )
            {
                continue;
            }

            if (drewProvider)
            {
                GUILayout.Space(
                    8f
                );
            }

            provider.DrawInformation(
                context,
                lattice
            );

            drewProvider =
                true;
        }

        if (!drewProvider)
        {
            GUILayout.Label(
                "No information available.",
                EditorStyles.centeredGreyMiniLabel
            );
        }

        GUILayout.EndArea();
    }
}
