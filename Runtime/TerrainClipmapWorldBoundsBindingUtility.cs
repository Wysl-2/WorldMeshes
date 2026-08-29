using UnityEngine;

/*
 * Shared MaterialPropertyBlock binding for the logical world
 * rectangle used by clipmap shaders.
 *
 * World bounds are deliberately independent of height-cache
 * readiness:
 *
 *     _WorldSizeXZ
 *     _WorldBoundsReady
 *
 * define where terrain is legally allowed to exist, while:
 *
 *     _HeightCacheReady
 *
 * defines whether terrain can currently be displaced.
 */
public static class TerrainClipmapWorldBoundsBindingUtility
{
    // =====================================================
    // SHADER PROPERTY IDS
    // =====================================================

    private static readonly int WorldSizeXZPropertyId =
        Shader.PropertyToID(
            "_WorldSizeXZ"
        );

    private static readonly int WorldBoundsReadyPropertyId =
        Shader.PropertyToID(
            "_WorldBoundsReady"
        );

    // =====================================================
    // BIND
    // =====================================================

    public static bool TryBind(
        Transform clipmapRoot,
        Vector2 worldSizeXZ,
        out int boundRendererCount,
        out string errorMessage
    )
    {
        boundRendererCount =
            0;

        errorMessage =
            "";

        if (clipmapRoot == null)
        {
            errorMessage =
                "Clipmap root is null.";

            return false;
        }

        if (
            !IsFinite(
                worldSizeXZ.x
            )
            ||
            !IsFinite(
                worldSizeXZ.y
            )
            ||
            worldSizeXZ.x <= 0f
            ||
            worldSizeXZ.y <= 0f
        )
        {
            errorMessage =
                "World size is invalid.";

            return false;
        }

        MeshRenderer[] renderers =
            clipmapRoot
                .GetComponentsInChildren<MeshRenderer>(
                    true
                );

        if (
            renderers == null
            ||
            renderers.Length == 0
        )
        {
            errorMessage =
                "No child MeshRenderer components were found " +
                "under the clipmap root.";

            return false;
        }

        MaterialPropertyBlock propertyBlock =
            new MaterialPropertyBlock();

        Vector4 worldSize =
            new Vector4(
                worldSizeXZ.x,
                worldSizeXZ.y,
                0f,
                0f
            );

        foreach (
            MeshRenderer meshRenderer
            in renderers
        )
        {
            if (
                !IsWorldBoundsCompatibleRenderer(
                    meshRenderer
                )
            )
            {
                continue;
            }

            /*
             * Preserve height-cache, stitch, visualization and any
             * other unrelated renderer overrides.
             */
            meshRenderer.GetPropertyBlock(
                propertyBlock
            );

            propertyBlock.SetVector(
                WorldSizeXZPropertyId,
                worldSize
            );

            /*
             * Readiness is set last so the shader never observes
             * an enabled boundary paired with stale dimensions.
             */
            propertyBlock.SetFloat(
                WorldBoundsReadyPropertyId,
                1f
            );

            meshRenderer.SetPropertyBlock(
                propertyBlock
            );

            boundRendererCount++;
        }

        if (boundRendererCount <= 0)
        {
            errorMessage =
                "No clipmap terrain renderer was found using a " +
                "material with the expected world-boundary " +
                "shader properties.";

            return false;
        }

        return true;
    }

    // =====================================================
    // DISABLE
    // =====================================================

    /*
     * Disables only world-boundary clipping.
     *
     * The stored dimensions are intentionally left intact; the
     * readiness flag is authoritative.
     */
    public static int Disable(
        Transform clipmapRoot
    )
    {
        if (clipmapRoot == null)
        {
            return 0;
        }

        MeshRenderer[] renderers =
            clipmapRoot
                .GetComponentsInChildren<MeshRenderer>(
                    true
                );

        if (renderers == null)
        {
            return 0;
        }

        MaterialPropertyBlock propertyBlock =
            new MaterialPropertyBlock();

        int disabledRendererCount =
            0;

        foreach (
            MeshRenderer meshRenderer
            in renderers
        )
        {
            if (
                !IsWorldBoundsCompatibleRenderer(
                    meshRenderer
                )
            )
            {
                continue;
            }

            meshRenderer.GetPropertyBlock(
                propertyBlock
            );

            propertyBlock.SetFloat(
                WorldBoundsReadyPropertyId,
                0f
            );

            meshRenderer.SetPropertyBlock(
                propertyBlock
            );

            disabledRendererCount++;
        }

        return disabledRendererCount;
    }

    // =====================================================
    // MATERIAL COMPATIBILITY
    // =====================================================

    private static bool IsWorldBoundsCompatibleRenderer(
        MeshRenderer meshRenderer
    )
    {
        if (
            meshRenderer == null
            ||
            meshRenderer.sharedMaterial == null
        )
        {
            return false;
        }

        Material material =
            meshRenderer.sharedMaterial;

        return
            material.HasProperty(
                WorldSizeXZPropertyId
            )
            &&
            material.HasProperty(
                WorldBoundsReadyPropertyId
            );
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }
}
