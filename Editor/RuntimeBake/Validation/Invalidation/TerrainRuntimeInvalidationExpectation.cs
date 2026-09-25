using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public sealed class TerrainRuntimeInvalidationExpectation
{
    private readonly ReadOnlyCollection<Vector2Int> stateHeightCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> stateHeightStreamingCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> stateSurfaceCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> stateCollisionCoordinates;

    private readonly ReadOnlyCollection<Vector2Int> planHeightCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> planHeightStreamingCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> planSurfaceCoordinates;
    private readonly ReadOnlyCollection<Vector2Int> planCollisionCoordinates;

    public TerrainRuntimeBakeWorkMode ExpectedHeightMode { get; private set; }
    public TerrainRuntimeBakeWorkMode ExpectedHeightStreamingMode { get; private set; }
    public TerrainRuntimeBakeWorkMode ExpectedSurfaceMode { get; private set; }
    public TerrainRuntimeBakeWorkMode ExpectedCollisionMode { get; private set; }

    public IReadOnlyList<Vector2Int> StateHeightCoordinates => stateHeightCoordinates;
    public IReadOnlyList<Vector2Int> StateHeightStreamingCoordinates => stateHeightStreamingCoordinates;
    public IReadOnlyList<Vector2Int> StateSurfaceCoordinates => stateSurfaceCoordinates;
    public IReadOnlyList<Vector2Int> StateCollisionCoordinates => stateCollisionCoordinates;

    public IReadOnlyList<Vector2Int> PlanHeightCoordinates => planHeightCoordinates;
    public IReadOnlyList<Vector2Int> PlanHeightStreamingCoordinates => planHeightStreamingCoordinates;
    public IReadOnlyList<Vector2Int> PlanSurfaceCoordinates => planSurfaceCoordinates;
    public IReadOnlyList<Vector2Int> PlanCollisionCoordinates => planCollisionCoordinates;

    public bool ExpectedFullHeight { get; private set; }
    public bool ExpectedFullHeightStreaming { get; private set; }
    public bool ExpectedFullSurface { get; private set; }
    public bool ExpectedFullCollision { get; private set; }

    public bool ExpectedStateAddressablesConfigurationDirty { get; private set; }
    public bool ExpectedStateAddressablesContentDirty { get; private set; }
    public bool ExpectedStateRuntimeSceneDirty { get; private set; }

    public bool ExpectedPlanAddressablesConfigurationRequired { get; private set; }
    public bool ExpectedPlanAddressablesContentRequired { get; private set; }
    public bool ExpectedPlanRuntimeSceneRequired { get; private set; }

    public string Description { get; private set; }

    internal TerrainRuntimeInvalidationExpectation(
        TerrainRuntimeBakeWorkMode heightMode,
        TerrainRuntimeBakeWorkMode surfaceMode,
        TerrainRuntimeBakeWorkMode collisionMode,
        IEnumerable<Vector2Int> stateHeightCoordinates,
        IEnumerable<Vector2Int> stateSurfaceCoordinates,
        IEnumerable<Vector2Int> stateCollisionCoordinates,
        IEnumerable<Vector2Int> planHeightCoordinates,
        IEnumerable<Vector2Int> planSurfaceCoordinates,
        IEnumerable<Vector2Int> planCollisionCoordinates,
        bool fullHeight,
        bool fullSurface,
        bool fullCollision,
        bool stateAddressablesConfigurationDirty,
        bool stateAddressablesContentDirty,
        bool stateRuntimeSceneDirty,
        bool planAddressablesConfigurationRequired,
        bool planAddressablesContentRequired,
        bool planRuntimeSceneRequired,
        string description
    )
    {
        ExpectedHeightMode = heightMode;
        ExpectedHeightStreamingMode = heightMode;
        ExpectedSurfaceMode = surfaceMode;
        ExpectedCollisionMode = collisionMode;

        this.stateHeightCoordinates = CreateSortedCoordinates(stateHeightCoordinates);
        this.stateHeightStreamingCoordinates = CreateSortedCoordinates(stateHeightCoordinates);
        this.stateSurfaceCoordinates = CreateSortedCoordinates(stateSurfaceCoordinates);
        this.stateCollisionCoordinates = CreateSortedCoordinates(stateCollisionCoordinates);

        this.planHeightCoordinates = CreateSortedCoordinates(planHeightCoordinates);
        this.planHeightStreamingCoordinates = CreateSortedCoordinates(planHeightCoordinates);
        this.planSurfaceCoordinates = CreateSortedCoordinates(planSurfaceCoordinates);
        this.planCollisionCoordinates = CreateSortedCoordinates(planCollisionCoordinates);

        ExpectedFullHeight = fullHeight;
        ExpectedFullHeightStreaming = fullHeight;
        ExpectedFullSurface = fullSurface;
        ExpectedFullCollision = fullCollision;

        ExpectedStateAddressablesConfigurationDirty =
            stateAddressablesConfigurationDirty;
        ExpectedStateAddressablesContentDirty =
            stateAddressablesContentDirty;
        ExpectedStateRuntimeSceneDirty =
            stateRuntimeSceneDirty;

        ExpectedPlanAddressablesConfigurationRequired =
            planAddressablesConfigurationRequired;
        ExpectedPlanAddressablesContentRequired =
            planAddressablesContentRequired;
        ExpectedPlanRuntimeSceneRequired =
            planRuntimeSceneRequired;

        Description = description ?? "";
    }

    private static ReadOnlyCollection<Vector2Int> CreateSortedCoordinates(
        IEnumerable<Vector2Int> source
    )
    {
        List<Vector2Int> coordinates =
            source != null
                ? new List<Vector2Int>(new HashSet<Vector2Int>(source))
                : new List<Vector2Int>();

        coordinates.Sort(CompareCoordinates);
        return coordinates.AsReadOnly();
    }

    internal static int CompareCoordinates(
        Vector2Int left,
        Vector2Int right
    )
    {
        int y = left.y.CompareTo(right.y);

        return y != 0
            ? y
            : left.x.CompareTo(right.x);
    }
}
