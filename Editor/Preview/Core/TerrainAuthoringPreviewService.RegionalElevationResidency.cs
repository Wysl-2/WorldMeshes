using System.Collections.Generic;
using UnityEngine;

public static partial class TerrainAuthoringPreviewService
{
    private static bool hasPendingRegionalElevationInvalidation;

    private static TerrainRegionalElevationInvalidationScope
        pendingRegionalElevationInvalidation =
            TerrainRegionalElevationInvalidationScope.None;

    private static TerrainRegionalElevationInvalidationKind
        lastRegionalInvalidationKind =
            TerrainRegionalElevationInvalidationKind.None;

    private static long lastRegionalLogicalAffectedTileCount;

    private static int lastRegionalResidentAffectedTileCount;

    private static long lastRegionalNonresidentAffectedTileCount;

    private static int lastRegionalPublishedCompositeTileCount;

    private static readonly List<Vector2Int>
        regionalResidencyResidentDirtyScratch =
            new List<Vector2Int>();

    private static readonly List<Vector2Int>
        regionalResidencyCompositeUnionScratch =
            new List<Vector2Int>();

    public static bool HasPendingRegionalElevationInvalidation =>
        hasPendingRegionalElevationInvalidation;

    public static string LastRegionalInvalidationKind =>
        TerrainRegionalElevationResidencyPolicy
            .GetDisplayName(
                lastRegionalInvalidationKind
            );

    public static long LastRegionalLogicalAffectedTileCount =>
        lastRegionalLogicalAffectedTileCount;

    public static int LastRegionalResidentAffectedTileCount =>
        lastRegionalResidentAffectedTileCount;

    public static long LastRegionalNonresidentAffectedTileCount =>
        lastRegionalNonresidentAffectedTileCount;

    public static int LastRegionalPublishedCompositeTileCount =>
        lastRegionalPublishedCompositeTileCount;

    public static bool InteractiveRegionalElevationEditActive =>
        TerrainRegionalElevationService.HasActiveInteractiveEdit
        ||
        TerrainRegionalElevationService.HasActiveInteractiveGroupEdit;

    internal static bool HasActiveInteractiveTerrainAuthoringEdit =>
        TerrainAuthoringModifierService.HasActiveInteractiveEdit
        ||
        InteractiveRegionalElevationEditActive;

    internal static void NotifyRegionalElevationAuthoringStateChanged(
        TerrainRegionalElevationInvalidationScope scope
    )
    {
        RegisterPreviewAuthoringInvalidation(
            "Regional elevation authoring state changed."
        );

        if (
            scope.Kind !=
                TerrainRegionalElevationInvalidationKind.None
        )
        {
            pendingRegionalElevationInvalidation =
                TerrainRegionalElevationResidencyPolicy
                    .Merge(
                        hasPendingRegionalElevationInvalidation
                            ? pendingRegionalElevationInvalidation
                            : TerrainRegionalElevationInvalidationScope.None,
                        scope
                    );

            hasPendingRegionalElevationInvalidation =
                pendingRegionalElevationInvalidation.Kind !=
                TerrainRegionalElevationInvalidationKind.None;

            lastRegionalInvalidationKind =
                pendingRegionalElevationInvalidation.Kind;

            WorldSettings worldSettings =
                LoadWorldSettings();

            lastRegionalLogicalAffectedTileCount =
                TerrainRegionalElevationResidencyPolicy
                    .GetLogicalAffectedTileCount(
                        worldSettings,
                        pendingRegionalElevationInvalidation
                    );

            lastRegionalResidentAffectedTileCount =
                0;

            lastRegionalNonresidentAffectedTileCount =
                lastRegionalLogicalAffectedTileCount;

            lastRegionalPublishedCompositeTileCount =
                0;
        }

        overallSignatureAcknowledgementRequested =
            true;

        ScheduleRefresh();
    }

    internal static bool TryCollectPendingRegionalResidentTiles(
        WorldSettings worldSettings,
        bool hasActiveWindow,
        TerrainHeightCacheWindow activeWindow,
        List<Vector2Int> output,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (output == null)
        {
            errorMessage =
                "Regional elevation resident scratch collection is null.";

            return false;
        }

        output.Clear();

        if (!hasPendingRegionalElevationInvalidation)
        {
            return true;
        }

        TerrainRegionalElevationInvalidationScope scope =
            pendingRegionalElevationInvalidation;

        if (
            !TerrainRegionalElevationResidencyPolicy
                .TryCollectResidentTiles(
                    worldSettings,
                    scope,
                    hasActiveWindow,
                    activeWindow,
                    output,
                    out errorMessage
                )
        )
        {
            return false;
        }

        lastRegionalInvalidationKind =
            scope.Kind;

        lastRegionalLogicalAffectedTileCount =
            TerrainRegionalElevationResidencyPolicy
                .GetLogicalAffectedTileCount(
                    worldSettings,
                    scope
                );

        lastRegionalResidentAffectedTileCount =
            output.Count;

        lastRegionalNonresidentAffectedTileCount =
            System.Math.Max(
                0L,
                lastRegionalLogicalAffectedTileCount -
                lastRegionalResidentAffectedTileCount
            );

        lastRegionalPublishedCompositeTileCount =
            0;

        return true;
    }

    internal static void BuildResidentAuthoringDirtyUnion(
        List<Vector2Int> modifierResidentTiles,
        List<Vector2Int> regionalResidentTiles,
        List<Vector2Int> output
    )
    {
        if (output == null)
        {
            return;
        }

        output.Clear();

        HashSet<Vector2Int> unique =
            new HashSet<Vector2Int>();

        if (modifierResidentTiles != null)
        {
            for (
                int index = 0;
                index < modifierResidentTiles.Count;
                index++
            )
            {
                unique.Add(
                    modifierResidentTiles[index]
                );
            }
        }

        if (regionalResidentTiles != null)
        {
            for (
                int index = 0;
                index < regionalResidentTiles.Count;
                index++
            )
            {
                unique.Add(
                    regionalResidentTiles[index]
                );
            }
        }

        output.AddRange(
            unique
        );

        SortWorldTilesRowMajor(
            output
        );
    }

    internal static bool IsWorldTilePendingRegionalElevationRecomposition(
        WorldSettings worldSettings,
        Vector2Int worldTile
    )
    {
        return
            hasPendingRegionalElevationInvalidation
            &&
            TerrainRegionalElevationResidencyPolicy
                .ScopeAffectsTile(
                    worldSettings,
                    pendingRegionalElevationInvalidation,
                    worldTile
                );
    }

    internal static void ConsumePendingRegionalElevationInvalidation(
        int regionalPublishedTileCount
    )
    {
        if (!hasPendingRegionalElevationInvalidation)
        {
            return;
        }

        lastRegionalPublishedCompositeTileCount =
            Mathf.Max(
                0,
                regionalPublishedTileCount
            );

        hasPendingRegionalElevationInvalidation =
            false;

        pendingRegionalElevationInvalidation =
            TerrainRegionalElevationInvalidationScope.None;
    }

    internal static void ClearPendingRegionalElevationInvalidationForCommittedChange()
    {
        hasPendingRegionalElevationInvalidation =
            false;

        pendingRegionalElevationInvalidation =
            TerrainRegionalElevationInvalidationScope.None;
    }

    internal static void ClearRegionalElevationResidencyForResourceRelease()
    {
        hasPendingRegionalElevationInvalidation =
            false;

        pendingRegionalElevationInvalidation =
            TerrainRegionalElevationInvalidationScope.None;
    }

    internal static void AcknowledgePendingRegionalElevationAfterActivation(
        long activatedAuthoringGeneration
    )
    {
        if (
            !hasPendingRegionalElevationInvalidation
            ||
            activatedAuthoringGeneration !=
                authoringGeneration
        )
        {
            return;
        }

        /*
         * The activated staging cache was composed against the current
         * authoring generation. Its entire resident target therefore already
         * contains the current regional state.
         */
        lastRegionalResidentAffectedTileCount =
            activeCache != null
            &&
            activeCache.IsReady
                ? activeCache.SliceCount
                : 0;

        /*
         * Activation publishes cache/coverage state rather than the
         * CompositeTilesUpdated in-place event. Keep this counter tied to
         * actual resident in-place publication semantics.
         */
        lastRegionalPublishedCompositeTileCount =
            0;

        lastRegionalNonresidentAffectedTileCount =
            System.Math.Max(
                0L,
                lastRegionalLogicalAffectedTileCount -
                lastRegionalResidentAffectedTileCount
            );

        hasPendingRegionalElevationInvalidation =
            false;

        pendingRegionalElevationInvalidation =
            TerrainRegionalElevationInvalidationScope.None;
    }
}
