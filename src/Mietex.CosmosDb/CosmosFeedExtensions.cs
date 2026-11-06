using System.Runtime.CompilerServices;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Mietex.CosmosDb;

/// <summary>Async helpers for Cosmos feed iterators and LINQ queries.</summary>
public static class CosmosFeedExtensions
{
    /// <summary>Converts a feed iterator to an async stream.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="iterator">Feed iterator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async stream of results.</returns>
    public static async IAsyncEnumerable<T> ToAsyncEnumerableFromFeedIterator<T>(
        this FeedIterator<T> iterator,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(iterator);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }

    /// <summary>Materialises a LINQ query, falling back to in-memory execution for <c>EnumerableQuery</c>.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="query">Query to materialise.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All results.</returns>
    public static async Task<List<T>> ToListWithFeedSupportAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Provider is EnumerableQuery<T>)
        {
            return query.ToList();
        }

        using var iterator = query.ToFeedIterator();
        return await ToListAsync(
            iterator.ToAsyncEnumerableFromFeedIterator(cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Materialises a transformed async stream produced from a LINQ query.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="query">Query to materialise.</param>
    /// <param name="transform">Async stream transform.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All transformed results.</returns>
    public static async Task<List<T>> ToListWithFeedSupportAsync<T>(
        this IQueryable<T> query,
        Func<IAsyncEnumerable<T>, IAsyncEnumerable<T>> transform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(transform);

        IAsyncEnumerable<T> source;
        if (query.Provider is EnumerableQuery<T>)
        {
            source = ToAsyncEnumerable(query, cancellationToken);
        }
        else
        {
            using var iterator = query.ToFeedIterator();
            source = iterator.ToAsyncEnumerableFromFeedIterator(cancellationToken);
            return await ToListAsync(
                transform(source),
                cancellationToken).ConfigureAwait(false);
        }

        return await ToListAsync(
            transform(source),
            cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    private static async Task<List<T>> ToListAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            results.Add(item);
        }

        return results;
    }
}

/// <summary>Formats query definitions for trace logging without changing the executed query.</summary>
public static class CosmosQueryLogFormatter
{
    /// <summary>Expands parameters into the query text for logging only.</summary>
    /// <param name="queryDefinition">Query to format.</param>
    /// <returns>Expanded query text.</returns>
    public static string Format(QueryDefinition queryDefinition)
    {
        ArgumentNullException.ThrowIfNull(queryDefinition);

        var query = queryDefinition.QueryText;
        var parameters = queryDefinition.GetQueryParameters();
        if (parameters.Count == 0)
        {
            return query;
        }

        // Replace the longest names first so @id does not corrupt @id2.
        foreach (var (name, value) in parameters.OrderByDescending(parameter => parameter.Name.Length))
        {
            var formattedValue = value switch
            {
                null => "null",
                string text => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'",
                DateTime dateTime => $"'{dateTime:o}'",
                DateTimeOffset dateTimeOffset => $"'{dateTimeOffset:o}'",
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
            };

            query = query.Replace(name, formattedValue, StringComparison.Ordinal);
        }

        return query;
    }
}
