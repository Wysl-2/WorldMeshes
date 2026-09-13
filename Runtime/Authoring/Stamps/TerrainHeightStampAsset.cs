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
        2;

    private const int CreationDefaultsVersionV1 =
        1;

    private const int CreationDefaultsVersionV2 =
        2;

    private const float MinimumDefaultSize =
        0.01f;

    private const float BaselineDefaultSize =
        128f;

    private const float BaselineDefaultHeightDelta =
        10f;

    private const float BaselineDefaultTargetBaseHeight =
        0f;

    private const float BaselineDefaultTargetHeightRange =
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
    private TerrainHeightBlendMode defaultBlendMode =
        TerrainHeightBlendMode.Additive;

    [SerializeField]
    private float defaultTargetBaseHeight =
        BaselineDefaultTargetBaseHeight;

    [SerializeField]
    private float defaultTargetHeightRange =
        BaselineDefaultTargetHeightRange;

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
                HasV1CreationDefaults
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
                HasV1CreationDefaults
                    ? defaultHeightDelta
                    : BaselineDefaultHeightDelta;

            return
                IsFinite(value)
                    ? value
                    : BaselineDefaultHeightDelta;
        }
    }

    public TerrainHeightBlendMode DefaultBlendMode
    {
        get
        {
            TerrainHeightBlendMode value =
                HasV2BlendCreationDefaults
                    ? defaultBlendMode
                    : TerrainHeightBlendMode.Additive;

            return
                TerrainHeightBlendModeUtility
                    .Sanitize(
                        value
                    );
        }
    }

    public float DefaultTargetBaseHeight
    {
        get
        {
            float value =
                HasV2BlendCreationDefaults
                    ? defaultTargetBaseHeight
                    : BaselineDefaultTargetBaseHeight;

            return
                IsFinite(value)
                    ? value
                    : BaselineDefaultTargetBaseHeight;
        }
    }

    public float DefaultTargetHeightRange
    {
        get
        {
            float value =
                HasV2BlendCreationDefaults
                    ? defaultTargetHeightRange
                    : BaselineDefaultTargetHeightRange;

            return
                IsFinite(value)
                    ? value
                    : BaselineDefaultTargetHeightRange;
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
                HasV1CreationDefaults
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
                HasV1CreationDefaults
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
                HasV1CreationDefaults
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
                HasV1CreationDefaults
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
                HasV1CreationDefaults
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

    private bool HasV1CreationDefaults
    {
        get
        {
            return
                creationDefaultsVersion >=
                    CreationDefaultsVersionV1;
        }
    }

    private bool HasV2BlendCreationDefaults
    {
        get
        {
            return
                creationDefaultsVersion >=
                    CreationDefaultsVersionV2;
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

    /*
     * Backward-compatible Package 3 creation-default setter.
     *
     * Existing callers that do not provide blend-specific defaults retain
     * their established Additive creation behavior while being upgraded to a
     * complete Version 2 default set.
     */
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
        SetCreationDefaultsInternal(
            sizeXZ,
            TerrainHeightBlendMode.Additive,
            heightDelta,
            BaselineDefaultTargetBaseHeight,
            BaselineDefaultTargetHeightRange,
            sourceInputMin,
            sourceInputMax,
            sourceGamma,
            falloffShape,
            falloffProfile,
            falloff,
            smoothingRadius,
            smoothingStrength
        );
    }

    internal void SetCreationDefaultsInternal(
        Vector2 sizeXZ,
        TerrainHeightBlendMode blendMode,
        float heightDelta,
        float targetBaseHeight,
        float targetHeightRange,
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

        defaultBlendMode =
            TerrainHeightBlendModeUtility
                .Sanitize(
                    blendMode
                );

        defaultHeightDelta =
            IsFinite(heightDelta)
                ? heightDelta
                : BaselineDefaultHeightDelta;

        defaultTargetBaseHeight =
            IsFinite(targetBaseHeight)
                ? targetBaseHeight
                : BaselineDefaultTargetBaseHeight;

        defaultTargetHeightRange =
            IsFinite(targetHeightRange)
                ? targetHeightRange
                : BaselineDefaultTargetHeightRange;

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
        if (!HasV1CreationDefaults)
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
