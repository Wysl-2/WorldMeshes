using UnityEngine;

/*
 * Pure source-value response math shared by runtime stamp data, editor
 * mutations, Package 2 UI constraints, and validation.
 *
 * This utility does not know about stamp geometry, source UVs, smoothing,
 * Falloff, preview state, or editor transactions.
 */
public static class TerrainStampSourceRemapUtility
{
    public const float IdentityInputMin =
        0f;

    public const float IdentityInputMax =
        1f;

    public const float IdentityGamma =
        1f;

    public const float MinimumInputRange =
        0.0001f;

    public const float MinimumGamma =
        0.01f;

    /*
     * Canonicalizes values that came from serialized backing fields.
     *
     * An invalid/degenerate stored input range is interpreted as complete
     * identity. This is deliberate backwards compatibility for old
     * SerializeReference stamp data that predates non-zero InputMax/Gamma
     * fields and may therefore expose default(float) for missing fields.
     */
    public static void SanitizeStoredValues(
        float inputMin,
        float inputMax,
        float gamma,
        out float safeInputMin,
        out float safeInputMax,
        out float safeGamma
    )
    {
        safeInputMin =
            Mathf.Clamp01(
                SanitizeFinite(
                    inputMin,
                    IdentityInputMin
                )
            );

        safeInputMax =
            Mathf.Clamp01(
                SanitizeFinite(
                    inputMax,
                    IdentityInputMax
                )
            );

        if (
            safeInputMax -
                safeInputMin
            <
            MinimumInputRange
        )
        {
            SetIdentity(
                out safeInputMin,
                out safeInputMax,
                out safeGamma
            );

            return;
        }

        float storedGamma =
            SanitizeFinite(
                gamma,
                IdentityGamma
            );

        safeGamma =
            storedGamma <
                MinimumGamma
                ? IdentityGamma
                : storedGamma;
    }

    /*
     * Canonicalizes an explicitly requested three-value state.
     *
     * Min is kept inside a range where a legal Max always exists. Max is then
     * raised as necessary to preserve MinimumInputRange. Normal production UI
     * keeps edits in-range already; this is the durable caller-independent
     * fallback.
     */
    public static void SanitizeRequestedValues(
        float inputMin,
        float inputMax,
        float gamma,
        out float safeInputMin,
        out float safeInputMax,
        out float safeGamma
    )
    {
        safeInputMin =
            Mathf.Clamp(
                SanitizeFinite(
                    inputMin,
                    IdentityInputMin
                ),
                0f,
                1f -
                    MinimumInputRange
            );

        safeInputMax =
            Mathf.Clamp01(
                SanitizeFinite(
                    inputMax,
                    IdentityInputMax
                )
            );

        safeInputMax =
            Mathf.Max(
                safeInputMax,
                safeInputMin +
                    MinimumInputRange
            );

        safeInputMax =
            Mathf.Min(
                1f,
                safeInputMax
            );

        safeGamma =
            SanitizeGamma(
                gamma
            );
    }

    /*
     * Sanitizes only Input Min while preserving the current canonical Max.
     */
    public static float SanitizeInputMin(
        float value,
        float currentInputMax
    )
    {
        float safeCurrentMax =
            Mathf.Clamp(
                SanitizeFinite(
                    currentInputMax,
                    IdentityInputMax
                ),
                MinimumInputRange,
                1f
            );

        float maximum =
            Mathf.Max(
                0f,
                safeCurrentMax -
                    MinimumInputRange
            );

        return
            Mathf.Clamp(
                SanitizeFinite(
                    value,
                    IdentityInputMin
                ),
                0f,
                maximum
            );
    }

    /*
     * Sanitizes only Input Max while preserving the current canonical Min.
     */
    public static float SanitizeInputMax(
        float value,
        float currentInputMin
    )
    {
        float safeCurrentMin =
            Mathf.Clamp(
                SanitizeFinite(
                    currentInputMin,
                    IdentityInputMin
                ),
                0f,
                1f -
                    MinimumInputRange
            );

        float minimum =
            Mathf.Min(
                1f,
                safeCurrentMin +
                    MinimumInputRange
            );

        return
            Mathf.Clamp(
                SanitizeFinite(
                    value,
                    IdentityInputMax
                ),
                minimum,
                1f
            );
    }

    public static float SanitizeGamma(
        float value
    )
    {
        return
            Mathf.Max(
                MinimumGamma,
                SanitizeFinite(
                    value,
                    IdentityGamma
                )
            );
    }

    /*
     * CPU reference implementation for validation and future editor previews.
     *
     * The compute shader contains the authoritative GPU implementation used by
     * authoring preview and runtime height compilation.
     */
    public static float Evaluate(
        float sourceWeight,
        float inputMin,
        float inputMax,
        float gamma
    )
    {
        SanitizeStoredValues(
            inputMin,
            inputMax,
            gamma,
            out float safeInputMin,
            out float safeInputMax,
            out float safeGamma
        );

        float safeSourceWeight =
            Mathf.Clamp01(
                SanitizeFinite(
                    sourceWeight,
                    0f
                )
            );

        if (
            Mathf.Approximately(
                safeInputMin,
                IdentityInputMin
            )
            &&
            Mathf.Approximately(
                safeInputMax,
                IdentityInputMax
            )
            &&
            Mathf.Approximately(
                safeGamma,
                IdentityGamma
            )
        )
        {
            return safeSourceWeight;
        }

        float inputRange =
            Mathf.Max(
                MinimumInputRange,
                safeInputMax -
                    safeInputMin
            );

        float normalizedWeight =
            Mathf.Clamp01(
                (
                    safeSourceWeight -
                        safeInputMin
                )
                /
                inputRange
            );

        return
            Mathf.Clamp01(
                Mathf.Pow(
                    normalizedWeight,
                    safeGamma
                )
            );
    }

    private static void SetIdentity(
        out float inputMin,
        out float inputMax,
        out float gamma
    )
    {
        inputMin =
            IdentityInputMin;

        inputMax =
            IdentityInputMax;

        gamma =
            IdentityGamma;
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
