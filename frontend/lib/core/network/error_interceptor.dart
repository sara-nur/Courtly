import 'package:dio/dio.dart';

import 'api_exception.dart';

/// Translates every transport-level [DioException] into a typed, user-safe
/// [ApiException] carried in `DioException.error` (rubric A.2 / §3.4: surface the
/// backend's real validation message, never a generic "Bad request", and never a
/// stack trace). Registered **after** [AuthInterceptor] so token refresh gets
/// first crack at a 401 before the error is mapped for display.
class ErrorInterceptor extends Interceptor {
  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    final apiError = ApiException.fromDio(err);
    handler.reject(
      DioException(
        requestOptions: err.requestOptions,
        response: err.response,
        type: err.type,
        error: apiError,
        message: apiError.message,
      ),
    );
  }
}
