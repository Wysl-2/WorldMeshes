using System.Text;

public sealed class TerrainHeightAddressablesScaleReport
{
    public int GeographicTileCount { get; internal set; }
    public int RepresentationLevelCount { get; internal set; }
    public int AuthoritativeAssetCount { get; internal set; }
    public int DerivedAssetCount { get; internal set; }
    public int ExpectedHeightEntryCount { get; internal set; }
    public int ActualHeightEntryCount { get; internal set; }
    public int ManagedStrideLabelCount { get; internal set; }
    public string PackingMode { get; internal set; }
    public int ExpectedHeightBundleCount { get; internal set; }
    public int BuiltBundleFileCount { get; internal set; }
    public long CatalogPayloadBytes { get; internal set; }
    public double HeightConfigurationReconciliationSeconds { get; internal set; }
    public double AddressablesBuildSeconds { get; internal set; }
    public string BuildOutputPath { get; internal set; }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("Height Addressables Scale");
        builder.AppendLine("Geographic Height Tiles: " + GeographicTileCount);
        builder.AppendLine("Height Representation Levels: " + RepresentationLevelCount);
        builder.AppendLine("Authoritative Assets: " + AuthoritativeAssetCount);
        builder.AppendLine("Derived Assets: " + DerivedAssetCount);
        builder.AppendLine("Expected Height Entries: " + ExpectedHeightEntryCount);
        builder.AppendLine("Actual Height Entries: " + ActualHeightEntryCount);
        builder.AppendLine("Managed Stride Labels: " + ManagedStrideLabelCount);
        builder.AppendLine("Packing Mode: " + (PackingMode ?? ""));
        builder.AppendLine("Expected Height Bundles: " + ExpectedHeightBundleCount);
        builder.AppendLine(
            "Built Bundle Files: " +
            (BuiltBundleFileCount >= 0
                ? BuiltBundleFileCount.ToString()
                : "Unavailable")
        );
        builder.AppendLine(
            "Catalog Payload Bytes: " +
            (CatalogPayloadBytes >= 0
                ? CatalogPayloadBytes.ToString()
                : "Unavailable")
        );
        builder.AppendLine(
            "Height Reconciliation Time: " +
            HeightConfigurationReconciliationSeconds.ToString("0.000") +
            " seconds"
        );
        builder.AppendLine(
            "Addressables Build Time: " +
            AddressablesBuildSeconds.ToString("0.000") +
            " seconds"
        );

        if (!string.IsNullOrEmpty(BuildOutputPath))
        {
            builder.AppendLine("Build Output Path: " + BuildOutputPath);
        }

        return builder.ToString();
    }
}
