using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/*
 * Persistent base type for broad regional-elevation authoring data.
 *
 * Package 1 defines source identity/data only. Terrain evaluation,
 * composition, interpolation, and editor authoring tools are added by
 * later packages.
 */
[Serializable]
public abstract class TerrainRegionalElevationSource
{
    /*
     * Appends deterministic, terrain-output-relevant source data.
     *
     * Editor identity is intentionally excluded by concrete source types.
     * Malformed output data rejects the signature instead of being silently
     * canonicalized into a valid authoring state.
     */
    internal bool TryAppendDeterministicSignatureData(
        StringBuilder builder,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (builder == null)
        {
            errorMessage =
                "Regional elevation signature builder is null.";

            return false;
        }

        if (!TryValidateOutputData(
            out errorMessage
        ))
        {
            return false;
        }

        AppendString(
            builder,
            GetSignatureTypeId()
        );

        AppendTypeSpecificSignatureData(
            builder
        );

        return true;
    }

    /*
     * Validates only data that can affect terrain output.
     *
     * Persistent editor identity must not participate here so repairing an
     * identity cannot invalidate otherwise identical terrain output.
     */
    internal abstract bool TryValidateOutputData(
        out string errorMessage
    );

    protected abstract string GetSignatureTypeId();

    protected abstract void AppendTypeSpecificSignatureData(
        StringBuilder builder
    );

    protected static void AppendString(
        StringBuilder builder,
        string value
    )
    {
        string safeValue =
            value ??
            "";

        builder.Append('|');

        builder.Append(
            safeValue.Length.ToString(
                CultureInfo.InvariantCulture
            )
        );

        builder.Append(':');

        builder.Append(
            safeValue
        );
    }

    protected static void AppendInt(
        StringBuilder builder,
        int value
    )
    {
        builder.Append('|');

        builder.Append(
            value.ToString(
                CultureInfo.InvariantCulture
            )
        );
    }

    protected static void AppendFloat(
        StringBuilder builder,
        float value
    )
    {
        builder.Append('|');

        builder.Append(
            value.ToString(
                "R",
                CultureInfo.InvariantCulture
            )
        );
    }

    protected static void AppendVector2(
        StringBuilder builder,
        Vector2 value
    )
    {
        AppendFloat(
            builder,
            value.x
        );

        AppendFloat(
            builder,
            value.y
        );
    }
}
