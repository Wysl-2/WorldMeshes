using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Persistent control data for one node in a node-based regional elevation
 * source.
 *
 * Package 1 stores identity and authoring values only. It does not define how
 * nodes interpolate or affect terrain.
 */
[Serializable]
public sealed class TerrainElevationNode
{
    [SerializeField]
    [HideInInspector]
    private string stableId =
        "";

    [SerializeField]
    private Vector2 positionXZ =
        Vector2.zero;

    [SerializeField]
    private float elevation =
        0f;

    public string StableId
    {
        get
        {
            return
                stableId;
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

    public float Elevation
    {
        get
        {
            return
                SanitizeFinite(
                    elevation,
                    0f
                );
        }
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
     * This inspects the stored values directly rather than the sanitized public
     * accessors. Corrupted serialized data therefore cannot silently become a
     * valid deterministic terrain signature.
     */
    internal bool TryValidateOutputData(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            !IsFinite(
                positionXZ.x
            )
            ||
            !IsFinite(
                positionXZ.y
            )
        )
        {
            errorMessage =
                "Elevation node position contains a non-finite value.";

            return false;
        }

        if (!IsFinite(
            elevation
        ))
        {
            errorMessage =
                "Elevation node height contains a non-finite value.";

            return false;
        }

        return true;
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

    internal void SetElevationInternal(
        float value
    )
    {
        elevation =
            SanitizeFinite(
                value,
                0f
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

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value)
            &&
            !float.IsInfinity(value);
    }

    private static float SanitizeFinite(
        float value,
        float fallback
    )
    {
        return
            IsFinite(value)
                ? value
                : fallback;
    }
}
