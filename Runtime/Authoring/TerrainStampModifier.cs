using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/*
 * Concrete non-destructive additive height-stamp modifier.
 *
 * Persistent values stored here describe terrain output only. Editor-only
 * selection, handles, diagnostics, and visualization preferences live outside
 * this runtime data type.
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

    /*
     * Positive Unity world-Y rotation around PositionXZ.
     *
     * Existing serialized stamps do not contain this field and therefore
     * naturally deserialize to 0 degrees.
     */
    [SerializeField]
    private float rotationDegrees =
        0f;

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

    [SerializeField]
    [Min(0f)]
    private float smoothingRadius =
        0f;

    [SerializeField]
    [Range(
        0f,
        1f
    )]
    private float smoothingStrength =
        1f;

    public TerrainHeightStampAsset StampAsset
    {
        get
        {
            return stampAsset;
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

    public float RotationDegrees
    {
        get
        {
            return
                TerrainStampTransformUtility
                    .NormalizeRotationDegrees(
                        rotationDegrees
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

    public float SmoothingRadius
    {
        get
        {
            return
                Mathf.Max(
                    0f,
                    SanitizeFinite(
                        smoothingRadius,
                        0f
                    )
                );
        }
    }

    public float SmoothingStrength
    {
        get
        {
            return
                Mathf.Clamp01(
                    SanitizeFinite(
                        smoothingStrength,
                        1f
                    )
                );
        }
    }

    public override Bounds GetAffectedWorldBounds()
    {
        /*
         * Smoothing stays inside the existing mathematical stamp footprint.
         * The compositor clamps Gaussian SOURCE lookup coordinates to the
         * stamp texture boundary while still rejecting terrain samples whose
         * central stamp coordinate lies outside this footprint.
         *
         * Dirty-region and runtime-bake systems consume an axis-aligned Bounds,
         * so rotated stamps report the conservative AABB enclosing their
         * oriented local SizeXZ rectangle.
         */
        return
            TerrainStampTransformUtility
                .CalculateWorldAabb(
                    PositionXZ,
                    SizeXZ,
                    RotationDegrees
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

    internal void SetRotationDegreesInternal(
        float value
    )
    {
        rotationDegrees =
            TerrainStampTransformUtility
                .NormalizeRotationDegrees(
                    value
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

    internal void SetSmoothingRadiusInternal(
        float value
    )
    {
        smoothingRadius =
            Mathf.Max(
                0f,
                SanitizeFinite(
                    value,
                    0f
                )
            );
    }

    internal void SetSmoothingStrengthInternal(
        float value
    )
    {
        smoothingStrength =
            Mathf.Clamp01(
                SanitizeFinite(
                    value,
                    1f
                )
            );
    }

    protected override string GetSignatureTypeId()
    {
        return
            "TerrainStampModifierV3";
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
            RotationDegrees
        );

        AppendFloat(
            builder,
            HeightDelta
        );

        AppendFloat(
            builder,
            Falloff
        );

        AppendFloat(
            builder,
            SmoothingRadius
        );

        AppendFloat(
            builder,
            SmoothingStrength
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
            return fallback;
        }

        return value;
    }
}
