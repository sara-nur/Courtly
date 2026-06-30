import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/reports_api.dart';
import '../data/reports_repository.dart';
import '../domain/report_filters.dart';
import '../domain/report_models.dart';

/// Transport over the report endpoints, bound to the app Dio client.
final reportsApiProvider = Provider<ReportsApi>(
  (ref) => ReportsApi(ref.watch(dioProvider)),
);

/// The reports repository (normalizes failures to `ApiException`). Overridden with a fake in widget tests.
final reportsRepositoryProvider = Provider<ReportsRepository>(
  (ref) => ReportsRepository(ref.watch(reportsApiProvider)),
);

/// The reservations report data for a given filter set (drives the in-app table). Keyed by the filters, so changing a
/// filter refetches; cached per distinct filter set.
final reservationsReportDataProvider =
    FutureProvider.family<ReservationsReportData, ReservationsReportFilters>(
  (ref, filters) => ref.watch(reportsRepositoryProvider).reservationsReportData(
        fromUtc: filters.fromUtc,
        toUtc: filters.toUtc,
        courtId: filters.courtId,
        status: filters.status,
      ),
);

/// The revenue & court-utilisation report data for a given month/court filter (drives the in-app table).
final revenueReportDataProvider =
    FutureProvider.family<RevenueUtilizationReportData, RevenueReportFilters>(
  (ref, filters) => ref.watch(reportsRepositoryProvider).revenueUtilizationReportData(
        year: filters.year,
        month: filters.monthNumber,
        courtId: filters.courtId,
      ),
);
