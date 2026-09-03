using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/*
 * Runtime-safe deterministic signatures for surface configuration/output.
 *
 * Keeping this outside Editor code lets the runtime streamer verify that the
 * baked SurfaceMaskManifest still matches the TerrainSurfaceSettings asset
 * included in the player.
 */
public static class TerrainSurfaceSignatureUtility
{
    public static string GetSettingsSignature(
        TerrainSurfaceSettings settings
    )
    {
        if (settings == null)
        {
            return "";
        }

        ScreeSettings scree =
            settings.Scree;

        if (scree == null)
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "TerrainSurfaceSettings"
        );

        AppendValue(
            builder,
            scree.slopeMin
        );

        AppendValue(
            builder,
            scree.slopePreferredMin
        );

        AppendValue(
            builder,
            scree.slopePreferredMax
        );

        AppendValue(
            builder,
            scree.slopeMax
        );

        AppendValue(
            builder,
            scree.curvatureScale
        );

        AppendValue(
            builder,
            scree.convexRejectStart
        );

        AppendValue(
            builder,
            scree.convexRejectEnd
        );

        AppendValue(
            builder,
            scree.geologyScale
        );

        AppendValue(
            builder,
            scree.geologyStrength
        );

        return
            ComputeSHA256(
                builder.ToString()
            );
    }

    public static string GetGenerationSignature(
        int compilerVersion,
        int channelLayoutVersion,
        int sourceHeightmapGenerationRevision,
        string sourceHeightmapSignature,
        string surfaceSettingsSignature
    )
    {
        if (
            sourceHeightmapGenerationRevision <= 0
            ||
            string.IsNullOrEmpty(
                sourceHeightmapSignature
            )
            ||
            string.IsNullOrEmpty(
                surfaceSettingsSignature
            )
        )
        {
            return "";
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "TerrainSurfaceMaskGeneration"
        );

        AppendValue(
            builder,
            compilerVersion
        );

        AppendValue(
            builder,
            channelLayoutVersion
        );

        AppendValue(
            builder,
            sourceHeightmapGenerationRevision
        );

        AppendValue(
            builder,
            sourceHeightmapSignature
        );

        AppendValue(
            builder,
            surfaceSettingsSignature
        );

        return
            ComputeSHA256(
                builder.ToString()
            );
    }

    private static void AppendValue(
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

    private static void AppendValue(
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

    private static void AppendValue(
        StringBuilder builder,
        string value
    )
    {
        builder.Append('|');
        builder.Append(
            value ?? ""
        );
    }

    private static string ComputeSHA256(
        string value
    )
    {
        byte[] inputBytes =
            Encoding.UTF8.GetBytes(
                value ?? ""
            );

        byte[] hashBytes;

        using (
            SHA256 sha256 =
                SHA256.Create()
        )
        {
            hashBytes =
                sha256.ComputeHash(
                    inputBytes
                );
        }

        StringBuilder result =
            new StringBuilder(
                hashBytes.Length * 2
            );

        for (
            int index = 0;
            index < hashBytes.Length;
            index++
        )
        {
            result.Append(
                hashBytes[index].ToString(
                    "x2",
                    CultureInfo.InvariantCulture
                )
            );
        }

        return
            result.ToString();
    }
}
