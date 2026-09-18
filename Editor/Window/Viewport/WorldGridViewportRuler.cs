using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

internal static class WorldGridViewportRuler
{
    internal const float HorizontalRulerHeight =
        24f;

    internal const float VerticalRulerWidth =
        64f;

    private const float TargetMajorTickPixels =
        100f;

    private const float MinimumMajorTickPixels =
        80f;

    private const float MaximumMajorTickPixels =
        120f;

    private const int MinorSubdivisionCount =
        5;

    private const float MinimumMinorTickPixels =
        6f;

    private const float MajorTickLength =
        8f;

    private const float MinorTickLength =
        4f;

    private const int MaximumTickIterations =
        4096;

    private static readonly Color MajorTickColor =
        new Color(
            0.72f,
            0.72f,
            0.72f,
            1f
        );

    private static readonly Color MinorTickColor =
        new Color(
            0.48f,
            0.48f,
            0.48f,
            1f
        );

    private static readonly Color SeparatorColor =
        new Color(
            0f,
            0f,
            0f,
            0.45f
        );

    internal static void Draw(
        Rect cornerArea,
        Rect horizontalRuler,
        Rect verticalRuler,
        Rect viewport,
        WorldGridViewportTransform viewportTransform
    )
    {
        DrawBackground(
            cornerArea
        );

        DrawBackground(
            horizontalRuler
        );

        DrawBackground(
            verticalRuler
        );

        DrawSeparators(
            horizontalRuler,
            verticalRuler
        );

        if (
            viewportTransform == null
            ||
            !viewportTransform.IsInitialized
            ||
            viewport.width <= 0f
            ||
            viewport.height <= 0f
        )
        {
            return;
        }

        float majorInterval =
            CalculateMajorInterval(
                viewportTransform.PixelsPerMeter
            );

        if (
            !IsFinite(majorInterval)
            ||
            majorInterval <= 0f
        )
        {
            return;
        }

        DrawHorizontalRuler(
            horizontalRuler,
            viewport,
            viewportTransform,
            majorInterval
        );

        DrawVerticalRuler(
            verticalRuler,
            viewport,
            viewportTransform,
            majorInterval
        );
    }

    private static void DrawBackground(
        Rect area
    )
    {
        if (
            area.width <= 0f
            ||
            area.height <= 0f
        )
        {
            return;
        }

        GUI.Box(
            area,
            GUIContent.none,
            EditorStyles.toolbar
        );
    }

    private static void DrawSeparators(
        Rect horizontalRuler,
        Rect verticalRuler
    )
    {
        if (
            horizontalRuler.width > 0f
            &&
            horizontalRuler.height > 0f
        )
        {
            EditorGUI.DrawRect(
                new Rect(
                    horizontalRuler.xMin,
                    horizontalRuler.yMax - 1f,
                    horizontalRuler.width,
                    1f
                ),
                SeparatorColor
            );
        }

        if (
            verticalRuler.width > 0f
            &&
            verticalRuler.height > 0f
        )
        {
            EditorGUI.DrawRect(
                new Rect(
                    verticalRuler.xMax - 1f,
                    verticalRuler.yMin,
                    1f,
                    verticalRuler.height
                ),
                SeparatorColor
            );
        }
    }

    private static void DrawHorizontalRuler(
        Rect ruler,
        Rect viewport,
        WorldGridViewportTransform viewportTransform,
        float majorInterval
    )
    {
        Vector2 leftWorld =
            viewportTransform.ViewportToWorld(
                new Vector2(
                    viewport.xMin,
                    viewport.center.y
                ),
                viewport
            );

        Vector2 rightWorld =
            viewportTransform.ViewportToWorld(
                new Vector2(
                    viewport.xMax,
                    viewport.center.y
                ),
                viewport
            );

        double visibleMin =
            Math.Min(
                leftWorld.x,
                rightWorld.x
            );

        double visibleMax =
            Math.Max(
                leftWorld.x,
                rightWorld.x
            );

        DrawHorizontalMinorTicks(
            ruler,
            viewport,
            viewportTransform,
            visibleMin,
            visibleMax,
            majorInterval
        );

        GUIStyle labelStyle =
            new GUIStyle(
                EditorStyles.miniLabel
            );

        labelStyle.alignment =
            TextAnchor.MiddleCenter;

        double firstMajor =
            Math.Ceiling(
                visibleMin /
                majorInterval
            ) *
            majorInterval;

        int iterations =
            0;

        for (
            double value = firstMajor;
            value <= visibleMax +
                majorInterval * 0.000001;
            value += majorInterval
        )
        {
            if (
                iterations++ >=
                MaximumTickIterations
            )
            {
                break;
            }

            Vector2 viewportPosition =
                viewportTransform.WorldToViewport(
                    new Vector2(
                        (float)value,
                        viewportTransform
                            .ViewCenterWorldXZ.y
                    ),
                    viewport
                );

            float x =
                viewportPosition.x;

            if (
                x < ruler.xMin - 1f
                ||
                x > ruler.xMax + 1f
            )
            {
                continue;
            }

            EditorGUI.DrawRect(
                new Rect(
                    x,
                    ruler.yMax -
                        MajorTickLength,
                    1f,
                    MajorTickLength
                ),
                MajorTickColor
            );

            const float labelWidth =
                100f;

            float labelX =
                Mathf.Clamp(
                    x -
                        labelWidth *
                        0.5f,
                    ruler.xMin,
                    Mathf.Max(
                        ruler.xMin,
                        ruler.xMax -
                            labelWidth
                    )
                );

            GUI.Label(
                new Rect(
                    labelX,
                    ruler.yMin,
                    labelWidth,
                    Mathf.Max(
                        0f,
                        ruler.height -
                            MajorTickLength
                    )
                ),
                FormatMeters(
                    value
                ),
                labelStyle
            );
        }
    }

    private static void DrawVerticalRuler(
        Rect ruler,
        Rect viewport,
        WorldGridViewportTransform viewportTransform,
        float majorInterval
    )
    {
        Vector2 topWorld =
            viewportTransform.ViewportToWorld(
                new Vector2(
                    viewport.center.x,
                    viewport.yMin
                ),
                viewport
            );

        Vector2 bottomWorld =
            viewportTransform.ViewportToWorld(
                new Vector2(
                    viewport.center.x,
                    viewport.yMax
                ),
                viewport
            );

        double visibleMin =
            Math.Min(
                topWorld.y,
                bottomWorld.y
            );

        double visibleMax =
            Math.Max(
                topWorld.y,
                bottomWorld.y
            );

        DrawVerticalMinorTicks(
            ruler,
            viewport,
            viewportTransform,
            visibleMin,
            visibleMax,
            majorInterval
        );

        GUIStyle labelStyle =
            new GUIStyle(
                EditorStyles.miniLabel
            );

        labelStyle.alignment =
            TextAnchor.MiddleRight;

        double firstMajor =
            Math.Ceiling(
                visibleMin /
                majorInterval
            ) *
            majorInterval;

        int iterations =
            0;

        for (
            double value = firstMajor;
            value <= visibleMax +
                majorInterval * 0.000001;
            value += majorInterval
        )
        {
            if (
                iterations++ >=
                MaximumTickIterations
            )
            {
                break;
            }

            Vector2 viewportPosition =
                viewportTransform.WorldToViewport(
                    new Vector2(
                        viewportTransform
                            .ViewCenterWorldXZ.x,
                        (float)value
                    ),
                    viewport
                );

            float y =
                viewportPosition.y;

            if (
                y < ruler.yMin - 1f
                ||
                y > ruler.yMax + 1f
            )
            {
                continue;
            }

            EditorGUI.DrawRect(
                new Rect(
                    ruler.xMax -
                        MajorTickLength,
                    y,
                    MajorTickLength,
                    1f
                ),
                MajorTickColor
            );

            const float labelHeight =
                16f;

            float labelY =
                Mathf.Clamp(
                    y -
                        labelHeight *
                        0.5f,
                    ruler.yMin,
                    Mathf.Max(
                        ruler.yMin,
                        ruler.yMax -
                            labelHeight
                    )
                );

            GUI.Label(
                new Rect(
                    ruler.xMin + 2f,
                    labelY,
                    Mathf.Max(
                        0f,
                        ruler.width -
                            MajorTickLength -
                            5f
                    ),
                    labelHeight
                ),
                FormatMeters(
                    value
                ),
                labelStyle
            );
        }
    }

    private static void DrawHorizontalMinorTicks(
        Rect ruler,
        Rect viewport,
        WorldGridViewportTransform viewportTransform,
        double visibleMin,
        double visibleMax,
        double majorInterval
    )
    {
        double minorInterval =
            majorInterval /
            MinorSubdivisionCount;

        if (
            minorInterval *
                viewportTransform.PixelsPerMeter <
            MinimumMinorTickPixels
        )
        {
            return;
        }

        double firstMajorBase =
            Math.Floor(
                visibleMin /
                majorInterval
            ) *
            majorInterval;

        int iterations =
            0;

        for (
            double majorBase = firstMajorBase;
            majorBase <= visibleMax +
                majorInterval;
            majorBase += majorInterval
        )
        {
            if (
                iterations++ >=
                MaximumTickIterations
            )
            {
                break;
            }

            for (
                int subdivision = 1;
                subdivision < MinorSubdivisionCount;
                subdivision++
            )
            {
                double value =
                    majorBase +
                    minorInterval *
                    subdivision;

                if (
                    value < visibleMin
                    ||
                    value > visibleMax
                )
                {
                    continue;
                }

                Vector2 viewportPosition =
                    viewportTransform.WorldToViewport(
                        new Vector2(
                            (float)value,
                            viewportTransform
                                .ViewCenterWorldXZ.y
                        ),
                        viewport
                    );

                float x =
                    viewportPosition.x;

                EditorGUI.DrawRect(
                    new Rect(
                        x,
                        ruler.yMax -
                            MinorTickLength,
                        1f,
                        MinorTickLength
                    ),
                    MinorTickColor
                );
            }
        }
    }

    private static void DrawVerticalMinorTicks(
        Rect ruler,
        Rect viewport,
        WorldGridViewportTransform viewportTransform,
        double visibleMin,
        double visibleMax,
        double majorInterval
    )
    {
        double minorInterval =
            majorInterval /
            MinorSubdivisionCount;

        if (
            minorInterval *
                viewportTransform.PixelsPerMeter <
            MinimumMinorTickPixels
        )
        {
            return;
        }

        double firstMajorBase =
            Math.Floor(
                visibleMin /
                majorInterval
            ) *
            majorInterval;

        int iterations =
            0;

        for (
            double majorBase = firstMajorBase;
            majorBase <= visibleMax +
                majorInterval;
            majorBase += majorInterval
        )
        {
            if (
                iterations++ >=
                MaximumTickIterations
            )
            {
                break;
            }

            for (
                int subdivision = 1;
                subdivision < MinorSubdivisionCount;
                subdivision++
            )
            {
                double value =
                    majorBase +
                    minorInterval *
                    subdivision;

                if (
                    value < visibleMin
                    ||
                    value > visibleMax
                )
                {
                    continue;
                }

                Vector2 viewportPosition =
                    viewportTransform.WorldToViewport(
                        new Vector2(
                            viewportTransform
                                .ViewCenterWorldXZ.x,
                            (float)value
                        ),
                        viewport
                    );

                float y =
                    viewportPosition.y;

                EditorGUI.DrawRect(
                    new Rect(
                        ruler.xMax -
                            MinorTickLength,
                        y,
                        MinorTickLength,
                        1f
                    ),
                    MinorTickColor
                );
            }
        }
    }

    internal static float CalculateMajorInterval(
        float pixelsPerMeter
    )
    {
        float safePixelsPerMeter =
            IsFinite(pixelsPerMeter)
            &&
            pixelsPerMeter > 0f
                ? pixelsPerMeter
                : 0.0001f;

        double desiredInterval =
            TargetMajorTickPixels /
            safePixelsPerMeter;

        if (
            double.IsNaN(desiredInterval)
            ||
            double.IsInfinity(desiredInterval)
            ||
            desiredInterval <= 0d
        )
        {
            return
                1f;
        }

        double exponent =
            Math.Floor(
                Math.Log10(
                    desiredInterval
                )
            );

        double bestReadableInterval =
            0d;

        double bestReadableDistance =
            double.PositiveInfinity;

        double bestFallbackInterval =
            0d;

        double bestFallbackDistance =
            double.PositiveInfinity;

        double[] multipliers =
        {
            1d,
            2d,
            5d
        };

        for (
            int exponentOffset = -1;
            exponentOffset <= 1;
            exponentOffset++
        )
        {
            double scale =
                Math.Pow(
                    10d,
                    exponent +
                        exponentOffset
                );

            for (
                int multiplierIndex = 0;
                multiplierIndex < multipliers.Length;
                multiplierIndex++
            )
            {
                double candidate =
                    multipliers[multiplierIndex] *
                    scale;

                if (
                    double.IsNaN(candidate)
                    ||
                    double.IsInfinity(candidate)
                    ||
                    candidate <= 0d
                )
                {
                    continue;
                }

                double pixelSpacing =
                    candidate *
                    safePixelsPerMeter;

                double distance =
                    Math.Abs(
                        pixelSpacing -
                        TargetMajorTickPixels
                    );

                if (
                    distance <
                    bestFallbackDistance
                )
                {
                    bestFallbackDistance =
                        distance;

                    bestFallbackInterval =
                        candidate;
                }

                if (
                    pixelSpacing >=
                        MinimumMajorTickPixels
                    &&
                    pixelSpacing <=
                        MaximumMajorTickPixels
                    &&
                    distance <
                        bestReadableDistance
                )
                {
                    bestReadableDistance =
                        distance;

                    bestReadableInterval =
                        candidate;
                }
            }
        }

        double result =
            bestReadableInterval > 0d
                ? bestReadableInterval
                : bestFallbackInterval;

        if (
            double.IsNaN(result)
            ||
            double.IsInfinity(result)
            ||
            result <= 0d
        )
        {
            return
                1f;
        }

        return
            (float)result;
    }

    private static string FormatMeters(
        double value
    )
    {
        if (
            Math.Abs(value) <
            0.0000005d
        )
        {
            value =
                0d;
        }

        return
            value.ToString(
                "0.######",
                CultureInfo.InvariantCulture
            ) +
            " m";
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
