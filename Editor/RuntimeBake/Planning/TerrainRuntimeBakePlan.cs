using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public enum TerrainRuntimeBakeWorkMode
{
    None,
    Incremental,
    Full
}

/*
 * Immutable effective runtime bake plan.
 *
 * The plan describes work only. It never mutates persistent bake state,
 * generated assets, manifests, Addressables configuration, or scenes.
 */
public sealed class TerrainRuntimeBakePlan
{
    private readonly ReadOnlyCollection<Vector2Int>
        heightTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        heightStreamingTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        surfaceTiles;

    private readonly ReadOnlyCollection<Vector2Int>
        collisionChunks;

    private readonly ReadOnlyCollection<string>
        safetyEscalationReasons;

    public TerrainRuntimeBakeWorkMode HeightWorkMode
    {
        get;
        private set;
    }

    public TerrainRuntimeBakeWorkMode HeightStreamingWorkMode
    {
        get;
        private set;
    }

    public TerrainRuntimeBakeWorkMode SurfaceWorkMode
    {
        get;
        private set;
    }

    public TerrainRuntimeBakeWorkMode CollisionWorkMode
    {
        get;
        private set;
    }

    public IReadOnlyList<Vector2Int> HeightTiles =>
        heightTiles;

    public IReadOnlyList<Vector2Int> HeightStreamingTiles =>
        heightStreamingTiles;

    public IReadOnlyList<Vector2Int> SurfaceTiles =>
        surfaceTiles;

    public IReadOnlyList<Vector2Int> CollisionChunks =>
        collisionChunks;

    public int HeightTileCount =>
        heightTiles.Count;

    public int HeightStreamingTileCount =>
        heightStreamingTiles.Count;

    public int SurfaceTileCount =>
        surfaceTiles.Count;

    public int CollisionChunkCount =>
        collisionChunks.Count;

    public bool AddressablesConfigurationRequired
    {
        get;
        private set;
    }

    public bool AddressablesContentBuildRequired
    {
        get;
        private set;
    }

    public bool RuntimeSceneMetadataUpdateRequired
    {
        get;
        private set;
    }

    public bool IsInitialBake
    {
        get;
        private set;
    }

    public bool IsBlocked
    {
        get;
        private set;
    }

    public string BlockReason
    {
        get;
        private set;
    }

    public IReadOnlyList<string> SafetyEscalationReasons =>
        safetyEscalationReasons;

    public long SourceStateRevision
    {
        get;
        private set;
    }

    public string CurrentAuthoringSignature
    {
        get;
        private set;
    }

    public string ObservedAuthoringSignature
    {
        get;
        private set;
    }

    public string CurrentSurfaceSettingsSignature
    {
        get;
        private set;
    }

    public string CurrentCollisionSettingsSignature
    {
        get;
        private set;
    }

    public bool HasWork
    {
        get
        {
            return
                HeightWorkMode !=
                    TerrainRuntimeBakeWorkMode.None
                ||
                HeightStreamingWorkMode !=
                    TerrainRuntimeBakeWorkMode.None
                ||
                SurfaceWorkMode !=
                    TerrainRuntimeBakeWorkMode.None
                ||
                CollisionWorkMode !=
                    TerrainRuntimeBakeWorkMode.None
                ||
                AddressablesConfigurationRequired
                ||
                AddressablesContentBuildRequired
                ||
                RuntimeSceneMetadataUpdateRequired;
        }
    }

    internal TerrainRuntimeBakePlan(
        TerrainRuntimeBakeWorkMode heightWorkMode,
        TerrainRuntimeBakeWorkMode heightStreamingWorkMode,
        TerrainRuntimeBakeWorkMode surfaceWorkMode,
        TerrainRuntimeBakeWorkMode collisionWorkMode,
        IEnumerable<Vector2Int> heightTiles,
        IEnumerable<Vector2Int> heightStreamingTiles,
        IEnumerable<Vector2Int> surfaceTiles,
        IEnumerable<Vector2Int> collisionChunks,
        bool addressablesConfigurationRequired,
        bool addressablesContentBuildRequired,
        bool runtimeSceneMetadataUpdateRequired,
        bool isInitialBake,
        bool isBlocked,
        string blockReason,
        IEnumerable<string> safetyEscalationReasons,
        long sourceStateRevision,
        string currentAuthoringSignature,
        string observedAuthoringSignature,
        string currentSurfaceSettingsSignature,
        string currentCollisionSettingsSignature
    )
    {
        HeightWorkMode =
            heightWorkMode;

        HeightStreamingWorkMode =
            heightStreamingWorkMode;

        SurfaceWorkMode =
            surfaceWorkMode;

        CollisionWorkMode =
            collisionWorkMode;

        this.heightTiles =
            CreateSortedCoordinates(
                heightTiles
            );

        this.heightStreamingTiles =
            CreateSortedCoordinates(
                heightStreamingTiles
            );

        this.surfaceTiles =
            CreateSortedCoordinates(
                surfaceTiles
            );

        this.collisionChunks =
            CreateSortedCoordinates(
                collisionChunks
            );

        AddressablesConfigurationRequired =
            addressablesConfigurationRequired;

        AddressablesContentBuildRequired =
            addressablesContentBuildRequired;

        RuntimeSceneMetadataUpdateRequired =
            runtimeSceneMetadataUpdateRequired;

        IsInitialBake =
            isInitialBake;

        IsBlocked =
            isBlocked;

        BlockReason =
            blockReason ??
            "";

        List<string> reasons =
            new List<string>();

        if (safetyEscalationReasons != null)
        {
            reasons.AddRange(
                safetyEscalationReasons
            );
        }

        this.safetyEscalationReasons =
            reasons.AsReadOnly();

        SourceStateRevision =
            sourceStateRevision;

        CurrentAuthoringSignature =
            currentAuthoringSignature ??
            "";

        ObservedAuthoringSignature =
            observedAuthoringSignature ??
            "";

        CurrentSurfaceSettingsSignature =
            currentSurfaceSettingsSignature ??
            "";

        CurrentCollisionSettingsSignature =
            currentCollisionSettingsSignature ??
            "";
    }

    private static ReadOnlyCollection<Vector2Int>
        CreateSortedCoordinates(
            IEnumerable<Vector2Int> source
        )
    {
        HashSet<Vector2Int> unique =
            source != null
                ? new HashSet<Vector2Int>(source)
                : new HashSet<Vector2Int>();

        List<Vector2Int> sorted =
            new List<Vector2Int>(
                unique
            );

        sorted.Sort(
            CompareCoordinates
        );

        return
            sorted.AsReadOnly();
    }

    private static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int yComparison =
            left.y.CompareTo(
                right.y
            );

        if (yComparison != 0)
        {
            return yComparison;
        }

        return
            left.x.CompareTo(
                right.x
            );
    }
}
