using Unity.Profiling;

public static class WorldMeshesProfiler
{
    public static readonly ProfilerMarker RuntimeBakeBuildPlan =
        new("WorldMeshes.RuntimeBake.BuildPlan");

    public static readonly ProfilerMarker RuntimeBakeStateSnapshot =
        new("WorldMeshes.RuntimeBake.State.GetSnapshot");

    public static readonly ProfilerMarker RuntimeBakeStateSummary =
        new("WorldMeshes.RuntimeBake.State.GetSummary");

    public static readonly ProfilerMarker AuthoringSignature =
        new("WorldMeshes.Authoring.ComputeSignature");

    public static readonly ProfilerMarker AuthoringSignatureCollectInputs =
        new("WorldMeshes.Authoring.ComputeSignature.CollectInputs");

    public static readonly ProfilerMarker AuthoringSignatureHash =
        new("WorldMeshes.Authoring.ComputeSignature.Hash");

    public static readonly ProfilerMarker PreviewUpdate =
        new("WorldMeshes.Preview.Update");

    public static readonly ProfilerMarker PreviewRebuild =
        new("WorldMeshes.Preview.Rebuild");

    public static readonly ProfilerMarker RuntimeBakeHeightGeneration =
        new("WorldMeshes.RuntimeBake.Generation.Height");

    public static readonly ProfilerMarker RuntimeBakeSurfaceGeneration =
        new("WorldMeshes.RuntimeBake.Generation.Surface");

    public static readonly ProfilerMarker RuntimeBakeCollisionGeneration =
        new("WorldMeshes.RuntimeBake.Generation.Collision");

    public static readonly ProfilerMarker AssetDatabaseSaveAssets =
        new("WorldMeshes.AssetDatabase.SaveAssets");

    public static readonly ProfilerMarker AssetDatabaseRefresh =
        new("WorldMeshes.AssetDatabase.Refresh");

    public static readonly ProfilerMarker AssetDatabaseImportAsset =
        new("WorldMeshes.AssetDatabase.ImportAsset");

    public static readonly ProfilerMarker ValidationRun =
        new("WorldMeshes.Validation.Run");

    public static readonly ProfilerMarker ValidationInspectOutputs =
        new("WorldMeshes.Validation.InspectOutputs");
}
