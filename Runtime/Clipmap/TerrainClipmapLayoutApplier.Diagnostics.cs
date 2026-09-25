using System.Collections.Generic;
using UnityEngine;

public sealed partial class TerrainClipmapLayoutApplier
{
    public bool TryGetAppliedLODAnchor(
        int level,
        out Vector3 anchor,
        out string errorMessage
    )
    {
        anchor = default;
        errorMessage = "";

        if (
            !TryEnsureHierarchyReferences(
                out errorMessage
            )
        )
        {
            return false;
        }

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        if (
            level < 0
            || level >= levelCount
        )
        {
            errorMessage =
                $"LOD level {level} is outside the configured clipmap range.";

            return false;
        }

        if (level == 0)
        {
            anchor =
                clipmapRoot.position;

            return true;
        }

        Transform levelTransform =
            lodLevelTransforms[level];

        if (levelTransform == null)
        {
            errorMessage =
                $"Generated clipmap group LOD{level} is missing.";

            return false;
        }

        anchor =
            levelTransform.position;

        return true;
    }

    public bool TryGetValidatedRendererBindings(
        List<TerrainClipmapRendererBinding> output,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !TryGetRendererBindings(
                output,
                out errorMessage
            )
        )
        {
            return false;
        }

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        int expectedCount =
            1 +
            Mathf.Max(
                0,
                levelCount - 1
            )
            * 2;

        if (output.Count != expectedCount)
        {
            errorMessage =
                $"Semantic clipmap renderer count is {output.Count}, expected {expectedCount}.";

            return false;
        }

        HashSet<int> rendererIds =
            new HashSet<int>();

        HashSet<string> roleKeys =
            new HashSet<string>();

        for (
            int index = 0;
            index < output.Count;
            index++
        )
        {
            TerrainClipmapRendererBinding binding =
                output[index];

            if (!binding.IsValid)
            {
                errorMessage =
                    $"Semantic renderer binding {index} is invalid.";

                return false;
            }

            int rendererId =
                binding.Renderer.GetInstanceID();

            if (!rendererIds.Add(rendererId))
            {
                errorMessage =
                    $"Renderer '{binding.Renderer.name}' appears more than once in semantic clipmap bindings.";

                return false;
            }

            string roleKey =
                GetRoleKey(
                    binding.Role
                );

            if (!roleKeys.Add(roleKey))
            {
                errorMessage =
                    $"Semantic role '{roleKey}' appears more than once in clipmap bindings.";

                return false;
            }

            if (
                binding.Role.Kind ==
                    TerrainClipmapRendererKind.Center
            )
            {
                if (
                    binding.Role.Level != 0
                    || binding.Role.HeightOwnerLevel != 0
                )
                {
                    errorMessage =
                        "Center renderer role does not own Height LOD0.";

                    return false;
                }
            }
            else if (
                binding.Role.Kind ==
                    TerrainClipmapRendererKind.Ring
            )
            {
                if (
                    binding.Role.Level < 1
                    || binding.Role.HeightOwnerLevel !=
                        binding.Role.Level
                )
                {
                    errorMessage =
                        $"Ring renderer '{binding.Renderer.name}' has invalid Height ownership.";

                    return false;
                }
            }
            else if (
                binding.Role.Kind ==
                    TerrainClipmapRendererKind.Stitch
            )
            {
                if (
                    binding.Role.CoarseLevel !=
                        binding.Role.FineLevel + 1
                    || binding.Role.HeightOwnerLevel !=
                        binding.Role.FineLevel
                )
                {
                    errorMessage =
                        $"Stitch renderer '{binding.Renderer.name}' does not own the finer adjacent Height LOD.";

                    return false;
                }
            }
        }

        MeshRenderer[] hierarchyRenderers =
            clipmapRoot.GetComponentsInChildren<MeshRenderer>(
                true
            );

        for (
            int index = 0;
            index < hierarchyRenderers.Length;
            index++
        )
        {
            MeshRenderer renderer =
                hierarchyRenderers[index];

            if (
                renderer == null
                || !LooksLikeManagedClipmapRenderer(
                    renderer.name
                )
            )
            {
                continue;
            }

            if (
                !rendererIds.Contains(
                    renderer.GetInstanceID()
                )
            )
            {
                errorMessage =
                    $"Managed-looking clipmap renderer '{renderer.name}' is not represented by the authoritative semantic binding list.";

                return false;
            }
        }

        return true;
    }

    private static string GetRoleKey(
        TerrainClipmapRendererRole role
    )
    {
        switch (role.Kind)
        {
            case TerrainClipmapRendererKind.Center:
                return "Center:0";

            case TerrainClipmapRendererKind.Ring:
                return
                    "Ring:" +
                    role.Level;

            case TerrainClipmapRendererKind.Stitch:
                return
                    "Stitch:" +
                    role.FineLevel +
                    ":" +
                    role.CoarseLevel;

            default:
                return "Unknown";
        }
    }

    private static bool LooksLikeManagedClipmapRenderer(
        string name
    )
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return
            name.StartsWith("Center_LOD")
            || name.StartsWith("Ring_LOD")
            || name.StartsWith("Stitch_LOD");
    }
}
