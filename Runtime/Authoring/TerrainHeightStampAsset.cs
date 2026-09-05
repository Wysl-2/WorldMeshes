using UnityEngine;

/*
 * Reusable height-stamp source data and creation defaults.
 *
 * Library identity is deliberately independent from the source Texture2D
 * filename. A texture such as:
 *
 *     heightmap_20260905222247_64S6.png
 *
 * can therefore be represented to authors as:
 *
 *     Heightmap_001
 *
 * without renaming or deriving identity from the source file.
 *
 * LibraryId == 0 is reserved for non-library/transient/generated validation
 * fixtures. User-facing library assets require a positive stable ID.
 *
 * Creation defaults are copied into a TerrainStampModifier when that modifier
 * is created. The modifier is independent afterward; this asset is not a live
 * parameter source for already-authored terrain.
 */
[CreateAssetMenu(
    fileName = "TerrainHeightStamp",
    menuName = "WorldMeshes/Terrain Height Stamp"
)]
public sealed class TerrainHeightStampAsset :
    ScriptableObject
{
    public const int CurrentCreationDefaultsVersion =
        1;

    private const float MinimumDefaultSize =
        0.01f;

    private const float BaselineDefaultSize =
        128f;

    private const float BaselineDefaultHeightDelta =
        10f;

    private const float BaselineDefaultFalloff =
        0.25f;

    private const float BaselineDefaultSmoothingRadius =
        0f;

    private const float BaselineDefaultSmoothingStrength =
        1f;

    [SerializeField]
    private int libraryId =
        TerrainHeightStampIdentityUtility
            .UnassignedLibraryId;

    [SerializeField]
    private Texture2D heightTexture;

    [SerializeField]
    private int creationDefaultsVersion =
        CurrentCreationDefaultsVersion;

    [SerializeField]
    private Vector2 defaultSizeXZ =
        new Vector2(
            BaselineDefaultSize,
            BaselineDefaultSize
        );

    [SerializeField]
    private float defaultHeightDelta =
        BaselineDefaultHeightDelta;

    [SerializeField]
    private float defaultSourceInputMin =
        TerrainStampSourceRemapUtility
            .IdentityInputMin;

    [SerializeField]
    private float defaultSourceInputMax =
        TerrainStampSourceRemapUtility
            .IdentityInputMax;

    [SerializeField]
    private float defaultSourceGamma =
        TerrainStampSourceRemapUtility
            .IdentityGamma;

    [SerializeField]
    private TerrainStampFalloffShape defaultFalloffShape =
        TerrainStampFalloffShape.Rectangle;

    [SerializeField]
    private TerrainStampFalloffProfile defaultFalloffProfile =
        TerrainStampFalloffProfile.Smooth;

    [SerializeField]
    [Range(0f, 1f)]
    private float defaultFalloff =
        BaselineDefaultFalloff;

    [SerializeField]
    [Min(0f)]
    private float defaultSmoothingRadius =
        BaselineDefaultSmoothingRadius;

    [SerializeField]
    [Range(0f, 1f)]
    private float defaultSmoothingStrength =
        BaselineDefaultSmoothingStrength;

    public int LibraryId
    {
        get
        {
            return libraryId;
        }
    }

    public bool HasLibraryId
    {
        get
        {
            return
                TerrainHeightStampIdentityUtility
                    .IsValidLibraryId(
                        libraryId
                    );
        }
    }

    public string DisplayName
    {
        get
        {
            if (HasLibraryId)
            {
                return
                    TerrainHeightStampIdentityUtility
                        .FormatDisplayName(
                            libraryId
                        );
            }

            return
                !string.IsNullOrEmpty(
                    name
                )
                    ? name
                    : TerrainHeightStampIdentityUtility
                        .FormatDisplayName(
                            TerrainHeightStampIdentityUtility
                                .UnassignedLibraryId
                        );
        }
    }

    public Texture2D HeightTexture
    {
        get
        {
            return
                heightTexture;
        }
    }

    public bool IsConfigured
    {
        get
        {
            return
                heightTexture != null;
        }
    }

    public Vector2 DefaultSizeXZ
    {
        get
        {
            Vector2 value =
                HasStoredCreationDefaults
                    ? defaultSizeXZ
                    : new Vector2(
                        BaselineDefaultSize,
                        BaselineDefaultSize
                    );

            return
                SanitizeSizeXZ(
                    value
                );
        }
    }

    public float DefaultHeightDelta
    {
        get
        {
            float value =
                HasStoredCreationDefaults
                    ? defaultHeightDelta
                    : BaselineDefaultHeightDelta;

            return
                IsFinite(value)
                    ? value
                    : BaselineDefaultHeightDelta;
        }
    }

    public float DefaultSourceInputMin
    {
        get
        {
            GetSanitizedSourceDefaults(
                out float inputMin,
                out _,
                out _
            );

            return inputMin;
        }
    }

    public float DefaultSourceInputMax
    {
        get
        {
            GetSanitizedSourceDefaults(
                out _,
                out float inputMax,
                out _
            );

            return inputMax;
        }
    }

    public float DefaultSourceGamma
    {
        get
        {
            GetSanitizedSourceDefaults(
                out _,
                out _,
                out float gamma
            );

            return gamma;
        }
    }

    public TerrainStampFalloffShape DefaultFalloffShape
    {
        get
        {
            TerrainStampFalloffShape value =
                HasStoredCreationDefaults
                    ? defaultFalloffShape
                    : TerrainStampFalloffShape.Rectangle;

            return
                TerrainStampFalloffUtility
                    .SanitizeShape(
                        value
                    );
        }
    }

    public TerrainStampFalloffProfile DefaultFalloffProfile
    {
        get
        {
            TerrainStampFalloffProfile value =
                HasStoredCreationDefaults
                    ? defaultFalloffProfile
                    : TerrainStampFalloffProfile.Smooth;

            return
                TerrainStampFalloffUtility
                    .SanitizeProfile(
                        value
                    );
        }
    }

    public float DefaultFalloff
    {
        get
        {
            float value =
                HasStoredCreationDefaults
                    ? defaultFalloff
                    : BaselineDefaultFalloff;

            return
                TerrainStampFalloffUtility
                    .SanitizeAmount(
                        value
                    );
        }
    }

    public float DefaultSmoothingRadius
    {
        get
        {
            float value =
                HasStoredCreationDefaults
                    ? defaultSmoothingRadius
                    : BaselineDefaultSmoothingRadius;

            if (!IsFinite(value))
            {
                return
                    BaselineDefaultSmoothingRadius;
            }

            return
                Mathf.Max(
                    0f,
                    value
                );
        }
    }

    public float DefaultSmoothingStrength
    {
        get
        {
            float value =
                HasStoredCreationDefaults
                    ? defaultSmoothingStrength
                    : BaselineDefaultSmoothingStrength;

            if (!IsFinite(value))
            {
                return
                    BaselineDefaultSmoothingStrength;
            }

            return
                Mathf.Clamp01(
                    value
                );
        }
    }

    private bool HasStoredCreationDefaults
    {
        get
        {
            return
                creationDefaultsVersion >=
                    CurrentCreationDefaultsVersion;
        }
    }

    internal void SetLibraryIdInternal(
        int value
    )
    {
        libraryId =
            Mathf.Max(
                TerrainHeightStampIdentityUtility
                    .UnassignedLibraryId,
                value
            );
    }

    internal void SetHeightTextureInternal(
        Texture2D texture
    )
    {
        heightTexture =
            texture;
    }

    internal void SetCreationDefaultsInternal(
        Vector2 sizeXZ,
        float heightDelta,
        float sourceInputMin,
        float sourceInputMax,
        float sourceGamma,
        TerrainStampFalloffShape falloffShape,
        TerrainStampFalloffProfile falloffProfile,
        float falloff,
        float smoothingRadius,
        float smoothingStrength
    )
    {
        creationDefaultsVersion =
            CurrentCreationDefaultsVersion;

        defaultSizeXZ =
            SanitizeSizeXZ(
                sizeXZ
            );

        defaultHeightDelta =
            IsFinite(heightDelta)
                ? heightDelta
                : BaselineDefaultHeightDelta;

        TerrainStampSourceRemapUtility
            .SanitizeRequestedValues(
                sourceInputMin,
                sourceInputMax,
                sourceGamma,
                out defaultSourceInputMin,
                out defaultSourceInputMax,
                out defaultSourceGamma
            );

        defaultFalloffShape =
            TerrainStampFalloffUtility
                .SanitizeShape(
                    falloffShape
                );

        defaultFalloffProfile =
            TerrainStampFalloffUtility
                .SanitizeProfile(
                    falloffProfile
                );

        defaultFalloff =
            TerrainStampFalloffUtility
                .SanitizeAmount(
                    falloff
                );

        defaultSmoothingRadius =
            IsFinite(smoothingRadius)
                ? Mathf.Max(
                    0f,
                    smoothingRadius
                )
                : BaselineDefaultSmoothingRadius;

        defaultSmoothingStrength =
            IsFinite(smoothingStrength)
                ? Mathf.Clamp01(
                    smoothingStrength
                )
                : BaselineDefaultSmoothingStrength;
    }

    private void GetSanitizedSourceDefaults(
        out float inputMin,
        out float inputMax,
        out float gamma
    )
    {
        if (!HasStoredCreationDefaults)
        {
            inputMin =
                TerrainStampSourceRemapUtility
                    .IdentityInputMin;

            inputMax =
                TerrainStampSourceRemapUtility
                    .IdentityInputMax;

            gamma =
                TerrainStampSourceRemapUtility
                    .IdentityGamma;

            return;
        }

        TerrainStampSourceRemapUtility
            .SanitizeStoredValues(
                defaultSourceInputMin,
                defaultSourceInputMax,
                defaultSourceGamma,
                out inputMin,
                out inputMax,
                out gamma
            );
    }

    private static Vector2 SanitizeSizeXZ(
        Vector2 value
    )
    {
        return
            new Vector2(
                SanitizeSizeComponent(
                    value.x
                ),
                SanitizeSizeComponent(
                    value.y
                )
            );
    }

    private static float SanitizeSizeComponent(
        float value
    )
    {
        if (!IsFinite(value))
        {
            return
                BaselineDefaultSize;
        }

        return
            Mathf.Max(
                MinimumDefaultSize,
                Mathf.Abs(value)
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
}
