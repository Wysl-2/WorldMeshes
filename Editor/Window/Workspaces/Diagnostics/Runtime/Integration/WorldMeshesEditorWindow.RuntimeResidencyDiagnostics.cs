using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private TerrainRuntimeResidencyBudgetResult
        lastRuntimeResidencyBudgetResult;

    private void DrawRuntimeResidencyBudgetEvaluation(
        TerrainHeightmapStreamer streamer,
        TerrainRuntimeResidencyDiagnosticsSnapshot snapshot
    )
    {
        GUILayout.Space(6f);

        if (
            GUILayout.Button(
                "Evaluate / Capture Residency Budget",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            lastRuntimeResidencyBudgetResult =
                TerrainRuntimeResidencyBudgetUtility
                    .Evaluate(
                        snapshot,
                        MiBToBytes(
                            runtimeTerrainResidencyBudgetMiB
                        ),
                        MiBToBytes(
                            runtimeSurfaceResidencyBudgetMiB
                        )
                    );

            lastRuntimeResidencyConfiguration =
                TerrainRuntimeCertificationConfigurationSnapshot
                    .Capture(
                        worldSettings,
                        streamer
                    );

            lastRuntimeFinalCertificationReport =
                null;

            Debug.Log(
                lastRuntimeResidencyBudgetResult
                    .BuildDiagnosticReport()
            );

            Repaint();
        }

        if (lastRuntimeResidencyBudgetResult == null)
        {
            return;
        }

        GUILayout.Space(6f);

        GUILayout.Label(
            "Captured Budget Evaluation",
            EditorStyles.boldLabel
        );

        TerrainRuntimeCertificationEvidenceState evidenceState =
            TerrainRuntimeCertificationEvidenceUtility
                .EvaluateState(
                    lastRuntimeResidencyConfiguration,
                    worldSettings,
                    streamer,
                    out string evidenceReason
                );

        EditorGUILayout.LabelField(
            "Evidence State",
            evidenceState.ToString()
        );

        if (
            evidenceState !=
                TerrainRuntimeCertificationEvidenceState.Current
            && !string.IsNullOrEmpty(evidenceReason)
        )
        {
            EditorGUILayout.HelpBox(
                evidenceReason,
                MessageType.Warning
            );
        }

        EditorGUILayout.LabelField(
            "Height Budget",
            lastRuntimeResidencyBudgetResult
                .HeightBudgetStatus
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Terrain Budget",
            lastRuntimeResidencyBudgetResult
                .TerrainBudgetStatus
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Surface Gate",
            FormatSurfaceResidencyGate(
                lastRuntimeResidencyBudgetResult
                    .SurfaceGateDecision
            )
        );

        EditorGUILayout.HelpBox(
            lastRuntimeResidencyBudgetResult
                .BuildDiagnosticReport(),
            MessageTypeForRuntimeValidationStatus(
                lastRuntimeResidencyBudgetResult
                    .TerrainBudgetStatus
            )
        );
    }

    private static string FormatSurfaceResidencyGate(
        TerrainSurfaceResidencyGateDecision decision
    )
    {
        switch (decision)
        {
            case TerrainSurfaceResidencyGateDecision
                .NotRequiredForTargetConfiguration:
                return "Not Required For Target Configuration";

            case TerrainSurfaceResidencyGateDecision
                .RequiredForTargetConfiguration:
                return "Required For Target Configuration";

            default:
                return "Not Evaluated";
        }
    }
}
