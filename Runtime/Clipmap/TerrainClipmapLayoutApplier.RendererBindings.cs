using System.Collections.Generic;
using UnityEngine;

public sealed partial class TerrainClipmapLayoutApplier
{
    /*
     * MRH06 authoritative renderer-role enumeration.
     *
     * TerrainClipmapLayoutApplier already owns generated hierarchy lookup.
     * Height binding, validation and diagnostics consume this semantic list
     * rather than independently interpreting renderer names.
     */
    public bool TryGetRendererBindings(
        List<TerrainClipmapRendererBinding> output,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (output == null)
        {
            errorMessage =
                "The renderer-binding output list is null.";

            return false;
        }

        output.Clear();

        if (
            !TryEnsureHierarchyReferences(
                out errorMessage
            )
        )
        {
            return false;
        }

        Transform centerTransform =
            clipmapRoot.Find(
                "Center_LOD0"
            );

        if (centerTransform == null)
        {
            errorMessage =
                "Missing generated clipmap renderer 'Center_LOD0'.";

            return false;
        }

        MeshRenderer centerRenderer =
            centerTransform.GetComponent<MeshRenderer>();

        if (centerRenderer == null)
        {
            errorMessage =
                "Generated clipmap object 'Center_LOD0' has no MeshRenderer.";

            return false;
        }

        output.Add(
            new TerrainClipmapRendererBinding(
                centerRenderer,
                TerrainClipmapRendererRole.CreateCenter()
            )
        );

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            Transform levelTransform =
                lodLevelTransforms[level];

            if (levelTransform == null)
            {
                errorMessage =
                    $"Generated clipmap group LOD{level} is missing.";

                return false;
            }

            Transform ringTransform =
                levelTransform.Find(
                    $"Ring_LOD{level}"
                );

            if (ringTransform == null)
            {
                errorMessage =
                    $"Missing generated clipmap ring 'Ring_LOD{level}'.";

                return false;
            }

            MeshRenderer ringRenderer =
                ringTransform.GetComponent<MeshRenderer>();

            if (ringRenderer == null)
            {
                errorMessage =
                    $"Generated clipmap ring 'Ring_LOD{level}' has no MeshRenderer.";

                return false;
            }

            if (
                !TerrainClipmapRendererRole.TryCreateRing(
                    level,
                    out TerrainClipmapRendererRole ringRole
                )
            )
            {
                errorMessage =
                    $"Could not create renderer role for Ring_LOD{level}.";

                return false;
            }

            output.Add(
                new TerrainClipmapRendererBinding(
                    ringRenderer,
                    ringRole
                )
            );

            MeshRenderer stitchRenderer =
                stitchRenderers[level];

            if (stitchRenderer == null)
            {
                errorMessage =
                    $"Missing generated stitch renderer for LOD{level - 1} -> LOD{level}.";

                return false;
            }

            if (
                !TerrainClipmapRendererRole.TryCreateStitch(
                    level - 1,
                    level,
                    out TerrainClipmapRendererRole stitchRole
                )
            )
            {
                errorMessage =
                    $"Could not create renderer role for Stitch_LOD{level - 1}_LOD{level}.";

                return false;
            }

            output.Add(
                new TerrainClipmapRendererBinding(
                    stitchRenderer,
                    stitchRole
                )
            );
        }

        return true;
    }
}
