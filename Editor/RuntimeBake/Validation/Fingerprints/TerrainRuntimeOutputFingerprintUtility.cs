using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

/*
 * Explicit, read-only runtime output fingerprinting for Package 10.1.
 *
 * No importer settings, manifests, Addressables state, persistent bake state,
 * generated revisions, or scenes are mutated by this utility.
 */
public static class TerrainRuntimeOutputFingerprintUtility
{
    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)] public float FloatValue;
        [FieldOffset(0)] public int IntValue;
    }

    private sealed class HashWriter : IDisposable
    {
        private IncrementalHash hash;
        private readonly byte[] intBuffer = new byte[4];
        private bool finished;

        public HashWriter()
        {
            hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }

        public void WriteInt32(int value)
        {
            intBuffer[0] = (byte)value;
            intBuffer[1] = (byte)(value >> 8);
            intBuffer[2] = (byte)(value >> 16);
            intBuffer[3] = (byte)(value >> 24);
            hash.AppendData(intBuffer, 0, intBuffer.Length);
        }

        public void WriteFloat(float value)
        {
            FloatBits bits = new FloatBits
            {
                FloatValue = value
            };

            WriteInt32(bits.IntValue);
        }

        public void WriteString(string value)
        {
            string safeValue = value ?? "";
            byte[] bytes = Encoding.UTF8.GetBytes(safeValue);
            WriteInt32(bytes.Length);
            hash.AppendData(bytes);
        }

        public void WriteBytes(byte[] bytes)
        {
            byte[] safeBytes = bytes ?? new byte[0];
            WriteInt32(safeBytes.Length);
            hash.AppendData(safeBytes);
        }

        public string Finish()
        {
            if (finished)
            {
                return "";
            }

            finished = true;
            byte[] bytes = hash.GetHashAndReset();
            return ToHex(bytes);
        }

        public void Dispose()
        {
            if (hash != null)
            {
                hash.Dispose();
                hash = null;
            }
        }
    }

    public static TerrainRuntimeOutputFingerprintSnapshot CaptureCurrentSnapshot(
        WorldSettings worldSettings = null
    )
    {
        List<TerrainRuntimeOutputFingerprint> height =
            new List<TerrainRuntimeOutputFingerprint>();

        List<TerrainRuntimeOutputFingerprint> surface =
            new List<TerrainRuntimeOutputFingerprint>();

        List<TerrainRuntimeOutputFingerprint> collision =
            new List<TerrainRuntimeOutputFingerprint>();

        List<string> issues = new List<string>();
        bool cancelled = false;
        string errorMessage = "";

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            return CreateBlockedSnapshot(
                "Runtime output fingerprinting cannot run while the unified runtime bake pipeline is active."
            );
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            return CreateBlockedSnapshot(
                "Runtime output fingerprinting cannot run while independent surface generation is active."
            );
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return CreateBlockedSnapshot(
                "Runtime output fingerprinting must run outside Play Mode."
            );
        }

        if (worldSettings == null)
        {
            worldSettings = AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );
        }

        if (worldSettings == null)
        {
            return CreateBlockedSnapshot(
                "WorldSettings is unavailable."
            );
        }

        TerrainRuntimeBakeStateSnapshot startState =
            TerrainRuntimeBakeStateService.GetSnapshot();

        int heightRevision = worldSettings.heightmapGenerationRevision;
        int surfaceRevision = worldSettings.surfaceMaskGenerationRevision;
        int collisionRevision = worldSettings.collisionMeshGenerationRevision;

        TerrainHeightmapManifest heightManifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        TerrainSurfaceMaskManifest surfaceManifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        if (heightManifest == null)
        {
            issues.Add("Runtime Heightmap manifest is missing.");
        }
        else
        {
            if (!heightManifest.isComplete)
            {
                issues.Add("Runtime Heightmap manifest is incomplete.");
            }

            if (
                heightManifest.heightTileGridWidth != worldSettings.HeightTileGridWidth
                || heightManifest.heightTileGridHeight != worldSettings.HeightTileGridHeight
                || heightManifest.heightTileSamplesPerSide != worldSettings.HeightTileSamplesPerSide
            )
            {
                issues.Add("Runtime Heightmap manifest layout does not match current WorldSettings.");
            }
        }

        if (surfaceManifest == null)
        {
            issues.Add("Runtime Surface Mask manifest is missing.");
        }
        else
        {
            if (!surfaceManifest.isComplete)
            {
                issues.Add("Runtime Surface Mask manifest is incomplete.");
            }

            if (
                surfaceManifest.tileGridWidth != worldSettings.HeightTileGridWidth
                || surfaceManifest.tileGridHeight != worldSettings.HeightTileGridHeight
                || surfaceManifest.samplesPerSide != worldSettings.HeightTileSamplesPerSide
            )
            {
                issues.Add("Runtime Surface Mask manifest layout does not match current WorldSettings.");
            }
        }

        if (
            TerrainGenerationStateUtility.GetHeightmapStatus(worldSettings)
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            issues.Add("Runtime Heightmaps are not current.");
        }

        if (
            TerrainGenerationStateUtility.GetSurfaceMaskStatus(worldSettings)
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            issues.Add("Runtime Surface Masks are not current.");
        }

        if (
            TerrainGenerationStateUtility.GetCollisionMeshStatus(worldSettings)
            != TerrainGenerationStateUtility.GenerationStatus.Current
        )
        {
            issues.Add("Runtime Collision Meshes are not current.");
        }

        List<Vector2Int> heightCoordinates = CollectAllHeightTiles(worldSettings);
        List<Vector2Int> surfaceCoordinates = CollectAllHeightTiles(worldSettings);
        List<Vector2Int> collisionCoordinates = CollectAllCollisionChunks(worldSettings);

        int total =
            heightCoordinates.Count
            + surfaceCoordinates.Count
            + collisionCoordinates.Count;

        int completed = 0;

        try
        {
            for (int index = 0; index < heightCoordinates.Count; index++)
            {
                Vector2Int coordinate = heightCoordinates[index];

                if (ShowProgress(
                    "Fingerprinting Runtime Heightmaps",
                    coordinate,
                    completed,
                    total
                ))
                {
                    cancelled = true;
                    break;
                }

                completed++;

                if (
                    TryFingerprintHeight(
                        coordinate,
                        worldSettings,
                        heightManifest,
                        out TerrainRuntimeOutputFingerprint fingerprint,
                        out string issue
                    )
                )
                {
                    height.Add(fingerprint);
                }
                else
                {
                    issues.Add(issue);
                }
            }

            if (!cancelled)
            {
                for (int index = 0; index < surfaceCoordinates.Count; index++)
                {
                    Vector2Int coordinate = surfaceCoordinates[index];

                    if (ShowProgress(
                        "Fingerprinting Runtime Surface Masks",
                        coordinate,
                        completed,
                        total
                    ))
                    {
                        cancelled = true;
                        break;
                    }

                    completed++;

                    if (
                        TryFingerprintSurface(
                            coordinate,
                            worldSettings,
                            surfaceManifest,
                            out TerrainRuntimeOutputFingerprint fingerprint,
                            out string issue
                        )
                    )
                    {
                        surface.Add(fingerprint);
                    }
                    else
                    {
                        issues.Add(issue);
                    }
                }
            }

            if (!cancelled)
            {
                for (int index = 0; index < collisionCoordinates.Count; index++)
                {
                    Vector2Int coordinate = collisionCoordinates[index];

                    if (ShowProgress(
                        "Fingerprinting Runtime Collision Meshes",
                        coordinate,
                        completed,
                        total
                    ))
                    {
                        cancelled = true;
                        break;
                    }

                    completed++;

                    if (
                        TryFingerprintCollision(
                            coordinate,
                            out TerrainRuntimeOutputFingerprint fingerprint,
                            out string issue
                        )
                    )
                    {
                        collision.Add(fingerprint);
                    }
                    else
                    {
                        issues.Add(issue);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                "Unexpected exception while capturing runtime output fingerprints: " +
                exception.Message;

            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        TerrainRuntimeBakeStateSnapshot endState =
            TerrainRuntimeBakeStateService.GetSnapshot();

        bool targetStable =
            startState.StateRevision == endState.StateRevision
            && worldSettings.heightmapGenerationRevision == heightRevision
            && worldSettings.surfaceMaskGenerationRevision == surfaceRevision
            && worldSettings.collisionMeshGenerationRevision == collisionRevision;

        if (!targetStable)
        {
            issues.Add(
                "Runtime bake state or generated revisions changed during fingerprint capture; the snapshot is not a stable baseline."
            );
        }

        SortFingerprints(height);
        SortFingerprints(surface);
        SortFingerprints(collision);

        string heightDatasetHash = ComputeDatasetHash(height);
        string surfaceDatasetHash = ComputeDatasetHash(surface);
        string collisionDatasetHash = ComputeDatasetHash(collision);

        bool complete =
            !cancelled
            && string.IsNullOrEmpty(errorMessage)
            && targetStable
            && issues.Count == 0
            && height.Count == heightCoordinates.Count
            && surface.Count == surfaceCoordinates.Count
            && collision.Count == collisionCoordinates.Count;

        return new TerrainRuntimeOutputFingerprintSnapshot(
            height,
            surface,
            collision,
            heightCoordinates.Count,
            surfaceCoordinates.Count,
            collisionCoordinates.Count,
            heightDatasetHash,
            surfaceDatasetHash,
            collisionDatasetHash,
            complete,
            cancelled,
            startState.StateRevision,
            issues,
            errorMessage
        );
    }

    public static TerrainRuntimeOutputFingerprintComparison Compare(
        TerrainRuntimeOutputFingerprintSnapshot baseline,
        TerrainRuntimeOutputFingerprintSnapshot current
    )
    {
        return new TerrainRuntimeOutputFingerprintComparison(
            baseline,
            current,
            CompareStream(
                "Heightmaps",
                baseline != null ? baseline.HeightFingerprints : null,
                current != null ? current.HeightFingerprints : null
            ),
            CompareStream(
                "Surface Masks",
                baseline != null ? baseline.SurfaceFingerprints : null,
                current != null ? current.SurfaceFingerprints : null
            ),
            CompareStream(
                "Collision Meshes",
                baseline != null ? baseline.CollisionFingerprints : null,
                current != null ? current.CollisionFingerprints : null
            )
        );
    }

    private static TerrainRuntimeOutputFingerprintStreamComparison CompareStream(
        string name,
        IReadOnlyList<TerrainRuntimeOutputFingerprint> baseline,
        IReadOnlyList<TerrainRuntimeOutputFingerprint> current
    )
    {
        Dictionary<Vector2Int, TerrainRuntimeOutputFingerprint> baselineByCoordinate =
            BuildFingerprintDictionary(baseline);

        Dictionary<Vector2Int, TerrainRuntimeOutputFingerprint> currentByCoordinate =
            BuildFingerprintDictionary(current);

        List<Vector2Int> matching = new List<Vector2Int>();
        List<Vector2Int> missing = new List<Vector2Int>();
        List<Vector2Int> unexpected = new List<Vector2Int>();
        List<Vector2Int> mismatched = new List<Vector2Int>();

        foreach (
            KeyValuePair<Vector2Int, TerrainRuntimeOutputFingerprint> pair
            in baselineByCoordinate
        )
        {
            if (
                !currentByCoordinate.TryGetValue(
                    pair.Key,
                    out TerrainRuntimeOutputFingerprint currentFingerprint
                )
            )
            {
                missing.Add(pair.Key);
                continue;
            }

            if (
                string.Equals(
                    pair.Value.PayloadHash,
                    currentFingerprint.PayloadHash,
                    StringComparison.Ordinal
                )
            )
            {
                matching.Add(pair.Key);
            }
            else
            {
                mismatched.Add(pair.Key);
            }
        }

        foreach (Vector2Int coordinate in currentByCoordinate.Keys)
        {
            if (!baselineByCoordinate.ContainsKey(coordinate))
            {
                unexpected.Add(coordinate);
            }
        }

        return new TerrainRuntimeOutputFingerprintStreamComparison(
            name,
            matching,
            missing,
            unexpected,
            mismatched
        );
    }

    private static bool TryFingerprintHeight(
        Vector2Int coordinate,
        WorldSettings worldSettings,
        TerrainHeightmapManifest manifest,
        out TerrainRuntimeOutputFingerprint fingerprint,
        out string issue
    )
    {
        int expectedSamples =
            manifest != null && manifest.heightTileSamplesPerSide > 0
                ? manifest.heightTileSamplesPerSide
                : worldSettings.HeightTileSamplesPerSide;

        return TryFingerprintTexture(
            TerrainRuntimeOutputFingerprintKind.Heightmap,
            coordinate,
            TerrainRuntimeHeightAssetUtility.GetHeightTilePath(
                coordinate.x,
                coordinate.y
            ),
            TextureFormat.RFloat,
            expectedSamples,
            out fingerprint,
            out issue
        );
    }

    private static bool TryFingerprintSurface(
        Vector2Int coordinate,
        WorldSettings worldSettings,
        TerrainSurfaceMaskManifest manifest,
        out TerrainRuntimeOutputFingerprint fingerprint,
        out string issue
    )
    {
        int expectedSamples =
            manifest != null && manifest.samplesPerSide > 0
                ? manifest.samplesPerSide
                : worldSettings.HeightTileSamplesPerSide;

        return TryFingerprintTexture(
            TerrainRuntimeOutputFingerprintKind.SurfaceMask,
            coordinate,
            TerrainRuntimeSurfaceMaskAssetUtility.GetSurfaceTilePath(
                coordinate.x,
                coordinate.y
            ),
            TextureFormat.R8,
            expectedSamples,
            out fingerprint,
            out issue
        );
    }

    private static bool TryFingerprintTexture(
        TerrainRuntimeOutputFingerprintKind kind,
        Vector2Int coordinate,
        string assetPath,
        TextureFormat expectedFormat,
        int expectedSamplesPerSide,
        out TerrainRuntimeOutputFingerprint fingerprint,
        out string issue
    )
    {
        fingerprint = null;
        issue = "";

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

        if (texture == null)
        {
            issue =
                kind + " asset is missing at (" + coordinate.x + ", " + coordinate.y + "): " +
                assetPath;
            return false;
        }

        if (
            texture.width != expectedSamplesPerSide
            || texture.height != expectedSamplesPerSide
            || texture.format != expectedFormat
            || !texture.isReadable
        )
        {
            issue =
                kind + " asset has unsupported/invalid fingerprint layout at (" +
                coordinate.x + ", " + coordinate.y + "): " + assetPath +
                " [" + texture.width + "x" + texture.height + ", " +
                texture.format + ", readable=" + texture.isReadable + "]";
            return false;
        }

        try
        {
            NativeArray<byte> rawData = texture.GetRawTextureData<byte>();
            byte[] payload = rawData.ToArray();

            string payloadHash;

            using (HashWriter writer = new HashWriter())
            {
                writer.WriteInt32((int)kind);
                writer.WriteInt32(coordinate.x);
                writer.WriteInt32(coordinate.y);
                writer.WriteInt32(texture.width);
                writer.WriteInt32(texture.height);
                writer.WriteInt32((int)texture.format);
                writer.WriteInt32(texture.mipmapCount);
                writer.WriteBytes(payload);
                payloadHash = writer.Finish();
            }

            fingerprint = new TerrainRuntimeOutputFingerprint(
                kind,
                coordinate,
                assetPath,
                AssetDatabase.AssetPathToGUID(assetPath),
                payloadHash,
                texture.width,
                texture.height,
                texture.format.ToString(),
                0,
                0,
                0
            );

            return true;
        }
        catch (Exception exception)
        {
            issue =
                "Could not fingerprint " + kind + " asset at (" +
                coordinate.x + ", " + coordinate.y + "): " +
                exception.Message;
            return false;
        }
    }

    private static bool TryFingerprintCollision(
        Vector2Int coordinate,
        out TerrainRuntimeOutputFingerprint fingerprint,
        out string issue
    )
    {
        fingerprint = null;
        issue = "";

        string assetPath = TerrainCollisionMeshGenerator.GetCollisionMeshPath(
            coordinate.x,
            coordinate.y
        );

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

        if (mesh == null)
        {
            issue =
                "Collision Mesh asset is missing at (" +
                coordinate.x + ", " + coordinate.y + "): " + assetPath;
            return false;
        }

        try
        {
            Vector3[] vertices = mesh.vertices;
            int totalIndexCount = 0;
            string payloadHash;

            using (HashWriter writer = new HashWriter())
            {
                writer.WriteInt32((int)TerrainRuntimeOutputFingerprintKind.CollisionMesh);
                writer.WriteInt32(coordinate.x);
                writer.WriteInt32(coordinate.y);
                writer.WriteInt32(vertices.Length);

                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 vertex = vertices[index];
                    writer.WriteFloat(vertex.x);
                    writer.WriteFloat(vertex.y);
                    writer.WriteFloat(vertex.z);
                }

                writer.WriteInt32(mesh.subMeshCount);

                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    MeshTopology topology = mesh.GetTopology(subMesh);
                    int[] indices = mesh.GetIndices(subMesh);
                    totalIndexCount += indices.Length;

                    writer.WriteInt32(subMesh);
                    writer.WriteInt32((int)topology);
                    writer.WriteInt32(indices.Length);

                    for (int index = 0; index < indices.Length; index++)
                    {
                        writer.WriteInt32(indices[index]);
                    }
                }

                payloadHash = writer.Finish();
            }

            fingerprint = new TerrainRuntimeOutputFingerprint(
                TerrainRuntimeOutputFingerprintKind.CollisionMesh,
                coordinate,
                assetPath,
                AssetDatabase.AssetPathToGUID(assetPath),
                payloadHash,
                0,
                0,
                "Mesh",
                vertices.Length,
                mesh.subMeshCount,
                totalIndexCount
            );

            return true;
        }
        catch (Exception exception)
        {
            issue =
                "Could not fingerprint Collision Mesh at (" +
                coordinate.x + ", " + coordinate.y + "): " +
                exception.Message;
            return false;
        }
    }

    private static string ComputeDatasetHash(
        IReadOnlyList<TerrainRuntimeOutputFingerprint> fingerprints
    )
    {
        if (fingerprints == null || fingerprints.Count == 0)
        {
            return "";
        }

        using (HashWriter writer = new HashWriter())
        {
            writer.WriteInt32(fingerprints.Count);

            for (int index = 0; index < fingerprints.Count; index++)
            {
                TerrainRuntimeOutputFingerprint fingerprint = fingerprints[index];
                writer.WriteInt32((int)fingerprint.Kind);
                writer.WriteInt32(fingerprint.Coordinate.x);
                writer.WriteInt32(fingerprint.Coordinate.y);
                writer.WriteString(fingerprint.PayloadHash);
            }

            return writer.Finish();
        }
    }

    private static Dictionary<Vector2Int, TerrainRuntimeOutputFingerprint>
        BuildFingerprintDictionary(
            IReadOnlyList<TerrainRuntimeOutputFingerprint> source
        )
    {
        Dictionary<Vector2Int, TerrainRuntimeOutputFingerprint> result =
            new Dictionary<Vector2Int, TerrainRuntimeOutputFingerprint>();

        if (source == null)
        {
            return result;
        }

        for (int index = 0; index < source.Count; index++)
        {
            TerrainRuntimeOutputFingerprint fingerprint = source[index];

            if (fingerprint != null)
            {
                result[fingerprint.Coordinate] = fingerprint;
            }
        }

        return result;
    }

    private static List<Vector2Int> CollectAllHeightTiles(
        WorldSettings worldSettings
    )
    {
        HashSet<Vector2Int> coordinates = new HashSet<Vector2Int>();
        TerrainRuntimeBakeDependencyUtility.CollectAllHeightTiles(
            worldSettings,
            coordinates
        );
        return SortCoordinates(coordinates);
    }

    private static List<Vector2Int> CollectAllCollisionChunks(
        WorldSettings worldSettings
    )
    {
        HashSet<Vector2Int> coordinates = new HashSet<Vector2Int>();
        TerrainRuntimeBakeDependencyUtility.CollectAllCollisionChunks(
            worldSettings,
            coordinates
        );
        return SortCoordinates(coordinates);
    }

    private static List<Vector2Int> SortCoordinates(
        IEnumerable<Vector2Int> source
    )
    {
        List<Vector2Int> result =
            new List<Vector2Int>(
                source != null
                    ? new HashSet<Vector2Int>(source)
                    : new HashSet<Vector2Int>()
            );

        result.Sort(TerrainRuntimeBakeStageValidationResult.CompareCoordinates);
        return result;
    }

    private static void SortFingerprints(
        List<TerrainRuntimeOutputFingerprint> fingerprints
    )
    {
        fingerprints.Sort(
            (left, right) =>
                TerrainRuntimeBakeStageValidationResult.CompareCoordinates(
                    left.Coordinate,
                    right.Coordinate
                )
        );
    }

    private static bool ShowProgress(
        string operation,
        Vector2Int coordinate,
        int completed,
        int total
    )
    {
        float progress =
            total > 0
                ? (float)completed / total
                : 1f;

        return EditorUtility.DisplayCancelableProgressBar(
            "WorldMeshes Runtime Output Fingerprints",
            operation + "\n(" + coordinate.x + ", " + coordinate.y + ")",
            progress
        );
    }

    private static TerrainRuntimeOutputFingerprintSnapshot CreateBlockedSnapshot(
        string errorMessage
    )
    {
        return new TerrainRuntimeOutputFingerprintSnapshot(
            null,
            null,
            null,
            0,
            0,
            0,
            "",
            "",
            "",
            false,
            false,
            TerrainRuntimeBakeStateService.GetSnapshot().StateRevision,
            null,
            errorMessage
        );
    }

    private static string ToHex(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return "";
        }

        char[] chars = new char[bytes.Length * 2];
        const string Hex = "0123456789abcdef";

        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            chars[index * 2] = Hex[value >> 4];
            chars[index * 2 + 1] = Hex[value & 0x0F];
        }

        return new string(chars);
    }
}
