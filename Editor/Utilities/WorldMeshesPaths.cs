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

    /*
     * The default surface settings live under a Resources folder so the
     * runtime terrain streamer can load them without AssetDatabase.
     */
    public const string ConfigurationResources =
        Configuration +
        "/Resources";

    public const string TerrainSurfaceSettingsAssetPath =
        ConfigurationResources +
        "/TerrainSurfaceSettings.asset";

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

    // =====================================================
    // GENERATED SURFACE MASKS
    // =====================================================

    public const string GeneratedSurfaceMasks =
        Generated +
        "/SurfaceMasks";

    public const string SurfaceMaskTiles =
        GeneratedSurfaceMasks +
        "/Tiles";

    public const string SurfaceMaskManifestAssetPath =
        GeneratedSurfaceMasks +
        "/SurfaceMaskManifest.asset";
}
