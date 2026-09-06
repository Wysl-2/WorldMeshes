using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawRuntimeBakePlanDiagnostics()
    {
        TerrainRuntimeBakePlan plan =
            TerrainRuntimeBakePlanner
                .BuildPlan(
                    worldSettings,
                    terrainAuthoringData
                );

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Bake Plan",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Source State Revision",
            plan.SourceStateRevision.ToString()
        );

        EditorGUILayout.LabelField(
            "Status",
            GetRuntimeBakePlanStatusLabel(
                plan
            )
        );

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Heightmaps",
            FormatRuntimeBakePlanStage(
                plan.HeightWorkMode,
                plan.HeightTileCount,
                "tiles"
            )
        );

        EditorGUILayout.LabelField(
            "Surface Masks",
            FormatRuntimeBakePlanStage(
                plan.SurfaceWorkMode,
                plan.SurfaceTileCount,
                "tiles"
            )
        );

        EditorGUILayout.LabelField(
            "Collision",
            FormatRuntimeBakePlanStage(
                plan.CollisionWorkMode,
                plan.CollisionChunkCount,
                "chunks"
            )
        );

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Addressables Configuration",
            plan.AddressablesConfigurationRequired
                ? "Required"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Addressables Content",
            plan.AddressablesContentBuildRequired
                ? "Required"
                : "No"
        );

        EditorGUILayout.LabelField(
            "Runtime Scene Metadata",
            plan.RuntimeSceneMetadataUpdateRequired
                ? "Required"
                : "No"
        );

        if (plan.IsBlocked)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                plan.BlockReason,
                MessageType.Warning
            );
        }

        if (plan.SafetyEscalationReasons.Count > 0)
        {
            GUILayout.Space(5f);

            StringBuilder reasons =
                new StringBuilder();

            for (
                int index = 0;
                index < plan.SafetyEscalationReasons.Count;
                index++
            )
            {
                if (index > 0)
                {
                    reasons.AppendLine();
                }

                reasons.Append(
                    "- "
                );

                reasons.Append(
                    plan.SafetyEscalationReasons[index]
                );
            }

            EditorGUILayout.HelpBox(
                "Safety Escalation:\n" +
                reasons,
                MessageType.Info
            );
        }

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Log Detailed Bake Plan",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            Debug.Log(
                BuildRuntimeBakePlanDiagnosticReport(
                    plan
                )
            );
        }

        EditorGUILayout.HelpBox(
            "Package 02 planning is diagnostic only. It determines effective " +
            "runtime work and safety fallbacks but does not generate assets, " +
            "configure Addressables, modify scenes, or clear persistent work.",
            MessageType.None
        );

        GUILayout.EndVertical();
    }

    private static string GetRuntimeBakePlanStatusLabel(
        TerrainRuntimeBakePlan plan
    )
    {
        if (plan.IsBlocked)
        {
            return
                "Blocked";
        }

        if (plan.IsInitialBake)
        {
            return
                "Initial Bake";
        }

        if (plan.HasWork)
        {
            return
                "Changes Pending";
        }

        return
            "Ready";
    }

    private static string FormatRuntimeBakePlanStage(
        TerrainRuntimeBakeWorkMode mode,
        int count,
        string unit
    )
    {
        if (
            mode ==
            TerrainRuntimeBakeWorkMode.None
        )
        {
            return
                "None";
        }

        return
            mode +
            " - " +
            count.ToString("N0") +
            " " +
            unit;
    }

    private static string BuildRuntimeBakePlanDiagnosticReport(
        TerrainRuntimeBakePlan plan
    )
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "WorldMeshes Runtime Bake Plan"
        );

        builder.AppendLine(
            "Source State Revision: " +
            plan.SourceStateRevision
        );

        builder.AppendLine(
            "Status: " +
            GetRuntimeBakePlanStatusLabel(
                plan
            )
        );

        builder.AppendLine(
            "Height Work: " +
            FormatRuntimeBakePlanStage(
                plan.HeightWorkMode,
                plan.HeightTileCount,
                "tiles"
            )
        );

        builder.AppendLine(
            "Surface Work: " +
            FormatRuntimeBakePlanStage(
                plan.SurfaceWorkMode,
                plan.SurfaceTileCount,
                "tiles"
            )
        );

        builder.AppendLine(
            "Collision Work: " +
            FormatRuntimeBakePlanStage(
                plan.CollisionWorkMode,
                plan.CollisionChunkCount,
                "chunks"
            )
        );

        builder.AppendLine(
            "Addressables Configuration Required: " +
            plan.AddressablesConfigurationRequired
        );

        builder.AppendLine(
            "Addressables Content Required: " +
            plan.AddressablesContentBuildRequired
        );

        builder.AppendLine(
            "Runtime Scene Metadata Required: " +
            plan.RuntimeSceneMetadataUpdateRequired
        );

        builder.AppendLine(
            "Blocked: " +
            plan.IsBlocked
        );

        if (plan.IsBlocked)
        {
            builder.AppendLine(
                "Block Reason: " +
                plan.BlockReason
            );
        }

        builder.AppendLine(
            "Current Authoring Signature: " +
            EmptySignatureLabel(
                plan.CurrentAuthoringSignature
            )
        );

        builder.AppendLine(
            "Observed Authoring Signature: " +
            EmptySignatureLabel(
                plan.ObservedAuthoringSignature
            )
        );

        builder.AppendLine(
            "Current Surface Settings Signature: " +
            EmptySignatureLabel(
                plan.CurrentSurfaceSettingsSignature
            )
        );

        builder.AppendLine(
            "Current Collision Settings Signature: " +
            EmptySignatureLabel(
                plan.CurrentCollisionSettingsSignature
            )
        );

        AppendRuntimeBakeCoordinates(
            builder,
            "Plan Height Tiles",
            plan.HeightTiles
        );

        AppendRuntimeBakeCoordinates(
            builder,
            "Plan Surface Tiles",
            plan.SurfaceTiles
        );

        AppendRuntimeBakeCoordinates(
            builder,
            "Plan Collision Chunks",
            plan.CollisionChunks
        );

        builder.Append(
            "Safety Escalations ("
        );

        builder.Append(
            plan.SafetyEscalationReasons.Count
        );

        builder.AppendLine(
            "):"
        );

        if (plan.SafetyEscalationReasons.Count == 0)
        {
            builder.Append(
                "None"
            );
        }
        else
        {
            for (
                int index = 0;
                index < plan.SafetyEscalationReasons.Count;
                index++
            )
            {
                builder.Append(
                    "- "
                );

                builder.AppendLine(
                    plan.SafetyEscalationReasons[index]
                );
            }
        }

        return
            builder.ToString();
    }

    private static string EmptySignatureLabel(
        string signature
    )
    {
        return
            string.IsNullOrEmpty(
                signature
            )
                ? "Not Available"
                : signature;
    }
}
