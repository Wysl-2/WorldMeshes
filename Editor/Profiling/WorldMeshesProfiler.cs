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

    public static readonly ProfilerMarker RuntimeBakeHeightSaveTile =
        new("WorldMeshes.RuntimeBake.Generation.Height.SaveTile");

    public static readonly ProfilerMarker RuntimeBakeHeightSaveManifest =
        new("WorldMeshes.RuntimeBake.Generation.Height.SaveManifest");

    public static readonly ProfilerMarker RuntimeBakeSurfaceSaveTile =
        new("WorldMeshes.RuntimeBake.Generation.Surface.SaveTile");

    public static readonly ProfilerMarker RuntimeBakeSurfaceSaveManifest =
        new("WorldMeshes.RuntimeBake.Generation.Surface.SaveManifest");

    public static readonly ProfilerMarker RuntimeBakeCollisionSaveMesh =
        new("WorldMeshes.RuntimeBake.Generation.Collision.SaveMesh");

    public static readonly ProfilerMarker RuntimeBakeAddressables =
        new("WorldMeshes.RuntimeBake.Addressables");

    public static readonly ProfilerMarker AddressablesHeightReconcile =
        new("WorldMeshes.Addressables.Reconcile.Height");

    public static readonly ProfilerMarker AddressablesSurfaceReconcile =
        new("WorldMeshes.Addressables.Reconcile.Surface");

    public static readonly ProfilerMarker AddressablesCollisionReconcile =
        new("WorldMeshes.Addressables.Reconcile.Collision");

    public static readonly ProfilerMarker AddressablesCreateOrMoveEntry =
        new("WorldMeshes.Addressables.CreateOrMoveEntry");

    public static readonly ProfilerMarker AddressablesSetAddress =
        new("WorldMeshes.Addressables.SetAddress");

    public static readonly ProfilerMarker AddressablesSetLabel =
        new("WorldMeshes.Addressables.SetLabel");

    public static readonly ProfilerMarker AddressablesRemoveEntry =
        new("WorldMeshes.Addressables.RemoveEntry");

    public static readonly ProfilerMarker AssetDatabaseCreateAsset =
        new("WorldMeshes.AssetDatabase.CreateAsset");

    public static readonly ProfilerMarker AssetDatabaseSaveAssetIfDirty =
        new("WorldMeshes.AssetDatabase.SaveAssetIfDirty");

    public static readonly ProfilerMarker AssetDatabaseSaveAssets =
        new("WorldMeshes.AssetDatabase.SaveAssets");

    public static readonly ProfilerMarker AssetDatabaseRefresh =
        new("WorldMeshes.AssetDatabase.Refresh");

    public static readonly ProfilerMarker AssetDatabaseImportAsset =
        new("WorldMeshes.AssetDatabase.ImportAsset");

    public static readonly ProfilerMarker PrefabSaveAsPrefabAsset =
        new("WorldMeshes.PrefabUtility.SaveAsPrefabAsset");

    public static readonly ProfilerMarker ValidationRun =
        new("WorldMeshes.Validation.Run");

    public static readonly ProfilerMarker ValidationInspectOutputs =
        new("WorldMeshes.Validation.InspectOutputs");
}
