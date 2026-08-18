using UnityEngine;

public class TerrainClipmapBoundsController :
    MonoBehaviour
{
    // =====================================================
    // CONFIGURATION
    // =====================================================

    [Header("Terrain Height Range")]

    [SerializeField]
    private float minimumTerrainHeight;

    [SerializeField]
    private float maximumTerrainHeight;

    /*
     * Small safety margin above and below the exact
     * generated terrain range.
     *
     * This prevents tiny floating-point differences from
     * putting displaced geometry exactly on the edge of
     * the renderer bounds.
     */
    [SerializeField]
    [Min(0f)]
    private float verticalPadding =
        1f;

    [Header("Debug")]

    [SerializeField]
    private bool logBoundsApplication =
        true;

    // =====================================================
    // RUNTIME STATE
    // =====================================================

    private bool boundsApplied;

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public float MinimumTerrainHeight
    {
        get
        {
            return minimumTerrainHeight;
        }
    }

    public float MaximumTerrainHeight
    {
        get
        {
            return maximumTerrainHeight;
        }
    }

    public bool BoundsApplied
    {
        get
        {
            return boundsApplied;
        }
    }

    // =====================================================
    // EDITOR / HIERARCHY CONFIGURATION
    // =====================================================

    /*
     * Called by TerrainWorldHierarchyGenerator.
     *
     * The Preview meshes contain the authoritative
     * CPU-side terrain heights, so the hierarchy generator
     * calculates their global minimum / maximum height and
     * stores those values here.
     */
    public bool Configure(
        float minimumHeight,
        float maximumHeight
    )
    {
        bool changed =
            false;

        if (
            !Mathf.Approximately(
                minimumTerrainHeight,
                minimumHeight
            )
        )
        {
            minimumTerrainHeight =
                minimumHeight;

            changed =
                true;
        }

        if (
            !Mathf.Approximately(
                maximumTerrainHeight,
                maximumHeight
            )
        )
        {
            maximumTerrainHeight =
                maximumHeight;

            changed =
                true;
        }

        return changed;
    }

    // =====================================================
    // ENABLE
    // =====================================================

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ApplyBounds();
    }

    // =====================================================
    // DISABLE
    // =====================================================

    private void OnDisable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ResetBounds();
    }

    // =====================================================
    // APPLY BOUNDS
    // =====================================================

    [ContextMenu("Apply Clipmap Bounds")]
    public void ApplyBounds()
    {
        if (
            maximumTerrainHeight <
            minimumTerrainHeight
        )
        {
            Debug.LogError(
                "Cannot apply clipmap displacement bounds.\n\n" +

                "Maximum terrain height is below minimum " +
                "terrain height.",
                this
            );

            boundsApplied =
                false;

            return;
        }

        MeshRenderer[] renderers =
            GetComponentsInChildren<MeshRenderer>(
                true
            );

        int rendererCount =
            0;

        float paddedMinimumHeight =
            minimumTerrainHeight
            -
            Mathf.Max(
                0f,
                verticalPadding
            );

        float paddedMaximumHeight =
            maximumTerrainHeight
            +
            Mathf.Max(
                0f,
                verticalPadding
            );

        foreach (
            MeshRenderer meshRenderer
            in renderers
        )
        {
            if (meshRenderer == null)
            {
                continue;
            }

            MeshFilter meshFilter =
                meshRenderer
                    .GetComponent<MeshFilter>();

            if (
                meshFilter == null
                ||
                meshFilter.sharedMesh == null
            )
            {
                continue;
            }

            /*
             * Clipmap geometry does not move in X/Z inside
             * the vertex shader.
             *
             * Therefore preserve the mesh's existing local
             * X/Z bounds and expand only the Y range.
             */

            Bounds meshBounds =
                meshFilter.sharedMesh.bounds;

            /*
             * The shader writes an absolute world-space Y:
             *
             *     positionWS.y = terrainHeight;
             *
             * Convert the generated world-space terrain
             * height range back into this renderer's local
             * coordinate system.
             *
             * This keeps the bounds correct even if the
             * clipmap hierarchy later has a non-zero Y
             * translation.
             */

            Vector3 minimumLocalPoint =
                meshRenderer.transform
                    .InverseTransformPoint(
                        new Vector3(
                            meshRenderer.transform.position.x,
                            paddedMinimumHeight,
                            meshRenderer.transform.position.z
                        )
                    );

            Vector3 maximumLocalPoint =
                meshRenderer.transform
                    .InverseTransformPoint(
                        new Vector3(
                            meshRenderer.transform.position.x,
                            paddedMaximumHeight,
                            meshRenderer.transform.position.z
                        )
                    );

            float minimumLocalY =
                Mathf.Min(
                    minimumLocalPoint.y,
                    maximumLocalPoint.y
                );

            float maximumLocalY =
                Mathf.Max(
                    minimumLocalPoint.y,
                    maximumLocalPoint.y
                );

            float localCenterY =
                (
                    minimumLocalY
                    +
                    maximumLocalY
                )
                *
                0.5f;

            float localSizeY =
                Mathf.Max(
                    0.001f,
                    maximumLocalY
                    -
                    minimumLocalY
                );

            Vector3 boundsCenter =
                meshBounds.center;

            boundsCenter.y =
                localCenterY;

            Vector3 boundsSize =
                meshBounds.size;

            boundsSize.y =
                localSizeY;

            meshRenderer.localBounds =
                new Bounds(
                    boundsCenter,
                    boundsSize
                );

            rendererCount++;
        }

        boundsApplied =
            rendererCount > 0;

        if (
            boundsApplied
            &&
            logBoundsApplication
        )
        {
            Debug.Log(
                "Clipmap displacement bounds applied.\n\n" +

                $"Renderers: " +
                $"{rendererCount}\n\n" +

                $"Terrain Height Range: " +
                $"{minimumTerrainHeight:R} -> " +
                $"{maximumTerrainHeight:R}\n" +

                $"Vertical Padding: " +
                $"{verticalPadding:R}\n\n" +

                $"Padded Height Range: " +
                $"{paddedMinimumHeight:R} -> " +
                $"{paddedMaximumHeight:R}",
                this
            );
        }
    }

    // =====================================================
    // RESET BOUNDS
    // =====================================================

    public void ResetBounds()
    {
        MeshRenderer[] renderers =
            GetComponentsInChildren<MeshRenderer>(
                true
            );

        foreach (
            MeshRenderer meshRenderer
            in renderers
        )
        {
            if (meshRenderer == null)
            {
                continue;
            }

            meshRenderer.ResetLocalBounds();
        }

        boundsApplied =
            false;
    }
}