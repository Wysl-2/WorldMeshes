public static class TerrainAnalysisService
{
    private static readonly TerrainAnalysisCache Cache =
        new TerrainAnalysisCache();

    private static ITerrainAnalysisGenerator generator;
    private static int generationRevision;

    public static int ActiveLayerCount => Cache.Count;
    public static bool GeneratorAvailable => generator != null;

    public static TerrainAnalysisLayer RequestLayer(
        TerrainAnalysisKey key
    )
    {
        TerrainAnalysisLayer layer =
            Cache.GetOrCreateLayer(key);

        if (
            layer.IsReady ||
            generator == null
        )
        {
            return layer;
        }

        if (
            generator.TryGenerate(
                key,
                out TerrainAnalysisGenerationResult result,
                out string errorMessage
            ) &&
            result.IsValid
        )
        {
            generationRevision++;

            if (generationRevision <= 0)
            {
                generationRevision = 1;
            }

            layer.SetResult(
                result,
                generationRevision
            );

            return layer;
        }

        layer.SetGenerationError(
            string.IsNullOrEmpty(errorMessage)
                ? "Terrain analysis generation failed."
                : errorMessage
        );

        return layer;
    }

    public static bool TryGetLayer(
        TerrainAnalysisKey key,
        out TerrainAnalysisLayer layer
    )
    {
        return Cache.TryGetLayer(
            key,
            out layer
        );
    }

    public static bool ReleaseLayer(
        TerrainAnalysisKey key
    )
    {
        return Cache.RemoveLayer(key);
    }

    public static void InvalidateAll()
    {
        Cache.InvalidateAll();
    }

    public static void Clear()
    {
        Cache.Clear();
    }

    public static void RegisterGenerator(
        ITerrainAnalysisGenerator newGenerator
    )
    {
        if (
            ReferenceEquals(
                generator,
                newGenerator
            )
        )
        {
            return;
        }

        generator = newGenerator;
        InvalidateAll();
    }

    public static void UnregisterGenerator(
        ITerrainAnalysisGenerator existingGenerator
    )
    {
        if (
            !ReferenceEquals(
                generator,
                existingGenerator
            )
        )
        {
            return;
        }

        generator = null;
        InvalidateAll();
    }
}
