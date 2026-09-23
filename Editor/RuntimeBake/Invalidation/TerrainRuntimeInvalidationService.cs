using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Authoritative dependency-policy boundary for persistent runtime bake state.
 *
 * Authoring/settings systems report what changed. This service translates that
 * change into generated runtime outputs that are no longer trustworthy.
 */
public static class TerrainRuntimeInvalidationService
{
    public static bool InvalidateAuthoringHeightTiles(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        IEnumerable<Vector2Int> dirtyHeightTiles
    )
    {
        if (
            worldSettings == null
            ||
            authoringData == null
        )
        {
            return false;
        }

        string currentAuthoringSignature =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        HashSet<Vector2Int> heightTiles =
            new HashSet<Vector2Int>();

        if (
            string.IsNullOrEmpty(
                currentAuthoringSignature
            )
            ||
            !TerrainRuntimeBakeDependencyUtility
                .TryCopyValidHeightTiles(
                    worldSettings,
                    dirtyHeightTiles,
                    heightTiles,
                    out _
                )
            ||
            heightTiles.Count == 0
        )
        {
            return
                InvalidateFullHeightDependencyChain(
                    currentAuthoringSignature,
                    false
                );
        }

        HashSet<Vector2Int> collisionChunks =
            new HashSet<Vector2Int>();

        if (
            !TerrainRuntimeBakeDependencyUtility
                .TryCollectDependentCollisionChunks(
                    worldSettings,
                    heightTiles,
                    collisionChunks,
                    out _
                )
        )
        {
            return
                InvalidateFullHeightDependencyChain(
                    currentAuthoringSignature,
                    true
                );
        }

        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation();

        mutation
            .AddHeightTiles(
                heightTiles
            )
            .AddCollisionChunks(
                collisionChunks
            )
            .DirtyAddressablesContent()
            .DirtyRuntimeSceneMetadata()
            .SetObservedAuthoringSignature(
                currentAuthoringSignature
            );

        if (
            IsCurrentStreamingBaseline(
                worldSettings
            )
        )
        {
            mutation.AddHeightStreamingTiles(
                heightTiles
            );
        }
        else
        {
            mutation.RequireFullHeightStreaming();
        }

        TerrainSurfaceSettings surfaceSettings =
            AssetDatabase
                .LoadAssetAtPath<TerrainSurfaceSettings>(
                    WorldMeshesPaths
                        .TerrainSurfaceSettingsAssetPath
                );

        HashSet<Vector2Int> surfaceTiles =
            new HashSet<Vector2Int>();

        if (
            surfaceSettings == null
            ||
            !TerrainRuntimeBakeDependencyUtility
                .TryCollectDependentSurfaceTiles(
                    worldSettings,
                    surfaceSettings,
                    heightTiles,
                    surfaceTiles,
                    out _
                )
        )
        {
            mutation.RequireFullSurface();
        }
        else
        {
            mutation.AddSurfaceTiles(
                surfaceTiles
            );
        }

        return
            TerrainRuntimeBakeStateService
                .ApplyMutation(
                    mutation
                );
    }

    public static bool InvalidateGlobalAuthoringHeightOutput(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        string currentAuthoringSignature =
            "";

        if (
            worldSettings != null
            &&
            authoringData != null
        )
        {
            currentAuthoringSignature =
                TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        worldSettings,
                        authoringData
                    );
        }

        return
            InvalidateFullHeightDependencyChain(
                currentAuthoringSignature,
                false
            );
    }

    public static bool InvalidateCommittedAuthoringHeightfield(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData
    )
    {
        string currentAuthoringSignature =
            "";

        if (
            worldSettings != null
            &&
            authoringData != null
        )
        {
            currentAuthoringSignature =
                TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        worldSettings,
                        authoringData
                    );
        }

        return
            InvalidateFullHeightDependencyChain(
                currentAuthoringSignature,
                false
            );
    }

    public static bool InvalidateWorldSettingsChanged(
        WorldSettings worldSettings,
        int previousGridWidth,
        int previousGridHeight,
        float previousChunkSize,
        int previousHeightfieldResolutionPerChunk
    )
    {
        if (worldSettings == null)
        {
            return false;
        }

        bool gridChanged =
            previousGridWidth !=
                worldSettings.gridWidth
            ||
            previousGridHeight !=
                worldSettings.gridHeight;

        bool metricLayoutChanged =
            !Mathf.Approximately(
                previousChunkSize,
                worldSettings.chunkSize
            )
            ||
            previousHeightfieldResolutionPerChunk !=
                worldSettings.heightfieldResolutionPerChunk;

        if (
            !gridChanged
            &&
            !metricLayoutChanged
        )
        {
            return false;
        }

        return
            InvalidateFullHeightDependencyChain(
                "",
                gridChanged
            );
    }

    public static bool InvalidateHeightTileChunkSpanChanged(
        WorldSettings worldSettings,
        int previousHeightTileChunkSpan
    )
    {
        if (
            worldSettings == null
            ||
            previousHeightTileChunkSpan ==
                worldSettings.heightTileChunkSpan
        )
        {
            return false;
        }

        return
            InvalidateFullHeightDependencyChain(
                "",
                true
            );
    }

    public static bool InvalidateHeightStreamingTiles(
        WorldSettings worldSettings,
        IEnumerable<Vector2Int> dirtyHeightTiles
    )
    {
        if (worldSettings == null)
        {
            return false;
        }

        HashSet<Vector2Int> heightTiles =
            new HashSet<Vector2Int>();

        if (
            !TerrainRuntimeBakeDependencyUtility
                .TryCopyValidHeightTiles(
                    worldSettings,
                    dirtyHeightTiles,
                    heightTiles,
                    out _
                )
            ||
            heightTiles.Count == 0
        )
        {
            return
                InvalidateFullHeightStreaming();
        }

        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation();

        if (
            IsCurrentStreamingBaseline(
                worldSettings
            )
        )
        {
            mutation.AddHeightStreamingTiles(
                heightTiles
            );
        }
        else
        {
            mutation.RequireFullHeightStreaming();
        }

        return
            TerrainRuntimeBakeStateService
                .ApplyMutation(
                    mutation
                );
    }

    public static bool InvalidateFullHeightStreaming()
    {
        return
            TerrainRuntimeBakeStateService
                .ApplyMutation(
                    new TerrainRuntimeBakeStateMutation()
                        .RequireFullHeightStreaming()
                );
    }

    public static bool InvalidateSurfaceSettingsChanged()
    {
        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation();

        mutation
            .RequireFullSurface()
            .DirtyAddressablesContent();

        return
            TerrainRuntimeBakeStateService
                .ApplyMutation(
                    mutation
                );
    }

    public static bool InvalidateCollisionSettingsChanged(
        WorldSettings worldSettings,
        int previousCollisionResolution
    )
    {
        if (
            worldSettings == null
            ||
            previousCollisionResolution ==
                worldSettings.collisionResolution
        )
        {
            return false;
        }

        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation();

        mutation
            .RequireFullCollision()
            .DirtyAddressablesContent();

        return
            TerrainRuntimeBakeStateService
                .ApplyMutation(
                    mutation
                );
    }

    private static bool InvalidateFullHeightDependencyChain(
        string observedAuthoringSignature,
        bool addressablesConfigurationDirty
    )
    {
        TerrainRuntimeBakeStateMutation mutation =
            new TerrainRuntimeBakeStateMutation();

        mutation
            .RequireFullHeight()
            .RequireFullHeightStreaming()
            .RequireFullSurface()
            .RequireFullCollision()
            .DirtyAddressablesContent()
            .DirtyRuntimeSceneMetadata()
            .SetObservedAuthoringSignature(
                observedAuthoringSignature
            );

        if (addressablesConfigurationDirty)
        {
            mutation
                .DirtyAddressablesConfiguration();
        }

        return
            TerrainRuntimeBakeStateService
                .ApplyMutation(
                    mutation
                );
    }

    private static bool IsCurrentStreamingBaseline(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return false;
        }

        TerrainHeightmapManifest manifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainHeightmapManifest>(
                    TerrainRuntimeHeightAssetUtility
                        .HeightmapManifestPath
                );

        return
            TerrainGenerationStateUtility
                .IsHeightStreamingManifestCurrent(
                    manifest,
                    worldSettings,
                    worldSettings.heightmapGenerationRevision
                );
    }
}
