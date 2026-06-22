import 'package:courtly/core/network/api_exception.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final requestOptions = RequestOptions(path: '/api/auth/login');

  DioException badResponse(Map<String, dynamic> body, int status) {
    return DioException(
      requestOptions: requestOptions,
      type: DioExceptionType.badResponse,
      response: Response(
        requestOptions: requestOptions,
        statusCode: status,
        data: body,
      ),
    );
  }

  group('ApiException.fromDio', () {
    test('maps a validation ErrorResponse dict to per-field errors', () {
      final ex = ApiException.fromDio(
        badResponse({
          'status': 400,
          'title': 'Validation failed',
          'errors': {
            'UserNameOrEmail': ['Username or email is required.'],
            'Password': ['Password is required.'],
          },
        }, 400),
      );

      expect(ex.statusCode, 400);
      expect(ex.message, 'Validation failed');
      expect(ex.hasFieldErrors, isTrue);
      // Keys are lower-cased so lookups are case-insensitive against the backend.
      expect(ex.errorFor('UserNameOrEmail'), 'Username or email is required.');
      expect(ex.errorFor('password'), 'Password is required.');
      expect(ex.errorFor('unknownField'), isNull);
    });

    test('maps a 401 to a banner message with no field errors', () {
      final ex = ApiException.fromDio(
        badResponse({'status': 401, 'title': 'Invalid credentials.'}, 401),
      );

      expect(ex.isUnauthorized, isTrue);
      expect(ex.message, 'Invalid credentials.');
      expect(ex.hasFieldErrors, isFalse);
    });

    test('falls back to a friendly message on a connection failure', () {
      final ex = ApiException.fromDio(
        DioException(
          requestOptions: requestOptions,
          type: DioExceptionType.connectionError,
        ),
      );

      expect(ex.statusCode, isNull);
      expect(ex.message, contains('Could not reach the server'));
    });
  });

  test('ApiException.from unwraps a DioException carrying an ApiException', () {
    const inner = ApiException(message: 'Invalid credentials.', statusCode: 401);
    final wrapped = DioException(requestOptions: requestOptions, error: inner);

    expect(identical(ApiException.from(wrapped), inner), isTrue);
  });
}
