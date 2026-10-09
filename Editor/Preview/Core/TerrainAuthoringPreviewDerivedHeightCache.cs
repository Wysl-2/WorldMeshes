using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

// Disposable, project-local committed data. There is no full-world index,
// asset import, modifier state, or GPU residency owner in this cache.
internal static class TerrainAuthoringPreviewDerivedHeightCache
{
    private const uint Magic = 0x48444D57;
    private const int SchemaVersion = 1;
    private const int IdentityBytes = 32;
    internal const int HeaderBytes = 36 + IdentityBytes * 2;

    internal enum ReadResult { Missing, Hit, Rejected, Failed }

    internal readonly struct EntryIdentity
    {
        internal readonly string Namespace;
        internal readonly string Source;
        internal readonly Vector2Int Tile;
        internal readonly int NativeSamples;
        internal readonly int Stride;
        internal readonly int Samples;
        internal readonly bool Persistable;

        internal EntryIdentity(string space, string source, Vector2Int tile,
            int nativeSamples, int stride, bool persistable = true)
        {
            Namespace = space;
            Source = source;
            Tile = tile;
            NativeSamples = nativeSamples;
            Stride = stride;
            Samples = stride > 0 && nativeSamples > 1 ? (nativeSamples - 1) / stride + 1 : 0;
            Persistable = persistable;
        }

        internal bool IsValid => IsDigest(Namespace) && IsDigest(Source)
            && Tile.x >= 0 && Tile.y >= 0 && NativeSamples > 1
            && TerrainHeightResolutionUtility.IsPowerOfTwo(Stride)
            && (NativeSamples - 1) % Stride == 0 && Samples > 1;

        internal bool Matches(EntryIdentity other) => IsValid && other.IsValid
            && Namespace == other.Namespace && Source == other.Source && Tile == other.Tile
            && NativeSamples == other.NativeSamples && Stride == other.Stride;
    }

    internal static string RootPath => Path.Combine(
        Directory.GetParent(Application.dataPath).FullName,
        "Library", "WorldMeshes", "EditorPreviewHeight");

    internal static bool TryCaptureIdentity(WorldSettings settings, Vector2Int tile, int stride,
        out EntryIdentity identity, out string error)
    {
        identity = default;
        error = "";
        if (!TerrainHeightResolutionUtility.IsRepresentationStrideCompatible(settings, stride)
            || tile.x < 0 || tile.y < 0 || tile.x >= settings.HeightTileGridWidth
            || tile.y >= settings.HeightTileGridHeight)
        {
            error = "The committed Height tile or representation stride is incompatible.";
            return false;
        }
        var manifest = TerrainAuthoringStateUtility.LoadAuthoringHeightManifest();
        if (manifest == null || !manifest.isComplete
            || manifest.manifestVersion != TerrainAuthoringHeightManifest.CurrentVersion
            || manifest.committedHeightRevision <= 0 || string.IsNullOrEmpty(manifest.committedContentHash)
            || !TerrainAuthoringStateUtility.ManifestMatchesWorldSettings(manifest, settings))
        {
            error = "The committed Height manifest is incomplete or incompatible.";
            return false;
        }

        string settingsPath = AssetDatabase.GetAssetPath(settings);
        string datasetPath = AssetDatabase.GetAssetPath(manifest);
        string sourcePath = TerrainAuthoringStateUtility.GetAuthoringHeightTilePath(tile.x, tile.y);
        string worldGuid = AssetDatabase.AssetPathToGUID(settingsPath);
        string datasetGuid = AssetDatabase.AssetPathToGUID(datasetPath);
        string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
        if (string.IsNullOrEmpty(sourceGuid))
        {
            error = "The committed Height source asset is missing: " + sourcePath;
            return false;
        }

        // Unsaved committed writes must never be encoded as current persisted
        // data. Inspect an already loaded tile without loading an unloaded tile.
        Texture2D loaded = AssetDatabase.IsMainAssetAtPathLoaded(sourcePath)
            ? AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath) : null;
        bool dirty = loaded != null && EditorUtility.IsDirty(loaded);
        bool persistable = !dirty && !EditorUtility.IsDirty(manifest)
            && !string.IsNullOrEmpty(worldGuid) && !string.IsNullOrEmpty(datasetGuid);
        string topology = string.Join("|",
            settings.gridWidth, settings.gridHeight,
            settings.chunkSize.ToString("R", CultureInfo.InvariantCulture),
            settings.heightfieldResolutionPerChunk, settings.heightTileChunkSpan,
            settings.HeightTileGridWidth, settings.HeightTileGridHeight,
            settings.HeightTileSamplesPerSide,
            settings.HeightTileWorldSize.ToString("R", CultureInfo.InvariantCulture));
        string space = Hash128.Compute("CommittedHeightSource|" + SchemaVersion + "|"
            + (string.IsNullOrEmpty(worldGuid) ? settings.GetInstanceID().ToString(CultureInfo.InvariantCulture) : worldGuid)
            + "|" + datasetGuid + "|" + topology).ToString();

        var file = new FileInfo(Path.Combine(Directory.GetParent(Application.dataPath).FullName, sourcePath));
        if (!file.Exists)
        {
            error = "The committed Height source file is missing: " + sourcePath;
            return false;
        }
        string fingerprint = Hash128.Compute(sourcePath + "|" + sourceGuid + "|"
            + AssetDatabase.GetAssetDependencyHash(sourcePath) + "|"
            + file.Length.ToString(CultureInfo.InvariantCulture) + "|"
            + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|"
            + (dirty ? EditorUtility.GetDirtyCount(loaded) : 0)).ToString();
        identity = new EntryIdentity(space, fingerprint, tile, settings.HeightTileSamplesPerSide, stride, persistable);
        return identity.IsValid;
    }

    internal static string GetEntryPath(string root, EntryIdentity identity)
    {
        if (!identity.IsValid) throw new ArgumentException("The derived Height cache identity is invalid.");
        return Path.Combine(root, identity.Namespace,
            "tile_" + identity.Tile.x.ToString(CultureInfo.InvariantCulture) + "_" + identity.Tile.y.ToString(CultureInfo.InvariantCulture),
            "stride_" + identity.Stride.ToString(CultureInfo.InvariantCulture) + ".height");
    }

    internal static ReadResult TryRead(string root, EntryIdentity identity, out byte[] payload, out string error)
    {
        payload = null;
        error = "";
        if (!identity.IsValid || !identity.Persistable || identity.Stride == 1 || !BitConverter.IsLittleEndian)
            return ReadResult.Missing;
        try
        {
            using (WorldMeshesProfiler.PreviewDerivedHeightRead.Auto())
            using (var stream = new FileStream(GetEntryPath(root, identity), FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                int bytes = checked(identity.Samples * identity.Samples * sizeof(float));
                if (stream.Length != HeaderBytes + (long)bytes) return ReadResult.Rejected;
                if (reader.ReadUInt32() != Magic || reader.ReadInt32() != SchemaVersion
                    || reader.ReadInt32() != identity.Tile.x || reader.ReadInt32() != identity.Tile.y
                    || reader.ReadInt32() != identity.Stride || reader.ReadInt32() != identity.NativeSamples
                    || reader.ReadInt32() != identity.Samples || reader.ReadInt32() != bytes)
                    return ReadResult.Rejected;
                uint checksum = reader.ReadUInt32();
                if (Encoding.ASCII.GetString(reader.ReadBytes(IdentityBytes)) != identity.Namespace
                    || Encoding.ASCII.GetString(reader.ReadBytes(IdentityBytes)) != identity.Source)
                    return ReadResult.Rejected;
                byte[] data = reader.ReadBytes(bytes);
                if (data.Length != bytes || Checksum(data) != checksum) return ReadResult.Rejected;
                for (int i = 0; i < bytes; i += sizeof(float))
                    if (!IsFinite(BitConverter.ToSingle(data, i))) return ReadResult.Rejected;
                payload = data;
                return ReadResult.Hit;
            }
        }
        catch (FileNotFoundException) { return ReadResult.Missing; }
        catch (DirectoryNotFoundException) { return ReadResult.Missing; }
        catch (EndOfStreamException) { return ReadResult.Rejected; }
        catch (Exception exception)
        {
            error = exception.Message;
            return ReadResult.Failed;
        }
    }

    internal static bool TryExtract(Texture2D native, int nativeSamples, int stride,
        out byte[] payload, out string error)
    {
        payload = null;
        error = "";
        if (!BitConverter.IsLittleEndian
            || !TerrainAuthoringPreviewHeightSourceUtility.TryValidateNativeSource(native, nativeSamples, out error)
            || !TerrainHeightResolutionUtility.IsPowerOfTwo(stride) || stride <= 1
            || (nativeSamples - 1) % stride != 0)
        {
            if (string.IsNullOrEmpty(error)) error = "The derived Height lattice or byte order is unsupported.";
            return false;
        }
        try
        {
            using (WorldMeshesProfiler.PreviewDerivedHeightGenerate.Auto())
            {
                NativeArray<float> source = native.GetPixelData<float>(0);
                if (source.Length != checked(nativeSamples * nativeSamples))
                    throw new InvalidDataException("The committed Height pixel count is invalid.");
                int size = (nativeSamples - 1) / stride + 1;
                var samples = new float[checked(size * size)];
                for (int z = 0; z < size; z++)
                    for (int x = 0; x < size; x++)
                    {
                        float value = source[x * stride + z * stride * nativeSamples];
                        if (!IsFinite(value)) throw new InvalidDataException("The committed Height lattice contains a non-finite sample.");
                        samples[x + z * size] = value;
                    }
                payload = new byte[checked(samples.Length * sizeof(float))];
                Buffer.BlockCopy(samples, 0, payload, 0, payload.Length);
                // The NativeArray is a borrowed texture view, never disposed.
            }
            return true;
        }
        catch (Exception exception) { error = exception.Message; return false; }
    }

    internal static bool TryWrite(string root, EntryIdentity identity, byte[] payload,
        Func<bool> sourceStillCurrent, out string error)
    {
        error = "";
        string temporary = null;
        try
        {
            if (!identity.IsValid || !identity.Persistable || identity.Stride <= 1
                || !BitConverter.IsLittleEndian || payload == null
                || payload.Length != checked(identity.Samples * identity.Samples * sizeof(float)))
                throw new InvalidDataException("The derived Height entry is incompatible.");
            using (WorldMeshesProfiler.PreviewDerivedHeightWrite.Auto())
            {
                string path = GetEntryPath(root, identity);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                    {
                        writer.Write(Magic);
                        writer.Write(SchemaVersion);
                        writer.Write(identity.Tile.x);
                        writer.Write(identity.Tile.y);
                        writer.Write(identity.Stride);
                        writer.Write(identity.NativeSamples);
                        writer.Write(identity.Samples);
                        writer.Write(payload.Length);
                        writer.Write(Checksum(payload));
                        writer.Write(Encoding.ASCII.GetBytes(identity.Namespace));
                        writer.Write(Encoding.ASCII.GetBytes(identity.Source));
                        writer.Write(payload);
                        writer.Flush();
                    }
                    stream.Flush(true);
                }
                if (sourceStillCurrent == null || !sourceStillCurrent())
                    throw new InvalidOperationException("The committed Height source changed during derived generation.");
                // Never delete a valid destination before the atomic operation.
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                temporary = null;
            }
            return true;
        }
        catch (Exception exception) { error = exception.Message; return false; }
        finally
        {
            if (temporary != null)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static bool TryCreateTexture(byte[] payload, int samples, out Texture2D texture, out string error)
    {
        texture = null;
        error = "";
        try
        {
            if (!BitConverter.IsLittleEndian || samples <= 1 || payload == null
                || payload.Length != checked(samples * samples * sizeof(float)))
                throw new InvalidDataException("The derived Height texture payload is invalid.");
            texture = new Texture2D(samples, samples, TextureFormat.RFloat, false, true)
            {
                name = "Committed Derived Height Source",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(payload);
            texture.Apply(false, true);
            if (texture.width != samples || texture.height != samples || texture.format != TextureFormat.RFloat)
                throw new InvalidOperationException("The derived Height texture format is unavailable.");
            return true;
        }
        catch (Exception exception)
        {
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
            error = exception.Message;
            return false;
        }
    }

    private static bool IsDigest(string value)
    {
        if (value == null || value.Length != IdentityBytes) return false;
        foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static uint Checksum(byte[] bytes)
    {
        uint value = 2166136261;
        unchecked { foreach (byte b in bytes) value = (value ^ b) * 16777619; }
        return value;
    }
}
