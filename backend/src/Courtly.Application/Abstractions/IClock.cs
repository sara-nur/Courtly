namespace Courtly.Application.Abstractions;

/// <summary>Abstracts <see cref="DateTime.UtcNow"/> so token expiry is deterministically testable.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
