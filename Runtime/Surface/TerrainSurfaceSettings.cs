using UnityEngine;

[CreateAssetMenu(
    fileName = "TerrainSurfaceSettings",
    menuName = "WorldMeshes/Terrain Surface Settings"
)]
public sealed class TerrainSurfaceSettings :
    ScriptableObject
{
    /*
     * Runtime loading uses a Resources folder so terrain configuration can be
     * obtained without an editor-only AssetDatabase dependency.
     */
    public const string DefaultResourcesPath =
        "TerrainSurfaceSettings";

    [Header("Scree")]

    [SerializeField]
    private ScreeSettings scree =
        new ScreeSettings();

    public ScreeSettings Scree
    {
        get
        {
            if (scree == null)
            {
                scree =
                    new ScreeSettings();
            }

            return scree;
        }
    }

    public static TerrainSurfaceSettings LoadDefault()
    {
        return
            Resources.Load<TerrainSurfaceSettings>(
                DefaultResourcesPath
            );
    }

    private void OnValidate()
    {
        Scree.Sanitize();
    }
}
