using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class TerrainAuthoringPreviewCache :
    IDisposable
{
    // =====================================================
    // GPU CACHE
    // =====================================================

    private RenderTexture heightCache;

    // =====================================================
    // SOURCE / LAYOUT STATE
    // =====================================================

    private string sourceAuthoringSignature =
        "";

    private string sourceContentHash =
        "";

    private Vector2Int cacheOriginTile =
        Vector2Int.zero;

    private int cacheWidth;

    private int cacheHeight;

    private int samplesPerSide;

    private float sampleSpacing;

    private Vector2 worldSizeXZ;

    private float minimumHeight;

    private float maximumHeight;

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public RenderTexture HeightCache
    {
        get
        {
            return heightCache;
        }
    }

    public string SourceAuthoringSignature
    {
        get
        {
            return sourceAuthoringSignature;
        }
    }

    public string SourceContentHash
    {
        get
        {
            return sourceContentHash;
        }
    }

    public Vector2Int CacheOriginTile
    {
        get
        {
            return cacheOriginTile;
        }
    }

    public int CacheWidth
    {
        get
        {
            return cacheWidth;
        }
    }

    public int CacheHeight
    {
        get
        {
            return cacheHeight;
        }
    }

    public Vector2Int CacheSize
    {
        get
        {
            return
                new Vector2Int(
                    cacheWidth,
                    cacheHeight
                );
        }
    }

    public int SliceCount
    {
        get
        {
            return
                Mathf.Max(
                    0,
                    cacheWidth
                )
                *
                Mathf.Max(
                    0,
                    cacheHeight
                );
        }
    }

    public int SamplesPerSide
    {
        get
        {
            return samplesPerSide;
        }
    }

    public float SampleSpacing
    {
        get
        {
            return sampleSpacing;
        }
    }

    public Vector2 WorldSizeXZ
    {
        get
        {
            return worldSizeXZ;
        }
    }

    public float MinimumHeight
    {
        get
        {
            return minimumHeight;
        }
    }

    public float MaximumHeight
    {
        get
        {
            return maximumHeight;
        }
    }

    public bool IsReady
    {
        get
        {
            return
                heightCache != null
                &&
                heightCache.IsCreated()
                &&
                cacheWidth > 0
                &&
                cacheHeight > 0
                &&
                samplesPerSide > 1
                &&
                !string.IsNullOrEmpty(
                    sourceAuthoringSignature
                );
        }
    }

    public long ApproximateGpuMemoryBytes
    {
        get
        {
            if (
                samplesPerSide <= 0
                ||
                SliceCount <= 0
            )
            {
                return 0L;
            }

            return
                (long)samplesPerSide
                *
                samplesPerSide
                *
                SliceCount
                *
                sizeof(float);
        }
    }

    // =====================================================
    // BUILD
    // =====================================================

    public bool TryBuild(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";

            return false;
        }

        // =================================================
        // VALIDATE PHYSICAL COMMITTED SOURCE
        // =================================================

        if (
            !TerrainAuthoringStateUtility
                .TryValidateCommittedHeightfield(
                    worldSettings,
                    authoringData,
                    out TerrainAuthoringHeightManifest manifest,
                    out string currentContentHash,
                    out string validationError
                )
        )
        {
            errorMessage =
                "The editor terrain preview could not validate " +
                "the committed authoring heightfield.\n\n" +
                validationError;

            return false;
        }

        string currentAuthoringSignature =
            TerrainAuthoringStateUtility
                .GetCurrentAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        if (
            string.IsNullOrEmpty(
                currentAuthoringSignature
            )
        )
        {
            errorMessage =
                "The current authoring signature could not be " +
                "calculated.";

            return false;
        }

        // =================================================
        // GPU SUPPORT
        // =================================================

        if (!SystemInfo.supports2DArrayTextures)
        {
            errorMessage =
                "The current graphics device does not support " +
                "2D texture arrays.";

            return false;
        }

        if (
            !SystemInfo.SupportsRenderTextureFormat(
                RenderTextureFormat.RFloat
            )
        )
        {
            errorMessage =
                "The current graphics device does not support " +
                "RFloat render textures.";

            return false;
        }

        if (
            SystemInfo.copyTextureSupport ==
            CopyTextureSupport.None
        )
        {
            errorMessage =
                "The current graphics device does not support " +
                "Graphics.CopyTexture.";

            return false;
        }

        int newCacheWidth =
            manifest.heightTileGridWidth;

        int newCacheHeight =
            manifest.heightTileGridHeight;

        int newSamplesPerSide =
            manifest.heightTileSamplesPerSide;

        int newSliceCount =
            newCacheWidth *
            newCacheHeight;

        if (
            newCacheWidth <= 0
            ||
            newCacheHeight <= 0
            ||
            newSamplesPerSide <= 1
            ||
            newSliceCount <= 0
        )
        {
            errorMessage =
                "The committed authoring manifest contains an " +
                "invalid height-tile layout.";

            return false;
        }

        if (
            newSamplesPerSide >
            SystemInfo.maxTextureSize
        )
        {
            errorMessage =
                "The authoring height tile size exceeds the " +
                "maximum texture size supported by the current " +
                "graphics device.\n\n" +
                $"Required: {newSamplesPerSide}\n" +
                $"Maximum: {SystemInfo.maxTextureSize}";

            return false;
        }

        if (
            SystemInfo.maxTextureArraySlices > 0
            &&
            newSliceCount >
            SystemInfo.maxTextureArraySlices
        )
        {
            errorMessage =
                "The full-world editor preview cache requires " +
                "more texture-array slices than the current " +
                "graphics device supports.\n\n" +
                $"Required: {newSliceCount}\n" +
                $"Maximum: {SystemInfo.maxTextureArraySlices}\n\n" +
                "A future windowed editor preview cache can " +
                "remove this whole-world limitation.";

            return false;
        }

        // =================================================
        // CREATE CANDIDATE CACHE
        // =================================================

        RenderTexture candidateCache =
            CreateHeightCache(
                newSamplesPerSide,
                newSliceCount
            );

        if (
            candidateCache == null
            ||
            !candidateCache.IsCreated()
        )
        {
            DestroyRenderTexture(
                candidateCache
            );

            errorMessage =
                "The editor preview GPU height cache could not " +
                "be created.";

            return false;
        }

        // =================================================
        // POPULATE CANDIDATE CACHE
        // =================================================

        try
        {
            for (
                int tileZ = 0;
                tileZ < newCacheHeight;
                tileZ++
            )
            {
                for (
                    int tileX = 0;
                    tileX < newCacheWidth;
                    tileX++
                )
                {
                    string sourcePath =
                        TerrainAuthoringStateUtility
                            .GetAuthoringHeightTilePath(
                                tileX,
                                tileZ
                            );

                    Texture2D sourceTexture =
                        AssetDatabase
                            .LoadAssetAtPath<Texture2D>(
                                sourcePath
                            );

                    if (sourceTexture == null)
                    {
                        throw
                            new InvalidOperationException(
                                "A committed authoring tile " +
                                "disappeared while building the " +
                                "editor preview cache.\n\n" +
                                sourcePath
                            );
                    }

                    int slice =
                        tileX
                        +
                        tileZ *
                        newCacheWidth;

                    Graphics.CopyTexture(
                        sourceTexture,
                        0,
                        0,

                        candidateCache,
                        slice,
                        0
                    );
                }
            }
        }
        catch (
            Exception exception
        )
        {
            DestroyRenderTexture(
                candidateCache
            );

            errorMessage =
                "The committed authoring height tiles could not " +
                "be copied into the editor preview cache.\n\n" +
                exception.Message;

            return false;
        }

        // =================================================
        // ATOMIC COMMIT
        // =================================================

        RenderTexture previousCache =
            heightCache;

        heightCache =
            candidateCache;

        cacheOriginTile =
            Vector2Int.zero;

        cacheWidth =
            newCacheWidth;

        cacheHeight =
            newCacheHeight;

        samplesPerSide =
            newSamplesPerSide;

        sampleSpacing =
            Mathf.Max(
                0.000001f,
                worldSettings.chunkSize
                /
                Mathf.Max(
                    1,
                    worldSettings
                        .heightfieldResolutionPerChunk
                )
            );

        /*
         * Keep world extent definition centralized with the
         * clipmap/runtime world-bounds systems.
         */
        worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        minimumHeight =
            manifest.minimumCommittedHeight;

        maximumHeight =
            manifest.maximumCommittedHeight;

        sourceAuthoringSignature =
            currentAuthoringSignature;

        sourceContentHash =
            currentContentHash;

        DestroyRenderTexture(
            previousCache
        );

        return true;
    }

    // =====================================================
    // CREATE GPU CACHE
    // =====================================================

    private static RenderTexture CreateHeightCache(
        int samplesPerSide,
        int sliceCount
    )
    {
        RenderTexture cache =
            new RenderTexture(
                samplesPerSide,
                samplesPerSide,
                0,
                RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear
            );

        cache.name =
            "Terrain Authoring Preview Height Cache";

        cache.dimension =
            TextureDimension.Tex2DArray;

        cache.volumeDepth =
            sliceCount;

        /*
         * Enable random write now so the same cache type is ready
         * for the upcoming GPU modifier compositor.
         */
        cache.enableRandomWrite =
            SystemInfo.supportsComputeShaders;

        cache.useMipMap =
            false;

        cache.autoGenerateMips =
            false;

        cache.wrapMode =
            TextureWrapMode.Clamp;

        cache.filterMode =
            FilterMode.Point;

        cache.anisoLevel =
            0;

        cache.hideFlags =
            HideFlags.HideAndDontSave;

        cache.Create();

        return cache;
    }

    // =====================================================
    // DISPOSE
    // =====================================================

    public void Dispose()
    {
        DestroyRenderTexture(
            heightCache
        );

        heightCache =
            null;

        sourceAuthoringSignature =
            "";

        sourceContentHash =
            "";

        cacheOriginTile =
            Vector2Int.zero;

        cacheWidth =
            0;

        cacheHeight =
            0;

        samplesPerSide =
            0;

        sampleSpacing =
            0f;

        worldSizeXZ =
            Vector2.zero;

        minimumHeight =
            0f;

        maximumHeight =
            0f;
    }

    private static void DestroyRenderTexture(
        RenderTexture texture
    )
    {
        if (texture == null)
        {
            return;
        }

        if (texture.IsCreated())
        {
            texture.Release();
        }

        UnityEngine.Object.DestroyImmediate(
            texture
        );
    }
}
