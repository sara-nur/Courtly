import '../../../core/constants/app_roles.dart';

/// The authenticated user (mirrors the backend `UserInfoResponse` from
/// `GET /api/auth/me` and the `user` of an `AuthResponse`). IDs are kept for API
/// calls but never shown in the UI (rubric §6).
class AuthUser {
  const AuthUser({
    required this.id,
    required this.userName,
    required this.email,
    required this.firstName,
    required this.lastName,
    required this.roles,
    this.cityId,
    this.avatarUrl,
  });

  final String id;
  final String userName;
  final String email;
  final String firstName;
  final String lastName;
  final int? cityId;
  final List<String> roles;

  /// Relative path (`/api/auth/me/avatar`) when the user has a profile image, or
  /// `null`. The image bytes are fetched separately (authenticated) — see the
  /// profile feature's `avatarBytesProvider`; this field only signals presence.
  final String? avatarUrl;

  /// Display name for the profile menu — full name, falling back to username.
  String get displayName {
    final full = '$firstName $lastName'.trim();
    return full.isEmpty ? userName : full;
  }

  /// The highest-privilege role this user holds, for display (Admin > Staff > …).
  String get primaryRole {
    if (roles.contains(AppRoles.admin)) return AppRoles.admin;
    if (roles.contains(AppRoles.staff)) return AppRoles.staff;
    return roles.isNotEmpty ? roles.first : AppRoles.user;
  }

  /// Whether this user holds any of [allowed] — the app's role gate (rubric §5).
  bool hasAnyRole(Set<String> allowed) => roles.any(allowed.contains);

  factory AuthUser.fromJson(Map<String, dynamic> json) => AuthUser(
        id: json['id'] as String,
        userName: json['userName'] as String,
        email: json['email'] as String,
        firstName: json['firstName'] as String? ?? '',
        lastName: json['lastName'] as String? ?? '',
        cityId: (json['cityId'] as num?)?.toInt(),
        avatarUrl: json['avatarUrl'] as String?,
        roles: ((json['roles'] as List?) ?? const <dynamic>[])
            .map((e) => e.toString())
            .toList(growable: false),
      );
}

/// A successful authentication result (login / register / refresh): the tokens
/// plus the authenticated user. The refresh token is the raw value; the server
/// stores only its hash.
class AuthSession {
  const AuthSession({
    required this.accessToken,
    required this.refreshToken,
    required this.accessTokenExpiresAtUtc,
    required this.user,
  });

  final String accessToken;
  final String refreshToken;
  final DateTime accessTokenExpiresAtUtc;
  final AuthUser user;

  factory AuthSession.fromJson(Map<String, dynamic> json) => AuthSession(
        accessToken: json['accessToken'] as String,
        refreshToken: json['refreshToken'] as String,
        accessTokenExpiresAtUtc:
            DateTime.parse(json['accessTokenExpiresAtUtc'] as String).toUtc(),
        user: AuthUser.fromJson(
          (json['user'] as Map).cast<String, dynamic>(),
        ),
      );
}

/// Lifecycle of the auth session, read by the router redirect.
/// [unknown] is the brief startup window while a stored session is validated.
enum AuthStatus { unknown, authenticated, unauthenticated }

/// Immutable auth state held by the [AuthController].
class AuthState {
  const AuthState._(this.status, this.user);

  const AuthState.unknown() : this._(AuthStatus.unknown, null);
  const AuthState.unauthenticated() : this._(AuthStatus.unauthenticated, null);
  const AuthState.authenticated(AuthUser user)
      : this._(AuthStatus.authenticated, user);

  final AuthStatus status;
  final AuthUser? user;

  bool get isAuthenticated => status == AuthStatus.authenticated;
  bool get isResolved => status != AuthStatus.unknown;
}
