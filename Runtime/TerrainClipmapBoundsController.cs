using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
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

    [SerializeField]
    [Min(0f)]
    private float verticalPadding =
        1f;

    [Header("Debug")]

    [SerializeField]
    private bool logBoundsApplication =
        true;

    // =====================================================
    // STATE
    // =====================================================

    private bool boundsApplied;

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public float MinimumTerrainHeight
    {
        get
        {
            return
                minimumTerrainHeight;
        }
    }

    public float MaximumTerrainHeight
    {
        get
        {
            return
                maximumTerrainHeight;
        }
    }

    public bool BoundsApplied
    {
        get
        {
            return
                boundsApplied;
        }
    }

    // =====================================================
    // CONFIGURATION
    // =====================================================

    /*
     * Configures renderer bounds for the supplied world-space
     * terrain displacement range.
     *
     * Runtime hierarchy synchronization supplies compiled
     * heightmap bounds. The edit-mode authoring preview can use
     * the same API for its current composite height range.
     */
    public bool Configure(
        float minimumHeight,
        float maximumHeight
    )
    {
        if (
            !IsFinite(
                minimumHeight
            )
            ||
            !IsFinite(
                maximumHeight
            )
            ||
            maximumHeight <
                minimumHeight
        )
        {
            Debug.LogError(
                "Cannot configure clipmap displacement bounds.\n\n" +
                "The supplied terrain height range is invalid.",
                this
            );

            return false;
        }

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

        if (
            changed
            &&
            isActiveAndEnabled
        )
        {
            ApplyBounds();
        }

        return
            changed;
    }

    // =====================================================
    // ENABLE / DISABLE
    // =====================================================

    private void OnEnable()
    {
        ApplyBounds();
    }

    private void OnDisable()
    {
        ResetBounds();
    }

    // =====================================================
    // APPLY BOUNDS
    // =====================================================

    [ContextMenu("Apply Clipmap Bounds")]
    public void ApplyBounds()
    {
        if (
            !IsFinite(
                minimumTerrainHeight
            )
            ||
            !IsFinite(
                maximumTerrainHeight
            )
            ||
            maximumTerrainHeight <
                minimumTerrainHeight
        )
        {
            Debug.LogError(
                "Cannot apply clipmap displacement bounds.\n\n" +
                "The configured terrain height range is invalid.",
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

            Bounds meshBounds =
                meshFilter.sharedMesh.bounds;

            /*
             * The clipmap shader writes an absolute world-space
             * terrain Y. Convert the configured world-space range
             * into this renderer's local coordinate space while
             * preserving the generated X/Z mesh bounds.
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
                    minimumLocalY +
                    maximumLocalY
                )
                *
                0.5f;

            float localSizeY =
                Mathf.Max(
                    0.001f,
                    maximumLocalY -
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
            &&
            Application.isPlaying
        )
        {
            Debug.Log(
                "Clipmap displacement bounds applied.\n\n" +
                $"Renderers: {rendererCount}\n\n" +
                $"Terrain Height Range: " +
                $"{minimumTerrainHeight:R} -> " +
                $"{maximumTerrainHeight:R}\n" +
                $"Vertical Padding: {verticalPadding:R}\n\n" +
                $"Padded Height Range: " +
                $"{paddedMinimumHeight:R} -> " +
                $"{paddedMaximumHeight:R}",
                this
            );
        }
    }

    // =====================================================
    // RESET
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
