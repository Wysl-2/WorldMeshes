using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class TerrainRuntimeBakeStateStorage
{
    internal const string PersistencePath =
        "Library/WorldMeshes/TerrainRuntimeBakeState.asset";
}

/*
 * Persistent editor-only queue of runtime bake work.
 *
 * This state is intentionally separate from:
 *
 * 1. WorldSettings / TerrainGenerationStateUtility, which describe the
 *    last successfully generated runtime state.
 *
 * 2. TerrainAuthoringPreviewService, whose dirty-tile state is transient
 *    GPU preview lifecycle state.
 *
 * Package 01 stores independent pending-work primitives only. Dependency
 * propagation and bake planning belong to later packages.
 */
[FilePath(
    TerrainRuntimeBakeStateStorage.PersistencePath,
    FilePathAttribute.Location.ProjectFolder
)]
internal sealed class TerrainRuntimeBakeState :
    ScriptableSingleton<TerrainRuntimeBakeState>
{
    internal const int CurrentSerializedVersion =
        2;

    [SerializeField]
    private int serializedVersion =
        CurrentSerializedVersion;

    [SerializeField]
    private long stateRevision =
        0L;

    [SerializeField]
    private List<Vector2Int> pendingHeightTiles =
        new List<Vector2Int>();

    [SerializeField]
    private List<Vector2Int> pendingSurfaceTiles =
        new List<Vector2Int>();

    [SerializeField]
    private List<Vector2Int> pendingCollisionChunks =
        new List<Vector2Int>();

    [SerializeField]
    private bool fullHeightRebuildRequired;

    [SerializeField]
    private bool fullSurfaceRebuildRequired;

    [SerializeField]
    private bool fullCollisionRebuildRequired;

    [SerializeField]
    private bool addressablesConfigurationDirty;

    [SerializeField]
    private bool addressablesContentDirty;

    [SerializeField]
    private bool runtimeSceneMetadataDirty;

    /*
     * Most recent overall authoring signature observed by the
     * authoritative runtime invalidation pipeline. The bake planner uses
     * this as a completeness guard for local dirty-tile sets.
     */
    [SerializeField]
    private string lastObservedAuthoringSignature =
        "";

    internal int SerializedVersion
    {
        get
        {
            return serializedVersion;
        }

        set
        {
            serializedVersion =
                value;
        }
    }

    internal long StateRevision
    {
        get
        {
            return stateRevision;
        }

        set
        {
            stateRevision =
                value;
        }
    }

    internal List<Vector2Int> PendingHeightTiles
    {
        get
        {
            return pendingHeightTiles;
        }
    }

    internal List<Vector2Int> PendingSurfaceTiles
    {
        get
        {
            return pendingSurfaceTiles;
        }
    }

    internal List<Vector2Int> PendingCollisionChunks
    {
        get
        {
            return pendingCollisionChunks;
        }
    }

    internal bool FullHeightRebuildRequired
    {
        get
        {
            return fullHeightRebuildRequired;
        }

        set
        {
            fullHeightRebuildRequired =
                value;
        }
    }

    internal bool FullSurfaceRebuildRequired
    {
        get
        {
            return fullSurfaceRebuildRequired;
        }

        set
        {
            fullSurfaceRebuildRequired =
                value;
        }
    }

    internal bool FullCollisionRebuildRequired
    {
        get
        {
            return fullCollisionRebuildRequired;
        }

        set
        {
            fullCollisionRebuildRequired =
                value;
        }
    }

    internal bool AddressablesConfigurationDirty
    {
        get
        {
            return addressablesConfigurationDirty;
        }

        set
        {
            addressablesConfigurationDirty =
                value;
        }
    }

    internal bool AddressablesContentDirty
    {
        get
        {
            return addressablesContentDirty;
        }

        set
        {
            addressablesContentDirty =
                value;
        }
    }

    internal bool RuntimeSceneMetadataDirty
    {
        get
        {
            return runtimeSceneMetadataDirty;
        }

        set
        {
            runtimeSceneMetadataDirty =
                value;
        }
    }

    internal string LastObservedAuthoringSignature
    {
        get
        {
            return
                lastObservedAuthoringSignature ??
                "";
        }

        set
        {
            lastObservedAuthoringSignature =
                value ??
                "";
        }
    }

    /*
     * Unity does not create arbitrary parent folders for every custom
     * ScriptableSingleton path on all editor versions. Ensure the
     * Library/WorldMeshes directory exists before saving.
     */
    internal void Persist()
    {
        string relativeDirectory =
            Path.GetDirectoryName(
                TerrainRuntimeBakeStateStorage.PersistencePath
            );

        if (
            !string.IsNullOrEmpty(
                relativeDirectory
            )
        )
        {
            string projectRoot =
                Directory
                    .GetParent(
                        Application.dataPath
                    )
                    .FullName;

            string absoluteDirectory =
                Path.Combine(
                    projectRoot,
                    relativeDirectory
                );

            Directory.CreateDirectory(
                absoluteDirectory
            );
        }

        Save(
            true
        );
    }

    /*
     * Repairs storage-only invariants after deserialization without
     * assigning bake dependency meaning to any state.
     */
    internal bool EnsureStorageInitialized()
    {
        bool changed =
            false;

        if (
            serializedVersion <
            CurrentSerializedVersion
        )
        {
            serializedVersion =
                CurrentSerializedVersion;

            changed =
                true;
        }

        if (stateRevision < 0L)
        {
            stateRevision =
                0L;

            changed =
                true;
        }

        if (pendingHeightTiles == null)
        {
            pendingHeightTiles =
                new List<Vector2Int>();

            changed =
                true;
        }

        if (pendingSurfaceTiles == null)
        {
            pendingSurfaceTiles =
                new List<Vector2Int>();

            changed =
                true;
        }

        if (pendingCollisionChunks == null)
        {
            pendingCollisionChunks =
                new List<Vector2Int>();

            changed =
                true;
        }

        if (lastObservedAuthoringSignature == null)
        {
            lastObservedAuthoringSignature =
                "";

            changed =
                true;
        }

        return changed;
    }
}
