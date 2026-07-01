import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/profile_api.dart';
import '../data/profile_repository.dart';

/// Transport over the `/api/auth` profile endpoints, bound to the shared Dio.
final profileApiProvider = Provider<ProfileApi>(
  (ref) => ProfileApi(ref.watch(dioProvider)),
);

/// The profile repository (error-normalizing wrapper over [ProfileApi]).
final profileRepositoryProvider = Provider<ProfileRepository>(
  (ref) => ProfileRepository(ref.watch(profileApiProvider)),
);

/// The signed-in user's current avatar bytes, or `null` when they have none.
/// Fetched through Dio (so the JWT is attached and refreshed on 401) and
/// rendered with `Image.memory`. Invalidated after an upload so the profile
/// header re-reads the freshly stored image.
final avatarBytesProvider = FutureProvider.autoDispose<Uint8List?>(
  (ref) => ref.watch(profileRepositoryProvider).fetchAvatarBytes(),
);
