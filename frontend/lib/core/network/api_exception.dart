import 'package:dio/dio.dart';

/// A typed, user-safe failure surfaced from the API layer.
///
/// It is built from the backend's standardized `ErrorResponse` body (feature 6:
/// `{ status, title, detail, traceId, errors }`). [message] is always safe to
/// show; [fieldErrors] carries per-field validation messages so the login form
/// can render each one **below its field** (rubric §4) instead of a generic
/// "Bad request" (rubric A.2 — never hide the backend's real message).
class ApiException implements Exception {
  const ApiException({
    required this.message,
    this.statusCode,
    this.fieldErrors,
    this.traceId,
  });

  /// Human-readable, user-safe summary (the `title`/`detail` of the error body,
  /// or a connection-failure fallback).
  final String message;

  /// HTTP status code when the failure carried a response.
  final int? statusCode;

  /// Field-level validation messages, keyed by **lower-cased** field name so
  /// lookups are case-insensitive (backend may key them `UserNameOrEmail`).
  final Map<String, List<String>>? fieldErrors;

  /// Correlation id for matching a client error to a server log line.
  final String? traceId;

  bool get isUnauthorized => statusCode == 401;
  bool get hasFieldErrors => fieldErrors != null && fieldErrors!.isNotEmpty;

  /// First validation message for [field] (case-insensitive), or `null`.
  String? errorFor(String field) {
    final errors = fieldErrors;
    if (errors == null) return null;
    final match = errors[field.toLowerCase()];
    return (match != null && match.isNotEmpty) ? match.first : null;
  }

  /// Builds an [ApiException] from the backend `ErrorResponse` JSON [body].
  /// Keys are serialized camelCase by ASP.NET (`status`, `title`, `errors`…).
  factory ApiException.fromErrorResponse(
    Map<String, dynamic> body, {
    int? statusCode,
  }) {
    final rawErrors = body['errors'];
    Map<String, List<String>>? fieldErrors;
    if (rawErrors is Map) {
      fieldErrors = {
        for (final entry in rawErrors.entries)
          entry.key.toString().toLowerCase(): (entry.value is List)
              ? (entry.value as List).map((e) => e.toString()).toList()
              : <String>[entry.value.toString()],
      };
    }

    final title = body['title'] as String?;
    final detail = body['detail'] as String?;
    final status = (body['status'] as num?)?.toInt() ?? statusCode;

    return ApiException(
      message: _resolveMessage(title, detail, fieldErrors),
      statusCode: status,
      fieldErrors: fieldErrors,
      traceId: body['traceId'] as String?,
    );
  }

  /// Maps any [DioException] to a user-safe [ApiException]: a structured error
  /// body becomes its fields; a network/timeout failure becomes a friendly
  /// connection message (no stack trace, no internals — rubric §3.4).
  factory ApiException.fromDio(DioException error) {
    final data = error.response?.data;
    if (data is Map<String, dynamic>) {
      return ApiException.fromErrorResponse(
        data,
        statusCode: error.response?.statusCode,
      );
    }
    if (data is Map) {
      return ApiException.fromErrorResponse(
        Map<String, dynamic>.from(data),
        statusCode: error.response?.statusCode,
      );
    }
    return ApiException(
      message: _connectionMessage(error),
      statusCode: error.response?.statusCode,
    );
  }

  /// Normalizes any thrown [error] (already-mapped [ApiException], a
  /// [DioException] carrying one in `.error`, or anything else) to an
  /// [ApiException]. Used by the data layer so callers only ever see this type.
  static ApiException from(Object error) {
    if (error is ApiException) return error;
    if (error is DioException) {
      final inner = error.error;
      if (inner is ApiException) return inner;
      return ApiException.fromDio(error);
    }
    return const ApiException(
      message: 'Something went wrong. Please try again.',
    );
  }

  static String _resolveMessage(
    String? title,
    String? detail,
    Map<String, List<String>>? fieldErrors,
  ) {
    if (title != null && title.isNotEmpty) return title;
    if (detail != null && detail.isNotEmpty) return detail;
    if (fieldErrors != null) {
      for (final messages in fieldErrors.values) {
        if (messages.isNotEmpty) return messages.first;
      }
    }
    return 'Something went wrong. Please try again.';
  }

  static String _connectionMessage(DioException error) {
    switch (error.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
        return 'The server took too long to respond. Please try again.';
      case DioExceptionType.connectionError:
        return 'Could not reach the server. Check that the API is running.';
      default:
        return 'Could not reach the server. Please try again.';
    }
  }

  @override
  String toString() => 'ApiException($statusCode): $message';
}
