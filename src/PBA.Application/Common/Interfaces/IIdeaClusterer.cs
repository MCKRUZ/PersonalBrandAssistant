namespace PBA.Application.Common.Interfaces;

public sealed record ClusterInput(int Index, string Title, string? Summary);

public interface IIdeaClusterer
{
    /// <summary>
    /// Groups items that cover the same real-world event. Returns groups of input indices;
    /// the first index in each group is the primary. Returns an empty list on parse failure.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<int>>> ClusterAsync(
        IReadOnlyList<ClusterInput> items, CancellationToken ct = default);
}
