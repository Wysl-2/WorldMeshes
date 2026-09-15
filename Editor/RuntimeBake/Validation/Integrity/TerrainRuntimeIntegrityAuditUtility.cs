using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class TerrainRuntimeGeneratedDataIntegrityResult
{
    public string DatasetName { get; internal set; }
    public bool ManifestPresent { get; internal set; }
    public bool ManifestComplete { get; internal set; }
    public bool ManifestCompatible { get; internal set; }
    public int ExpectedAssetCount { get; internal set; }
    public int PresentAssetCount { get; internal set; }
    public List<Vector2Int> MissingCoordinates { get; internal set; } = new List<Vector2Int>();
    public List<string> MissingAssetPaths { get; internal set; } = new List<string>();
    public List<string> WrongAssetTypes { get; internal set; } = new List<string>();
    public List<string> Errors { get; internal set; } = new List<string>();

    public bool IsValid =>
        ManifestPresent
        && ManifestComplete
        && ManifestCompatible
        && ExpectedAssetCount > 0
        && PresentAssetCount == ExpectedAssetCount
        && MissingCoordinates.Count == 0
        && MissingAssetPaths.Count == 0
        && WrongAssetTypes.Count == 0
        && Errors.Count == 0;
}

public sealed class TerrainRuntimeIntegrityAuditResult
{
    public TerrainRuntimeGeneratedDataIntegrityResult Height { get; internal set; }
    public TerrainRuntimeGeneratedDataIntegrityResult Surface { get; internal set; }
    public TerrainRuntimeGeneratedDataIntegrityResult Collision { get; internal set; }
    public TerrainRuntimeAddressablesValidationResult Addressables { get; internal set; }

    public bool GeneratedDataValid =>
        Height != null && Height.IsValid
        && Surface != null && Surface.IsValid
        && Collision != null && Collision.IsValid;

    public bool AddressablesValid =>
        Addressables != null && Addressables.IsValid;

    public bool IsValid =>
        GeneratedDataValid && AddressablesValid;
}

[InitializeOnLoad]
public static class TerrainRuntimeIntegrityAuditUtility
{
    private static TerrainRuntimeIntegrityAuditResult cachedGeneratedAudit;
    private static TerrainRuntimeAddressablesValidationResult cachedAddressablesValidation;
    private static string cachedGeneratedAuditKey = "";
    private static string cachedAddressablesValidationKey = "";

    static TerrainRuntimeIntegrityAuditUtility()
    {
        EditorApplication.projectChanged += InvalidateCachedAudit;
    }

    public static void InvalidateCachedAudit()
    {
        cachedGeneratedAudit = null;
        cachedAddressablesValidation = null;
        cachedGeneratedAuditKey = "";
        cachedAddressablesValidationKey = "";
    }

    public static TerrainRuntimeIntegrityAuditResult GetCachedGeneratedDataAudit(
        WorldSettings worldSettings
    )
    {
        string key = BuildKey(worldSettings);

        if (
            cachedGeneratedAudit == null
            || cachedGeneratedAuditKey != key
        )
        {
            using (WorldMeshesProfiler.ValidationInspectOutputs.Auto())
            {
                cachedGeneratedAudit =
                    new TerrainRuntimeIntegrityAuditResult
                    {
                        Height = ValidateHeight(worldSettings),
                        Surface = ValidateSurface(worldSettings),
                        Collision = ValidateCollision(worldSettings)
                    };
            }

            cachedGeneratedAuditKey = key;
        }

        return cachedGeneratedAudit;
    }

    public static bool TryGetCachedAddressablesValidation(
        WorldSettings worldSettings,
        out TerrainRuntimeAddressablesValidationResult validation
    )
    {
        string key = BuildKey(worldSettings);

        if (
            cachedAddressablesValidation != null
            &&
            cachedAddressablesValidationKey == key
        )
        {
            validation =
                cachedAddressablesValidation;

            return true;
        }

        validation = null;
        return false;
    }

    public static TerrainRuntimeAddressablesValidationResult GetCachedAddressablesValidation(
        WorldSettings worldSettings
    )
    {
        string key = BuildKey(worldSettings);

        if (
            cachedAddressablesValidation == null
            ||
            cachedAddressablesValidationKey != key
        )
        {
            cachedAddressablesValidation =
                TerrainRuntimeAddressablesUtility
                    .ValidateExistingRuntimeConfiguration(
                        worldSettings
                    );

            cachedAddressablesValidationKey =
                key;
        }

        return cachedAddressablesValidation;
    }

    public static TerrainRuntimeIntegrityAuditResult GetCachedAudit(
        WorldSettings worldSettings
    )
    {
        TerrainRuntimeIntegrityAuditResult result =
            GetCachedGeneratedDataAudit(worldSettings);

        result.Addressables =
            GetCachedAddressablesValidation(worldSettings);

        return result;
    }

    public static TerrainRuntimeIntegrityAuditResult ForceFreshAudit(
        WorldSettings worldSettings
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.ValidationRun.Auto();

        InvalidateCachedAudit();
        return GetCachedAudit(worldSettings);
    }

    private static TerrainRuntimeGeneratedDataIntegrityResult ValidateHeight(
        WorldSettings worldSettings
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.ValidationInspectOutputsHeight.Auto();

        TerrainRuntimeGeneratedDataIntegrityResult result =
            NewResult("Height");

        if (worldSettings == null)
        {
            result.Errors.Add("WorldSettings is unavailable.");
            return result;
        }

        TerrainHeightmapManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        result.ManifestPresent = manifest != null;
        result.ManifestComplete =
            manifest != null
            && manifest.isComplete
            && manifest.HasCompleteTileHeightRanges
            && manifest.HasValidHeightRange;

        int width = Mathf.Max(1, worldSettings.HeightTileGridWidth);
        int height = Mathf.Max(1, worldSettings.HeightTileGridHeight);

        result.ManifestCompatible =
            manifest != null
            && manifest.compilerVersion ==
                TerrainGenerationStateUtility.RuntimeHeightCompilerVersion
            && manifest.gridWidth == Mathf.Max(1, worldSettings.gridWidth)
            && manifest.gridHeight == Mathf.Max(1, worldSettings.gridHeight)
            && manifest.heightTileChunkSpan == Mathf.Max(1, worldSettings.heightTileChunkSpan)
            && manifest.heightTileGridWidth == width
            && manifest.heightTileGridHeight == height
            && manifest.heightfieldResolutionPerChunk ==
                Mathf.Max(1, worldSettings.heightfieldResolutionPerChunk)
            && Mathf.Approximately(
                manifest.chunkSize,
                Mathf.Max(0.01f, worldSettings.chunkSize)
            )
            && manifest.heightTileSamplesPerSide ==
                worldSettings.HeightTileSamplesPerSide
            && Mathf.Approximately(
                manifest.heightTileWorldSize,
                worldSettings.HeightTileWorldSize
            );

        result.ExpectedAssetCount = width * height;

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                string path =
                    TerrainRuntimeHeightAssetUtility
                        .GetHeightTilePath(x, z);

                Texture2D texture =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (texture != null)
                {
                    result.PresentAssetCount++;
                    continue;
                }

                result.MissingCoordinates.Add(new Vector2Int(x, z));

                Object main =
                    AssetDatabase.LoadMainAssetAtPath(path);

                if (main == null)
                {
                    result.MissingAssetPaths.Add(path);
                }
                else
                {
                    result.WrongAssetTypes.Add(
                        path + " (" + main.GetType().Name + ")"
                    );
                }
            }
        }

        return result;
    }

    private static TerrainRuntimeGeneratedDataIntegrityResult ValidateSurface(
        WorldSettings worldSettings
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.ValidationInspectOutputsSurface.Auto();

        TerrainRuntimeGeneratedDataIntegrityResult result =
            NewResult("Surface");

        if (worldSettings == null)
        {
            result.Errors.Add("WorldSettings is unavailable.");
            return result;
        }

        TerrainSurfaceMaskManifest manifest =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceMaskManifest>(
                TerrainRuntimeSurfaceMaskAssetUtility.SurfaceMaskManifestPath
            );

        TerrainHeightmapManifest heightManifest =
            AssetDatabase.LoadAssetAtPath<TerrainHeightmapManifest>(
                TerrainRuntimeHeightAssetUtility.HeightmapManifestPath
            );

        TerrainSurfaceSettings surfaceSettings =
            AssetDatabase.LoadAssetAtPath<TerrainSurfaceSettings>(
                WorldMeshesPaths.TerrainSurfaceSettingsAssetPath
            );

        string currentSettingsSignature =
            TerrainSurfaceSignatureUtility.GetSettingsSignature(
                surfaceSettings
            );

        int width = Mathf.Max(1, worldSettings.HeightTileGridWidth);
        int height = Mathf.Max(1, worldSettings.HeightTileGridHeight);

        result.ManifestPresent = manifest != null;
        result.ManifestComplete = manifest != null && manifest.isComplete;

        result.ManifestCompatible =
            manifest != null
            && manifest.compilerVersion ==
                TerrainGenerationStateUtility.SurfaceMaskCompilerVersion
            && manifest.channelLayoutVersion ==
                TerrainSurfaceMaskManifest.CurrentChannelLayoutVersion
            && manifest.tileGridWidth == width
            && manifest.tileGridHeight == height
            && manifest.MatchesHeightLayout(heightManifest)
            && surfaceSettings != null
            && !string.IsNullOrEmpty(currentSettingsSignature)
            && manifest.surfaceSettingsSignature == currentSettingsSignature;

        result.ExpectedAssetCount = width * height;

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                string path =
                    TerrainRuntimeSurfaceMaskAssetUtility
                        .GetSurfaceTilePath(x, z);

                Texture2D texture =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (texture != null)
                {
                    result.PresentAssetCount++;
                    continue;
                }

                result.MissingCoordinates.Add(new Vector2Int(x, z));

                Object main =
                    AssetDatabase.LoadMainAssetAtPath(path);

                if (main == null)
                {
                    result.MissingAssetPaths.Add(path);
                }
                else
                {
                    result.WrongAssetTypes.Add(
                        path + " (" + main.GetType().Name + ")"
                    );
                }
            }
        }

        return result;
    }

    private static TerrainRuntimeGeneratedDataIntegrityResult ValidateCollision(
        WorldSettings worldSettings
    )
    {
        using var profilerScope =
            WorldMeshesProfiler.ValidationInspectOutputsCollision.Auto();

        TerrainRuntimeGeneratedDataIntegrityResult result =
            NewResult("Collision");

        if (worldSettings == null)
        {
            result.Errors.Add("WorldSettings is unavailable.");
            return result;
        }

        /*
         * Collision has no separate generation manifest. Existing WorldSettings
         * generation metadata is the structural manifest-equivalent.
         */
        result.ManifestPresent =
            worldSettings.collisionMeshGenerationRevision > 0
            && !string.IsNullOrEmpty(
                worldSettings.lastGeneratedCollisionSignature
            );

        result.ManifestComplete =
            result.ManifestPresent
            && worldSettings.collisionSourceHeightmapGenerationRevision > 0;

        result.ManifestCompatible =
            result.ManifestComplete
            && worldSettings.collisionSourceHeightmapGenerationRevision ==
                worldSettings.heightmapGenerationRevision
            && worldSettings.lastGeneratedCollisionSignature ==
                TerrainGenerationStateUtility
                    .GetCurrentCollisionSettingsSignature(
                        worldSettings
                    )
            && AssetDatabase.IsValidFolder(
                TerrainCollisionMeshGenerator.CollisionMeshFolder
            );

        int width = Mathf.Max(1, worldSettings.gridWidth);
        int height = Mathf.Max(1, worldSettings.gridHeight);

        result.ExpectedAssetCount = width * height;

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                string path =
                    TerrainCollisionMeshGenerator
                        .GetCollisionMeshPath(x, z);

                Mesh mesh =
                    AssetDatabase.LoadAssetAtPath<Mesh>(path);

                if (mesh != null)
                {
                    result.PresentAssetCount++;
                    continue;
                }

                result.MissingCoordinates.Add(new Vector2Int(x, z));

                Object main =
                    AssetDatabase.LoadMainAssetAtPath(path);

                if (main == null)
                {
                    result.MissingAssetPaths.Add(path);
                }
                else
                {
                    result.WrongAssetTypes.Add(
                        path + " (" + main.GetType().Name + ")"
                    );
                }
            }
        }

        return result;
    }

    private static TerrainRuntimeGeneratedDataIntegrityResult NewResult(
        string name
    )
    {
        return
            new TerrainRuntimeGeneratedDataIntegrityResult
            {
                DatasetName = name
            };
    }

    private static string BuildKey(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return "<null>";
        }

        return
            worldSettings.GetInstanceID() + "|" +
            worldSettings.gridWidth + "|" +
            worldSettings.gridHeight + "|" +
            worldSettings.chunkSize + "|" +
            worldSettings.heightfieldResolutionPerChunk + "|" +
            worldSettings.heightTileChunkSpan + "|" +
            worldSettings.collisionResolution + "|" +
            worldSettings.heightmapGenerationRevision + "|" +
            worldSettings.lastGeneratedHeightSignature + "|" +
            worldSettings.surfaceMaskGenerationRevision + "|" +
            worldSettings.lastGeneratedSurfaceSignature + "|" +
            worldSettings.collisionMeshGenerationRevision + "|" +
            worldSettings.lastGeneratedCollisionSignature;
    }
}
