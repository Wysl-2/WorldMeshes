using System.Globalization;

public static class TerrainHeightStampIdentityUtility
{
    public const int UnassignedLibraryId =
        0;

    public static bool IsValidLibraryId(
        int libraryId
    )
    {
        return
            libraryId > 0;
    }

    public static string FormatDisplayName(
        int libraryId
    )
    {
        if (
            !IsValidLibraryId(
                libraryId
            )
        )
        {
            return
                "Unassigned Height Stamp";
        }

        return
            "Heightmap_" +
            libraryId.ToString(
                "D3",
                CultureInfo.InvariantCulture
            );
    }
}
