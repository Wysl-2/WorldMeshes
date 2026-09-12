using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * Package I2 validation for deterministic, derived regional-node topology.
 *
 * All topology fixtures are transient. The real authoring asset is observed
 * only so validation can prove that derived geometry work leaves project and
 * Package 7 selection state unchanged.
 */
public static class TerrainRegionalElevationTriangulationValidationUtility
{
    private enum ValidationOutcome
    {
        Pass,
        Fail,
        Blocked
    }

    private sealed class ValidationResult
    {
        public string Name;
        public ValidationOutcome Outcome;
        public string Details;
    }

    private struct NodeSpec
    {
        public Vector2 Position;
        public float Elevation;

        public NodeSpec(Vector2 position, float elevation)
        {
            Position = position;
            Elevation = elevation;
        }
    }

    private static readonly List<ValidationResult> results =
        new List<ValidationResult>();

    private static bool validationRunning;
    private static bool validationScheduled;
    private static WorldSettings worldSettings;
    private static TerrainAuthoringData realAuthoringData;
    private static int realRevisionBefore;
    private static string realCommittedBefore = "";
    private static string realOverallBefore = "";
    private static TerrainRegionalElevationSource realRegionalBefore;
    private static TerrainNodeElevationInterpolationMode realInterpolationBefore;
    private static readonly List<string> realSelectedIdsBefore =
        new List<string>();
    private static string realPrimaryBefore = "";

    public static bool IsRunning =>
        validationRunning || validationScheduled;

    public static void ValidateRegionalElevationTriangulation()
    {
        if (validationRunning || validationScheduled)
        {
            return;
        }

        validationScheduled = true;
        EditorApplication.delayCall -= RunScheduledValidation;
        EditorApplication.delayCall += RunScheduledValidation;
    }

    private static void RunScheduledValidation()
    {
        EditorApplication.delayCall -= RunScheduledValidation;

        if (!validationScheduled || validationRunning)
        {
            return;
        }

        validationScheduled = false;
        validationRunning = true;
        results.Clear();

        try
        {
            if (!TryValidatePrerequisites(out string prerequisiteError))
            {
                AddResult(
                    "Validation prerequisites",
                    ValidationOutcome.Blocked,
                    prerequisiteError);
                return;
            }

            CaptureRealBaseline();

            AddResult(
                "Validation prerequisites",
                ValidationOutcome.Pass,
                "Current WorldSettings, TerrainAuthoringData, Package I1 state, and Package 7 selection state are available.");

            ValidateTopologyKinds();
            ValidateSquareDeterminism();
            ValidateTriangulatedInvariants();
            ValidateCoincidentPolicy();
            ValidateNonFiniteGeometrySafety();
            ValidateCacheBehavior();
            ValidateIdentityAndSourceOrderSafety();
            ValidateContainmentQueries();
            ValidateIdwAndFutureModeCompatibility();
            ValidateRealStateUnchanged();
        }
        catch (Exception exception)
        {
            AddResult(
                "Unexpected validation exception",
                ValidationOutcome.Fail,
                exception.ToString());
        }
        finally
        {
            validationRunning = false;
            WriteReport();
        }
    }

    private static bool TryValidatePrerequisites(out string errorMessage)
    {
        errorMessage = "";

        if (Application.isPlaying ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            errorMessage = "Validation cannot run in or while entering Play Mode.";
            return false;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            errorMessage =
                "Wait for Unity to finish compiling/importing and run validation again.";
            return false;
        }

        worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath);

        realAuthoringData =
            AssetDatabase.LoadAssetAtPath<TerrainAuthoringData>(
                WorldMeshesPaths.TerrainAuthoringDataAssetPath);

        if (worldSettings == null || realAuthoringData == null)
        {
            errorMessage = "WorldSettings or TerrainAuthoringData could not be loaded.";
            return false;
        }

        string committed =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings);

        string overall =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData);

        if (string.IsNullOrEmpty(committed) || string.IsNullOrEmpty(overall))
        {
            errorMessage =
                "Initialize the committed authoring heightfield before running Package I2 validation.";
            return false;
        }

        return true;
    }

    private static void CaptureRealBaseline()
    {
        realRevisionBefore = realAuthoringData.authoringRevision;
        realCommittedBefore =
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings);
        realOverallBefore =
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData);
        realRegionalBefore = realAuthoringData.RegionalElevationSource;

        TerrainNodeElevationSource realNodeSource =
            realRegionalBefore as TerrainNodeElevationSource;

        realInterpolationBefore =
            realNodeSource != null
                ? realNodeSource.InterpolationMode
                : TerrainNodeElevationInterpolationMode.InverseDistanceWeighted;

        realSelectedIdsBefore.Clear();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            realSelectedIdsBefore);

        realPrimaryBefore =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(
                realAuthoringData);
    }

    private static void ValidateTopologyKinds()
    {
        TerrainNodeElevationSource empty = CreateSource();
        TerrainNodeElevationSource single = CreateSource(
            new NodeSpec(new Vector2(10f, 20f), 5f));
        TerrainNodeElevationSource line = CreateSource(
            new NodeSpec(new Vector2(100f, 0f), 10f),
            new NodeSpec(new Vector2(0f, 0f), 20f));
        TerrainNodeElevationSource collinear = CreateSource(
            new NodeSpec(new Vector2(30f, 60f), 100f),
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(10f, 20f), 25f),
            new NodeSpec(new Vector2(20f, 40f), 50f));
        TerrainNodeElevationSource triangle = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(100f, 0f), 10f),
            new NodeSpec(new Vector2(0f, 100f), 20f));

        bool emptyOk =
            TryBuild(empty, out TerrainNodeElevationTopology emptyTopology) &&
            emptyTopology.Kind == TerrainNodeElevationTopologyKind.Empty &&
            emptyTopology.VertexCount == 0 &&
            emptyTopology.TriangleCount == 0 &&
            emptyTopology.HullVertexCount == 0 &&
            emptyTopology.HullEdgeCount == 0;

        bool singleOk =
            TryBuild(single, out TerrainNodeElevationTopology singleTopology) &&
            singleTopology.Kind == TerrainNodeElevationTopologyKind.SinglePoint &&
            singleTopology.VertexCount == 1 &&
            singleTopology.TriangleCount == 0 &&
            singleTopology.HullVertexCount == 1 &&
            singleTopology.HullVertexIndices[0] == 0;

        bool lineOk =
            TryBuild(line, out TerrainNodeElevationTopology lineTopology) &&
            lineTopology.Kind == TerrainNodeElevationTopologyKind.LineSegment &&
            lineTopology.VertexCount == 2 &&
            lineTopology.TriangleCount == 0 &&
            lineTopology.EdgeCount == 1 &&
            lineTopology.HullEdgeCount == 1 &&
            lineTopology.GetVertexNeighbors(0).Count == 1 &&
            lineTopology.GetVertexNeighbors(0)[0] == 1 &&
            lineTopology.GetVertexNeighbors(1).Count == 1 &&
            lineTopology.GetVertexNeighbors(1)[0] == 0;

        bool collinearOk =
            TryBuild(collinear, out TerrainNodeElevationTopology collinearTopology) &&
            collinearTopology.Kind == TerrainNodeElevationTopologyKind.Collinear &&
            collinearTopology.VertexCount == 4 &&
            collinearTopology.TriangleCount == 0 &&
            collinearTopology.HullVertexCount == 4 &&
            collinearTopology.EdgeCount == 3 &&
            IsCanonicalVertexOrder(collinearTopology);

        bool triangleOk =
            TryBuild(triangle, out TerrainNodeElevationTopology triangleTopology) &&
            triangleTopology.Kind == TerrainNodeElevationTopologyKind.Triangulated &&
            triangleTopology.VertexCount == 3 &&
            triangleTopology.TriangleCount == 1 &&
            triangleTopology.EdgeCount == 3 &&
            triangleTopology.HullVertexCount == 3 &&
            triangleTopology.HullEdgeCount == 3 &&
            TriangleIsCcwAndCanonical(triangleTopology, 0);

        bool passed =
            emptyOk && singleOk && lineOk && collinearOk && triangleOk;

        AddResult(
            "Empty, point, segment, collinear, and basic triangle topology kinds are deterministic",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Degenerate layouts produce explicit topology kinds, while three non-collinear nodes produce one canonical CCW triangle."
                : "One or more foundational topology-kind fixtures did not match the Package I2 contract.");
    }

    private static void ValidateSquareDeterminism()
    {
        TerrainNodeElevationSource square = CreateSource(
            new NodeSpec(new Vector2(100f, 100f), 40f),
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(100f, 0f), 30f),
            new NodeSpec(new Vector2(0f, 100f), 20f));

        bool firstBuilt = TryBuild(square, out TerrainNodeElevationTopology first);
        string firstKey = firstBuilt ? TopologyIndexKey(first) : "";
        bool repeatedSame = firstBuilt;

        for (int iteration = 0; iteration < 5 && repeatedSame; iteration++)
        {
            repeatedSame =
                TryBuild(square, out TerrainNodeElevationTopology repeated) &&
                TopologyIndexKey(repeated) == firstKey;
        }

        TerrainNodeElevationTopologyEdge internalEdge = default(TerrainNodeElevationTopologyEdge);
        int internalEdgeCount = 0;

        if (firstBuilt)
        {
            for (int index = 0; index < first.Edges.Count; index++)
            {
                if (first.Edges[index].TriangleReferenceCount == 2)
                {
                    internalEdge = first.Edges[index];
                    internalEdgeCount++;
                }
            }
        }

        bool tieBreakOk =
            firstBuilt &&
            first.VertexCount == 4 &&
            first.TriangleCount == 2 &&
            first.EdgeCount == 5 &&
            first.HullEdgeCount == 4 &&
            internalEdgeCount == 1 &&
            internalEdge.VertexA == 0 &&
            internalEdge.VertexB == 3;

        TerrainNodeElevationSource reordered = CreateSource(
            new NodeSpec(new Vector2(0f, 100f), 200f),
            new NodeSpec(new Vector2(100f, 0f), 300f),
            new NodeSpec(new Vector2(0f, 0f), 100f),
            new NodeSpec(new Vector2(100f, 100f), 400f));

        bool reorderedBuilt =
            TryBuild(reordered, out TerrainNodeElevationTopology reorderedTopology);

        bool geometryEquivalent =
            firstBuilt &&
            reorderedBuilt &&
            TopologyGeometryKey(first) == TopologyGeometryKey(reorderedTopology);

        bool passed = repeatedSame && tieBreakOk && geometryEquivalent;

        AddResult(
            "Cocircular square triangulation uses a repeatable canonical diagonal",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "The square always produces two triangles/five edges, selects canonical diagonal (0,3), and remains geometrically equivalent under different persistent source ordering."
                : "Square Delaunay tie-breaking or source-order geometric determinism failed.");
    }

    private static void ValidateTriangulatedInvariants()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(120f, 10f), 20f),
            new NodeSpec(new Vector2(220f, 90f), 30f),
            new NodeSpec(new Vector2(170f, 210f), 40f),
            new NodeSpec(new Vector2(40f, 190f), 50f),
            new NodeSpec(new Vector2(90f, 80f), 60f));

        bool built = TryBuild(source, out TerrainNodeElevationTopology topology);

        bool trianglesValid = built && topology.Kind == TerrainNodeElevationTopologyKind.Triangulated;
        bool edgesValid = built;
        bool adjacencyValid = built;
        bool hullValid = built;

        HashSet<string> triangleKeys = new HashSet<string>();
        HashSet<string> edgeKeys = new HashSet<string>();

        if (built)
        {
            TerrainNodeElevationTopologyTriangle previous =
                default(TerrainNodeElevationTopologyTriangle);

            for (int index = 0; index < topology.TriangleCount; index++)
            {
                TerrainNodeElevationTopologyTriangle triangle = topology.Triangles[index];
                string key = triangle.VertexA + ":" + triangle.VertexB + ":" + triangle.VertexC;

                trianglesValid &=
                    TriangleIsCcwAndCanonical(topology, index) &&
                    triangleKeys.Add(key);

                if (index > 0)
                {
                    trianglesValid &= CompareTriangle(previous, triangle) < 0;
                }

                previous = triangle;

                adjacencyValid &= ValidateTriangleNeighbors(topology, index);
            }

            TerrainNodeElevationTopologyEdge previousEdge =
                default(TerrainNodeElevationTopologyEdge);

            for (int index = 0; index < topology.EdgeCount; index++)
            {
                TerrainNodeElevationTopologyEdge edge = topology.Edges[index];
                string key = edge.VertexA + ":" + edge.VertexB;

                edgesValid &=
                    edge.VertexA < edge.VertexB &&
                    (edge.TriangleReferenceCount == 1 || edge.TriangleReferenceCount == 2) &&
                    edgeKeys.Add(key);

                if (index > 0)
                {
                    edgesValid &= previousEdge.CompareTo(edge) < 0;
                }

                previousEdge = edge;
            }

            for (int vertexIndex = 0; vertexIndex < topology.VertexCount; vertexIndex++)
            {
                IReadOnlyList<int> neighbors = topology.GetVertexNeighbors(vertexIndex);
                int previousNeighbor = -1;

                for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                {
                    int neighbor = neighbors[neighborIndex];
                    adjacencyValid &=
                        neighbor >= 0 &&
                        neighbor < topology.VertexCount &&
                        neighbor != vertexIndex &&
                        neighbor > previousNeighbor &&
                        Contains(topology.GetVertexNeighbors(neighbor), vertexIndex);
                    previousNeighbor = neighbor;
                }
            }

            hullValid &=
                topology.HullVertexCount >= 3 &&
                topology.HullEdgeCount == topology.HullVertexCount &&
                SignedHullArea(topology) > 0.0;

            HashSet<int> hullVertices = new HashSet<int>();
            HashSet<string> hullEdgeKeys = new HashSet<string>();

            for (int index = 0; index < topology.HullVertexCount; index++)
            {
                hullValid &= hullVertices.Add(topology.HullVertexIndices[index]);
            }

            for (int index = 0; index < topology.HullEdgeCount; index++)
            {
                TerrainNodeElevationTopologyEdge edge = topology.HullEdges[index];
                hullValid &=
                    edge.TriangleReferenceCount == 1 &&
                    hullEdgeKeys.Add(edge.VertexA + ":" + edge.VertexB) &&
                    ContainsEdge(topology.Edges, edge.VertexA, edge.VertexB, 1);
            }

            int interiorVertex = FindVertexByPosition(topology, new Vector2(90f, 80f));
            hullValid &= interiorVertex >= 0 && !hullVertices.Contains(interiorVertex);
        }

        bool passed = trianglesValid && edgesValid && adjacencyValid && hullValid;

        AddResult(
            "Triangulated topology exposes canonical triangles, manifold edges, symmetric adjacency, and an ordered CCW hull",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "An irregular layout produced unique sorted CCW triangles/edges, symmetric triangle and node adjacency, and a boundary-only CCW hull excluding the interior node."
                : "One or more triangulated topology invariants failed.");
    }

    private static void ValidateCoincidentPolicy()
    {
        TerrainNodeElevationSource coincident = CreateSource(
            new NodeSpec(new Vector2(50f, 50f), 10f),
            new NodeSpec(new Vector2(50f, 50f), 500f),
            new NodeSpec(new Vector2(100f, 0f), 20f));

        string[] stableIdsBefore = CaptureStableIds(coincident);
        Vector2[] positionsBefore = CapturePositions(coincident);
        float[] elevationsBefore = CaptureElevations(coincident);

        bool outputValid = coincident.TryValidateOutputData(out _);
        bool topologyRejected =
            !TerrainNodeElevationTriangulationUtility.TryBuild(
                coincident,
                out _,
                out string coincidentError) &&
            coincidentError.Contains("coincident");

        bool idwStillValid =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                coincident,
                new Vector2(50f, 50f),
                out float coincidentHeight,
                out _) &&
            Mathf.Abs(coincidentHeight - 255f) <= 0.0001f;

        bool sourceUnchanged =
            ArraysEqual(stableIdsBefore, CaptureStableIds(coincident)) &&
            ArraysEqual(positionsBefore, CapturePositions(coincident)) &&
            ArraysEqual(elevationsBefore, CaptureElevations(coincident));

        float nearDistance =
            TerrainNodeElevationGeometryUtility.MinimumTriangulationVertexSeparation * 0.5f;

        TerrainNodeElevationSource nearCoincident = CreateSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(nearDistance, 0f), 100f),
            new NodeSpec(new Vector2(100f, 100f), 50f));

        bool nearRejected =
            !TerrainNodeElevationTriangulationUtility.TryBuild(
                nearCoincident,
                out _,
                out string nearError) &&
            nearError.Contains("minimum triangulation separation");

        bool passed =
            outputValid &&
            topologyRejected &&
            idwStillValid &&
            sourceUnchanged &&
            nearRejected;

        AddResult(
            "Coincident/near-coincident positions are topology errors without changing IDW source semantics",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Triangulation rejects ambiguous vertices without merging/discarding data, while the existing IDW exact-match average remains valid and source state is untouched."
                : coincidentError + " " + nearError);
    }

    private static void ValidateNonFiniteGeometrySafety()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(100f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 100f), 200f));

        FieldInfo positionField =
            typeof(TerrainElevationNode).GetField(
                "positionXZ",
                BindingFlags.Instance | BindingFlags.NonPublic);

        bool reflectionReady = positionField != null;

        if (reflectionReady)
        {
            positionField.SetValue(
                source.Nodes[1],
                new Vector2(float.NaN, 0f));
        }

        bool rejected =
            reflectionReady &&
            !TerrainNodeElevationTriangulationUtility.TryBuild(
                source,
                out _,
                out string errorMessage) &&
            errorMessage.Contains("invalid geometry");

        AddResult(
            "Non-finite stored node positions fail topology construction safely",
            rejected ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            rejected
                ? "The topology builder inspects stored XZ geometry directly and rejected NaN without producing topology or mutating the source."
                : "Malformed stored position geometry was not rejected as expected.");
    }

    private static void ValidateCacheBehavior()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 10f),
            new NodeSpec(new Vector2(120f, 0f), 20f),
            new NodeSpec(new Vector2(100f, 100f), 30f),
            new NodeSpec(new Vector2(0f, 120f), 40f));

        TerrainNodeElevationTopologyCache cache =
            new TerrainNodeElevationTopologyCache();

        bool firstBuilt =
            cache.TryGetOrBuild(source, out TerrainNodeElevationTopology first, out _);
        int countAfterFirst = cache.RebuildCount;

        source.Nodes[0].SetElevationInternal(999f);

        bool elevationReused =
            cache.TryGetOrBuild(source, out TerrainNodeElevationTopology afterElevation, out _) &&
            ReferenceEquals(first, afterElevation) &&
            cache.RebuildCount == countAfterFirst;

        source.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedLinear);

        bool modeReused =
            cache.TryGetOrBuild(source, out TerrainNodeElevationTopology afterMode, out _) &&
            ReferenceEquals(first, afterMode) &&
            cache.RebuildCount == countAfterFirst;

        string replacementStableId = Guid.NewGuid().ToString("N");
        bool stableIdChanged = SetPrivateStableId(source.Nodes[0], replacementStableId);

        bool identityReused =
            stableIdChanged &&
            cache.TryGetOrBuild(source, out TerrainNodeElevationTopology afterIdentity, out _) &&
            ReferenceEquals(first, afterIdentity) &&
            cache.RebuildCount == countAfterFirst;

        source.Nodes[0].SetPositionXZInternal(new Vector2(5f, 5f));
        bool positionRebuilt =
            cache.TryGetOrBuild(source, out TerrainNodeElevationTopology afterPosition, out _) &&
            !ReferenceEquals(first, afterPosition) &&
            cache.RebuildCount == countAfterFirst + 1;

        TerrainElevationNode added = CreateNode(new Vector2(180f, 160f), 50f);
        source.AddNodeInternal(added);
        source.RepairNodeStableIds();

        bool addRebuilt =
            cache.TryGetOrBuild(source, out _, out _) &&
            cache.RebuildCount == countAfterFirst + 2;

        source.RemoveNodeAtInternal(source.NodeCount - 1);

        bool removeRebuilt =
            cache.TryGetOrBuild(source, out _, out _) &&
            cache.RebuildCount == countAfterFirst + 3;

        source.Nodes[0].SetPositionXZInternal(source.Nodes[0].PositionXZ + new Vector2(10f, 0f));
        source.Nodes[1].SetPositionXZInternal(source.Nodes[1].PositionXZ + new Vector2(10f, 0f));

        bool groupLikeMoveRebuilt =
            cache.TryGetOrBuild(source, out _, out _) &&
            cache.RebuildCount == countAfterFirst + 4;

        bool passed =
            firstBuilt &&
            countAfterFirst == 1 &&
            elevationReused &&
            modeReused &&
            identityReused &&
            positionRebuilt &&
            addRebuilt &&
            removeRebuilt &&
            groupLikeMoveRebuilt;

        AddResult(
            "Topology cache keys only source-order XZ geometry",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Elevation, interpolation mode, and StableId changes reused topology; position, add, remove, and multi-node XZ changes rebuilt lazily on the next request."
                : "Geometry-only topology cache invalidation did not match the Package I2 contract.");
    }

    private static void ValidateIdentityAndSourceOrderSafety()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(new Vector2(80f, 40f), 10f),
            new NodeSpec(new Vector2(0f, 0f), 20f),
            new NodeSpec(new Vector2(100f, 120f), 30f),
            new NodeSpec(new Vector2(10f, 100f), 40f));

        TerrainElevationNode[] nodeReferences =
            new TerrainElevationNode[source.NodeCount];
        string[] stableIdsBefore = CaptureStableIds(source);

        for (int index = 0; index < source.NodeCount; index++)
        {
            nodeReferences[index] = source.Nodes[index];
        }

        bool firstBuilt = TryBuild(source, out TerrainNodeElevationTopology first);
        string geometryBefore = firstBuilt ? TopologyGeometryKey(first) : "";

        bool stableIdSet =
            SetPrivateStableId(
                source.Nodes[2],
                Guid.NewGuid().ToString("N"));

        bool secondBuilt = TryBuild(source, out TerrainNodeElevationTopology second);

        bool orderUnchanged = true;
        for (int index = 0; index < source.NodeCount; index++)
        {
            orderUnchanged &= ReferenceEquals(nodeReferences[index], source.Nodes[index]);
        }

        string[] stableIdsAfter = CaptureStableIds(source);
        bool unrelatedStableIdsUnchanged =
            stableIdsAfter[0] == stableIdsBefore[0] &&
            stableIdsAfter[1] == stableIdsBefore[1] &&
            stableIdsAfter[3] == stableIdsBefore[3];

        bool passed =
            firstBuilt &&
            stableIdSet &&
            secondBuilt &&
            geometryBefore == TopologyGeometryKey(second) &&
            orderUnchanged &&
            unrelatedStableIdsUnchanged &&
            stableIdsAfter[2] != stableIdsBefore[2];

        AddResult(
            "Triangulation is independent of StableId and never reorders persistent nodes",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Changing only one transient StableId left geometric topology identical, source reference ordering intact, and every unrelated StableId untouched."
                : "StableId independence or source-order non-mutation validation failed.");
    }

    private static void ValidateContainmentQueries()
    {
        TerrainNodeElevationSource source = CreateSource(
            new NodeSpec(new Vector2(0f, 0f), 0f),
            new NodeSpec(new Vector2(100f, 0f), 100f),
            new NodeSpec(new Vector2(100f, 100f), 200f),
            new NodeSpec(new Vector2(0f, 100f), 300f));

        bool built = TryBuild(source, out TerrainNodeElevationTopology topology);

        bool interior = false;
        bool outside = false;
        bool sharedEdge = false;
        bool vertex = false;

        if (built && topology.TriangleCount == 2)
        {
            TerrainNodeElevationTopologyTriangle triangle = topology.Triangles[0];
            Vector2 a = topology.Vertices[triangle.VertexA].PositionXZ;
            Vector2 b = topology.Vertices[triangle.VertexB].PositionXZ;
            Vector2 c = topology.Vertices[triangle.VertexC].PositionXZ;
            Vector2 centroid = (a + b + c) / 3f;

            interior =
                topology.TryFindContainingTriangle(centroid, out int interiorIndex) &&
                interiorIndex == 0;

            outside =
                !topology.TryFindContainingTriangle(
                    new Vector2(-1000f, -1000f),
                    out _);

            TerrainNodeElevationTopologyEdge internalEdge = default(TerrainNodeElevationTopologyEdge);
            bool foundInternalEdge = false;

            for (int index = 0; index < topology.EdgeCount; index++)
            {
                if (topology.Edges[index].TriangleReferenceCount == 2)
                {
                    internalEdge = topology.Edges[index];
                    foundInternalEdge = true;
                    break;
                }
            }

            if (foundInternalEdge)
            {
                Vector2 edgeA = topology.Vertices[internalEdge.VertexA].PositionXZ;
                Vector2 edgeB = topology.Vertices[internalEdge.VertexB].PositionXZ;
                Vector2 midpoint = (edgeA + edgeB) * 0.5f;

                sharedEdge =
                    topology.TryFindContainingTriangle(midpoint, out int sharedIndex) &&
                    sharedIndex == 0;
            }

            Vector2 vertexPosition = topology.Vertices[0].PositionXZ;
            vertex =
                topology.TryFindContainingTriangle(vertexPosition, out int vertexIndex) &&
                vertexIndex == LowestTriangleContainingVertex(topology, 0);
        }

        bool passed = built && interior && outside && sharedEdge && vertex;

        AddResult(
            "Containing-triangle queries are inclusive and deterministic on boundaries",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Interior samples resolve correctly, outside-hull samples return none, and shared-edge/vertex ambiguity resolves to the lowest canonical triangle index."
                : "Point-to-triangle containment behavior did not match the Package I2 contract.");
    }

    private static void ValidateIdwAndFutureModeCompatibility()
    {
        bool oneNode = EvaluateEquals(
            CreateSource(new NodeSpec(new Vector2(10f, 20f), 42f)),
            new Vector2(-500f, 900f),
            42f,
            0f);

        bool midpoint = EvaluateEquals(
            CreateSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f)),
            new Vector2(5f, 0f),
            50f,
            0.0001f);

        bool exactNode = EvaluateEquals(
            CreateSource(
                new NodeSpec(Vector2.zero, 15f),
                new NodeSpec(new Vector2(100f, 0f), 200f)),
            Vector2.zero,
            15f,
            0f);

        bool coincident = EvaluateEquals(
            CreateSource(
                new NodeSpec(new Vector2(25f, 25f), 20f),
                new NodeSpec(new Vector2(25f, 25f), 40f),
                new NodeSpec(new Vector2(100f, 25f), 1000f)),
            new Vector2(25f, 25f),
            30f,
            0.0001f);

        bool arbitrary = EvaluateEquals(
            CreateSource(
                new NodeSpec(Vector2.zero, 0f),
                new NodeSpec(new Vector2(10f, 0f), 100f)),
            new Vector2(2f, 0f),
            5.882353f,
            0.0005f);

        TerrainNodeElevationSource linear = CreateSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(100f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 100f), 50f));
        linear.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedLinear);

        bool topologyAvailableForLinear = TryBuild(linear, out _);
        bool linearCpuSupported =
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                linear,
                new Vector2(20f, 20f),
                out float linearHeight,
                out string linearError) &&
            Mathf.Abs(linearHeight - 30f) <= 0.0001f;

        TerrainNodeElevationSource smooth = CreateSource(
            new NodeSpec(Vector2.zero, 0f),
            new NodeSpec(new Vector2(100f, 0f), 100f),
            new NodeSpec(new Vector2(0f, 100f), 50f));
        smooth.SetInterpolationModeInternal(
            TerrainNodeElevationInterpolationMode.TriangulatedSmooth);

        bool smoothStillUnsupported =
            !TerrainNodeElevationEvaluator.TryEvaluateHeight(
                smooth,
                new Vector2(20f, 20f),
                out _,
                out string smoothError) &&
            smoothError.Contains("CPU");

        bool passed =
            oneNode &&
            midpoint &&
            exactNode &&
            coincident &&
            arbitrary &&
            topologyAvailableForLinear &&
            linearCpuSupported &&
            smoothStillUnsupported;

        AddResult(
            "Package I2 topology remains compatible with IDW and Linear CPU evaluation",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Package I1 IDW regression samples are unchanged; Package I2 topology is reusable by Linear CPU evaluation, while Smooth remains explicitly unsupported."
                : linearError + " " + smoothError);
    }

    private static void ValidateRealStateUnchanged()
    {
        List<string> selectedAfter = new List<string>();
        TerrainRegionalElevationSelectionState.CopySelectedStableIds(
            realAuthoringData,
            selectedAfter);

        string primaryAfter =
            TerrainRegionalElevationSelectionState.GetPrimaryStableId(
                realAuthoringData);

        TerrainNodeElevationSource realNodeSource =
            realAuthoringData.RegionalElevationSource as TerrainNodeElevationSource;

        TerrainNodeElevationInterpolationMode interpolationAfter =
            realNodeSource != null
                ? realNodeSource.InterpolationMode
                : TerrainNodeElevationInterpolationMode.InverseDistanceWeighted;

        bool passed =
            realAuthoringData.authoringRevision == realRevisionBefore &&
            ReferenceEquals(realAuthoringData.RegionalElevationSource, realRegionalBefore) &&
            TerrainAuthoringStateUtility.GetCommittedHeightfieldSignature(
                worldSettings) == realCommittedBefore &&
            TerrainAuthoringStateUtility.GetOverallAuthoringSignature(
                worldSettings,
                realAuthoringData) == realOverallBefore &&
            interpolationAfter == realInterpolationBefore &&
            realPrimaryBefore == primaryAfter &&
            SequenceEqual(realSelectedIdsBefore, selectedAfter);

        AddResult(
            "Real authoring, interpolation, and Package 7 selection state remain unchanged",
            passed ? ValidationOutcome.Pass : ValidationOutcome.Fail,
            passed
                ? "Derived topology validation created no authoring revision/signature/source/interpolation/selection mutation and therefore required no terrain invalidation transaction."
                : "Real WorldMeshes state changed during Package I2 validation.");
    }

    private static bool TryBuild(
        TerrainNodeElevationSource source,
        out TerrainNodeElevationTopology topology)
    {
        return TerrainNodeElevationTriangulationUtility.TryBuild(
            source,
            out topology,
            out _);
    }

    private static TerrainNodeElevationSource CreateSource(
        params NodeSpec[] nodes)
    {
        TerrainNodeElevationSource source =
            new TerrainNodeElevationSource();

        if (nodes != null)
        {
            for (int index = 0; index < nodes.Length; index++)
            {
                source.AddNodeInternal(
                    CreateNode(
                        nodes[index].Position,
                        nodes[index].Elevation));
            }
        }

        source.RepairNodeStableIds();
        return source;
    }

    private static TerrainElevationNode CreateNode(
        Vector2 position,
        float elevation)
    {
        TerrainElevationNode node = new TerrainElevationNode();
        node.SetPositionXZInternal(position);
        node.SetElevationInternal(elevation);
        return node;
    }

    private static bool IsCanonicalVertexOrder(
        TerrainNodeElevationTopology topology)
    {
        for (int index = 1; index < topology.VertexCount; index++)
        {
            Vector2 previous = topology.Vertices[index - 1].PositionXZ;
            Vector2 current = topology.Vertices[index].PositionXZ;

            if (previous.x > current.x ||
                (previous.x == current.x && previous.y > current.y))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TriangleIsCcwAndCanonical(
        TerrainNodeElevationTopology topology,
        int triangleIndex)
    {
        TerrainNodeElevationTopologyTriangle triangle =
            topology.Triangles[triangleIndex];

        if (
            triangle.VertexA < 0 ||
            triangle.VertexB < 0 ||
            triangle.VertexC < 0 ||
            triangle.VertexA >= topology.VertexCount ||
            triangle.VertexB >= topology.VertexCount ||
            triangle.VertexC >= topology.VertexCount ||
            triangle.VertexA == triangle.VertexB ||
            triangle.VertexB == triangle.VertexC ||
            triangle.VertexC == triangle.VertexA)
        {
            return false;
        }

        int minimum = Math.Min(
            triangle.VertexA,
            Math.Min(triangle.VertexB, triangle.VertexC));

        if (triangle.VertexA != minimum)
        {
            return false;
        }

        Vector2 a = topology.Vertices[triangle.VertexA].PositionXZ;
        Vector2 b = topology.Vertices[triangle.VertexB].PositionXZ;
        Vector2 c = topology.Vertices[triangle.VertexC].PositionXZ;

        return TerrainNodeElevationGeometryUtility.IsCounterClockwise(a, b, c);
    }

    private static int CompareTriangle(
        TerrainNodeElevationTopologyTriangle a,
        TerrainNodeElevationTopologyTriangle b)
    {
        int compare = a.VertexA.CompareTo(b.VertexA);
        if (compare != 0)
        {
            return compare;
        }

        compare = a.VertexB.CompareTo(b.VertexB);
        if (compare != 0)
        {
            return compare;
        }

        return a.VertexC.CompareTo(b.VertexC);
    }

    private static bool ValidateTriangleNeighbors(
        TerrainNodeElevationTopology topology,
        int triangleIndex)
    {
        TerrainNodeElevationTopologyTriangle triangle =
            topology.Triangles[triangleIndex];

        return
            ValidateNeighbor(
                topology,
                triangleIndex,
                triangle.VertexA,
                triangle.VertexB,
                triangle.NeighborAcrossAB) &&
            ValidateNeighbor(
                topology,
                triangleIndex,
                triangle.VertexB,
                triangle.VertexC,
                triangle.NeighborAcrossBC) &&
            ValidateNeighbor(
                topology,
                triangleIndex,
                triangle.VertexC,
                triangle.VertexA,
                triangle.NeighborAcrossCA);
    }

    private static bool ValidateNeighbor(
        TerrainNodeElevationTopology topology,
        int triangleIndex,
        int edgeA,
        int edgeB,
        int neighborIndex)
    {
        int expectedReferenceCount =
            FindEdgeReferenceCount(topology, edgeA, edgeB);

        if (neighborIndex < 0)
        {
            return expectedReferenceCount == 1;
        }

        if (neighborIndex >= topology.TriangleCount || neighborIndex == triangleIndex)
        {
            return false;
        }

        TerrainNodeElevationTopologyTriangle neighbor =
            topology.Triangles[neighborIndex];

        return
            expectedReferenceCount == 2 &&
            TriangleContainsEdge(neighbor, edgeA, edgeB) &&
            TriangleReferencesNeighborAcrossEdge(
                neighbor,
                edgeA,
                edgeB,
                triangleIndex);
    }

    private static bool TriangleContainsEdge(
        TerrainNodeElevationTopologyTriangle triangle,
        int a,
        int b)
    {
        return
            HasPair(triangle.VertexA, triangle.VertexB, a, b) ||
            HasPair(triangle.VertexB, triangle.VertexC, a, b) ||
            HasPair(triangle.VertexC, triangle.VertexA, a, b);
    }

    private static bool TriangleReferencesNeighborAcrossEdge(
        TerrainNodeElevationTopologyTriangle triangle,
        int a,
        int b,
        int expectedNeighbor)
    {
        if (HasPair(triangle.VertexA, triangle.VertexB, a, b))
        {
            return triangle.NeighborAcrossAB == expectedNeighbor;
        }

        if (HasPair(triangle.VertexB, triangle.VertexC, a, b))
        {
            return triangle.NeighborAcrossBC == expectedNeighbor;
        }

        if (HasPair(triangle.VertexC, triangle.VertexA, a, b))
        {
            return triangle.NeighborAcrossCA == expectedNeighbor;
        }

        return false;
    }

    private static bool HasPair(int first, int second, int a, int b)
    {
        return
            (first == a && second == b) ||
            (first == b && second == a);
    }

    private static int FindEdgeReferenceCount(
        TerrainNodeElevationTopology topology,
        int a,
        int b)
    {
        int low = Math.Min(a, b);
        int high = Math.Max(a, b);

        for (int index = 0; index < topology.EdgeCount; index++)
        {
            TerrainNodeElevationTopologyEdge edge = topology.Edges[index];
            if (edge.VertexA == low && edge.VertexB == high)
            {
                return edge.TriangleReferenceCount;
            }
        }

        return 0;
    }

    private static bool ContainsEdge(
        IReadOnlyList<TerrainNodeElevationTopologyEdge> edges,
        int a,
        int b,
        int expectedReferenceCount)
    {
        int low = Math.Min(a, b);
        int high = Math.Max(a, b);

        for (int index = 0; index < edges.Count; index++)
        {
            TerrainNodeElevationTopologyEdge edge = edges[index];
            if (
                edge.VertexA == low &&
                edge.VertexB == high &&
                edge.TriangleReferenceCount == expectedReferenceCount)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(IReadOnlyList<int> values, int value)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static double SignedHullArea(
        TerrainNodeElevationTopology topology)
    {
        double twiceArea = 0.0;

        for (int index = 0; index < topology.HullVertexCount; index++)
        {
            Vector2 current =
                topology.Vertices[topology.HullVertexIndices[index]].PositionXZ;
            Vector2 next =
                topology.Vertices[
                    topology.HullVertexIndices[
                        (index + 1) % topology.HullVertexCount]].PositionXZ;

            twiceArea +=
                (double)current.x * next.y -
                (double)current.y * next.x;
        }

        return twiceArea * 0.5;
    }

    private static int FindVertexByPosition(
        TerrainNodeElevationTopology topology,
        Vector2 position)
    {
        for (int index = 0; index < topology.VertexCount; index++)
        {
            if (topology.Vertices[index].PositionXZ == position)
            {
                return index;
            }
        }

        return -1;
    }

    private static int LowestTriangleContainingVertex(
        TerrainNodeElevationTopology topology,
        int vertexIndex)
    {
        for (int index = 0; index < topology.TriangleCount; index++)
        {
            TerrainNodeElevationTopologyTriangle triangle = topology.Triangles[index];
            if (
                triangle.VertexA == vertexIndex ||
                triangle.VertexB == vertexIndex ||
                triangle.VertexC == vertexIndex)
            {
                return index;
            }
        }

        return -1;
    }

    private static string TopologyIndexKey(
        TerrainNodeElevationTopology topology)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append((int)topology.Kind);
        builder.Append('|');

        for (int index = 0; index < topology.TriangleCount; index++)
        {
            TerrainNodeElevationTopologyTriangle triangle = topology.Triangles[index];
            builder.Append(triangle.VertexA).Append(',')
                .Append(triangle.VertexB).Append(',')
                .Append(triangle.VertexC).Append(';');
        }

        builder.Append('|');

        for (int index = 0; index < topology.EdgeCount; index++)
        {
            TerrainNodeElevationTopologyEdge edge = topology.Edges[index];
            builder.Append(edge.VertexA).Append(',')
                .Append(edge.VertexB).Append(',')
                .Append(edge.TriangleReferenceCount).Append(';');
        }

        builder.Append('|');

        for (int index = 0; index < topology.HullVertexCount; index++)
        {
            builder.Append(topology.HullVertexIndices[index]).Append(',');
        }

        return builder.ToString();
    }

    private static string TopologyGeometryKey(
        TerrainNodeElevationTopology topology)
    {
        List<string> triangleKeys = new List<string>();

        for (int index = 0; index < topology.TriangleCount; index++)
        {
            TerrainNodeElevationTopologyTriangle triangle = topology.Triangles[index];
            string[] positions =
            {
                PositionKey(topology.Vertices[triangle.VertexA].PositionXZ),
                PositionKey(topology.Vertices[triangle.VertexB].PositionXZ),
                PositionKey(topology.Vertices[triangle.VertexC].PositionXZ)
            };

            Array.Sort(positions, StringComparer.Ordinal);
            triangleKeys.Add(positions[0] + "/" + positions[1] + "/" + positions[2]);
        }

        triangleKeys.Sort(StringComparer.Ordinal);
        return string.Join("|", triangleKeys.ToArray());
    }

    private static string PositionKey(Vector2 position)
    {
        return
            position.x.ToString("R", CultureInfo.InvariantCulture) + "," +
            position.y.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool SetPrivateStableId(
        TerrainElevationNode node,
        string stableId)
    {
        FieldInfo field =
            typeof(TerrainElevationNode).GetField(
                "stableId",
                BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null || node == null)
        {
            return false;
        }

        field.SetValue(node, stableId);
        return true;
    }

    private static string[] CaptureStableIds(TerrainNodeElevationSource source)
    {
        string[] result = new string[source.NodeCount];
        for (int index = 0; index < source.NodeCount; index++)
        {
            result[index] = source.Nodes[index].StableId;
        }

        return result;
    }

    private static Vector2[] CapturePositions(TerrainNodeElevationSource source)
    {
        Vector2[] result = new Vector2[source.NodeCount];
        for (int index = 0; index < source.NodeCount; index++)
        {
            result[index] = source.Nodes[index].PositionXZ;
        }

        return result;
    }

    private static float[] CaptureElevations(TerrainNodeElevationSource source)
    {
        float[] result = new float[source.NodeCount];
        for (int index = 0; index < source.NodeCount; index++)
        {
            result[index] = source.Nodes[index].Elevation;
        }

        return result;
    }

    private static bool ArraysEqual(string[] a, string[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        for (int index = 0; index < a.Length; index++)
        {
            if (!string.Equals(a[index], b[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ArraysEqual(Vector2[] a, Vector2[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        for (int index = 0; index < a.Length; index++)
        {
            if (a[index] != b[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool ArraysEqual(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        for (int index = 0; index < a.Length; index++)
        {
            if (a[index] != b[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateEquals(
        TerrainNodeElevationSource source,
        Vector2 sample,
        float expected,
        float tolerance)
    {
        return
            TerrainNodeElevationEvaluator.TryEvaluateHeight(
                source,
                sample,
                out float actual,
                out _) &&
            Mathf.Abs(actual - expected) <= tolerance;
    }

    private static bool SequenceEqual(
        IReadOnlyList<string> a,
        IReadOnlyList<string> b)
    {
        if (a == null || b == null || a.Count != b.Count)
        {
            return false;
        }

        for (int index = 0; index < a.Count; index++)
        {
            if (!string.Equals(a[index], b[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddResult(
        string name,
        ValidationOutcome outcome,
        string details)
    {
        results.Add(
            new ValidationResult
            {
                Name = name,
                Outcome = outcome,
                Details = details ?? ""
            });
    }

    private static void WriteReport()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("WorldMeshes Regional Elevation Triangulation Validation");
        builder.AppendLine("=========================================================");
        builder.AppendLine();

        int passed = 0;
        int failed = 0;
        int blocked = 0;

        for (int index = 0; index < results.Count; index++)
        {
            ValidationResult result = results[index];
            string label = result.Outcome.ToString().ToUpperInvariant();

            builder.AppendLine(label + " - " + result.Name);
            builder.AppendLine(
                "       " + result.Details.Replace("\n", "\n       "));
            builder.AppendLine();

            switch (result.Outcome)
            {
                case ValidationOutcome.Pass:
                    passed++;
                    break;

                case ValidationOutcome.Fail:
                    failed++;
                    break;

                default:
                    blocked++;
                    break;
            }
        }

        builder.AppendLine("---------------------------------------------------------");
        builder.AppendLine(passed + " passed");
        builder.AppendLine(failed + " failed");
        builder.AppendLine(blocked + " blocked");
        builder.AppendLine();
        builder.AppendLine(
            failed == 0 && blocked == 0
                ? "Regional elevation triangulation validation: PASSED"
                : failed > 0
                    ? "Regional elevation triangulation validation: FAILED"
                    : "Regional elevation triangulation validation: BLOCKED");

        Debug.Log(builder.ToString());
    }
}
