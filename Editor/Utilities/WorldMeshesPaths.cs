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

    // =====================================================
    // MATERIALS
    // =====================================================

    public const string Materials =
        Root + "/Materials";

    public const string PreviewTerrainMaterialPath =
        Materials + "/MAT_PreviewTerrain.mat";

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
    // GENERATED
    // =====================================================

    public const string Generated =
        Root + "/Generated";

    // =====================================================
    // GENERATED MESHES
    // =====================================================

    public const string GeneratedMeshes =
        Generated + "/Meshes";

    public const string GeneratedBaseMeshes =
        GeneratedMeshes + "/Base";

    public const string GeneratedChunkMeshes =
        GeneratedMeshes + "/Chunks";

    public const string GeneratedClipmapMeshes =
        GeneratedMeshes + "/Clipmap";

    public const string GeneratedCollisionMeshes =
        GeneratedMeshes + "/Collision";

    public const string BaseMeshAssetPath =
        GeneratedBaseMeshes +
        "/TerrainChunk_LOD0.asset";

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