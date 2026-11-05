using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb;

/// <summary>Async helpers for Cosmos feed iterators and LINQ queries.</summary>
public static class CosmosFeedExtensions
{
    /// <summary>Converts a feed iterator to an async stream.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="iterator">Feed iterator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async stream of results.</returns>
    public static IAsyncEnumerable<T> ToAsyncEnumerableFromFeedIterator<T>(
        this FeedIterator<T> iterator,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    /// <summary>Materialises a LINQ query, falling back to in-memory execution for <c>EnumerableQuery</c>.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="query">Query to materialise.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All results.</returns>
    public static Task<List<T>> ToListWithFeedSupportAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    /// <summary>Materialises a transformed async stream produced from a LINQ query.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="query">Query to materialise.</param>
    /// <param name="transform">Async stream transform.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All transformed results.</returns>
    public static Task<List<T>> ToListWithFeedSupportAsync<T>(
        this IQueryable<T> query,
        Func<IAsyncEnumerable<T>, IAsyncEnumerable<T>> transform,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

/// <summary>Formats query definitions for trace logging without changing the executed query.</summary>
public static class CosmosQueryLogFormatter
{
    /// <summary>Expands parameters into the query text for logging only.</summary>
    /// <param name="queryDefinition">Query to format.</param>
    /// <returns>Expanded query text.</returns>
    public static string Format(QueryDefinition queryDefinition) => throw new NotImplementedException();
}
