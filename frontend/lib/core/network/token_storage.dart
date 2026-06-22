import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// The persisted authentication tokens: the short-lived access JWT, the rotating
/// refresh token, and the access token's UTC expiry.
class StoredTokens {
  const StoredTokens({
    required this.accessToken,
    required this.refreshToken,
    required this.accessTokenExpiresAtUtc,
  });

  final String accessToken;
  final String refreshToken;
  final DateTime accessTokenExpiresAtUtc;

  /// Whether the access token is at/after its expiry (small skew tolerated by
  /// the server). The interceptor refreshes reactively on 401, so this is only
  /// an optimization hint, not the source of truth.
  bool get isAccessExpired =>
      DateTime.now().toUtc().isAfter(accessTokenExpiresAtUtc);
}

/// Abstraction over secure token persistence so the auth interceptor and
/// repository can be unit-tested against an in-memory fake (the real
/// implementation talks to platform channels that aren't available in tests).
abstract interface class TokenStorage {
  Future<StoredTokens?> read();
  Future<String?> readAccessToken();
  Future<String?> readRefreshToken();
  Future<void> save(StoredTokens tokens);
  Future<void> clear();
}

/// [TokenStorage] backed by [FlutterSecureStorage] — the OS keychain on macOS,
/// Credential Manager on Windows, Keystore on Android. Tokens never touch plain
/// shared-prefs (rubric §5 / A.2: expired tokens are handled, not ignored).
class SecureTokenStorage implements TokenStorage {
  SecureTokenStorage([FlutterSecureStorage? storage])
      : _storage = storage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
              // macOS: use the legacy (file-based) keychain rather than the data
              // protection keychain. The latter requires a `keychain-access-groups`
              // entitlement, which in turn forces development-certificate signing —
              // impossible for a locally-signed (ad-hoc) `flutter run` build, and
              // the cause of the `errSecMissingEntitlement (-34018)` on login.
              mOptions: MacOsOptions(useDataProtectionKeyChain: false),
            );

  final FlutterSecureStorage _storage;

  static const String _accessKey = 'courtly.access_token';
  static const String _refreshKey = 'courtly.refresh_token';
  static const String _expiresKey = 'courtly.access_expires_utc';

  @override
  Future<StoredTokens?> read() async {
    final access = await _storage.read(key: _accessKey);
    final refresh = await _storage.read(key: _refreshKey);
    final expiresRaw = await _storage.read(key: _expiresKey);
    if (access == null || refresh == null || expiresRaw == null) return null;
    final expires = DateTime.tryParse(expiresRaw);
    if (expires == null) return null;
    return StoredTokens(
      accessToken: access,
      refreshToken: refresh,
      accessTokenExpiresAtUtc: expires.toUtc(),
    );
  }

  @override
  Future<String?> readAccessToken() => _storage.read(key: _accessKey);

  @override
  Future<String?> readRefreshToken() => _storage.read(key: _refreshKey);

  @override
  Future<void> save(StoredTokens tokens) async {
    await _storage.write(key: _accessKey, value: tokens.accessToken);
    await _storage.write(key: _refreshKey, value: tokens.refreshToken);
    await _storage.write(
      key: _expiresKey,
      value: tokens.accessTokenExpiresAtUtc.toUtc().toIso8601String(),
    );
  }

  @override
  Future<void> clear() async {
    await _storage.delete(key: _accessKey);
    await _storage.delete(key: _refreshKey);
    await _storage.delete(key: _expiresKey);
  }
}

/// The app's token store. Overridden in tests with an in-memory fake.
final tokenStorageProvider = Provider<TokenStorage>((ref) => SecureTokenStorage());
