using Unity.Profiling;

public static class WorldMeshesProfiler
{
    public static readonly ProfilerMarker RuntimeBakeBuildPlan =
        new("WorldMeshes.RuntimeBake.BuildPlan");

    public static readonly ProfilerMarker RuntimeBakeStateSnapshot =
        new("WorldMeshes.RuntimeBake.State.GetSnapshot");

    public static readonly ProfilerMarker AuthoringSignature =
        new("WorldMeshes.Authoring.ComputeSignature");

    public static readonly ProfilerMarker PreviewRebuild =
        new("WorldMeshes.Preview.Rebuild");

    public static readonly ProfilerMarker RuntimeBakeGeneration =
        new("WorldMeshes.RuntimeBake.Generation");

    public static readonly ProfilerMarker AssetDatabaseSaveAssets =
        new("WorldMeshes.AssetDatabase.SaveAssets");

    public static readonly ProfilerMarker AssetDatabaseRefresh =
        new("WorldMeshes.AssetDatabase.Refresh");

    public static readonly ProfilerMarker AssetDatabaseImportAsset =
        new("WorldMeshes.AssetDatabase.ImportAsset");
}
