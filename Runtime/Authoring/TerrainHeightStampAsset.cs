using UnityEngine;

/*
 * Reusable height-stamp source data.
 *
 * Library identity is deliberately independent from the source Texture2D
 * filename. A texture such as:
 *
 *     heightmap_20260905222247_64S6.png
 *
 * can therefore be represented to authors as:
 *
 *     Heightmap_001
 *
 * without renaming or deriving identity from the source file.
 *
 * LibraryId == 0 is reserved for non-library/transient/generated validation
 * fixtures. User-facing library assets require a positive stable ID.
 */
[CreateAssetMenu(
    fileName = "TerrainHeightStamp",
    menuName = "WorldMeshes/Terrain Height Stamp"
)]
public sealed class TerrainHeightStampAsset :
    ScriptableObject
{
    [SerializeField]
    private int libraryId =
        TerrainHeightStampIdentityUtility
            .UnassignedLibraryId;

    [SerializeField]
    private Texture2D heightTexture;

    public int LibraryId
    {
        get
        {
            return libraryId;
        }
    }

    public bool HasLibraryId
    {
        get
        {
            return
                TerrainHeightStampIdentityUtility
                    .IsValidLibraryId(
                        libraryId
                    );
        }
    }

    public string DisplayName
    {
        get
        {
            if (HasLibraryId)
            {
                return
                    TerrainHeightStampIdentityUtility
                        .FormatDisplayName(
                            libraryId
                        );
            }

            return
                !string.IsNullOrEmpty(
                    name
                )
                    ? name
                    : TerrainHeightStampIdentityUtility
                        .FormatDisplayName(
                            TerrainHeightStampIdentityUtility
                                .UnassignedLibraryId
                        );
        }
    }

    public Texture2D HeightTexture
    {
        get
        {
            return
                heightTexture;
        }
    }

    public bool IsConfigured
    {
        get
        {
            return
                heightTexture != null;
        }
    }

    internal void SetLibraryIdInternal(
        int value
    )
    {
        libraryId =
            Mathf.Max(
                TerrainHeightStampIdentityUtility
                    .UnassignedLibraryId,
                value
            );
    }

    internal void SetHeightTextureInternal(
        Texture2D texture
    )
    {
        heightTexture =
            texture;
    }
}
