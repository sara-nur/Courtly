/// Mirrors the backend `TimeOfDayBucket` enum (int values 0..2).
/// Groups bookable slots into Morning / Afternoon / Evening and is also a
/// recommender signal.
enum TimeOfDayBucket {
  morning,
  afternoon,
  evening;

  int get wireValue => index;

  static TimeOfDayBucket fromWire(int value) =>
      (value >= 0 && value < values.length) ? values[value] : morning;

  String get label => switch (this) {
        TimeOfDayBucket.morning => 'Morning',
        TimeOfDayBucket.afternoon => 'Afternoon',
        TimeOfDayBucket.evening => 'Evening',
      };
}
