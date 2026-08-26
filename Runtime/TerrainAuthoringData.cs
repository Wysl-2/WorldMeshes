using UnityEngine;

public enum TerrainHeightSourceMode
{
    Flat,
    Procedural,
    Imported
}

[CreateAssetMenu(
    fileName = "TerrainAuthoringData",
    menuName = "WorldMeshes/Terrain Authoring Data"
)]
public class TerrainAuthoringData :
    ScriptableObject
{
    [Header("Heightfield Initialization")]

    public TerrainHeightSourceMode sourceMode =
        TerrainHeightSourceMode.Procedural;

    public float flatHeight =
        0f;

    public Texture2D importedHeightmap;

    [Header("State")]

    [HideInInspector]
    public int authoringRevision =
        0;
}