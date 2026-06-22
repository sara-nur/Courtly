import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../env/app_config.dart';
import 'auth_interceptor.dart';
import 'error_interceptor.dart';
import 'token_storage.dart';

/// Network timeouts (single source — rubric §3.3: no scattered magic numbers).
abstract final class _NetworkTimeouts {
  static const Duration connect = Duration(seconds: 15);
  static const Duration receive = Duration(seconds: 20);
  static const Duration send = Duration(seconds: 20);
}

/// Broadcasts that the session became invalid (a refresh failed inside the auth
/// interceptor). The [AuthController] listens and flips to unauthenticated; the
/// router redirect then sends the user to the login screen.
class SessionExpiredSignal extends ChangeNotifier {
  void trigger() => notifyListeners();
}

/// One [SessionExpiredSignal] per app, bridging the interceptor (which has no
/// Riverpod access) to the auth controller.
final sessionExpiredSignalProvider = Provider<SessionExpiredSignal>((ref) {
  final signal = SessionExpiredSignal();
  ref.onDispose(signal.dispose);
  return signal;
});

/// The configured [Dio] for the whole app. Base URL comes from [AppConfig]
/// (read once from `--dart-define`, rubric §3.3). Two interceptors are wired in
/// order: [AuthInterceptor] (attach token, refresh on 401) then
/// [ErrorInterceptor] (map failures to [ApiException]).
final dioProvider = Provider<Dio>((ref) {
  final config = ref.watch(appConfigProvider);
  final storage = ref.watch(tokenStorageProvider);
  final signal = ref.watch(sessionExpiredSignalProvider);

  final baseOptions = BaseOptions(
    baseUrl: config.apiBaseUrl,
    connectTimeout: _NetworkTimeouts.connect,
    receiveTimeout: _NetworkTimeouts.receive,
    sendTimeout: _NetworkTimeouts.send,
    contentType: Headers.jsonContentType,
    responseType: ResponseType.json,
    headers: {Headers.acceptHeader: Headers.jsonContentType},
  );

  final dio = Dio(baseOptions);

  // A separate, interceptor-free client used to refresh the token and replay
  // the original request — prevents the auth interceptor from recursing.
  final refreshClient = Dio(baseOptions);

  dio.interceptors.add(
    AuthInterceptor(
      storage: storage,
      refreshClient: refreshClient,
      onSessionExpired: signal.trigger,
    ),
  );
  dio.interceptors.add(ErrorInterceptor());

  ref.onDispose(() {
    dio.close(force: true);
    refreshClient.close(force: true);
  });

  return dio;
});
