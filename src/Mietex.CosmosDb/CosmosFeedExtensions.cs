using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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

        var parameters = queryDefinition.GetQueryParameters();
        if (parameters.Count == 0)
        {
            return queryDefinition.QueryText;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            if (!string.IsNullOrEmpty(name))
            {
                values[name] = FormatValue(value);
            }
        }

        // Single pass: values containing '@name' are never expanded again.
        return Regex.Replace(
            queryDefinition.QueryText,
            "@[A-Za-z_][A-Za-z0-9_]*",
            match => values.TryGetValue(match.Value, out var value) ? value : match.Value,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static string FormatValue(object? value)
    {
        try
        {
            return value switch
            {
                null => "null",
                string text => $"'{EscapeText(text)}'",
                DateTime dateTime => $"'{dateTime:o}'",
                DateTimeOffset dateTimeOffset => $"'{dateTimeOffset:o}'",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
            };
        }
        catch (Exception)
        {
            return "<unformattable>";
        }
    }

    private static string EscapeText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            switch (character)
            {
                case '\'':
                    builder.Append("''");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
