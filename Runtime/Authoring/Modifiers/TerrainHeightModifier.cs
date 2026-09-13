using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/*
 * Persistent base type for non-destructive height authoring modifiers.
 *
 * Stage 11 defines modifier identity/data only. It does not perform
 * preview composition or runtime terrain evaluation.
 */
[Serializable]
public abstract class TerrainHeightModifier
{
    [SerializeField]
    [HideInInspector]
    private string stableId =
        "";

    [SerializeField]
    private bool enabled =
        true;

    [SerializeField]
    private TerrainHeightBlendMode blendMode =
        TerrainHeightBlendMode.Additive;

    public string StableId
    {
        get
        {
            return
                stableId;
        }
    }

    public bool Enabled
    {
        get
        {
            return
                enabled;
        }
    }

    public TerrainHeightBlendMode BlendMode
    {
        get
        {
            return
                blendMode;
        }
    }

    /*
     * Returns the complete mathematical XZ footprint of the modifier.
     *
     * Do not clamp this to world bounds. Dirty-region code owns world
     * clipping and tile conversion.
     *
     * Disabled modifiers still report their footprint so disabling or
     * deleting one can invalidate the region that previously contained
     * its contribution.
     */
    public abstract Bounds GetAffectedWorldBounds();

    /*
     * Appends deterministic, output-relevant data for the overall
     * authoring signature.
     *
     * StableId is intentionally NOT appended: it is editor identity,
     * not terrain output. Repairing an ID must not make generated
     * terrain stale when all output-affecting data is unchanged.
     */
    internal void AppendDeterministicSignatureData(
        StringBuilder builder
    )
    {
        if (builder == null)
        {
            return;
        }

        AppendString(
            builder,
            GetSignatureTypeId()
        );

        AppendBool(
            builder,
            enabled
        );

        AppendInt(
            builder,
            (int)blendMode
        );

        AppendTypeSpecificSignatureData(
            builder
        );
    }

    /*
     * Dependencies are resolved to Asset GUID + dependency hash by the
     * editor-side TerrainAuthoringStateUtility.
     */
    internal virtual void CollectSignatureDependencies(
        List<UnityEngine.Object> dependencies
    )
    {
    }

    internal bool EnsureUniqueStableId(
        HashSet<string> usedIds
    )
    {
        if (usedIds == null)
        {
            return false;
        }

        string normalized =
            NormalizeStableId(
                stableId
            );

        bool valid =
            !string.IsNullOrEmpty(
                normalized
            )
            &&
            !usedIds.Contains(
                normalized
            );

        if (!valid)
        {
            normalized =
                GenerateStableId();

            while (
                usedIds.Contains(
                    normalized
                )
            )
            {
                normalized =
                    GenerateStableId();
            }
        }

        bool changed =
            stableId !=
            normalized;

        stableId =
            normalized;

        usedIds.Add(
            stableId
        );

        return changed;
    }

    internal bool HasValidStableId()
    {
        return
            !string.IsNullOrEmpty(
                NormalizeStableId(
                    stableId
                )
            );
    }

    /*
     * Stage 12's authoring mutation service will be the production
     * caller for these internal setters.
     */
    internal void SetEnabledInternal(
        bool value
    )
    {
        enabled =
            value;
    }

    internal void SetBlendModeInternal(
        TerrainHeightBlendMode value
    )
    {
        blendMode =
            value;
    }

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

    protected static void AppendBool(
        StringBuilder builder,
        bool value
    )
    {
        builder.Append('|');

        builder.Append(
            value
                ? '1'
                : '0'
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

    private static string GenerateStableId()
    {
        return
            Guid.NewGuid()
                .ToString(
                    "N"
                );
    }

    private static string NormalizeStableId(
        string value
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                value
            )
        )
        {
            return
                "";
        }

        string trimmed =
            value.Trim();

        if (
            !Guid.TryParseExact(
                trimmed,
                "N",
                out Guid parsed
            )
        )
        {
            return
                "";
        }

        return
            parsed.ToString(
                "N"
            );
    }
}
