import 'dart:convert';
import 'dart:typed_data';

import 'package:courtly/core/network/auth_interceptor.dart';
import 'package:courtly/core/network/token_storage.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AuthInterceptor', () {
    test('refreshes on 401, replays the request, and stores the new tokens',
        () async {
      final harness = _Harness.seeded();
      final response = await harness.dio.get<dynamic>('/api/auth/me');

      expect(response.statusCode, 200);
      expect((response.data as Map)['ok'], isTrue);
      expect(harness.refreshHits, 1);
      expect(harness.retryHits, 1);
      // Tokens rotated to the refreshed pair.
      expect(await harness.storage.readAccessToken(), 'access-new');
      expect(await harness.storage.readRefreshToken(), 'refresh-new');
    });

    test('coalesces concurrent 401s into a single refresh (single-flight)',
        () async {
      final harness = _Harness.seeded();

      final responses = await Future.wait([
        harness.dio.get<dynamic>('/api/auth/me'),
        harness.dio.get<dynamic>('/api/auth/me'),
      ]);

      expect(responses.every((r) => r.statusCode == 200), isTrue);
      expect(harness.refreshHits, 1); // only one refresh despite two 401s
      expect(harness.retryHits, 2); // each request replayed on its own
    });

    test('clears the session and signals expiry when refresh fails', () async {
      final harness = _Harness.seeded(refreshSucceeds: false);

      await expectLater(
        harness.dio.get<dynamic>('/api/auth/me'),
        throwsA(isA<DioException>()),
      );

      expect(harness.sessionExpired, isTrue);
      expect(await harness.storage.read(), isNull);
    });
  });
}

/// Wires a main Dio (always 401s `/me`) plus a bare refresh client, an in-memory
/// token store, and the [AuthInterceptor] under test, counting each hit.
class _Harness {
  _Harness({required this.refreshSucceeds}) {
    storage = _InMemoryTokenStorage(
      StoredTokens(
        accessToken: 'access-old',
        refreshToken: 'refresh-old',
        accessTokenExpiresAtUtc:
            DateTime.now().toUtc().add(const Duration(minutes: 5)),
      ),
    );

    refreshClient = Dio(BaseOptions(baseUrl: 'http://test'))
      ..httpClientAdapter = _FakeAdapter((options) async {
        if (options.path.contains('/api/auth/refresh')) {
          refreshHits++;
          await Future<void>.delayed(const Duration(milliseconds: 5));
          if (!refreshSucceeds) return _json({'title': 'Invalid token'}, 401);
          return _json(_newSession, 200);
        }
        if (options.path.contains('/api/auth/me')) {
          retryHits++;
          return _json({'ok': true}, 200);
        }
        return _json({'title': 'Not found'}, 404);
      });

    dio = Dio(BaseOptions(baseUrl: 'http://test'))
      ..httpClientAdapter = _FakeAdapter((options) async {
        // The main client always rejects /me to drive the refresh path.
        return _json({'title': 'Unauthorized'}, 401);
      });

    dio.interceptors.add(
      AuthInterceptor(
        storage: storage,
        refreshClient: refreshClient,
        onSessionExpired: () => sessionExpired = true,
      ),
    );
  }

  factory _Harness.seeded({bool refreshSucceeds = true}) =>
      _Harness(refreshSucceeds: refreshSucceeds);

  final bool refreshSucceeds;
  late final _InMemoryTokenStorage storage;
  late final Dio dio;
  late final Dio refreshClient;
  int refreshHits = 0;
  int retryHits = 0;
  bool sessionExpired = false;

  static final Map<String, dynamic> _newSession = {
    'accessToken': 'access-new',
    'refreshToken': 'refresh-new',
    'accessTokenExpiresAtUtc':
        DateTime.now().toUtc().add(const Duration(minutes: 15)).toIso8601String(),
    'user': {
      'id': '11111111-1111-1111-1111-111111111111',
      'userName': 'desktop',
      'email': 'desktop@courtly.test',
      'firstName': 'Desktop',
      'lastName': 'Admin',
      'cityId': null,
      'roles': ['Admin'],
    },
  };
}

ResponseBody _json(Map<String, dynamic> body, int status) {
  return ResponseBody.fromString(
    jsonEncode(body),
    status,
    headers: {
      Headers.contentTypeHeader: [Headers.jsonContentType],
    },
  );
}

class _FakeAdapter implements HttpClientAdapter {
  _FakeAdapter(this.onFetch);

  final Future<ResponseBody> Function(RequestOptions options) onFetch;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) =>
      onFetch(options);

  @override
  void close({bool force = false}) {}
}

class _InMemoryTokenStorage implements TokenStorage {
  _InMemoryTokenStorage([this._tokens]);

  StoredTokens? _tokens;

  @override
  Future<StoredTokens?> read() async => _tokens;

  @override
  Future<String?> readAccessToken() async => _tokens?.accessToken;

  @override
  Future<String?> readRefreshToken() async => _tokens?.refreshToken;

  @override
  Future<void> save(StoredTokens tokens) async => _tokens = tokens;

  @override
  Future<void> clear() async => _tokens = null;
}
