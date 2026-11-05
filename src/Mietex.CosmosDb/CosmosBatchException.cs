using System.Net;

namespace Mietex.CosmosDb;

/// <summary>One failed operation inside a transactional batch.</summary>
/// <param name="Index">Zero-based operation index.</param>
/// <param name="StatusCode">Operation status code.</param>
/// <param name="Message">Truncated operation error message, when available.</param>
public sealed record CosmosBatchOperationError(int Index, HttpStatusCode StatusCode, string? Message);

/// <summary>
/// Thrown when a transactional batch does not succeed. Carries per-operation failures and Cosmos
/// diagnostics so callers can classify the failure without parsing strings.
/// </summary>
public sealed class CosmosBatchException : Exception
{
    /// <summary>Initializes a batch exception.</summary>
    /// <param name="message">Error message.</param>
    /// <param name="statusCode">Batch status code.</param>
    /// <param name="operationCount">Number of operations in the batch.</param>
    /// <param name="failures">Per-operation failures.</param>
    /// <param name="diagnostics">Cosmos diagnostics, when available.</param>
    /// <param name="activityId">Cosmos activity id, when available.</param>
    /// <param name="requestCharge">Request charge, when available.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public CosmosBatchException(
        string message,
        HttpStatusCode statusCode,
        int operationCount,
        IReadOnlyList<CosmosBatchOperationError> failures,
        string? diagnostics = null,
        string? activityId = null,
        double requestCharge = 0,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        OperationCount = operationCount;
        Failures = failures;
        Diagnostics = diagnostics;
        ActivityId = activityId;
        RequestCharge = requestCharge;
    }

    /// <summary>Gets the batch status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Gets the number of operations in the batch.</summary>
    public int OperationCount { get; }

    /// <summary>Gets the per-operation failures.</summary>
    public IReadOnlyList<CosmosBatchOperationError> Failures { get; }

    /// <summary>Gets Cosmos diagnostics, when available.</summary>
    public string? Diagnostics { get; }

    /// <summary>Gets the Cosmos activity id, when available.</summary>
    public string? ActivityId { get; }

    /// <summary>Gets the request charge, when available.</summary>
    public double RequestCharge { get; }
}
