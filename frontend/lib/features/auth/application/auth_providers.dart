import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/constants/app_roles.dart';
import '../../../core/env/app_config.dart';
import '../../../core/network/dio_client.dart';
import '../../../core/network/token_storage.dart';
import '../data/auth_api.dart';
import '../data/auth_repository.dart';

/// Transport over `/api/auth`, bound to the configured Dio client.
final authApiProvider = Provider<AuthApi>(
  (ref) => AuthApi(ref.watch(dioProvider)),
);

/// The auth repository, with the **role gate** chosen by build flavor: the admin
/// desktop admits Admin + Staff; the client app (feature 22) admits User.
final authRepositoryProvider = Provider<AuthRepository>((ref) {
  final config = ref.watch(appConfigProvider);
  final allowedRoles = config.isAdmin
      ? <String>{AppRoles.admin, AppRoles.staff}
      : <String>{AppRoles.user};
  return AuthRepository(
    api: ref.watch(authApiProvider),
    storage: ref.watch(tokenStorageProvider),
    allowedRoles: allowedRoles,
  );
});
