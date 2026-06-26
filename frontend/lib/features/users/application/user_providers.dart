import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/user_api.dart';
import '../data/user_repository.dart';
import '../domain/user_summary.dart';

/// Transport over the user endpoint, bound to the app Dio client.
final userApiProvider = Provider<UserApi>(
  (ref) => UserApi(ref.watch(dioProvider)),
);

/// The user repository (normalizes failures to `ApiException`).
final userRepositoryProvider = Provider<UserRepository>(
  (ref) => UserRepository(ref.watch(userApiProvider)),
);

/// Page size for the customer lookup behind "+ New Booking". One large page is
/// the practical lookup (the picker is searchable; this caps the initial load).
const int kUserLookupPageSize = 100;

/// Active customers for the "+ New Booking" customer [DbDropdown] (name + email,
/// never a raw id). Bookings can only be created for an active user, so the
/// picker lists active users only.
final userLookupProvider = FutureProvider<List<UserSummary>>((ref) async {
  final page = await ref
      .watch(userRepositoryProvider)
      .search(pageSize: kUserLookupPageSize);
  return page.items.where((u) => u.isActive).toList(growable: false);
});
