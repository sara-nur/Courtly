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

/// <summary>
/// A single user's full admin detail (feature 15A user management). DTO only — the Identity entity never leaves the
/// service. <see cref="FullName"/> is the resolved first + last name; <see cref="CityName"/> is the resolved city
/// (null when the user has no city); <see cref="Roles"/> is the complete role set (single-role today, but modelled as a
/// list so the contract does not change if that ever loosens). The user's reservations are <b>not</b> embedded here —
/// the client reuses <c>GET /api/reservations?userId=</c> for that. <see cref="IsProtected"/> marks the seeded super
/// administrator, which cannot be deactivated or have its role changed by anyone (the client disables those actions).
/// </summary>
public sealed record UserDetailDto(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    string? Email,
    string? PhoneNumber,
    string? CityName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    DateTime CreatedAtUtc,
    bool IsProtected);

/// <summary>Admin request to activate/deactivate a user (feature 15A). The target is the route id; the acting admin
/// comes from the token — a self-deactivate is rejected in the service.</summary>
public sealed record SetUserActiveRequest(bool IsActive);

/// <summary>Admin request to assign a user's single role (feature 15A). <see cref="Role"/> must be one of the canonical
/// role names; the service replaces the user's current roles with it. A self-role-change is rejected in the service.
/// </summary>
public sealed record AssignRoleRequest(string Role);
