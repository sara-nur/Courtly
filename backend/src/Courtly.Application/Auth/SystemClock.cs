using Courtly.Application.Abstractions;

namespace Courtly.Application.Auth;

/// <summary>Production <see cref="IClock"/> — the only place auth reads the wall clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
