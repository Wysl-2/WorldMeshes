using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/*
 * First concrete non-destructive height modifier data type.
 *
 * No terrain composition is performed here. The class only stores the
 * persistent authoring parameters required by later compositor stages.
 */
[Serializable]
public sealed class TerrainStampModifier :
    TerrainHeightModifier
{
    private const float MinimumSize =
        0.01f;

    [SerializeField]
    private TerrainHeightStampAsset stampAsset;

    [SerializeField]
    private Vector2 positionXZ =
        Vector2.zero;

    [SerializeField]
    private Vector2 sizeXZ =
        new Vector2(
            128f,
            128f
        );

    [SerializeField]
    private float heightDelta =
        10f;

    [SerializeField]
    [Range(
        0f,
        1f
    )]
    private float falloff =
        0.25f;

    public TerrainHeightStampAsset StampAsset
    {
        get
        {
            return
                stampAsset;
        }
    }

    public Vector2 PositionXZ
    {
        get
        {
            return
                new Vector2(
                    SanitizeFinite(
                        positionXZ.x,
                        0f
                    ),
                    SanitizeFinite(
                        positionXZ.y,
                        0f
                    )
                );
        }
    }

    public Vector2 SizeXZ
    {
        get
        {
            return
                new Vector2(
                    Mathf.Max(
                        MinimumSize,
                        Mathf.Abs(
                            SanitizeFinite(
                                sizeXZ.x,
                                MinimumSize
                            )
                        )
                    ),
                    Mathf.Max(
                        MinimumSize,
                        Mathf.Abs(
                            SanitizeFinite(
                                sizeXZ.y,
                                MinimumSize
                            )
                        )
                    )
                );
        }
    }

    public float HeightDelta
    {
        get
        {
            return
                SanitizeFinite(
                    heightDelta,
                    0f
                );
        }
    }

    public float Falloff
    {
        get
        {
            return
                Mathf.Clamp01(
                    SanitizeFinite(
                        falloff,
                        0f
                    )
                );
        }
    }

    public override Bounds GetAffectedWorldBounds()
    {
        Vector2 safePosition =
            PositionXZ;

        Vector2 safeSize =
            SizeXZ;

        return
            new Bounds(
                new Vector3(
                    safePosition.x,
                    0f,
                    safePosition.y
                ),
                new Vector3(
                    safeSize.x,
                    0f,
                    safeSize.y
                )
            );
    }

    internal override void CollectSignatureDependencies(
        List<UnityEngine.Object> dependencies
    )
    {
        if (
            dependencies == null
            ||
            stampAsset == null
        )
        {
            return;
        }

        dependencies.Add(
            stampAsset
        );
    }

    internal void SetStampAssetInternal(
        TerrainHeightStampAsset value
    )
    {
        stampAsset =
            value;
    }

    internal void SetPositionXZInternal(
        Vector2 value
    )
    {
        positionXZ =
            new Vector2(
                SanitizeFinite(
                    value.x,
                    0f
                ),
                SanitizeFinite(
                    value.y,
                    0f
                )
            );
    }

    internal void SetSizeXZInternal(
        Vector2 value
    )
    {
        sizeXZ =
            new Vector2(
                Mathf.Max(
                    MinimumSize,
                    Mathf.Abs(
                        SanitizeFinite(
                            value.x,
                            MinimumSize
                        )
                    )
                ),
                Mathf.Max(
                    MinimumSize,
                    Mathf.Abs(
                        SanitizeFinite(
                            value.y,
                            MinimumSize
                        )
                    )
                )
            );
    }

    internal void SetHeightDeltaInternal(
        float value
    )
    {
        heightDelta =
            SanitizeFinite(
                value,
                0f
            );
    }

    internal void SetFalloffInternal(
        float value
    )
    {
        falloff =
            Mathf.Clamp01(
                SanitizeFinite(
                    value,
                    0f
                )
            );
    }

    protected override string GetSignatureTypeId()
    {
        return
            "TerrainStampModifierV1";
    }

    protected override void AppendTypeSpecificSignatureData(
        StringBuilder builder
    )
    {
        AppendVector2(
            builder,
            PositionXZ
        );

        AppendVector2(
            builder,
            SizeXZ
        );

        AppendFloat(
            builder,
            HeightDelta
        );

        AppendFloat(
            builder,
            Falloff
        );
    }

    private static float SanitizeFinite(
        float value,
        float fallback
    )
    {
        if (
            float.IsNaN(
                value
            )
            ||
            float.IsInfinity(
                value
            )
        )
        {
            return
                fallback;
        }

        return
            value;
    }
}
