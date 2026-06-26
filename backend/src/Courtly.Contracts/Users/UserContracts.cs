namespace Courtly.Contracts.Users;

/// <summary>
/// A user summary for the admin user lookup (feature 15 "+ New Booking" customer picker). DTO only — the Identity
/// entity never leaves the service. <see cref="FullName"/> is the resolved first + last name (never raw ids on the
/// wire); <see cref="Role"/> is the user's primary role name (informational, may be null). This is the minimal slice
/// feature 15 needs; the full Users management surface (detail + their reservations, activate/deactivate, role
/// assignment, admin-edit profile) is feature 15A, which extends this same contract/service/controller.
/// </summary>
public sealed record UserSummaryDto(
    Guid Id,
    string FullName,
    string? Email,
    string? Role,
    bool IsActive);
