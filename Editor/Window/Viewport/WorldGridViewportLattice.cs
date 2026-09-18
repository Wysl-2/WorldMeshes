public enum WorldGridViewportLattice
{
    None = 0,
    WorldChunks = 1,
    HeightTiles = 2
}

internal static class WorldGridViewportLatticeUtility
{
    internal static string GetDisplayName(
        WorldGridViewportLattice lattice
    )
    {
        switch (lattice)
        {
            case WorldGridViewportLattice.None:
                return
                    "None";

            case WorldGridViewportLattice.HeightTiles:
                return
                    "Height Tiles";

            case WorldGridViewportLattice.WorldChunks:
            default:
                return
                    "World Chunks";
        }
    }
}
