// F27 — Client notifications UI. Unit test: AppNotification.fromJson parsing + type mapping.
import 'package:courtly/core/enums/notification_type.dart';
import 'package:courtly/core/theme/app_colors.dart';
import 'package:courtly/features/notifications/domain/notification_models.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AppNotification.fromJson', () {
    test('parses a full camelCase map with a non-null readAtUtc', () {
      final json = <String, dynamic>{
        'id': 42,
        'type': 1, // reservationConfirmed
        'typeName': 'Reservation confirmed',
        'title': 'Booking confirmed',
        'text': 'Your court is booked for tomorrow.',
        'isRead': true,
        'createdAtUtc': '2026-07-01T08:30:00Z',
        'readAtUtc': '2026-07-01T09:00:00Z',
      };

      final n = AppNotification.fromJson(json);

      expect(n.id, 42);
      expect(n.type, NotificationType.reservationConfirmed);
      expect(n.title, 'Booking confirmed');
      expect(n.text, 'Your court is booked for tomorrow.');
      expect(n.isRead, isTrue);
      expect(n.createdAtUtc, DateTime.utc(2026, 7, 1, 8, 30));
      expect(n.createdAtUtc.isUtc, isTrue);
      expect(n.readAtUtc, DateTime.utc(2026, 7, 1, 9, 0));
      expect(n.readAtUtc!.isUtc, isTrue);
    });

    test('parses a null readAtUtc (unread) and coerces createdAtUtc to UTC', () {
      final json = <String, dynamic>{
        'id': 7,
        'type': 0, // reservationCreated
        'typeName': 'Reservation created',
        'title': 'New reservation',
        'text': 'A slot was reserved.',
        'isRead': false,
        'createdAtUtc': '2026-06-30T22:15:00+02:00',
        'readAtUtc': null,
      };

      final n = AppNotification.fromJson(json);

      expect(n.id, 7);
      expect(n.type, NotificationType.reservationCreated);
      expect(n.isRead, isFalse);
      expect(n.readAtUtc, isNull);
      // +02:00 offset normalized to UTC (20:15Z).
      expect(n.createdAtUtc.isUtc, isTrue);
      expect(n.createdAtUtc, DateTime.utc(2026, 6, 30, 20, 15));
    });

    test('falls back to general for an out-of-range type value', () {
      final json = <String, dynamic>{
        'id': 9,
        'type': 99,
        'title': 't',
        'text': 'x',
        'isRead': false,
        'createdAtUtc': '2026-07-01T00:00:00Z',
        'readAtUtc': null,
      };

      final n = AppNotification.fromJson(json);

      expect(n.type, NotificationType.general);
    });

    test('exposes the enum label and tone used by the tile/badge', () {
      final confirmed = AppNotification.fromJson(<String, dynamic>{
        'id': 1,
        'type': 1, // reservationConfirmed → success
        'title': 't',
        'text': 'x',
        'isRead': false,
        'createdAtUtc': '2026-07-01T00:00:00Z',
        'readAtUtc': null,
      });
      final cancelled = AppNotification.fromJson(<String, dynamic>{
        'id': 2,
        'type': 2, // reservationCancelled → danger
        'title': 't',
        'text': 'x',
        'isRead': false,
        'createdAtUtc': '2026-07-01T00:00:00Z',
        'readAtUtc': null,
      });

      expect(confirmed.type.label, 'Reservation confirmed');
      expect(confirmed.type.tone, StatusTone.success);
      expect(cancelled.type.label, 'Reservation cancelled');
      expect(cancelled.type.tone, StatusTone.danger);
    });

    test('copyWith flips isRead and stamps readAtUtc without mutating others', () {
      final original = AppNotification.fromJson(<String, dynamic>{
        'id': 5,
        'type': 4, // paymentSucceeded
        'title': 'Payment received',
        'text': 'Thanks!',
        'isRead': false,
        'createdAtUtc': '2026-07-01T10:00:00Z',
        'readAtUtc': null,
      });

      final readAt = DateTime.utc(2026, 7, 1, 11, 0);
      final updated = original.copyWith(isRead: true, readAtUtc: readAt);

      expect(updated.id, original.id);
      expect(updated.type, original.type);
      expect(updated.title, original.title);
      expect(updated.text, original.text);
      expect(updated.createdAtUtc, original.createdAtUtc);
      expect(updated.isRead, isTrue);
      expect(updated.readAtUtc, readAt);
      // Original untouched (immutable).
      expect(original.isRead, isFalse);
      expect(original.readAtUtc, isNull);
    });
  });
}
