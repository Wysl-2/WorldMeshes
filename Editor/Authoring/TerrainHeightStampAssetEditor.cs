using UnityEditor;
using UnityEngine;

[CustomEditor(
    typeof(TerrainHeightStampAsset)
)]
public sealed class TerrainHeightStampAssetEditor :
    Editor
{
    private const float MinimumSize =
        0.01f;

    private SerializedProperty heightTextureProperty;
    private SerializedProperty creationDefaultsVersionProperty;
    private SerializedProperty defaultSizeXZProperty;
    private SerializedProperty defaultHeightDeltaProperty;
    private SerializedProperty defaultSourceInputMinProperty;
    private SerializedProperty defaultSourceInputMaxProperty;
    private SerializedProperty defaultSourceGammaProperty;
    private SerializedProperty defaultFalloffShapeProperty;
    private SerializedProperty defaultFalloffProfileProperty;
    private SerializedProperty defaultFalloffProperty;
    private SerializedProperty defaultSmoothingRadiusProperty;
    private SerializedProperty defaultSmoothingStrengthProperty;

    private void OnEnable()
    {
        heightTextureProperty =
            serializedObject.FindProperty(
                "heightTexture"
            );

        creationDefaultsVersionProperty =
            serializedObject.FindProperty(
                "creationDefaultsVersion"
            );

        defaultSizeXZProperty =
            serializedObject.FindProperty(
                "defaultSizeXZ"
            );

        defaultHeightDeltaProperty =
            serializedObject.FindProperty(
                "defaultHeightDelta"
            );

        defaultSourceInputMinProperty =
            serializedObject.FindProperty(
                "defaultSourceInputMin"
            );

        defaultSourceInputMaxProperty =
            serializedObject.FindProperty(
                "defaultSourceInputMax"
            );

        defaultSourceGammaProperty =
            serializedObject.FindProperty(
                "defaultSourceGamma"
            );

        defaultFalloffShapeProperty =
            serializedObject.FindProperty(
                "defaultFalloffShape"
            );

        defaultFalloffProfileProperty =
            serializedObject.FindProperty(
                "defaultFalloffProfile"
            );

        defaultFalloffProperty =
            serializedObject.FindProperty(
                "defaultFalloff"
            );

        defaultSmoothingRadiusProperty =
            serializedObject.FindProperty(
                "defaultSmoothingRadius"
            );

        defaultSmoothingStrengthProperty =
            serializedObject.FindProperty(
                "defaultSmoothingStrength"
            );
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        TerrainHeightStampAsset stampAsset =
            (TerrainHeightStampAsset)target;

        GUILayout.Label(
            "Identity",
            EditorStyles.boldLabel
        );

        EditorGUI.BeginDisabledGroup(
            true
        );

        EditorGUILayout.IntField(
            "Library ID",
            stampAsset.LibraryId
        );

        EditorGUILayout.TextField(
            "Display Name",
            stampAsset.DisplayName
        );

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            8f
        );

        GUILayout.Label(
            "Height Source",
            EditorStyles.boldLabel
        );

        EditorGUI.BeginDisabledGroup(
            true
        );

        EditorGUILayout.ObjectField(
            "Height Texture",
            heightTextureProperty.objectReferenceValue,
            typeof(Texture2D),
            false
        );

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "Height Texture and library identity are managed by the WorldMeshes stamp library.",
            MessageType.Info
        );

        GUILayout.Space(
            8f
        );

        Vector2 sizeXZ =
            stampAsset.DefaultSizeXZ;

        float heightDelta =
            stampAsset.DefaultHeightDelta;

        float sourceInputMin =
            stampAsset.DefaultSourceInputMin;

        float sourceInputMax =
            stampAsset.DefaultSourceInputMax;

        float sourceGamma =
            stampAsset.DefaultSourceGamma;

        TerrainStampFalloffShape falloffShape =
            stampAsset.DefaultFalloffShape;

        TerrainStampFalloffProfile falloffProfile =
            stampAsset.DefaultFalloffProfile;

        float falloff =
            stampAsset.DefaultFalloff;

        float smoothingRadius =
            stampAsset.DefaultSmoothingRadius;

        float smoothingStrength =
            stampAsset.DefaultSmoothingStrength;

        EditorGUI.BeginChangeCheck();

        GUILayout.Label(
            "Placement Defaults",
            EditorStyles.boldLabel
        );

        sizeXZ =
            EditorGUILayout.Vector2Field(
                "Size X / Z",
                sizeXZ
            );

        heightDelta =
            EditorGUILayout.FloatField(
                "Height Delta",
                heightDelta
            );

        GUILayout.Space(
            8f
        );

        GUILayout.Label(
            "Source Response Defaults",
            EditorStyles.boldLabel
        );

        sourceInputMin =
            EditorGUILayout.FloatField(
                "Input Min",
                sourceInputMin
            );

        sourceInputMax =
            EditorGUILayout.FloatField(
                "Input Max",
                sourceInputMax
            );

        sourceGamma =
            EditorGUILayout.FloatField(
                "Gamma",
                sourceGamma
            );

        GUILayout.Space(
            8f
        );

        GUILayout.Label(
            "Falloff Defaults",
            EditorStyles.boldLabel
        );

        falloffShape =
            (TerrainStampFalloffShape)
            EditorGUILayout.EnumPopup(
                "Shape",
                falloffShape
            );

        falloffProfile =
            (TerrainStampFalloffProfile)
            EditorGUILayout.EnumPopup(
                "Profile",
                falloffProfile
            );

        falloff =
            EditorGUILayout.Slider(
                "Amount",
                falloff,
                0f,
                1f
            );

        GUILayout.Space(
            8f
        );

        GUILayout.Label(
            "Smoothing Defaults",
            EditorStyles.boldLabel
        );

        smoothingRadius =
            EditorGUILayout.FloatField(
                "Radius",
                smoothingRadius
            );

        smoothingStrength =
            EditorGUILayout.Slider(
                "Strength",
                smoothingStrength,
                0f,
                1f
            );

        if (EditorGUI.EndChangeCheck())
        {
            sizeXZ =
                SanitizeSizeXZ(
                    sizeXZ
                );

            if (!IsFinite(heightDelta))
            {
                heightDelta =
                    stampAsset.DefaultHeightDelta;
            }

            TerrainStampSourceRemapUtility
                .SanitizeRequestedValues(
                    sourceInputMin,
                    sourceInputMax,
                    sourceGamma,
                    out sourceInputMin,
                    out sourceInputMax,
                    out sourceGamma
                );

            falloffShape =
                TerrainStampFalloffUtility
                    .SanitizeShape(
                        falloffShape
                    );

            falloffProfile =
                TerrainStampFalloffUtility
                    .SanitizeProfile(
                        falloffProfile
                    );

            falloff =
                TerrainStampFalloffUtility
                    .SanitizeAmount(
                        falloff
                    );

            smoothingRadius =
                IsFinite(smoothingRadius)
                    ? Mathf.Max(
                        0f,
                        smoothingRadius
                    )
                    : stampAsset.DefaultSmoothingRadius;

            smoothingStrength =
                IsFinite(smoothingStrength)
                    ? Mathf.Clamp01(
                        smoothingStrength
                    )
                    : stampAsset.DefaultSmoothingStrength;

            creationDefaultsVersionProperty.intValue =
                TerrainHeightStampAsset
                    .CurrentCreationDefaultsVersion;

            defaultSizeXZProperty.vector2Value =
                sizeXZ;

            defaultHeightDeltaProperty.floatValue =
                heightDelta;

            defaultSourceInputMinProperty.floatValue =
                sourceInputMin;

            defaultSourceInputMaxProperty.floatValue =
                sourceInputMax;

            defaultSourceGammaProperty.floatValue =
                sourceGamma;

            defaultFalloffShapeProperty.enumValueIndex =
                (int)falloffShape;

            defaultFalloffProfileProperty.enumValueIndex =
                (int)falloffProfile;

            defaultFalloffProperty.floatValue =
                falloff;

            defaultSmoothingRadiusProperty.floatValue =
                smoothingRadius;

            defaultSmoothingStrengthProperty.floatValue =
                smoothingStrength;
        }

        serializedObject.ApplyModifiedProperties();
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
            return 128f;
        }

        return
            Mathf.Max(
                MinimumSize,
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
