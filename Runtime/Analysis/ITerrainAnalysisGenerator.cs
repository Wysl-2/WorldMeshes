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
}
