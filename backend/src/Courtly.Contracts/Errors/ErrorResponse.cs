namespace Courtly.Contracts.Errors;

/// <summary>
/// The single standardized error body returned for every failed request (rubric §3.4: clients get a
/// standardized message and a typed DTO — never a raw <c>Exception</c>, stack trace, or anonymous object).
/// ProblemDetails-shaped so the Flutter dio error interceptor (feature 8) can read <see cref="Errors"/> and
/// surface each message below its field. <see cref="Detail"/> carries diagnostic text only in Development.
/// </summary>
public sealed record ErrorResponse
{
    /// <summary>HTTP status code, echoed in the body for clients that only read the payload.</summary>
    public required int Status { get; init; }

    /// <summary>Short, human-readable summary safe to show the user (e.g. "Validation failed").</summary>
    public required string Title { get; init; }

    /// <summary>Optional longer explanation. Populated with exception detail only in Development.</summary>
    public string? Detail { get; init; }

    /// <summary>Correlation id (the request's trace identifier) for matching a client error to a server log.</summary>
    public string? TraceId { get; init; }

    /// <summary>Field-level validation messages keyed by field name; null for non-validation errors.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
