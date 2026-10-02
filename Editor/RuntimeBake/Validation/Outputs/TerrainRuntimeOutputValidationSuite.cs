using System;
using UnityEditor;
using UnityEngine;

public static class TerrainRuntimeOutputValidationSuite
{
    private sealed class SynchronousValidationCase
    {
        private readonly string caseName;
        private readonly Func<WorldSettings, bool> validate;

        private TerrainValidationRunSummary lastRunSummary =
            TerrainValidationRunSummary.CreateNotRun();

        public SynchronousValidationCase(
            string caseName,
            Func<WorldSettings, bool> validate
        )
        {
            this.caseName = caseName;
            this.validate = validate;
        }

        public bool TryStart()
        {
            if (!TryGetValidationWorldSettings(out WorldSettings settings, out string blockedReason))
            {
                lastRunSummary =
                    TerrainValidationRunSummary.CreateCompleted(
                        0,
                        0,
                        1,
                        blockedReason
                    );

                return true;
            }

            lastRunSummary =
                TerrainValidationRunSummary.CreateRunning(
                    caseName + " is running."
                );

            try
            {
                bool passed = validate(settings);

                lastRunSummary =
                    TerrainValidationRunSummary.CreateCompleted(
                        passed ? 1 : 0,
                        passed ? 0 : 1,
                        0,
                        passed
                            ? caseName + " passed."
                            : caseName + " failed. See the Unity Console for details."
                    );
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                lastRunSummary =
                    TerrainValidationRunSummary.CreateCompleted(
                        0,
                        1,
                        0,
                        caseName + " failed with an exception.\n\n" + exception
                    );
            }

            return true;
        }

        public bool IsRunning()
        {
            return false;
        }

        public TerrainValidationRunSummary GetSummary()
        {
            return lastRunSummary;
        }
    }

    private sealed class HeightCompositionValidationCase
    {
        private TerrainValidationRunSummary lastRunSummary =
            TerrainValidationRunSummary.CreateNotRun();

        public bool TryStart()
        {
            if (
                TerrainRuntimeHeightCompositionValidationUtility.IsScheduled
                || TerrainRuntimeHeightCompositionValidationUtility.IsRunning
            )
            {
                return false;
            }

            if (!TryGetValidationWorldSettings(out _, out string blockedReason))
            {
                lastRunSummary =
                    TerrainValidationRunSummary.CreateCompleted(
                        0,
                        0,
                        1,
                        blockedReason
                    );

                return true;
            }

            lastRunSummary =
                TerrainValidationRunSummary.CreateRunning(
                    "Runtime Height Composition is running."
                );

            TerrainRuntimeHeightCompositionValidationUtility.RequestValidation();

            return
                TerrainRuntimeHeightCompositionValidationUtility.IsScheduled
                || TerrainRuntimeHeightCompositionValidationUtility.IsRunning;
        }

        public bool IsRunning()
        {
            return
                TerrainRuntimeHeightCompositionValidationUtility.IsScheduled
                || TerrainRuntimeHeightCompositionValidationUtility.IsRunning;
        }

        public TerrainValidationRunSummary GetSummary()
        {
            if (
                lastRunSummary.State == TerrainValidationRunState.Running
                && !IsRunning()
            )
            {
                int failed =
                    TerrainRuntimeHeightCompositionValidationUtility.LastFailedCount;

                int blocked =
                    TerrainRuntimeHeightCompositionValidationUtility.LastBlockedCount;

                lastRunSummary =
                    TerrainValidationRunSummary.CreateCompleted(
                        failed == 0 && blocked == 0 ? 1 : 0,
                        failed > 0 ? 1 : 0,
                        failed == 0 && blocked > 0 ? 1 : 0,
                        TerrainRuntimeHeightCompositionValidationUtility.LastSummary
                    );
            }

            return lastRunSummary;
        }
    }

    public static TerrainValidationSuiteRunner CreateRunner()
    {
        SynchronousValidationCase heightContent =
            new SynchronousValidationCase(
                "Runtime Height Content",
                TerrainHeightmapValidator.ValidateHeightmaps
            );

        SynchronousValidationCase heightStreaming =
            new SynchronousValidationCase(
                "Height Streaming Pyramid",
                TerrainHeightStreamingPyramidValidator.Validate
            );

        SynchronousValidationCase heightRange =
            new SynchronousValidationCase(
                "Height Range Metadata",
                settings =>
                    TerrainRuntimeHeightRangeMetadataValidator.Validate(
                        settings,
                        true
                    )
            );

        HeightCompositionValidationCase heightComposition =
            new HeightCompositionValidationCase();

        SynchronousValidationCase collisionSeams =
            new SynchronousValidationCase(
                "Collision Seams",
                TerrainCollisionSeamValidator.ValidateCollisionSeams
            );

        SynchronousValidationCase clipmapGeometry =
            new SynchronousValidationCase(
                "Clipmap Geometry",
                TerrainClipmapGeometryValidator.ValidateClipmapGeometry
            );

        return
            new TerrainValidationSuiteRunner(
                new[]
                {
                    new TerrainValidationSuiteRunner.Case(
                        "Runtime Height Content",
                        heightContent.TryStart,
                        heightContent.IsRunning,
                        heightContent.GetSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Height Streaming Pyramid",
                        heightStreaming.TryStart,
                        heightStreaming.IsRunning,
                        heightStreaming.GetSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Height Range Metadata",
                        heightRange.TryStart,
                        heightRange.IsRunning,
                        heightRange.GetSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Runtime Height Composition",
                        heightComposition.TryStart,
                        heightComposition.IsRunning,
                        heightComposition.GetSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Collision Seams",
                        collisionSeams.TryStart,
                        collisionSeams.IsRunning,
                        collisionSeams.GetSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Clipmap Geometry",
                        clipmapGeometry.TryStart,
                        clipmapGeometry.IsRunning,
                        clipmapGeometry.GetSummary
                    )
                }
            );
    }

    private static bool TryGetValidationWorldSettings(
        out WorldSettings settings,
        out string blockedReason
    )
    {
        settings = null;
        blockedReason = "";

        if (
            Application.isPlaying
            || EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            blockedReason =
                "Runtime Output Validation runs in Edit Mode.";
            return false;
        }

        if (EditorApplication.isCompiling)
        {
            blockedReason =
                "Runtime Output Validation is unavailable while scripts are compiling.";
            return false;
        }

        if (EditorApplication.isUpdating)
        {
            blockedReason =
                "Runtime Output Validation is unavailable while the Asset Database is updating.";
            return false;
        }

        if (TerrainRuntimeBakePipeline.IsRunning)
        {
            blockedReason =
                "Runtime Output Validation is unavailable while a Runtime Bake is running.";
            return false;
        }

        if (TerrainSurfaceMaskCompiler.IsGenerating)
        {
            blockedReason =
                "Runtime Output Validation is unavailable while Surface generation is running.";
            return false;
        }

        settings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        if (settings == null)
        {
            blockedReason =
                "WorldSettings is unavailable.";
            return false;
        }

        return true;
    }
}
