namespace Courtly.Domain.Constants;

/// <summary>
/// Canonical role names. Single source of truth shared by the seeder (feature 4) and the
/// <c>[Authorize(Roles = ...)]</c> attributes added in feature 5, so the two never drift.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string User = "User";

    /// <summary>All roles in seed order.</summary>
    public static readonly string[] All = { Admin, Staff, User };
}
