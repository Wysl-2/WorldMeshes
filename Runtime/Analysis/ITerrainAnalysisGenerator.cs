using System.Collections.Generic;
using UnityEngine;

/*
 * Generation backend used by TerrainAnalysisService.
 *
 * Runtime analysis ownership stays independent from the concrete source of
 * analysis data. In the editor, TerrainAnalysisGpuGenerator implements this
 * interface using the current TerrainAuthoringPreviewCache.
 */
public interface ITerrainAnalysisGenerator
{
    bool TryGenerate(
        TerrainAnalysisKey key,
        out TerrainAnalysisGenerationResult result,
        out string errorMessage
    );

    /*
     * Regenerate selected absolute terrain-tile coordinates directly into
     * an existing ready analysis layer.
     *
     * The implementation must leave the layer texture allocated and update
     * only the requested slices. On failure, TerrainAnalysisService marks the
     * layer stale so the next RequestLayer() can fall back to full generation.
     */
    bool TryUpdateTiles(
        TerrainAnalysisLayer layer,
        IReadOnlyList<Vector2Int> tileCoordinates,
        out string sourceSignature,
        out string errorMessage
    );
}
