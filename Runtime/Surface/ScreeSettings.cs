using System;
using UnityEngine;

/*
 * Suitability/rule configuration for Scree.
 *
 * Surface appearance (texture/tint/triplanar settings) intentionally remains
 * material-owned. These values answer "where should Scree exist?" and are
 * therefore shared configuration for rendering, baking, diagnostics, and
 * future placement systems.
 */
[Serializable]
public sealed class ScreeSettings
{
    [Header("Slope")]

    [Range(0f, 90f)]
    public float slopeMin =
        15f;

    [Range(0f, 90f)]
    public float slopePreferredMin =
        25f;

    [Range(0f, 90f)]
    public float slopePreferredMax =
        40f;

    [Range(0f, 90f)]
    public float slopeMax =
        55f;

    [Header("Curvature")]

    [Range(1f, 256f)]
    public float curvatureScale =
        16f;

    [Range(0f, 0.5f)]
    public float convexRejectStart =
        0.03f;

    [Range(0f, 0.5f)]
    public float convexRejectEnd =
        0.12f;

    [Header("Geological Variation")]

    [Range(1f, 512f)]
    public float geologyScale =
        120f;

    [Range(0f, 1f)]
    public float geologyStrength =
        0.45f;

    public void Sanitize()
    {
        slopeMin =
            Mathf.Clamp(
                slopeMin,
                0f,
                90f
            );

        slopePreferredMin =
            Mathf.Clamp(
                slopePreferredMin,
                0f,
                90f
            );

        slopePreferredMax =
            Mathf.Clamp(
                slopePreferredMax,
                0f,
                90f
            );

        slopeMax =
            Mathf.Clamp(
                slopeMax,
                0f,
                90f
            );

        curvatureScale =
            Mathf.Clamp(
                curvatureScale,
                1f,
                256f
            );

        convexRejectStart =
            Mathf.Clamp(
                convexRejectStart,
                0f,
                0.5f
            );

        convexRejectEnd =
            Mathf.Clamp(
                convexRejectEnd,
                0f,
                0.5f
            );

        geologyScale =
            Mathf.Clamp(
                geologyScale,
                1f,
                512f
            );

        geologyStrength =
            Mathf.Clamp01(
                geologyStrength
            );
    }
}
