using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class TerrainAuthoringModifierMutationDiagnostics
{
    public string Operation { get; internal set; }
    public bool Changed { get; internal set; }
    public int RevisionBefore { get; internal set; }
    public int RevisionAfter { get; internal set; }
    public string CommittedSignatureBefore { get; internal set; }
    public string CommittedSignatureAfter { get; internal set; }
    public string OverallSignatureBefore { get; internal set; }
    public string OverallSignatureAfter { get; internal set; }
    public string PreviewNotificationMode { get; internal set; }

    private readonly List<Vector2Int>
        dirtyTiles =
            new List<Vector2Int>();

    public IReadOnlyList<Vector2Int> DirtyTiles =>
        dirtyTiles;

    public int DirtyTileCount =>
        dirtyTiles.Count;

    internal void SetDirtyTiles(
        IEnumerable<Vector2Int> source
    )
    {
        dirtyTiles.Clear();

        if (source == null)
        {
            return;
        }

        dirtyTiles.AddRange(source);
    }
}

/*
 * Stage 12 authoring mutation boundary.
 *
 * Editor UI and Scene tools should change persistent height modifiers
 * through this service instead of calling TerrainAuthoringData or
 * TerrainHeightModifier internal setters directly.
 */
public static partial class TerrainAuthoringModifierService
{
    public static TerrainAuthoringModifierMutationDiagnostics
        LastMutationDiagnostics
    {
        get;
        private set;
    }

    // =====================================================
    // STRUCTURAL OPERATIONS
    // =====================================================

    public static bool AddStampModifier(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainHeightStampAsset stampAsset,
        Vector2 positionXZ,
        Vector2 sizeXZ,
        float heightDelta,
        float falloff,
        out string stableId,
        out string errorMessage
    )
    {
        stableId = "";
        errorMessage = "";

        TerrainStampModifier newModifier =
            new TerrainStampModifier();

        newModifier.SetStampAssetInternal(
            stampAsset
        );

        newModifier.SetPositionXZInternal(
            positionXZ
        );

        newModifier.SetSizeXZInternal(
            sizeXZ
        );

        newModifier.SetHeightDeltaInternal(
            heightDelta
        );

        newModifier.SetFalloffInternal(
            falloff
        );

        newModifier.SetBlendModeInternal(
            TerrainHeightBlendMode.Additive
        );

        newModifier.SetEnabledInternal(
            true
        );

        if (
            !ExecuteMutation(
                authoringData,
                worldSettings,
                "Add Terrain Height Modifier",
                () =>
                {
                    authoringData
                        .AddHeightModifierInternal(
                            newModifier
                        );

                    return true;
                },
                out errorMessage
            )
        )
        {
            return false;
        }

        stableId =
            newModifier.StableId;

        return
            !string.IsNullOrEmpty(
                stableId
            );
    }

    public static bool RemoveModifier(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !TryFindModifier(
                authoringData,
                stableId,
                out _,
                out int index,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Remove Terrain Height Modifier",
                () =>
                    authoringData
                        .RemoveHeightModifierAtInternal(
                            index
                        ),
                out errorMessage
            );
    }

    public static bool DuplicateModifier(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        out string duplicateStableId,
        out string errorMessage
    )
    {
        duplicateStableId = "";
        errorMessage = "";

        if (
            !TryFindModifier(
                authoringData,
                stableId,
                out TerrainHeightModifier source,
                out int sourceIndex,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !TryCreateDuplicate(
                source,
                out TerrainHeightModifier duplicate,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (
            !ExecuteMutation(
                authoringData,
                worldSettings,
                "Duplicate Terrain Height Modifier",
                () =>
                {
                    authoringData
                        .InsertHeightModifierInternal(
                            sourceIndex + 1,
                            duplicate
                        );

                    return true;
                },
                out errorMessage
            )
        )
        {
            return false;
        }

        duplicateStableId =
            duplicate.StableId;

        return
            !string.IsNullOrEmpty(
                duplicateStableId
            )
            &&
            duplicateStableId !=
                stableId;
    }

    public static bool ReorderModifier(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        int newIndex,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (
            !TryFindModifier(
                authoringData,
                stableId,
                out _,
                out int oldIndex,
                out errorMessage
            )
        )
        {
            return false;
        }

        int safeIndex =
            Mathf.Clamp(
                newIndex,
                0,
                Mathf.Max(
                    0,
                    authoringData
                        .HeightModifierCount -
                    1
                )
            );

        if (safeIndex == oldIndex)
        {
            SetNoChangeDiagnostics(
                "Reorder Terrain Height Modifier",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Reorder Terrain Height Modifier",
                () =>
                    authoringData
                        .MoveHeightModifierInternal(
                            oldIndex,
                            safeIndex
                        ),
                out errorMessage
            );
    }

    // =====================================================
    // BASE MODIFIER PARAMETERS
    // =====================================================

    public static bool SetModifierEnabled(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        bool enabled,
        out string errorMessage
    )
    {
        if (
            !TryFindModifier(
                authoringData,
                stableId,
                out TerrainHeightModifier modifier,
                out _,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (modifier.Enabled == enabled)
        {
            SetNoChangeDiagnostics(
                "Set Terrain Modifier Enabled",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                enabled
                    ? "Enable Terrain Height Modifier"
                    : "Disable Terrain Height Modifier",
                () =>
                {
                    modifier.SetEnabledInternal(
                        enabled
                    );

                    return true;
                },
                out errorMessage
            );
    }

    public static bool SetModifierBlendMode(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        TerrainHeightBlendMode blendMode,
        out string errorMessage
    )
    {
        if (
            !TryFindModifier(
                authoringData,
                stableId,
                out TerrainHeightModifier modifier,
                out _,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (modifier.BlendMode == blendMode)
        {
            SetNoChangeDiagnostics(
                "Set Terrain Modifier Blend Mode",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Modifier Blend Mode",
                () =>
                {
                    modifier.SetBlendModeInternal(
                        blendMode
                    );

                    return true;
                },
                out errorMessage
            );
    }

    // =====================================================
    // STAMP PARAMETERS
    // =====================================================

    public static bool SetStampAsset(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        TerrainHeightStampAsset stampAsset,
        out string errorMessage
    )
    {
        if (
            !TryFindStampModifier(
                authoringData,
                stableId,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (modifier.StampAsset == stampAsset)
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Asset",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Asset",
                () =>
                {
                    modifier.SetStampAssetInternal(
                        stampAsset
                    );

                    return true;
                },
                out errorMessage
            );
    }

    public static bool SetStampPositionXZ(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        Vector2 positionXZ,
        out string errorMessage
    )
    {
        if (
            !TryFindStampModifier(
                authoringData,
                stableId,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Move Terrain Stamp Modifier",
                () =>
                {
                    modifier.SetPositionXZInternal(
                        positionXZ
                    );

                    return true;
                },
                out errorMessage
            );
    }

    public static bool SetStampSizeXZ(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        Vector2 sizeXZ,
        out string errorMessage
    )
    {
        if (
            !TryFindStampModifier(
                authoringData,
                stableId,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Resize Terrain Stamp Modifier",
                () =>
                {
                    modifier.SetSizeXZInternal(
                        sizeXZ
                    );

                    return true;
                },
                out errorMessage
            );
    }

    public static bool SetStampHeightDelta(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        float heightDelta,
        out string errorMessage
    )
    {
        if (
            !TryFindStampModifier(
                authoringData,
                stableId,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Height",
                () =>
                {
                    modifier.SetHeightDeltaInternal(
                        heightDelta
                    );

                    return true;
                },
                out errorMessage
            );
    }

    public static bool SetStampFalloff(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        float falloff,
        out string errorMessage
    )
    {
        if (
            !TryFindStampModifier(
                authoringData,
                stableId,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Falloff",
                () =>
                {
                    modifier.SetFalloffInternal(
                        falloff
                    );

                    return true;
                },
                out errorMessage
            );
    }

    // =====================================================
    // TRANSACTION
    // =====================================================

    private static bool ExecuteMutation(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string undoLabel,
        Func<bool> mutation,
        out string errorMessage
    )
    {
        errorMessage = "";


        if (HasActiveInteractiveEdit)
        {
            errorMessage =
                "A terrain modifier interactive edit is currently active.";

            return false;
        }

        if (
            authoringData == null
            ||
            worldSettings == null
            ||
            mutation == null
        )
        {
            errorMessage =
                "Terrain modifier mutation received an invalid context.";

            return false;
        }

        if (
            authoringData.authoringRevision ==
            int.MaxValue
        )
        {
            errorMessage =
                "TerrainAuthoringData.authoringRevision reached Int32.MaxValue.";

            return false;
        }

        if (
            !authoringData
                .TryValidateModifierStableIds(
                    out string identityError
                )
            &&
            authoringData.HeightModifierCount >
                0
        )
        {
            errorMessage =
                "Modifier identity state is invalid before mutation.\n\n" +
                identityError;

            return false;
        }

        bool notifyPreview =
            ShouldNotifyPreview(
                authoringData
            );

        TerrainAuthoringModifierChangeTracker
            .EnsureTracked(
                authoringData,
                worldSettings,
                notifyPreview
            );

        if (
            !TerrainAuthoringModifierChangeTracker
                .TryCaptureStack(
                    authoringData,
                    out List<TerrainHeightModifierSnapshot> before,
                    out errorMessage
                )
        )
        {
            return false;
        }

        int revisionBefore =
            authoringData.authoringRevision;

        string committedBefore =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallBefore =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        /*
         * Explicitly isolate every discrete Stage 12 service mutation
         * into its own Unity Undo group.
         *
         * Unity normally increments Undo groups from editor events
         * (mouse down, menu commands, etc.). The Stage 12 validator
         * intentionally executes many logical edits synchronously from
         * one button callback, so relying on implicit event grouping
         * causes all edits to collapse into one Undo step.
         */
        Undo.IncrementCurrentGroup();

        int undoGroup =
            Undo.GetCurrentGroup();

        Undo.SetCurrentGroupName(
            undoLabel
        );

        Undo.RecordObject(
            authoringData,
            undoLabel
        );

        bool mutationReportedChange =
            mutation();

        if (!mutationReportedChange)
        {
            /*
             * Flush before leaving the explicitly-created group so the
             * next synchronous service call starts from a clean Undo
             * boundary. No serialized delta means Unity should not add
             * a visible Undo entry.
             */
            Undo.FlushUndoRecordObjects();

            Undo.CollapseUndoOperations(
                undoGroup
            );

            SetNoChangeDiagnostics(
                undoLabel,
                authoringData,
                worldSettings
            );

            return true;
        }

        authoringData
            .RepairModifierStableIds();

        if (
            !authoringData
                .TryValidateModifierStableIds(
                    out identityError
                )
        )
        {
            errorMessage =
                "Modifier identity state is invalid after mutation.\n\n" +
                identityError;

            return false;
        }

        if (
            !TerrainAuthoringModifierChangeTracker
                .TryCaptureStack(
                    authoringData,
                    out List<TerrainHeightModifierSnapshot> after,
                    out errorMessage
                )
        )
        {
            return false;
        }

        if (
            TerrainHeightModifierSnapshot
                .StackEquals(
                    before,
                    after
                )
        )
        {
            Undo.FlushUndoRecordObjects();

            Undo.CollapseUndoOperations(
                undoGroup
            );

            SetNoChangeDiagnostics(
                undoLabel,
                authoringData,
                worldSettings
            );

            return true;
        }

        authoringData.authoringRevision =
            Mathf.Max(
                0,
                revisionBefore
            )
            +
            1;

        HashSet<Vector2Int> dirtyTiles =
            new HashSet<Vector2Int>();

        TerrainAuthoringModifierChangeTracker
            .CollectChangedTiles(
                worldSettings,
                before,
                after,
                dirtyTiles
            );

        EditorUtility.SetDirty(
            authoringData
        );

        TerrainAuthoringModifierChangeTracker
            .UpdateTrackedState(
                authoringData,
                worldSettings,
                after,
                notifyPreview
            );

        if (notifyPreview)
        {
            /*
             * This API also acknowledges overall-authoring changes
             * when the dirty set is empty, e.g. editing a disabled or
             * completely out-of-world modifier.
             */
            TerrainAuthoringPreviewService
                .NotifyCompositeAuthoringStateChanged(
                    dirtyTiles
                );
        }

        string committedAfter =
            TerrainAuthoringStateUtility
                .GetCommittedHeightfieldSignature(
                    worldSettings
                );

        string overallAfter =
            TerrainAuthoringStateUtility
                .GetOverallAuthoringSignature(
                    worldSettings,
                    authoringData
                );

        TerrainAuthoringModifierMutationDiagnostics diagnostics =
            new TerrainAuthoringModifierMutationDiagnostics
            {
                Operation =
                    undoLabel,

                Changed =
                    true,

                RevisionBefore =
                    revisionBefore,

                RevisionAfter =
                    authoringData.authoringRevision,

                CommittedSignatureBefore =
                    committedBefore,

                CommittedSignatureAfter =
                    committedAfter,

                OverallSignatureBefore =
                    overallBefore,

                OverallSignatureAfter =
                    overallAfter,

                PreviewNotificationMode =
                    notifyPreview
                        ? (
                            dirtyTiles.Count > 0
                                ? "DirtyTiles"
                                : "MetadataOnly"
                        )
                        : "Suppressed"
            };

        diagnostics.SetDirtyTiles(
            dirtyTiles
        );

        LastMutationDiagnostics =
            diagnostics;

        /*
         * RecordObject changes are normally flushed by Unity at
         * conventional editor-event boundaries. Stage 12 deliberately
         * supports multiple discrete service calls in one callback, so
         * flush this logical edit now and keep its group isolated from
         * the next service call.
         */
        Undo.FlushUndoRecordObjects();

        /*
         * If any nested Undo operations were created during this
         * logical mutation, collapse them into this service-owned group.
         * The next service call begins by incrementing to a fresh group.
         */
        Undo.CollapseUndoOperations(
            undoGroup
        );

        return true;
    }

    // =====================================================
    // LOOKUP / DUPLICATION
    // =====================================================

    private static bool TryFindModifier(
        TerrainAuthoringData authoringData,
        string stableId,
        out TerrainHeightModifier modifier,
        out int index,
        out string errorMessage
    )
    {
        modifier = null;
        index = -1;
        errorMessage = "";

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";

            return false;
        }

        if (string.IsNullOrEmpty(stableId))
        {
            errorMessage =
                "Modifier stable ID is empty.";

            return false;
        }

        IReadOnlyList<TerrainHeightModifier> modifiers =
            authoringData.HeightModifiers;

        for (int current = 0; current < modifiers.Count; current++)
        {
            TerrainHeightModifier candidate =
                modifiers[current];

            if (
                candidate != null
                &&
                candidate.StableId ==
                    stableId
            )
            {
                modifier = candidate;
                index = current;
                return true;
            }
        }

        errorMessage =
            $"Terrain modifier {stableId} was not found.";

        return false;
    }

    private static bool TryFindStampModifier(
        TerrainAuthoringData authoringData,
        string stableId,
        out TerrainStampModifier modifier,
        out string errorMessage
    )
    {
        modifier = null;

        if (
            !TryFindModifier(
                authoringData,
                stableId,
                out TerrainHeightModifier baseModifier,
                out _,
                out errorMessage
            )
        )
        {
            return false;
        }

        modifier =
            baseModifier as
                TerrainStampModifier;

        if (modifier == null)
        {
            errorMessage =
                $"Modifier {stableId} is not a TerrainStampModifier.";

            return false;
        }

        return true;
    }

    private static bool TryCreateDuplicate(
        TerrainHeightModifier source,
        out TerrainHeightModifier duplicate,
        out string errorMessage
    )
    {
        duplicate = null;
        errorMessage = "";

        if (
            source is
                TerrainStampModifier stamp
        )
        {
            TerrainStampModifier copy =
                new TerrainStampModifier();

            copy.SetStampAssetInternal(
                stamp.StampAsset
            );

            copy.SetPositionXZInternal(
                stamp.PositionXZ
            );

            copy.SetSizeXZInternal(
                stamp.SizeXZ
            );

            copy.SetRotationDegreesInternal(
                stamp.RotationDegrees
            );

            copy.SetHeightDeltaInternal(
                stamp.HeightDelta
            );

            copy.SetFalloffInternal(
                stamp.Falloff
            );

            copy.SetSmoothingRadiusInternal(
                stamp.SmoothingRadius
            );

            copy.SetSmoothingStrengthInternal(
                stamp.SmoothingStrength
            );

            copy.SetBlendModeInternal(
                stamp.BlendMode
            );

            copy.SetEnabledInternal(
                stamp.Enabled
            );

            duplicate = copy;

            return true;
        }

        errorMessage =
            "The modifier type does not implement Stage 12 duplication: " +
            source.GetType().FullName;

        return false;
    }

    // =====================================================
    // DIAGNOSTICS / HELPERS
    // =====================================================

    private static bool ShouldNotifyPreview(
        TerrainAuthoringData authoringData
    )
    {
        string path =
            AssetDatabase.GetAssetPath(
                authoringData
            );

        return
            !string.IsNullOrEmpty(
                path
            )
            &&
            path ==
                WorldMeshesPaths
                    .TerrainAuthoringDataAssetPath;
    }

    private static void SetNoChangeDiagnostics(
        string operation,
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings
    )
    {
        string committed =
            worldSettings != null
                ? TerrainAuthoringStateUtility
                    .GetCommittedHeightfieldSignature(
                        worldSettings
                    )
                : "";

        string overall =
            worldSettings != null
            &&
            authoringData != null
                ? TerrainAuthoringStateUtility
                    .GetOverallAuthoringSignature(
                        worldSettings,
                        authoringData
                    )
                : "";

        LastMutationDiagnostics =
            new TerrainAuthoringModifierMutationDiagnostics
            {
                Operation =
                    operation,

                Changed =
                    false,

                RevisionBefore =
                    authoringData != null
                        ? authoringData.authoringRevision
                        : 0,

                RevisionAfter =
                    authoringData != null
                        ? authoringData.authoringRevision
                        : 0,

                CommittedSignatureBefore =
                    committed,

                CommittedSignatureAfter =
                    committed,

                OverallSignatureBefore =
                    overall,

                OverallSignatureAfter =
                    overall,

                PreviewNotificationMode =
                    "None"
            };
    }
}
