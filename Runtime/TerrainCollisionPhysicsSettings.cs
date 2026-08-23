using UnityEngine;

public static class TerrainCollisionPhysicsSettings
{
    /*
     * Collision terrain is a non-convex static surface.
     *
     * These values are shared by:
     *
     * - editor-time Physics.BakeMesh()
     * - runtime MeshCollider configuration
     *
     * The cooking options MUST match exactly or Unity cannot
     * reuse the pre-baked PhysX collision data.
     */
    public const bool Convex =
        false;

    public const MeshColliderCookingOptions CookingOptions =
        MeshColliderCookingOptions.CookForFasterSimulation |
        MeshColliderCookingOptions.EnableMeshCleaning |
        MeshColliderCookingOptions.WeldColocatedVertices |
        MeshColliderCookingOptions.UseFastMidphase;
}