using System.Text;

public enum TerrainRuntimeAddressablesOperationMode
{
    None,
    ContentOnly,
    ConfigureAndBuild
}

public enum TerrainRuntimeAddressablesOutcome
{
    NoWork,
    Completed,
    Cancelled,
    Failed,
    Blocked,
    StalePlan
}

/*
 * Read-only validation summary used by runtime diagnostics and the
 * ContentOnly path. Validation never creates/moves Addressables entries and
 * never rewrites marker prefabs.
 */
public sealed class TerrainRuntimeAddressablesValidationResult
{
    public bool HeightConfigurationValid { get; private set; }
    public bool SurfaceConfigurationValid { get; private set; }
    public bool CollisionConfigurationValid { get; private set; }
    public bool CollisionMarkerStructureValid { get; private set; }
    public bool CollisionPreparedManifestStructurallyValid { get; private set; }

    public int CollisionMarkerCount { get; private set; }

    public string HeightError { get; private set; }
    public string SurfaceError { get; private set; }
    public string CollisionError { get; private set; }
    public string CollisionMarkerError { get; private set; }
    public string CollisionManifestError { get; private set; }

    public bool IsValid =>
        HeightConfigurationValid
        &&
        SurfaceConfigurationValid
        &&
        CollisionConfigurationValid
        &&
        CollisionMarkerStructureValid
        &&
        CollisionPreparedManifestStructurallyValid;

    internal TerrainRuntimeAddressablesValidationResult(
        bool heightConfigurationValid,
        bool surfaceConfigurationValid,
        bool collisionConfigurationValid,
        bool collisionMarkerStructureValid,
        bool collisionPreparedManifestStructurallyValid,
        int collisionMarkerCount,
        string heightError,
        string surfaceError,
        string collisionError,
        string collisionMarkerError,
        string collisionManifestError
    )
    {
        HeightConfigurationValid = heightConfigurationValid;
        SurfaceConfigurationValid = surfaceConfigurationValid;
        CollisionConfigurationValid = collisionConfigurationValid;
        CollisionMarkerStructureValid = collisionMarkerStructureValid;
        CollisionPreparedManifestStructurallyValid =
            collisionPreparedManifestStructurallyValid;

        CollisionMarkerCount = collisionMarkerCount;

        HeightError = heightError ?? "";
        SurfaceError = surfaceError ?? "";
        CollisionError = collisionError ?? "";
        CollisionMarkerError = collisionMarkerError ?? "";
        CollisionManifestError = collisionManifestError ?? "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Runtime Addressables Configuration Validation"
        );

        AppendStatus(
            builder,
            "Height Addressables",
            HeightConfigurationValid,
            HeightError
        );

        AppendStatus(
            builder,
            "Surface Addressables",
            SurfaceConfigurationValid,
            SurfaceError
        );

        AppendStatus(
            builder,
            "Collision Addressables",
            CollisionConfigurationValid,
            CollisionError
        );

        AppendStatus(
            builder,
            "Collision Marker Structure",
            CollisionMarkerStructureValid,
            CollisionMarkerError
        );

        AppendStatus(
            builder,
            "Collision Prepared Manifest Structure",
            CollisionPreparedManifestStructurallyValid,
            CollisionManifestError
        );

        builder.AppendLine(
            "Collision Markers: " +
            CollisionMarkerCount
        );

        builder.AppendLine(
            "Overall: " +
            (IsValid ? "Valid" : "Needs Repair")
        );

        return builder.ToString();
    }

    private static void AppendStatus(
        StringBuilder builder,
        string label,
        bool valid,
        string error
    )
    {
        builder.AppendLine(
            label +
            ": " +
            (valid ? "Valid" : "Invalid")
        );

        if (!valid && !string.IsNullOrEmpty(error))
        {
            builder.AppendLine(
                "  " +
                error.Replace(
                    "\n",
                    "\n  "
                )
            );
        }
    }
}

/*
 * Aggregated mutation, memory-hardening, and spatial-packing
 * statistics shared internally by Addressables reconciliation/build utilities.
 */
internal sealed class TerrainAddressablesOperationStats
{
    public bool heightConfigurationChanged;
    public bool surfaceConfigurationChanged;
    public bool collisionConfigurationChanged;
    public bool collisionRuntimeMetadataUpdated;
    public bool collisionManifestCreated;

    public int collisionMarkersRegenerated;
    public int collisionMarkersReused;
    public int collisionMarkersRemoved;

    public int entriesCreated;
    public int entriesMoved;
    public int entriesRemoved;
    public int addressesUpdated;
    public int labelsUpdated;

    public int groupsCreated;
    public int schemasCreatedOrChanged;

    public int heightGeographicTileCount;
    public int heightRepresentationLevelCount;
    public int heightAuthoritativeAssetCount;
    public int heightDerivedAssetCount;
    public int heightExpectedEntryCount;
    public int heightActualEntryCount;
    public int heightManagedStrideLabelCount;
    public int heightManagedRegionLabelCount;
    public int heightPackingRegionTileSpan;
    public int heightPackingRegionCount;
    public double heightConfigurationReconciliationSeconds;

    public int surfaceManagedRegionLabelCount;
    public int surfacePackingRegionTileSpan;
    public int surfacePackingRegionCount;

    public int heightPeakStrideExpectedGuidCount;
    public int collisionPeakRegionExpectedGuidCount;

    public int residencyReleaseCount;

    public long managedBytesBeforeContentBuild = -1L;
    public long managedBytesAfterPreBuildCleanup = -1L;

    public long unityAllocatedBytesBeforeContentBuild = -1L;
    public long unityAllocatedBytesAfterPreBuildCleanup = -1L;

    public long processWorkingSetBytesBeforeContentBuild = -1L;
    public long processWorkingSetBytesAfterPreBuildCleanup = -1L;
    public long processWorkingSetBytesAfterContentBuild = -1L;
    public long processWorkingSetBytesAfterPostBuildCleanup = -1L;

    public long expectedHeightEntryCount;
    public long expectedHeightBundleCount;
    public long expectedSurfaceEntryCount;
    public long expectedSurfaceBundleCount;
    public long expectedCollisionMeshEntryCount;
    public long expectedCollisionMarkerEntryCount;
    public long expectedCollisionBundleCount;
    public long expectedTotalManagedEntryCount;
    public long expectedTotalBundleCount;

    public bool AnyConfigurationChanged =>
        heightConfigurationChanged
        ||
        surfaceConfigurationChanged
        ||
        collisionConfigurationChanged
        ||
        collisionMarkersRegenerated > 0
        ||
        collisionMarkersRemoved > 0
        ||
        entriesCreated > 0
        ||
        entriesMoved > 0
        ||
        entriesRemoved > 0
        ||
        addressesUpdated > 0
        ||
        labelsUpdated > 0
        ||
        groupsCreated > 0
        ||
        schemasCreatedOrChanged > 0;
}

public sealed class TerrainRuntimeAddressablesResult
{
    public TerrainRuntimeAddressablesOutcome Outcome { get; private set; }
    public TerrainRuntimeAddressablesOperationMode OperationMode { get; private set; }

    public bool ConfigurationWasRequired { get; private set; }
    public bool ConfigurationPerformed { get; private set; }

    public bool ContentBuildWasRequired { get; private set; }
    public bool ContentBuildPerformed { get; private set; }

    public bool HeightConfigurationChanged { get; private set; }
    public bool SurfaceConfigurationChanged { get; private set; }
    public bool CollisionConfigurationChanged { get; private set; }

    public int CollisionMarkersRegenerated { get; private set; }
    public int CollisionMarkersReused { get; private set; }
    public int CollisionMarkersRemoved { get; private set; }

    public bool CollisionRuntimeMetadataUpdated { get; private set; }

    public int EntriesCreated { get; private set; }
    public int EntriesMoved { get; private set; }
    public int EntriesRemoved { get; private set; }

    public int AddressesUpdated { get; private set; }
    public int LabelsUpdated { get; private set; }

    public int GroupsCreated { get; private set; }
    public int SchemasCreatedOrChanged { get; private set; }

    public int ResidencyReleaseCount { get; private set; }
    public int HeightPeakStrideExpectedGuidCount { get; private set; }
    public int CollisionPeakRegionExpectedGuidCount { get; private set; }

    public int HeightManagedRegionLabelCount { get; private set; }
    public int HeightPackingRegionTileSpan { get; private set; }
    public int HeightPackingRegionCount { get; private set; }

    public int SurfaceManagedRegionLabelCount { get; private set; }
    public int SurfacePackingRegionTileSpan { get; private set; }
    public int SurfacePackingRegionCount { get; private set; }

    public long ManagedBytesBeforeContentBuild { get; private set; }
    public long ManagedBytesAfterPreBuildCleanup { get; private set; }
    public long UnityAllocatedBytesBeforeContentBuild { get; private set; }
    public long UnityAllocatedBytesAfterPreBuildCleanup { get; private set; }
    public long ProcessWorkingSetBytesBeforeContentBuild { get; private set; }
    public long ProcessWorkingSetBytesAfterPreBuildCleanup { get; private set; }
    public long ProcessWorkingSetBytesAfterContentBuild { get; private set; }
    public long ProcessWorkingSetBytesAfterPostBuildCleanup { get; private set; }

    public string BuildOutputPath { get; private set; }
    public double BuildDuration { get; private set; }

    public TerrainHeightAddressablesScaleReport HeightScaleReport { get; private set; }
    public TerrainRuntimeAddressablesScaleReport AddressablesScaleReport { get; private set; }

    public bool AddressablesConfigurationDirtyCleared { get; private set; }
    public bool AddressablesContentDirtyCleared { get; private set; }

    public string ErrorMessage { get; private set; }
    public string SummaryMessage { get; private set; }

    internal TerrainRuntimeAddressablesResult(
        TerrainRuntimeAddressablesOutcome outcome,
        TerrainRuntimeAddressablesOperationMode operationMode,
        bool configurationWasRequired,
        bool configurationPerformed,
        bool contentBuildWasRequired,
        bool contentBuildPerformed,
        TerrainAddressablesOperationStats stats,
        string buildOutputPath,
        double buildDuration,
        bool addressablesConfigurationDirtyCleared,
        bool addressablesContentDirtyCleared,
        string errorMessage,
        string summaryMessage
    )
    {
        Outcome = outcome;
        OperationMode = operationMode;

        ConfigurationWasRequired = configurationWasRequired;
        ConfigurationPerformed = configurationPerformed;

        ContentBuildWasRequired = contentBuildWasRequired;
        ContentBuildPerformed = contentBuildPerformed;

        HeightConfigurationChanged =
            stats != null && stats.heightConfigurationChanged;

        SurfaceConfigurationChanged =
            stats != null && stats.surfaceConfigurationChanged;

        CollisionConfigurationChanged =
            stats != null && stats.collisionConfigurationChanged;

        CollisionMarkersRegenerated =
            stats != null ? stats.collisionMarkersRegenerated : 0;

        CollisionMarkersReused =
            stats != null ? stats.collisionMarkersReused : 0;

        CollisionMarkersRemoved =
            stats != null ? stats.collisionMarkersRemoved : 0;

        CollisionRuntimeMetadataUpdated =
            stats != null && stats.collisionRuntimeMetadataUpdated;

        EntriesCreated =
            stats != null ? stats.entriesCreated : 0;

        EntriesMoved =
            stats != null ? stats.entriesMoved : 0;

        EntriesRemoved =
            stats != null ? stats.entriesRemoved : 0;

        AddressesUpdated =
            stats != null ? stats.addressesUpdated : 0;

        LabelsUpdated =
            stats != null ? stats.labelsUpdated : 0;

        GroupsCreated =
            stats != null ? stats.groupsCreated : 0;

        SchemasCreatedOrChanged =
            stats != null ? stats.schemasCreatedOrChanged : 0;

        ResidencyReleaseCount =
            stats != null ? stats.residencyReleaseCount : 0;

        HeightPeakStrideExpectedGuidCount =
            stats != null ? stats.heightPeakStrideExpectedGuidCount : 0;

        CollisionPeakRegionExpectedGuidCount =
            stats != null ? stats.collisionPeakRegionExpectedGuidCount : 0;

        HeightManagedRegionLabelCount =
            stats != null ? stats.heightManagedRegionLabelCount : 0;

        HeightPackingRegionTileSpan =
            stats != null ? stats.heightPackingRegionTileSpan : 0;

        HeightPackingRegionCount =
            stats != null ? stats.heightPackingRegionCount : 0;

        SurfaceManagedRegionLabelCount =
            stats != null ? stats.surfaceManagedRegionLabelCount : 0;

        SurfacePackingRegionTileSpan =
            stats != null ? stats.surfacePackingRegionTileSpan : 0;

        SurfacePackingRegionCount =
            stats != null ? stats.surfacePackingRegionCount : 0;

        ManagedBytesBeforeContentBuild =
            stats != null ? stats.managedBytesBeforeContentBuild : -1L;

        ManagedBytesAfterPreBuildCleanup =
            stats != null ? stats.managedBytesAfterPreBuildCleanup : -1L;

        UnityAllocatedBytesBeforeContentBuild =
            stats != null ? stats.unityAllocatedBytesBeforeContentBuild : -1L;

        UnityAllocatedBytesAfterPreBuildCleanup =
            stats != null ? stats.unityAllocatedBytesAfterPreBuildCleanup : -1L;

        ProcessWorkingSetBytesBeforeContentBuild =
            stats != null ? stats.processWorkingSetBytesBeforeContentBuild : -1L;

        ProcessWorkingSetBytesAfterPreBuildCleanup =
            stats != null ? stats.processWorkingSetBytesAfterPreBuildCleanup : -1L;

        ProcessWorkingSetBytesAfterContentBuild =
            stats != null ? stats.processWorkingSetBytesAfterContentBuild : -1L;

        ProcessWorkingSetBytesAfterPostBuildCleanup =
            stats != null ? stats.processWorkingSetBytesAfterPostBuildCleanup : -1L;

        BuildOutputPath = buildOutputPath ?? "";
        BuildDuration = buildDuration;

        HeightScaleReport =
            TerrainHeightAddressablesScaleUtility
                .CreateReport(
                    stats,
                    BuildOutputPath,
                    BuildDuration
                );

        AddressablesScaleReport =
            TerrainRuntimeAddressablesScaleUtility
                .CreateReport(
                    stats
                );

        AddressablesConfigurationDirtyCleared =
            addressablesConfigurationDirtyCleared;

        AddressablesContentDirtyCleared =
            addressablesContentDirtyCleared;

        ErrorMessage = errorMessage ?? "";
        SummaryMessage = summaryMessage ?? "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Runtime Addressables Result"
        );

        builder.AppendLine(
            "Outcome: " +
            Outcome
        );

        builder.AppendLine(
            "Operation Mode: " +
            OperationMode
        );

        builder.AppendLine(
            "Configuration Required: " +
            ConfigurationWasRequired
        );

        builder.AppendLine(
            "Configuration Performed: " +
            ConfigurationPerformed
        );

        builder.AppendLine(
            "Content Build Required: " +
            ContentBuildWasRequired
        );

        builder.AppendLine(
            "Content Build Performed: " +
            ContentBuildPerformed
        );

        builder.AppendLine(
            "Height Configuration Changed: " +
            HeightConfigurationChanged
        );

        builder.AppendLine(
            "Surface Configuration Changed: " +
            SurfaceConfigurationChanged
        );

        builder.AppendLine(
            "Collision Configuration Changed: " +
            CollisionConfigurationChanged
        );

        builder.AppendLine(
            "Collision Runtime Metadata Updated: " +
            CollisionRuntimeMetadataUpdated
        );

        builder.AppendLine(
            "Collision Markers Regenerated: " +
            CollisionMarkersRegenerated
        );

        builder.AppendLine(
            "Collision Markers Reused: " +
            CollisionMarkersReused
        );

        builder.AppendLine(
            "Collision Markers Removed: " +
            CollisionMarkersRemoved
        );

        builder.AppendLine(
            "Entries Created: " +
            EntriesCreated
        );

        builder.AppendLine(
            "Entries Moved: " +
            EntriesMoved
        );

        builder.AppendLine(
            "Entries Removed: " +
            EntriesRemoved
        );

        builder.AppendLine(
            "Addresses Updated: " +
            AddressesUpdated
        );

        builder.AppendLine(
            "Labels Updated: " +
            LabelsUpdated
        );

        builder.AppendLine(
            "Groups Created: " +
            GroupsCreated
        );

        builder.AppendLine(
            "Schemas Created / Changed: " +
            SchemasCreatedOrChanged
        );

        builder.AppendLine(
            "Addressables Configuration Dirty Cleared: " +
            AddressablesConfigurationDirtyCleared
        );

        builder.AppendLine(
            "Addressables Content Dirty Cleared: " +
            AddressablesContentDirtyCleared
        );

        builder.AppendLine();
        builder.AppendLine("Addressables Spatial Packing");
        builder.AppendLine(
            "Height Packing Region Tile Span: " +
            HeightPackingRegionTileSpan
        );
        builder.AppendLine(
            "Height Packing Region Count: " +
            HeightPackingRegionCount
        );
        builder.AppendLine(
            "Height Managed Region Labels: " +
            HeightManagedRegionLabelCount
        );
        builder.AppendLine(
            "Surface Packing Region Tile Span: " +
            SurfacePackingRegionTileSpan
        );
        builder.AppendLine(
            "Surface Packing Region Count: " +
            SurfacePackingRegionCount
        );
        builder.AppendLine(
            "Surface Managed Region Labels: " +
            SurfaceManagedRegionLabelCount
        );

        builder.AppendLine();
        builder.AppendLine("Addressables Memory Hardening");
        builder.AppendLine(
            "Residency Releases: " +
            ResidencyReleaseCount
        );
        builder.AppendLine(
            "Peak Height Stride Expected GUIDs: " +
            HeightPeakStrideExpectedGuidCount
        );
        builder.AppendLine(
            "Peak Collision Region Expected GUIDs: " +
            CollisionPeakRegionExpectedGuidCount
        );
        AppendBytes(builder, "Managed Heap Before Build", ManagedBytesBeforeContentBuild);
        AppendBytes(builder, "Managed Heap After Pre-Build Cleanup", ManagedBytesAfterPreBuildCleanup);
        AppendBytes(builder, "Unity Allocated Before Build", UnityAllocatedBytesBeforeContentBuild);
        AppendBytes(builder, "Unity Allocated After Pre-Build Cleanup", UnityAllocatedBytesAfterPreBuildCleanup);
        AppendBytes(builder, "Process Working Set Before Build", ProcessWorkingSetBytesBeforeContentBuild);
        AppendBytes(builder, "Process Working Set After Pre-Build Cleanup", ProcessWorkingSetBytesAfterPreBuildCleanup);
        AppendBytes(builder, "Process Working Set After Build", ProcessWorkingSetBytesAfterContentBuild);
        AppendBytes(builder, "Process Working Set After Post-Build Cleanup", ProcessWorkingSetBytesAfterPostBuildCleanup);

        if (!string.IsNullOrEmpty(BuildOutputPath))
        {
            builder.AppendLine();
            builder.AppendLine(
                "Build Output Path:"
            );
            builder.AppendLine(
                BuildOutputPath
            );

            builder.AppendLine(
                "Build Duration: " +
                BuildDuration.ToString("0.00") +
                " seconds"
            );
        }

        if (AddressablesScaleReport != null)
        {
            builder.AppendLine();
            builder.Append(
                AddressablesScaleReport.BuildDiagnosticReport()
            );
        }

        if (HeightScaleReport != null)
        {
            builder.AppendLine();
            builder.Append(
                HeightScaleReport.BuildDiagnosticReport()
            );
        }

        if (!string.IsNullOrEmpty(SummaryMessage))
        {
            builder.AppendLine();
            builder.AppendLine(
                "Summary: " +
                SummaryMessage
            );
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            builder.AppendLine();
            builder.AppendLine(
                "Error: " +
                ErrorMessage
            );
        }

        return builder.ToString();
    }

    private static void AppendBytes(
        StringBuilder builder,
        string label,
        long bytes
    )
    {
        builder.AppendLine(
            label +
            ": " +
            (bytes >= 0L
                ? bytes.ToString()
                : "Unavailable")
        );
    }
}
