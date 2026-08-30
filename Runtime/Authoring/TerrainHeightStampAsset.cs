using UnityEngine;

/*
 * Reusable height-stamp source data.
 *
 * Stage 13 will sample the red channel as normalized 0..1 stamp weight.
 * Stage 11 only establishes the persistent asset/reference contract.
 */
[CreateAssetMenu(
    fileName = "TerrainHeightStamp",
    menuName = "WorldMeshes/Terrain Height Stamp"
)]
public sealed class TerrainHeightStampAsset :
    ScriptableObject
{
    [SerializeField]
    private Texture2D heightTexture;

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

    /*
     * Reserved for editor authoring/validation infrastructure.
     * Stage 12/15 tooling should own user-facing mutation.
     */
    internal void SetHeightTextureInternal(
        Texture2D texture
    )
    {
        heightTexture =
            texture;
    }
}
