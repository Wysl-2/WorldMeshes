using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStampFalloffSettings(
        TerrainStampModifier stamp
    )
    {
        if (
            stamp == null
            ||
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            return;
        }

        GUILayout.Space(
            4f
        );

        GUILayout.Label(
            "Falloff",
            EditorStyles.miniBoldLabel
        );

        /*
         * Shape/Profile/Amount are normal discrete inspector mutations.
         * They must not compete with an active Scene handle or any other
         * interactive terrain modifier transaction.
         */
        EditorGUI.BeginDisabledGroup(
            TerrainAuthoringModifierService
                .HasActiveInteractiveEdit
        );

        TerrainStampFalloffShape editedShape =
            (TerrainStampFalloffShape)
            EditorGUILayout.EnumPopup(
                "Shape",
                stamp.FalloffShape
            );

        if (
            editedShape !=
                stamp.FalloffShape
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampFalloffShape(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        editedShape,
                        out string shapeError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    shapeError
                );
            }
        }

        TerrainStampFalloffProfile editedProfile =
            (TerrainStampFalloffProfile)
            EditorGUILayout.EnumPopup(
                "Profile",
                stamp.FalloffProfile
            );

        if (
            editedProfile !=
                stamp.FalloffProfile
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampFalloffProfile(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        editedProfile,
                        out string profileError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    profileError
                );
            }
        }

        float editedAmount =
            EditorGUILayout.DelayedFloatField(
                "Amount",
                stamp.Falloff
            );

        if (
            !Mathf.Approximately(
                editedAmount,
                stamp.Falloff
            )
        )
        {
            if (
                TerrainAuthoringModifierService
                    .SetStampFalloff(
                        terrainAuthoringData,
                        worldSettings,
                        stamp.StableId,
                        editedAmount,
                        out string falloffError
                    )
            )
            {
                OnModifierMutationSucceeded();
            }
            else
            {
                SetModifierAuthoringError(
                    falloffError
                );
            }
        }

        EditorGUI.EndDisabledGroup();
    }
}
