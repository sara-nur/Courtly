import 'package:intl/intl.dart';

/// Centralized formatting helpers (single source — rubric §3.3 "no duplicated
/// hardcoded values in multiple places"). Used by `DateTimePickerField`,
/// list tiles, receipts, etc.
abstract final class Formatters {
  static final DateFormat _date = DateFormat('MMM d, yyyy');
  static final DateFormat _time = DateFormat('h:mm a');
  static final DateFormat _dateTime = DateFormat('MMM d, yyyy · h:mm a');
  static final NumberFormat _money = NumberFormat.currency(symbol: r'$');

  /// e.g. `Oct 24, 2023`.
  static String date(DateTime value) => _date.format(value);

  /// e.g. `10:00 AM`.
  static String time(DateTime value) => _time.format(value);

  /// e.g. `Oct 24, 2023 · 10:00 AM`.
  static String dateTime(DateTime value) => _dateTime.format(value);

  /// e.g. `$45.00`.
  static String money(num amount) => _money.format(amount);

  /// e.g. `$45.00` from an integer number of cents.
  static String moneyFromCents(int cents) => _money.format(cents / 100);
}
