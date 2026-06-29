using Courtly.Application.Messaging;
using Xunit;

namespace Courtly.Tests.Messaging;

/// <summary>
/// Feature 17: the pure backoff/dead-letter core of the email consumer's retry loop (rubric Appendix A.1). Verifies the
/// exponential schedule <c>2^attempt</c> (1s → 2s → 4s → 8s), that dead-lettering only kicks in once the final attempt
/// has failed, and the bounded attempt count — kept I/O-free so it asserts the policy without any RabbitMQ plumbing.
/// </summary>
public class RetryPolicyTests
{
    [Theory]
    [InlineData(0, 1)] // first retry waits 1s
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)] // last allowed attempt
    public void DelayForAttempt_follows_the_exponential_schedule(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.DelayForAttempt(attempt));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)] // MaxAttempts exhausted -> route to DLQ
    public void ShouldDeadLetter_only_after_the_final_attempt(int attempt, bool expected)
    {
        Assert.Equal(expected, RetryPolicy.ShouldDeadLetter(attempt));
    }

    [Fact]
    public void MaxAttempts_is_four()
    {
        Assert.Equal(4, RetryPolicy.MaxAttempts);
    }
}
