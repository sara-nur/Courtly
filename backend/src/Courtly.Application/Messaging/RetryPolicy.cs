namespace Courtly.Application.Messaging;

/// <summary>
/// The pure, side-effect-free core of the email consumer's retry loop (rubric Appendix A.1: bounded retries with
/// exponential backoff, then dead-letter). A failed delivery is retried with a backoff of <c>1s → 2s → 4s → 8s</c>
/// keyed off the zero-based attempt number; once <see cref="MaxAttempts"/> is exhausted the message is routed to the
/// dead-letter queue instead of being retried forever. Kept free of I/O and logging so the backoff/dead-letter policy
/// can be reasoned about and unit-tested in isolation, separate from the RabbitMQ plumbing that consumes it.
/// </summary>
public static class RetryPolicy
{
    /// <summary>Total delivery attempts before a message is dead-lettered (attempts 0..3).</summary>
    public const int MaxAttempts = 4;

    /// <summary>Backoff before the next attempt: <c>2^attempt</c> seconds — 0→1s, 1→2s, 2→4s, 3→8s.</summary>
    public static TimeSpan DelayForAttempt(int attempt) => TimeSpan.FromSeconds(1 << attempt);

    /// <summary>True when the just-failed <paramref name="attempt"/> was the last one allowed, so the message must be
    /// dead-lettered rather than retried.</summary>
    public static bool ShouldDeadLetter(int attempt) => attempt >= MaxAttempts - 1;
}
