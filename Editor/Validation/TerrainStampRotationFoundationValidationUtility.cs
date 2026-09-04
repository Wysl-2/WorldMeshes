using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainStampRotationFoundationValidationUtility
{
    private sealed class Result
    {
        public string Name;
        public bool Passed;
        public string Details;
    }

    private sealed class TestContext : IDisposable
    {
        public TerrainAuthoringData AuthoringData;
        public TerrainStampModifier Stamp;
        public TerrainHeightStampAsset StampAsset;
        public Texture2D StampTexture;
        public Texture2D CommittedTile;
        public int SamplesPerSide;
        public int CenterIndex;
        public int HalfStampSamples;
        public float SampleSpacing;
        public float TileWorldSize;
        public Vector2 WorldSizeXZ;

        public void Dispose()
        {
            if (AuthoringData != null)
            {
                TerrainAuthoringModifierChangeTracker.Forget(AuthoringData);
            }

            Destroy(CommittedTile);
            Destroy(StampTexture);
            Destroy(StampAsset);
            Destroy(AuthoringData);
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value != null)
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }

    private const float MathTolerance = 0.0001f;
    private const float GpuTolerance = 0.0005f;

    private static readonly List<Result> results = new List<Result>();
    private static bool validationScheduled;
    private static bool validationRunning;
    private static int lastPassedCount;
    private static int lastFailedCount;
    private static string lastSummary = "Not run.";

    public static bool IsScheduled => validationScheduled;
    public static bool IsRunning => validationRunning;
    public static int LastPassedCount => lastPassedCount;
    public static int LastFailedCount => lastFailedCount;
    public static string LastSummary => lastSummary;

    public static void RequestValidation()
    {
        if (validationScheduled || validationRunning)
        {
            return;
        }

        validationScheduled = true;
        EditorApplication.delayCall -= RunScheduledValidation;
        EditorApplication.delayCall += RunScheduledValidation;
    }

    private static void RunScheduledValidation()
    {
        EditorApplication.delayCall -= RunScheduledValidation;

        if (!validationScheduled || validationRunning)
        {
            return;
        }

        validationScheduled = false;
        validationRunning = true;
        results.Clear();

        try
        {
            RunValidation();
        }
        catch (Exception exception)
        {
            Add("Unexpected validation exception", false, exception.ToString());
        }
        finally
        {
            Finish();
        }
    }

    private static void RunValidation()
    {
        if (
            Application.isPlaying
            || EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling
            || EditorApplication.isUpdating
        )
        {
            Add(
                "Validation prerequisites",
                false,
                "Run while the editor is idle in Edit Mode."
            );
            return;
        }

        if (TerrainAuthoringModifierService.HasActiveInteractiveEdit)
        {
            Add(
                "Validation prerequisites",
                false,
                "Finish or cancel the active terrain modifier interaction first."
            );
            return;
        }

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        if (worldSettings == null)
        {
            Add("Validation prerequisites", false, "WorldSettings could not be loaded.");
            return;
        }

        ValidateNormalization();
        ValidateAxes();
        ValidateRoundTrip();
        ValidateBounds();
        ValidateSignature();

        bool gpuSupported =
            SystemInfo.supportsComputeShaders
            && SystemInfo.supports2DArrayTextures
            && SystemInfo.supportsAsyncGPUReadback
            && SystemInfo.SupportsTextureFormat(TextureFormat.RFloat)
            && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat);

        if (!gpuSupported)
        {
            Add(
                "GPU/runtime rotation validation",
                false,
                "RFloat, compute shaders, 2D texture arrays, or AsyncGPUReadback are unavailable."
            );
            return;
        }

        TestContext context = null;

        try
        {
            if (!TryCreateContext(worldSettings, out context, out string error))
            {
                Add("Transient rotation test context", false, error);
                return;
            }

            ValidateZeroDegreeUv(context);
            ValidateGpuQuarterTurn(context);
            ValidateRuntimeParity(worldSettings, context);
        }
        finally
        {
            context?.Dispose();
        }
    }

    private static void ValidateNormalization()
    {
        bool passed =
            Approx(TerrainStampTransformUtility.NormalizeRotationDegrees(0f), 0f)
            && Approx(TerrainStampTransformUtility.NormalizeRotationDegrees(360f), 0f)
            && Approx(TerrainStampTransformUtility.NormalizeRotationDegrees(-360f), 0f)
            && Approx(TerrainStampTransformUtility.NormalizeRotationDegrees(720f), 0f)
            && Approx(TerrainStampTransformUtility.NormalizeRotationDegrees(450f), 90f)
            && Approx(TerrainStampTransformUtility.NormalizeRotationDegrees(180f), -180f)
            && Approx(TerrainStampTransformUtility.NormalizeRotationDegrees(270f), -90f)
            && Approx(TerrainStampTransformUtility.NormalizeRotationDegrees(float.NaN), 0f)
            && Approx(TerrainStampTransformUtility.NormalizeRotationDegrees(float.PositiveInfinity), 0f);

        Add(
            "Canonical angle normalization",
            passed,
            passed
                ? "Equivalent turns normalize to [-180, 180); non-finite values become 0."
                : "One or more canonical-angle cases failed."
        );
    }

    private static void ValidateAxes()
    {
        TerrainStampTransformUtility.GetWorldAxes(0f, out Vector2 right0, out Vector2 forward0);
        TerrainStampTransformUtility.GetWorldAxes(90f, out Vector2 right90, out Vector2 forward90);

        bool passed =
            Vec(right0, new Vector2(1f, 0f))
            && Vec(forward0, new Vector2(0f, 1f))
            && Vec(right90, new Vector2(0f, -1f))
            && Vec(forward90, new Vector2(1f, 0f))
            && Approx(Vector2.Dot(right90, forward90), 0f)
            && Approx(right90.magnitude, 1f)
            && Approx(forward90.magnitude, 1f);

        Add(
            "Unity world-Y stamp axes",
            passed,
            passed
                ? "+90 degrees maps local +X to world -Z and local +Z to world +X."
                : "Stamp-axis convention or orthonormality is incorrect."
        );
    }

    private static void ValidateRoundTrip()
    {
        Vector2 center = new Vector2(123.5f, 456.25f);
        Vector2 local = new Vector2(17.75f, -9.125f);
        float[] angles = { 17f, 45f, -73f };
        bool passed = true;

        for (int i = 0; i < angles.Length; i++)
        {
            Vector2 world = TerrainStampTransformUtility.LocalToWorldXZ(local, center, angles[i]);
            Vector2 restored = TerrainStampTransformUtility.WorldToLocalXZ(world, center, angles[i]);

            if (!Vec(local, restored))
            {
                passed = false;
                break;
            }
        }

        Add(
            "World/local stamp transform round trip",
            passed,
            passed ? "Representative non-cardinal rotations round-trip within tolerance."
                   : "World/local transform round-trip exceeded tolerance."
        );
    }

    private static void ValidateBounds()
    {
        Vector2 center = new Vector2(100f, 200f);
        Vector2 size = new Vector2(40f, 20f);
        Bounds b0 = TerrainStampTransformUtility.CalculateWorldAabb(center, size, 0f);
        Bounds b90 = TerrainStampTransformUtility.CalculateWorldAabb(center, size, 90f);
        Bounds b45 = TerrainStampTransformUtility.CalculateWorldAabb(center, size, 45f);
        float expected45 = Mathf.Sqrt(0.5f) * (size.x + size.y);

        TerrainStampTransformUtility.GetWorldCorners(
            center,
            size,
            45f,
            out Vector2 c0,
            out Vector2 c1,
            out Vector2 c2,
            out Vector2 c3
        );

        bool passed =
            Approx(b0.size.x, 40f) && Approx(b0.size.z, 20f)
            && Approx(b90.size.x, 20f) && Approx(b90.size.z, 40f)
            && Approx(b45.size.x, expected45) && Approx(b45.size.z, expected45)
            && ContainsXZ(b45, c0) && ContainsXZ(b45, c1)
            && ContainsXZ(b45, c2) && ContainsXZ(b45, c3);

        Add(
            "Rotated stamp AABB",
            passed,
            passed
                ? "0, 90, and 45 degree AABBs match expectations and contain every corner."
                : "Rotated Bounds are not conservative or dimensions are incorrect."
        );
    }

    private static void ValidateSignature()
    {
        TerrainStampModifier stamp = new TerrainStampModifier();
        stamp.SetSizeXZInternal(new Vector2(40f, 20f));

        stamp.SetRotationDegreesInternal(0f);
        string s0 = TerrainAuthoringStateUtility.GetModifierContentSignature(stamp);

        stamp.SetRotationDegreesInternal(360f);
        string s360 = TerrainAuthoringStateUtility.GetModifierContentSignature(stamp);

        stamp.SetRotationDegreesInternal(90f);
        string s90 = TerrainAuthoringStateUtility.GetModifierContentSignature(stamp);

        bool passed =
            !string.IsNullOrEmpty(s0)
            && s0 == s360
            && s0 != s90;

        Add(
            "Rotation deterministic signature",
            passed,
            passed
                ? "0/360 share output identity while 90 degrees changes it."
                : "Rotation signature canonicalization failed."
        );
    }

    private static void ValidateZeroDegreeUv(TestContext context)
    {
        Vector2 center = context.Stamp.PositionXZ;
        Vector2 size = context.Stamp.SizeXZ;
        int step = Mathf.Max(1, context.HalfStampSamples / 4);
        bool passed = true;

        for (int z = -context.HalfStampSamples; z <= context.HalfStampSamples; z += step)
        {
            for (int x = -context.HalfStampSamples; x <= context.HalfStampSamples; x += step)
            {
                Vector2 world = center + new Vector2(x, z) * context.SampleSpacing;
                Vector2 legacyUv = (world - (center - size * 0.5f)) / size;
                Vector2 local = TerrainStampTransformUtility.WorldToLocalXZ(world, center, 0f);
                Vector2 rotatedUv = new Vector2(local.x / size.x + 0.5f, local.y / size.y + 0.5f);

                if (!Vec(legacyUv, rotatedUv))
                {
                    passed = false;
                    break;
                }
            }

            if (!passed)
            {
                break;
            }
        }

        Add(
            "0-degree UV addressing regression",
            passed,
            passed
                ? "Center/local-axis UV addressing reduces to the legacy mapping at 0 degrees."
                : "The 0-degree UV mapping differs from the legacy mapping."
        );
    }

    private static void ValidateGpuQuarterTurn(TestContext context)
    {
        context.Stamp.SetRotationDegreesInternal(0f);

        if (!TryComposeDirect(context, out float[] baseline, out string error0))
        {
            Add("Asymmetric GPU stamp rotation", false, "0-degree composition failed: " + error0);
            return;
        }

        context.Stamp.SetRotationDegreesInternal(90f);

        if (!TryComposeDirect(context, out float[] rotated, out string error90))
        {
            Add("Asymmetric GPU stamp rotation", false, "90-degree composition failed: " + error90);
            return;
        }

        int step = Mathf.Max(1, context.HalfStampSamples / 8);
        bool mapped = true;
        float visibleDifference = 0f;

        for (int dz = -context.HalfStampSamples; dz <= context.HalfStampSamples; dz += step)
        {
            for (int dx = -context.HalfStampSamples; dx <= context.HalfStampSamples; dx += step)
            {
                int rx = context.CenterIndex + dx;
                int rz = context.CenterIndex + dz;
                int bx = context.CenterIndex - dz;
                int bz = context.CenterIndex + dx;

                if (!InRange(rx, context.SamplesPerSide)
                    || !InRange(rz, context.SamplesPerSide)
                    || !InRange(bx, context.SamplesPerSide)
                    || !InRange(bz, context.SamplesPerSide))
                {
                    continue;
                }

                float actual = rotated[rz * context.SamplesPerSide + rx];
                float expected = baseline[bz * context.SamplesPerSide + bx];

                if (Mathf.Abs(actual - expected) > GpuTolerance)
                {
                    mapped = false;
                    break;
                }

                float samePosition = baseline[rz * context.SamplesPerSide + rx];
                visibleDifference = Mathf.Max(visibleDifference, Mathf.Abs(actual - samePosition));
            }

            if (!mapped)
            {
                break;
            }
        }

        bool passed = mapped && visibleDifference > 0.05f;

        Add(
            "Asymmetric GPU stamp rotation",
            passed,
            passed
                ? "The transient asymmetric height stamp rotates +90 degrees around its center."
                : "GPU quarter-turn mapping is incorrect or the asymmetric source did not visibly rotate."
        );
    }

    private static void ValidateRuntimeParity(WorldSettings worldSettings, TestContext context)
    {
        context.Stamp.SetRotationDegreesInternal(37f);

        if (!TryComposeDirect(context, out float[] direct, out string directError))
        {
            Add("Runtime/shared compositor rotation parity", false, "Direct composition failed: " + directError);
            return;
        }

        TerrainRuntimeHeightCompositionContext runtime = new TerrainRuntimeHeightCompositionContext();

        try
        {
            if (!runtime.TryPrepare(worldSettings, context.AuthoringData, out string prepareError))
            {
                Add("Runtime/shared compositor rotation parity", false, "Runtime preparation failed: " + prepareError);
                return;
            }

            if (!runtime.RequiresComposition(Vector2Int.zero))
            {
                Add("Runtime/shared compositor rotation parity", false, "Rotated stamp was not classified as affecting tile (0, 0). ");
                return;
            }

            float[] runtimeOutput = new float[context.SamplesPerSide * context.SamplesPerSide];

            if (!runtime.TryComposeCommittedTile(
                    context.CommittedTile,
                    Vector2Int.zero,
                    runtimeOutput,
                    out string composeError
                ))
            {
                Add("Runtime/shared compositor rotation parity", false, "Runtime composition failed: " + composeError);
                return;
            }

            bool passed = ArraysEqual(direct, runtimeOutput, GpuTolerance);
            Add(
                "Runtime/shared compositor rotation parity",
                passed,
                passed
                    ? "Runtime context and direct shared compositor match for a 37-degree stamp."
                    : "Runtime and direct shared-compositor output differ beyond tolerance."
            );
        }
        finally
        {
            runtime.Dispose();
        }
    }

    private static bool TryCreateContext(
        WorldSettings worldSettings,
        out TestContext context,
        out string errorMessage
    )
    {
        context = null;
        errorMessage = "";

        int samples = worldSettings.HeightTileSamplesPerSide;
        float spacing = worldSettings.chunkSize
            / Mathf.Max(1, worldSettings.heightfieldResolutionPerChunk);
        float tileSize = worldSettings.HeightTileWorldSize;
        Vector2 worldSize = TerrainClipmapLayoutUtility.CalculateWorldSizeXZ(worldSettings);

        if (samples < 17 || spacing <= 0f || tileSize <= 0f
            || worldSize.x < tileSize || worldSize.y < tileSize)
        {
            errorMessage = "Current world/height-tile layout is invalid for transient validation.";
            return false;
        }

        int centerIndex = (samples - 1) / 2;
        int halfSamples = Mathf.Max(4, Mathf.Min(32, centerIndex - 2));
        float stampSize = halfSamples * 2f * spacing;
        TestContext created = new TestContext();

        try
        {
            created.SamplesPerSide = samples;
            created.CenterIndex = centerIndex;
            created.HalfStampSamples = halfSamples;
            created.SampleSpacing = spacing;
            created.TileWorldSize = tileSize;
            created.WorldSizeXZ = worldSize;
            created.StampTexture = CreateAsymmetricTexture();
            created.StampAsset = ScriptableObject.CreateInstance<TerrainHeightStampAsset>();
            created.StampAsset.SetHeightTextureInternal(created.StampTexture);
            created.Stamp = new TerrainStampModifier();
            created.Stamp.SetStampAssetInternal(created.StampAsset);
            created.Stamp.SetPositionXZInternal(new Vector2(centerIndex * spacing, centerIndex * spacing));
            created.Stamp.SetSizeXZInternal(new Vector2(stampSize, stampSize));
            created.Stamp.SetRotationDegreesInternal(0f);
            created.Stamp.SetHeightDeltaInternal(1f);
            created.Stamp.SetFalloffInternal(0f);
            created.Stamp.SetSmoothingRadiusInternal(0f);
            created.Stamp.SetSmoothingStrengthInternal(1f);
            created.Stamp.SetBlendModeInternal(TerrainHeightBlendMode.Additive);
            created.Stamp.SetEnabledInternal(true);
            created.AuthoringData = ScriptableObject.CreateInstance<TerrainAuthoringData>();
            created.AuthoringData.AddHeightModifierInternal(created.Stamp);
            created.AuthoringData.RepairModifierStableIds();
            created.CommittedTile = CreateZeroTile(samples);
            context = created;
            return true;
        }
        catch (Exception exception)
        {
            created.Dispose();
            errorMessage = "Could not create transient rotation resources.\n\n" + exception.Message;
            return false;
        }
    }

    private static Texture2D CreateAsymmetricTexture()
    {
        Texture2D texture = new Texture2D(5, 5, TextureFormat.RFloat, false, true);
        texture.name = "WorldMeshes Rotation Validation Stamp";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        float[] values =
        {
            0f, 0f, 0f, 0f, 1f,
            0f, 0f, 0f, 0f, 1f,
            0f, 0f, 0f, 0f, 1f,
            0f, 0f, 0f, 0f, 1f,
            1f, 1f, 1f, 1f, 1f
        };

        texture.SetPixelData(values, 0);
        texture.Apply(false, false);
        return texture;
    }

    private static Texture2D CreateZeroTile(int samples)
    {
        Texture2D texture = new Texture2D(samples, samples, TextureFormat.RFloat, false, true);
        texture.name = "WorldMeshes Rotation Validation Base";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Point;
        texture.SetPixelData(new float[samples * samples], 0);
        texture.Apply(false, false);
        return texture;
    }

    private static bool TryComposeDirect(TestContext context, out float[] output, out string errorMessage)
    {
        output = null;
        errorMessage = "";
        RenderTexture target = null;

        try
        {
            target = new RenderTexture(
                context.SamplesPerSide,
                context.SamplesPerSide,
                0,
                RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear
            );
            target.dimension = TextureDimension.Tex2DArray;
            target.volumeDepth = 1;
            target.enableRandomWrite = true;
            target.useMipMap = false;
            target.autoGenerateMips = false;
            target.wrapMode = TextureWrapMode.Clamp;
            target.filterMode = FilterMode.Point;

            if (!target.Create())
            {
                errorMessage = "Could not create transient RFloat composition target.";
                return false;
            }

            Graphics.CopyTexture(context.CommittedTile, 0, 0, target, 0, 0);
            TerrainHeightCompositor compositor = new TerrainHeightCompositor();

            if (!compositor.TryComposeTile(
                    target,
                    Vector2Int.zero,
                    0,
                    context.SamplesPerSide,
                    context.SampleSpacing,
                    context.TileWorldSize,
                    context.WorldSizeXZ,
                    context.AuthoringData,
                    out _,
                    out _,
                    out errorMessage
                ))
            {
                return false;
            }

            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(
                target,
                0,
                0,
                context.SamplesPerSide,
                0,
                context.SamplesPerSide,
                0,
                1,
                TextureFormat.RFloat,
                null
            );

            request.WaitForCompletion();

            if (request.hasError)
            {
                errorMessage = "Async GPU readback failed.";
                return false;
            }

            var data = request.GetData<float>();
            output = new float[data.Length];

            for (int i = 0; i < data.Length; i++)
            {
                output[i] = data[i];
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
        finally
        {
            if (target != null)
            {
                if (target.IsCreated())
                {
                    target.Release();
                }

                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }

    private static bool ContainsXZ(Bounds bounds, Vector2 point)
    {
        return point.x >= bounds.min.x - MathTolerance
            && point.x <= bounds.max.x + MathTolerance
            && point.y >= bounds.min.z - MathTolerance
            && point.y <= bounds.max.z + MathTolerance;
    }

    private static bool ArraysEqual(float[] a, float[] b, float tolerance)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (Mathf.Abs(a[i] - b[i]) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static bool InRange(int value, int length)
    {
        return value >= 0 && value < length;
    }

    private static bool Vec(Vector2 a, Vector2 b)
    {
        return Approx(a.x, b.x) && Approx(a.y, b.y);
    }

    private static bool Approx(float a, float b)
    {
        return Mathf.Abs(a - b) <= MathTolerance;
    }

    private static void Add(string name, bool passed, string details)
    {
        results.Add(new Result { Name = name, Passed = passed, Details = details ?? "" });
    }

    private static void Finish()
    {
        validationRunning = false;
        lastPassedCount = 0;
        lastFailedCount = 0;
        System.Text.StringBuilder builder = new System.Text.StringBuilder();

        for (int i = 0; i < results.Count; i++)
        {
            Result result = results[i];

            if (result.Passed)
            {
                lastPassedCount++;
            }
            else
            {
                lastFailedCount++;
            }

            builder.Append(result.Passed ? "PASS: " : "FAIL: ");
            builder.Append(result.Name);

            if (!string.IsNullOrEmpty(result.Details))
            {
                builder.Append("\n");
                builder.Append(result.Details);
            }

            if (i < results.Count - 1)
            {
                builder.Append("\n\n");
            }
        }

        lastSummary = builder.Length > 0 ? builder.ToString() : "No validation results were produced.";

        if (lastFailedCount > 0)
        {
            Debug.LogError("Stamp rotation foundation validation failed.\n\n" + lastSummary);
        }
        else
        {
            Debug.Log("Stamp rotation foundation validation passed.\n\n" + lastSummary);
        }

        SceneView.RepaintAll();
    }
}
