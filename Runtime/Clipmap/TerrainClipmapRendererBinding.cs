using UnityEngine;

public readonly struct TerrainClipmapRendererBinding
{
    public MeshRenderer Renderer { get; }
    public TerrainClipmapRendererRole Role { get; }

    public bool IsValid =>
        Renderer != null
        && Role.IsValid;

    public TerrainClipmapRendererBinding(
        MeshRenderer renderer,
        TerrainClipmapRendererRole role
    )
    {
        Renderer = renderer;
        Role = role;
    }
}
