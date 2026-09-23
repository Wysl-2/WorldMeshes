using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public sealed class TerrainHeightStreamingCompileSummary
{
    private readonly ReadOnlyCollection<int>
        targetStrides;

    public static TerrainHeightStreamingCompileSummary
        NotEvaluated =>
            new TerrainHeightStreamingCompileSummary(
                false,
                false,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                ""
            );

    public bool Evaluated
    {
        get;
        private set;
    }

    public bool GenerationEnabled
    {
        get;
        private set;
    }

    public IReadOnlyList<int> TargetStrides =>
        targetStrides;

    public int RequestedFamilyCount
    {
        get;
        private set;
    }

    public int CompleteFamilyCount
    {
        get;
        private set;
    }

    public int IncompleteFamilyCount
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

    public int RemovedAssetCount
    {
        get;
        private set;
    }

    public bool DatasetFinalized
    {
        get;
        private set;
    }

    public string FirstError
    {
        get;
        private set;
    }

    internal TerrainHeightStreamingCompileSummary(
        bool evaluated,
        bool generationEnabled,
        IEnumerable<int> targetStrides,
        int requestedFamilyCount,
        int completeFamilyCount,
        int incompleteFamilyCount,
        int createdAssetCount,
        int updatedAssetCount,
        int removedAssetCount,
        bool datasetFinalized,
        string firstError
    )
    {
        Evaluated =
            evaluated;

        GenerationEnabled =
            generationEnabled;

        List<int> strides =
            targetStrides != null
                ? new List<int>(targetStrides)
                : new List<int>();

        strides.Sort();

        this.targetStrides =
            strides.AsReadOnly();

        RequestedFamilyCount =
            Mathf.Max(
                0,
                requestedFamilyCount
            );

        CompleteFamilyCount =
            Mathf.Max(
                0,
                completeFamilyCount
            );

        IncompleteFamilyCount =
            Mathf.Max(
                0,
                incompleteFamilyCount
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

        RemovedAssetCount =
            Mathf.Max(
                0,
                removedAssetCount
            );

        DatasetFinalized =
            datasetFinalized;

        FirstError =
            firstError ??
            "";
    }
}
