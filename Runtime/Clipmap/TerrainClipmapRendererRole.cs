public enum TerrainClipmapRendererKind
{
    Unknown = 0,
    Center = 1,
    Ring = 2,
    Stitch = 3
}

/*
 * Semantic identity for one generated clipmap renderer.
 *
 * This type deliberately contains no hierarchy lookup or name parsing.
 * Later runtime packages can pair generated MeshRenderers with these roles
 * at the existing clipmap hierarchy ownership boundary.
 */
public readonly struct TerrainClipmapRendererRole
{
    private readonly TerrainClipmapRendererKind kind;
    private readonly int level;
    private readonly int fineLevel;
    private readonly int coarseLevel;

    public TerrainClipmapRendererKind Kind =>
        kind;

    public int Level =>
        level;

    public int FineLevel =>
        fineLevel;

    public int CoarseLevel =>
        coarseLevel;

    public int HeightOwnerLevel
    {
        get
        {
            if (!IsValid)
            {
                return -1;
            }

            return
                kind ==
                    TerrainClipmapRendererKind.Stitch
                    ? fineLevel
                    : level;
        }
    }

    public bool IsValid
    {
        get
        {
            switch (kind)
            {
                case TerrainClipmapRendererKind.Center:
                    return
                        level == 0
                        &&
                        fineLevel < 0
                        &&
                        coarseLevel < 0;

                case TerrainClipmapRendererKind.Ring:
                    return
                        level >= 1
                        &&
                        fineLevel < 0
                        &&
                        coarseLevel < 0;

                case TerrainClipmapRendererKind.Stitch:
                    return
                        level < 0
                        &&
                        fineLevel >= 0
                        &&
                        coarseLevel ==
                            fineLevel + 1;

                default:
                    return false;
            }
        }
    }

    private TerrainClipmapRendererRole(
        TerrainClipmapRendererKind kind,
        int level,
        int fineLevel,
        int coarseLevel
    )
    {
        this.kind =
            kind;

        this.level =
            level;

        this.fineLevel =
            fineLevel;

        this.coarseLevel =
            coarseLevel;
    }

    public static TerrainClipmapRendererRole CreateCenter()
    {
        return
            new TerrainClipmapRendererRole(
                TerrainClipmapRendererKind.Center,
                0,
                -1,
                -1
            );
    }

    public static bool TryCreateRing(
        int level,
        out TerrainClipmapRendererRole role
    )
    {
        role =
            default;

        if (level < 1)
        {
            return false;
        }

        role =
            new TerrainClipmapRendererRole(
                TerrainClipmapRendererKind.Ring,
                level,
                -1,
                -1
            );

        return true;
    }

    public static bool TryCreateStitch(
        int fineLevel,
        int coarseLevel,
        out TerrainClipmapRendererRole role
    )
    {
        role =
            default;

        if (
            fineLevel < 0
            ||
            coarseLevel !=
                fineLevel + 1
        )
        {
            return false;
        }

        role =
            new TerrainClipmapRendererRole(
                TerrainClipmapRendererKind.Stitch,
                -1,
                fineLevel,
                coarseLevel
            );

        return true;
    }
}
