using UnityEngine;

public sealed class TerrainHeightStreamingFamilyWriteResult
{
    public Vector2Int Coordinate
    {
        get;
        private set;
    }

    public bool Attempted
    {
        get;
        private set;
    }

    public bool PreparedComplete
    {
        get;
        private set;
    }

    public int RequestedStrideCount
    {
        get;
        private set;
    }

    public int PreparedStrideCount
    {
        get;
        private set;
    }

    public int CreatedAssetCount
    {
        get;
        private set;
    }

    public int UpdatedAssetCount
    {
        get;
        private set;
    }

    public int FailedStride
    {
        get;
        private set;
    }

    public bool OutputMayHaveChanged
    {
        get;
        private set;
    }

    public string ErrorMessage
    {
        get;
        private set;
    }

    internal TerrainHeightStreamingFamilyWriteResult(
        Vector2Int coordinate,
        bool attempted,
        bool preparedComplete,
        int requestedStrideCount,
        int preparedStrideCount,
        int createdAssetCount,
        int updatedAssetCount,
        int failedStride,
        bool outputMayHaveChanged,
        string errorMessage
    )
    {
        Coordinate =
            coordinate;

        Attempted =
            attempted;

        PreparedComplete =
            preparedComplete;

        RequestedStrideCount =
            Mathf.Max(
                0,
                requestedStrideCount
            );

        PreparedStrideCount =
            Mathf.Max(
                0,
                preparedStrideCount
            );

        CreatedAssetCount =
            Mathf.Max(
                0,
                createdAssetCount
            );

        UpdatedAssetCount =
            Mathf.Max(
                0,
                updatedAssetCount
            );

        FailedStride =
            Mathf.Max(
                0,
                failedStride
            );

        OutputMayHaveChanged =
            outputMayHaveChanged;

        ErrorMessage =
            errorMessage ??
            "";
    }

    public static TerrainHeightStreamingFamilyWriteResult
        NotAttempted(
            Vector2Int coordinate,
            int requestedStrideCount,
            string errorMessage = ""
        )
    {
        return
            new TerrainHeightStreamingFamilyWriteResult(
                coordinate,
                false,
                false,
                requestedStrideCount,
                0,
                0,
                0,
                0,
                false,
                errorMessage
            );
    }
}
