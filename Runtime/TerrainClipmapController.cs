using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TerrainHeightmapStreamer))]
public class TerrainClipmapController :
    MonoBehaviour
{
    // =====================================================
    // SOURCE DATA
    // =====================================================

    [Header("Source Data")]

    [SerializeField]
    private WorldSettings worldSettings;

    // =====================================================
    // TARGET
    // =====================================================

    [Header("Target")]

    /*
     * Transform that the clipmap follows horizontally.
     *
     * Target Y is intentionally ignored.
     */
    [SerializeField]
    private Transform target;

    // =====================================================
    // DEBUG
    // =====================================================

    [Header("Debug")]

    [SerializeField]
    private bool logCoverageStalls =
        true;

    // =====================================================
    // RUNTIME STATE
    // =====================================================

    private TerrainHeightmapStreamer streamer;

    private float fixedY;

    private bool waitingForHeightData;

    private bool hasWarnedMissingSettings;

    private bool hasWarnedMissingTarget;

    private bool hasWarnedMissingStreamer;

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public Transform Target
    {
        get
        {
            return target;
        }

        set
        {
            target =
                value;

            hasWarnedMissingTarget =
                false;
        }
    }

    /*
     * True only when the desired target position cannot yet be
     * represented safely by the currently active GPU height cache.
     *
     * Gameplay movement systems may use this as a streaming gate.
     */
    public bool IsWaitingForHeightData
    {
        get
        {
            return waitingForHeightData;
        }
    }

    // =====================================================
    // EDITOR / HIERARCHY CONFIGURATION
    // =====================================================

    /*
     * Called by TerrainWorldHierarchyGenerator.
     *
     * The movement target is deliberately not changed here so a
     * manually assigned Player reference survives hierarchy sync.
     */
    public bool Configure(
        WorldSettings settings
    )
    {
        if (
            worldSettings ==
            settings
        )
        {
            return false;
        }

        worldSettings =
            settings;

        return true;
    }

    // =====================================================
    // ENABLE
    // =====================================================

    private void OnEnable()
    {
        fixedY =
            transform.position.y;

        waitingForHeightData =
            false;

        hasWarnedMissingSettings =
            false;

        hasWarnedMissingTarget =
            false;

        hasWarnedMissingStreamer =
            false;

        EnsureStreamerReference();

        if (!Application.isPlaying)
        {
            return;
        }

        UpdateClipmapPosition();
    }

    // =====================================================
    // DISABLE
    // =====================================================

    private void OnDisable()
    {
        if (
            Application.isPlaying
            &&
            streamer != null
        )
        {
            streamer.ClearCoverageRequest();
        }

        waitingForHeightData =
            false;
    }

    // =====================================================
    // LATE UPDATE
    // =====================================================

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        UpdateClipmapPosition();
    }

    // =====================================================
    // CAN TARGET MOVE TO
    // =====================================================

    /*
     * Optional gameplay movement gate.
     *
     * A player/controller can call this BEFORE committing its own
     * movement. The method also requests/prefetches the cache window
     * needed by that proposed target position.
     *
     * Returning false means the active cache is not yet able to
     * support the corresponding clipmap position.
     *
     * Near or outside world bounds this can still return true:
     * out-of-world clipmap fragments are intentionally clipped and
     * do not require height tiles.
     */
    public bool CanTargetMoveTo(
        Vector3 targetWorldPosition
    )
    {
        if (
            !TryGetDesiredClipmapPosition(
                targetWorldPosition,
                out Vector3 desiredPosition
            )
        )
        {
            return false;
        }

        EnsureStreamerReference();

        if (streamer == null)
        {
            return false;
        }

        streamer.RequestCoverageForClipmapCenter(
            desiredPosition
        );

        return
            streamer.CanActiveCacheCoverClipmapAt(
                desiredPosition
            );
    }

    // =====================================================
    // UPDATE CLIPMAP POSITION
    // =====================================================

    private void UpdateClipmapPosition()
    {
        if (
            !TryGetDesiredClipmapPosition(
                target != null
                    ? target.position
                    : Vector3.zero,
                out Vector3 desiredPosition
            )
        )
        {
            return;
        }

        EnsureStreamerReference();

        if (streamer == null)
        {
            if (!hasWarnedMissingStreamer)
            {
                Debug.LogWarning(
                    "TerrainClipmapController requires " +
                    "TerrainHeightmapStreamer on the same " +
                    "GameObject.",
                    this
                );

                hasWarnedMissingStreamer =
                    true;
            }

            return;
        }

        hasWarnedMissingStreamer =
            false;

        // -------------------------------------------------
        // Request / prefetch desired cache coverage
        // -------------------------------------------------

        streamer.RequestCoverageForClipmapCenter(
            desiredPosition
        );

        // -------------------------------------------------
        // Active-cache safety
        // -------------------------------------------------

        if (
            !streamer.CanActiveCacheCoverClipmapAt(
                desiredPosition
            )
        )
        {
            if (!waitingForHeightData)
            {
                waitingForHeightData =
                    true;

                if (logCoverageStalls)
                {
                    Debug.Log(
                        "Terrain clipmap movement is waiting " +
                        "for height-cache coverage.\n\n" +
                        $"Desired Center: " +
                        $"({desiredPosition.x}, " +
                        $"{desiredPosition.y}, " +
                        $"{desiredPosition.z})",
                        this
                    );
                }
            }

            /*
             * Keep the last safe clipmap transform until the
             * pending cache commits.
             *
             * The streamer still knows desiredPosition because the
             * request above is independent of transform.position.
             */
            return;
        }

        if (waitingForHeightData)
        {
            waitingForHeightData =
                false;

            if (logCoverageStalls)
            {
                Debug.Log(
                    "Terrain height-cache coverage is ready. " +
                    "Clipmap movement resumed.",
                    this
                );
            }
        }

        // -------------------------------------------------
        // Apply
        // -------------------------------------------------

        if (
            transform.position ==
            desiredPosition
        )
        {
            return;
        }

        transform.position =
            desiredPosition;
    }

    // =====================================================
    // DESIRED CLIPMAP POSITION
    // =====================================================

    private bool TryGetDesiredClipmapPosition(
        Vector3 targetWorldPosition,
        out Vector3 desiredPosition
    )
    {
        desiredPosition =
            transform.position;

        // -------------------------------------------------
        // WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            if (!hasWarnedMissingSettings)
            {
                Debug.LogWarning(
                    "TerrainClipmapController cannot follow " +
                    "a target because WorldSettings is not " +
                    "assigned.",
                    this
                );

                hasWarnedMissingSettings =
                    true;
            }

            return false;
        }

        hasWarnedMissingSettings =
            false;

        // -------------------------------------------------
        // Target
        // -------------------------------------------------

        if (target == null)
        {
            if (!hasWarnedMissingTarget)
            {
                Debug.LogWarning(
                    "TerrainClipmapController has no target.\n\n" +
                    "Assign the Player Transform to the Target " +
                    "field on the Clipmap GameObject.",
                    this
                );

                hasWarnedMissingTarget =
                    true;
            }

            return false;
        }

        hasWarnedMissingTarget =
            false;

        // -------------------------------------------------
        // Finest clipmap spacing
        // -------------------------------------------------

        float baseSpacing =
            Mathf.Max(
                0.0001f,
                worldSettings.ClipmapBaseSpacing
            );

        // -------------------------------------------------
        // Snap target XZ
        // -------------------------------------------------

        float snappedX =
            Mathf.Round(
                targetWorldPosition.x /
                baseSpacing
            )
            *
            baseSpacing;

        float snappedZ =
            Mathf.Round(
                targetWorldPosition.z /
                baseSpacing
            )
            *
            baseSpacing;

        /*
         * IMPORTANT:
         *
         * There is deliberately NO world-boundary clamp here.
         *
         * The clipmap remains centered on the player near and
         * beyond world edges. Geometry outside the authoritative
         * world is culled by ClipmapTerrain.shader.
         */
        desiredPosition =
            new Vector3(
                snappedX,
                fixedY,
                snappedZ
            );

        return true;
    }

    // =====================================================
    // STREAMER REFERENCE
    // =====================================================

    private void EnsureStreamerReference()
    {
        if (streamer != null)
        {
            return;
        }

        streamer =
            GetComponent<TerrainHeightmapStreamer>();
    }
}
