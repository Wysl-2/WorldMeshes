using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

public enum TerrainRuntimeSceneSynchronizationOutcome
{
    NoWork,
    Completed,
    CompletedWithWarnings,
    RepairRequired,
    Blocked,
    Failed
}

public sealed class TerrainRuntimeSceneSynchronizationResult
{
    private readonly ReadOnlyCollection<string>
        warningMessages;

    public TerrainRuntimeSceneSynchronizationOutcome Outcome
    {
        get;
        private set;
    }

    public long SourceStateRevision
    {
        get;
        private set;
    }

    public bool BoundsAvailable
    {
        get;
        private set;
    }

    public bool BoundsApplied
    {
        get;
        private set;
    }

    public float MinimumTerrainHeight
    {
        get;
        private set;
    }

    public float MaximumTerrainHeight
    {
        get;
        private set;
    }

    public bool BoundsControllerSerializedChanged
    {
        get;
        private set;
    }

    public bool HeightStreamerSynchronized
    {
        get;
        private set;
    }

    public bool HeightStreamerSerializedChanged
    {
        get;
        private set;
    }

    public bool SurfaceManifestSynchronized
    {
        get;
        private set;
    }

    public bool SurfaceManifestSerializedChanged
    {
        get;
        private set;
    }

    public bool CollisionStreamerSynchronized
    {
        get;
        private set;
    }

    public bool CollisionStreamerSerializedChanged
    {
        get;
        private set;
    }

    public int SerializedComponentChangeCount
    {
        get;
        private set;
    }

    public bool SceneMarkedDirty
    {
        get;
        private set;
    }

    public bool PersistentSceneDirtyCleared
    {
        get;
        private set;
    }

    public bool RepairRequired
    {
        get
        {
            return
                Outcome ==
                TerrainRuntimeSceneSynchronizationOutcome
                    .RepairRequired;
        }
    }

    public IReadOnlyList<string> WarningMessages =>
        warningMessages;

    public string ErrorMessage
    {
        get;
        private set;
    }

    public string SummaryMessage
    {
        get;
        private set;
    }

    internal TerrainRuntimeSceneSynchronizationResult(
        TerrainRuntimeSceneSynchronizationOutcome outcome,
        long sourceStateRevision,
        bool boundsAvailable,
        bool boundsApplied,
        float minimumTerrainHeight,
        float maximumTerrainHeight,
        bool boundsControllerSerializedChanged,
        bool heightStreamerSynchronized,
        bool heightStreamerSerializedChanged,
        bool surfaceManifestSynchronized,
        bool surfaceManifestSerializedChanged,
        bool collisionStreamerSynchronized,
        bool collisionStreamerSerializedChanged,
        int serializedComponentChangeCount,
        bool sceneMarkedDirty,
        bool persistentSceneDirtyCleared,
        IEnumerable<string> warningMessages,
        string errorMessage,
        string summaryMessage
    )
    {
        Outcome =
            outcome;

        SourceStateRevision =
            sourceStateRevision;

        BoundsAvailable =
            boundsAvailable;

        BoundsApplied =
            boundsApplied;

        MinimumTerrainHeight =
            minimumTerrainHeight;

        MaximumTerrainHeight =
            maximumTerrainHeight;

        BoundsControllerSerializedChanged =
            boundsControllerSerializedChanged;

        HeightStreamerSynchronized =
            heightStreamerSynchronized;

        HeightStreamerSerializedChanged =
            heightStreamerSerializedChanged;

        SurfaceManifestSynchronized =
            surfaceManifestSynchronized;

        SurfaceManifestSerializedChanged =
            surfaceManifestSerializedChanged;

        CollisionStreamerSynchronized =
            collisionStreamerSynchronized;

        CollisionStreamerSerializedChanged =
            collisionStreamerSerializedChanged;

        SerializedComponentChangeCount =
            serializedComponentChangeCount;

        SceneMarkedDirty =
            sceneMarkedDirty;

        PersistentSceneDirtyCleared =
            persistentSceneDirtyCleared;

        List<string> warnings =
            warningMessages != null
                ? new List<string>(warningMessages)
                : new List<string>();

        this.warningMessages =
            warnings.AsReadOnly();

        ErrorMessage =
            errorMessage ??
            "";

        SummaryMessage =
            summaryMessage ??
            "";
    }

    public string BuildDiagnosticReport()
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Runtime Scene Synchronization"
        );

        builder.AppendLine(
            "Outcome: " +
            Outcome
        );

        builder.AppendLine(
            "Source Bake State Revision: " +
            SourceStateRevision
        );

        builder.AppendLine();

        if (BoundsAvailable)
        {
            builder.AppendLine(
                "Height Range: " +
                MinimumTerrainHeight.ToString("R") +
                " -> " +
                MaximumTerrainHeight.ToString("R")
            );
        }
        else
        {
            builder.AppendLine(
                "Height Range: Not Available"
            );
        }

        builder.AppendLine(
            "Bounds Applied: " +
            BoundsApplied
        );

        builder.AppendLine(
            "Bounds Serialized Changed: " +
            BoundsControllerSerializedChanged
        );

        builder.AppendLine();

        builder.AppendLine(
            "Height Streamer Synchronized: " +
            HeightStreamerSynchronized
        );

        builder.AppendLine(
            "Height Streamer Serialized Changed: " +
            HeightStreamerSerializedChanged
        );

        builder.AppendLine(
            "Surface Manifest Synchronized: " +
            SurfaceManifestSynchronized
        );

        builder.AppendLine(
            "Surface Manifest Serialized Changed: " +
            SurfaceManifestSerializedChanged
        );

        builder.AppendLine(
            "Collision Streamer Synchronized: " +
            CollisionStreamerSynchronized
        );

        builder.AppendLine(
            "Collision Streamer Serialized Changed: " +
            CollisionStreamerSerializedChanged
        );

        builder.AppendLine();

        builder.AppendLine(
            "Serialized Components Changed: " +
            SerializedComponentChangeCount
        );

        builder.AppendLine(
            "Scene Marked Dirty: " +
            SceneMarkedDirty
        );

        builder.AppendLine(
            "RuntimeSceneMetadataDirty Cleared: " +
            PersistentSceneDirtyCleared
        );

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

        builder.AppendLine();
        builder.Append(
            "Warnings (" +
            warningMessages.Count +
            "):"
        );

        if (warningMessages.Count == 0)
        {
            builder.AppendLine();
            builder.Append(
                "None"
            );
        }
        else
        {
            for (
                int index = 0;
                index < warningMessages.Count;
                index++
            )
            {
                builder.AppendLine();
                builder.Append(
                    "- " +
                    warningMessages[index]
                );
            }
        }

        return
            builder.ToString();
    }
}
