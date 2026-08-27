public static class WorldMeshesPaths
{
    // =====================================================
    // ROOT
    // =====================================================

    public const string Root =
        "Assets/WorldMeshes";

    // =====================================================
    // CONFIGURATION
    // =====================================================

    public const string Configuration =
        Root + "/Configuration";

    public const string WorldSettingsAssetPath =
        Configuration + "/WorldSettings.asset";

    public const string TerrainAuthoringDataAssetPath =
        Configuration +
        "/TerrainAuthoringData.asset";

    // =====================================================
    // MATERIALS
    // =====================================================

    public const string Materials =
        Root + "/Materials";

    public const string ClipmapTerrainMaterialPath =
        Materials + "/MAT_ClipmapTerrain.mat";

    // =====================================================
    // SHADERS
    // =====================================================

    public const string Shaders =
        Root + "/Shaders";

    public const string ComputeShaders =
        Shaders + "/Compute";

    public const string TerrainHeightmapComputeShaderPath =
        ComputeShaders + "/TerrainHeightmap.compute";

    // =====================================================
    // AUTHORING
    // =====================================================

    public const string Authoring =
        Root + "/Authoring";

    public const string AuthoringHeight =
        Authoring + "/Height";

    public const string AuthoringHeightTiles =
        AuthoringHeight + "/Tiles";

    public const string AuthoringHeightManifestAssetPath =
        AuthoringHeight +
        "/AuthoringHeightManifest.asset";

    // =====================================================
    // GENERATED
    // =====================================================

    public const string Generated =
        Root + "/Generated";

    public const string GeneratedMeshes =
        Generated + "/Meshes";

    /*
     * Visual terrain is now represented exclusively by the
     * generated clipmap geometry. The legacy Base/ and Chunks/
     * visual-mesh folders are intentionally no longer part of
     * the active pipeline.
     */
    public const string GeneratedClipmapMeshes =
        GeneratedMeshes + "/Clipmap";

    public const string GeneratedCollisionMeshes =
        GeneratedMeshes + "/Collision";

    public const string GeneratedCollisionBakeMarkers =
        GeneratedCollisionMeshes + "/BakeMarkers";

    public const string CollisionManifestAssetPath =
        GeneratedCollisionMeshes +
        "/CollisionManifest.asset";

    // =====================================================
    // GENERATED HEIGHTMAPS
    // =====================================================

    public const string GeneratedHeightmaps =
        Generated + "/Heightmaps";

    public const string HeightmapTiles =
        GeneratedHeightmaps + "/Tiles";

    public const string HeightmapManifestAssetPath =
        GeneratedHeightmaps +
        "/HeightmapManifest.asset";
}
