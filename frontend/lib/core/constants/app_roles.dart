/// Role names — must match the backend seed roles and `[Authorize(Roles=...)]`
/// attributes exactly (rubric §5). Centralized here so the magic strings live
/// in one place (rubric §3.4), never scattered as literals.
abstract final class AppRoles {
  static const String admin = 'Admin';
  static const String staff = 'Staff';
  static const String user = 'User';
}
