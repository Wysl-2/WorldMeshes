using UnityEngine;

[DisallowMultipleComponent]
public class TerrainWorldRuntime :
    MonoBehaviour
{
    // =====================================================
    // STREAMING SOURCE
    // =====================================================

    [Header("Streaming")]

    /*
     * Authoritative scene object that WorldMeshes runtime terrain
     * systems follow.
     *
     * Generated Clipmap and Collision components receive derived
     * target references from this value during hierarchy
     * synchronization.
     */
    [SerializeField]
    private Transform streamingSource;

    public Transform StreamingSource
    {
        get
        {
            return streamingSource;
        }
    }

    // =====================================================
    // SET STREAMING SOURCE
    // =====================================================

    /*
     * Returns true only when the serialized source changed.
     *
     * Runtime switching behavior is intentionally not implemented
     * here. Editor hierarchy synchronization is responsible for
     * propagating this authoritative value into generated systems.
     */
    public bool SetStreamingSource(
        Transform source
    )
    {
        if (
            streamingSource ==
            source
        )
        {
            return false;
        }

        streamingSource =
            source;

        return true;
    }
}
