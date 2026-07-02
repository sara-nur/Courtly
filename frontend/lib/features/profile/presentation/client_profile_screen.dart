import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/validators.dart';
import '../../../core/widgets/app_text_field.dart';
import '../../../core/widgets/confirm_dialog.dart';
import '../../../core/widgets/db_dropdown.dart';
import '../../auth/application/auth_controller.dart';
import '../../court_catalog/application/court_providers.dart' show cityLookupProvider;
import '../../reference_data/domain/reference_models.dart' show City;
import '../application/profile_providers.dart';

/// Client **Profile** tab (feature 28). Lets the signed-in user edit their own
/// personal data (name, email, city) and profile image, change their password
/// behind a "Change password" toggle (rubric §294 — the current password must
/// be confirmed), and sign out. Profile edit and password change are two
/// independent forms, so saving the profile never requires the password fields
/// (rubric §288/§290) and validation messages render **below** each control.
class ClientProfileScreen extends ConsumerStatefulWidget {
  const ClientProfileScreen({super.key});

  @override
  ConsumerState<ClientProfileScreen> createState() =>
      _ClientProfileScreenState();
}

class _ClientProfileScreenState extends ConsumerState<ClientProfileScreen> {
  final _profileFormKey = GlobalKey<FormState>();
  final _passwordFormKey = GlobalKey<FormState>();

  final _firstNameController = TextEditingController();
  final _lastNameController = TextEditingController();
  final _emailController = TextEditingController();
  final _currentPasswordController = TextEditingController();
  final _newPasswordController = TextEditingController();
  final _confirmPasswordController = TextEditingController();

  // Field keys mirror the backend's (lower-cased) error keys so server-side
  // messages land below the matching control.
  static const String _firstNameField = 'firstname';
  static const String _lastNameField = 'lastname';
  static const String _emailField = 'email';
  static const String _cityField = 'cityid';
  static const String _currentPasswordField = 'currentpassword';
  static const String _newPasswordField = 'newpassword';
  static const String _avatarField = 'file';

  int? _cityId;
  bool _changingPassword = false;
  bool _obscureCurrent = true;
  bool _obscureNew = true;
  bool _obscureConfirm = true;
  bool _savingProfile = false;
  bool _savingPassword = false;
  String? _profileError;
  String? _passwordError;
  final Map<String, String> _serverErrors = {};

  Uint8List? _pickedAvatarBytes;
  String? _pickedAvatarName;
  bool _seeded = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_seeded) return;
    final user = ref.read(authControllerProvider).user;
    if (user != null) {
      _firstNameController.text = user.firstName;
      _lastNameController.text = user.lastName;
      _emailController.text = user.email;
      _cityId = user.cityId;
      _seeded = true;
    }
  }

  @override
  void dispose() {
    _firstNameController.dispose();
    _lastNameController.dispose();
    _emailController.dispose();
    _currentPasswordController.dispose();
    _newPasswordController.dispose();
    _confirmPasswordController.dispose();
    super.dispose();
  }

  void _clearServerError(String fieldKey) {
    if (_serverErrors.remove(fieldKey) != null) setState(() {});
  }

  City? _selectedCity(List<City> cities) {
    if (_cityId == null) return null;
    for (final c in cities) {
      if (c.id == _cityId) return c;
    }
    return null;
  }

  Future<void> _pickAvatar() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.image,
      withData: true,
    );
    if (result == null || result.files.isEmpty) return;
    final file = result.files.first;
    if (file.bytes == null) return;
    setState(() {
      _pickedAvatarBytes = file.bytes;
      _pickedAvatarName = file.name;
      _serverErrors.remove(_avatarField);
    });
  }

  Future<void> _saveProfile() async {
    setState(() {
      _profileError = null;
      _serverErrors
        ..remove(_firstNameField)
        ..remove(_lastNameField)
        ..remove(_emailField)
        ..remove(_cityField)
        ..remove(_avatarField);
    });
    if (!_profileFormKey.currentState!.validate()) return;

    setState(() => _savingProfile = true);
    try {
      final repo = ref.read(profileRepositoryProvider);
      var user = await repo.updateProfile(
        firstName: _firstNameController.text.trim(),
        lastName: _lastNameController.text.trim(),
        email: _emailController.text.trim(),
        cityId: _cityId,
      );
      if (_pickedAvatarBytes != null) {
        user = await repo.uploadAvatar(
          bytes: _pickedAvatarBytes!,
          filename: _pickedAvatarName ?? 'avatar.png',
        );
        ref.invalidate(avatarBytesProvider);
      }
      ref.read(authControllerProvider.notifier).updateUser(user);
      if (!mounted) return;
      setState(() {
        _pickedAvatarBytes = null;
        _pickedAvatarName = null;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Profile saved.')),
      );
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        if (e.hasFieldErrors) {
          e.fieldErrors!.forEach((field, messages) {
            if (messages.isNotEmpty) _serverErrors[field] = messages.first;
          });
        }
        _profileError = _serverErrors.keys.any(_isProfileKey) ? null : e.message;
      });
      _profileFormKey.currentState!.validate();
    } catch (_) {
      if (!mounted) return;
      setState(() => _profileError = 'Something went wrong. Please try again.');
    } finally {
      if (mounted) setState(() => _savingProfile = false);
    }
  }

  bool _isProfileKey(String key) =>
      key == _firstNameField ||
      key == _lastNameField ||
      key == _emailField ||
      key == _cityField ||
      key == _avatarField;

  Future<void> _changePassword() async {
    setState(() {
      _passwordError = null;
      _serverErrors
        ..remove(_currentPasswordField)
        ..remove(_newPasswordField);
    });
    if (!_passwordFormKey.currentState!.validate()) return;

    setState(() => _savingPassword = true);
    try {
      await ref.read(profileRepositoryProvider).changePassword(
            currentPassword: _currentPasswordController.text,
            newPassword: _newPasswordController.text,
          );
      if (!mounted) return;
      setState(() {
        _changingPassword = false;
        _currentPasswordController.clear();
        _newPasswordController.clear();
        _confirmPasswordController.clear();
      });
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Password updated.')),
      );
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        if (e.hasFieldErrors) {
          e.fieldErrors!.forEach((field, messages) {
            if (messages.isNotEmpty) _serverErrors[field] = messages.first;
          });
        }
        final hasFieldError =
            _serverErrors.containsKey(_currentPasswordField) ||
                _serverErrors.containsKey(_newPasswordField);
        _passwordError = hasFieldError ? null : e.message;
      });
      _passwordFormKey.currentState!.validate();
    } catch (_) {
      if (!mounted) return;
      setState(() => _passwordError = 'Something went wrong. Please try again.');
    } finally {
      if (mounted) setState(() => _savingPassword = false);
    }
  }

  Future<void> _signOut() async {
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Sign out',
      message: 'Sign out of Courtly?',
      confirmLabel: 'Sign out',
      icon: Icons.logout,
    );
    if (confirmed) {
      await ref.read(authControllerProvider.notifier).logout();
      // The router redirect returns to the sign-in screen.
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final citiesAsync = ref.watch(cityLookupProvider);
    final avatarAsync = ref.watch(avatarBytesProvider);

    return SingleChildScrollView(
      padding: const EdgeInsets.all(AppSpacing.lg),
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 480),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _buildAvatar(avatarAsync),
              const SizedBox(height: AppSpacing.lg),
              Text('Personal details', style: theme.textTheme.titleMedium),
              const SizedBox(height: AppSpacing.sm),
              _buildProfileForm(citiesAsync),
              const SizedBox(height: AppSpacing.xl),
              _buildPasswordSection(),
              const SizedBox(height: AppSpacing.xl),
              Card(
                child: ListTile(
                  leading: const Icon(Icons.logout, color: AppColors.danger),
                  title: const Text('Sign out'),
                  onTap: _signOut,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  // --- Avatar (kept compact so the image is well under 50% of the form) ------
  Widget _buildAvatar(AsyncValue<Uint8List?> avatarAsync) {
    ImageProvider? image;
    if (_pickedAvatarBytes != null) {
      image = MemoryImage(_pickedAvatarBytes!);
    } else {
      final bytes = avatarAsync.asData?.value;
      if (bytes != null && bytes.isNotEmpty) image = MemoryImage(bytes);
    }

    return Column(
      children: [
        CircleAvatar(
          radius: 44,
          backgroundColor: AppColors.surfaceMuted,
          backgroundImage: image,
          child: image == null
              ? const Icon(Icons.person_outline,
                  size: 40, color: AppColors.textSecondary)
              : null,
        ),
        const SizedBox(height: AppSpacing.xs),
        TextButton.icon(
          onPressed: _savingProfile ? null : _pickAvatar,
          icon: const Icon(Icons.photo_camera_outlined, size: 18),
          label: const Text('Change photo'),
        ),
        if (_serverErrors[_avatarField] != null)
          Text(
            _serverErrors[_avatarField]!,
            style: const TextStyle(color: AppColors.danger, fontSize: 12),
            textAlign: TextAlign.center,
          ),
      ],
    );
  }

  Widget _buildProfileForm(AsyncValue<List<City>> citiesAsync) {
    return Form(
      key: _profileFormKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (_profileError != null) ...[
            _ErrorBanner(message: _profileError!),
            const SizedBox(height: AppSpacing.md),
          ],
          AppTextField(
            controller: _firstNameController,
            label: 'First name',
            prefixIcon: Icons.badge_outlined,
            enabled: !_savingProfile,
            textInputAction: TextInputAction.next,
            autovalidateMode: AutovalidateMode.onUserInteraction,
            onChanged: (_) => _clearServerError(_firstNameField),
            validator: (v) =>
                Validators.required('First name', v) ??
                _serverErrors[_firstNameField],
          ),
          const SizedBox(height: AppSpacing.md),
          AppTextField(
            controller: _lastNameController,
            label: 'Last name',
            prefixIcon: Icons.badge_outlined,
            enabled: !_savingProfile,
            textInputAction: TextInputAction.next,
            autovalidateMode: AutovalidateMode.onUserInteraction,
            onChanged: (_) => _clearServerError(_lastNameField),
            validator: (v) =>
                Validators.required('Last name', v) ??
                _serverErrors[_lastNameField],
          ),
          const SizedBox(height: AppSpacing.md),
          AppTextField(
            controller: _emailController,
            label: 'Email',
            prefixIcon: Icons.email_outlined,
            keyboardType: TextInputType.emailAddress,
            enabled: !_savingProfile,
            textInputAction: TextInputAction.next,
            autovalidateMode: AutovalidateMode.onUserInteraction,
            onChanged: (_) => _clearServerError(_emailField),
            validator: (v) =>
                Validators.email(v) ?? _serverErrors[_emailField],
          ),
          const SizedBox(height: AppSpacing.md),
          citiesAsync.when(
            data: (cities) => DbDropdown<City>(
              value: _selectedCity(cities),
              items: cities,
              itemLabel: (c) => c.name,
              label: 'City',
              hint: 'Select a city',
              enabled: !_savingProfile,
              validator: (_) => _serverErrors[_cityField],
              onChanged: (c) => setState(() {
                _cityId = c?.id;
                _serverErrors.remove(_cityField);
              }),
            ),
            loading: () => const Padding(
              padding: EdgeInsets.symmetric(vertical: AppSpacing.sm),
              child: LinearProgressIndicator(),
            ),
            error: (_, __) => const Text('Could not load cities.'),
          ),
          const SizedBox(height: AppSpacing.lg),
          SizedBox(
            height: AppSpacing.inputHeight,
            child: ElevatedButton(
              onPressed: _savingProfile ? null : _saveProfile,
              child: _savingProfile
                  ? const _ButtonSpinner()
                  : const Text('Save changes'),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildPasswordSection() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SwitchListTile(
          contentPadding: EdgeInsets.zero,
          title: const Text('Change password'),
          subtitle: const Text('Update your account password'),
          value: _changingPassword,
          onChanged: _savingPassword
              ? null
              : (on) => setState(() {
                    _changingPassword = on;
                    _passwordError = null;
                    if (!on) {
                      _currentPasswordController.clear();
                      _newPasswordController.clear();
                      _confirmPasswordController.clear();
                      _serverErrors
                        ..remove(_currentPasswordField)
                        ..remove(_newPasswordField);
                    }
                  }),
        ),
        if (_changingPassword) ...[
          const SizedBox(height: AppSpacing.sm),
          Form(
            key: _passwordFormKey,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (_passwordError != null) ...[
                  _ErrorBanner(message: _passwordError!),
                  const SizedBox(height: AppSpacing.md),
                ],
                AppTextField(
                  controller: _currentPasswordController,
                  label: 'Current password',
                  prefixIcon: Icons.lock_outline,
                  obscureText: _obscureCurrent,
                  enabled: !_savingPassword,
                  autovalidateMode: AutovalidateMode.onUserInteraction,
                  onChanged: (_) => _clearServerError(_currentPasswordField),
                  validator: (v) =>
                      Validators.required('Current password', v) ??
                      _serverErrors[_currentPasswordField],
                  suffixIcon: _obscureToggle(
                    _obscureCurrent,
                    () => setState(() => _obscureCurrent = !_obscureCurrent),
                  ),
                ),
                const SizedBox(height: AppSpacing.md),
                AppTextField(
                  controller: _newPasswordController,
                  label: 'New password',
                  prefixIcon: Icons.lock_reset_outlined,
                  obscureText: _obscureNew,
                  enabled: !_savingPassword,
                  autovalidateMode: AutovalidateMode.onUserInteraction,
                  onChanged: (_) => _clearServerError(_newPasswordField),
                  validator: (v) =>
                      Validators.password(v) ??
                      _serverErrors[_newPasswordField],
                  suffixIcon: _obscureToggle(
                    _obscureNew,
                    () => setState(() => _obscureNew = !_obscureNew),
                  ),
                ),
                const SizedBox(height: AppSpacing.md),
                AppTextField(
                  controller: _confirmPasswordController,
                  label: 'Confirm new password',
                  prefixIcon: Icons.lock_outline,
                  obscureText: _obscureConfirm,
                  enabled: !_savingPassword,
                  autovalidateMode: AutovalidateMode.onUserInteraction,
                  validator: (v) => Validators.confirmPassword(
                      v, _newPasswordController.text),
                  suffixIcon: _obscureToggle(
                    _obscureConfirm,
                    () => setState(() => _obscureConfirm = !_obscureConfirm),
                  ),
                ),
                const SizedBox(height: AppSpacing.lg),
                SizedBox(
                  height: AppSpacing.inputHeight,
                  child: ElevatedButton(
                    onPressed: _savingPassword ? null : _changePassword,
                    child: _savingPassword
                        ? const _ButtonSpinner()
                        : const Text('Update password'),
                  ),
                ),
              ],
            ),
          ),
        ],
      ],
    );
  }

  Widget _obscureToggle(bool obscured, VoidCallback onToggle) => IconButton(
        icon: Icon(obscured
            ? Icons.visibility_outlined
            : Icons.visibility_off_outlined),
        tooltip: obscured ? 'Show' : 'Hide',
        onPressed: onToggle,
      );
}

/// A small red information banner for a form-level (non-field) error.
class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    // Reuse the shared danger tone (precomputed ARGB consts — no version-specific
    // withOpacity/withValues call), matching StatusBadge's danger pill.
    final tone = StatusToneColors.of(StatusTone.danger);
    return Container(
      padding: const EdgeInsets.all(AppSpacing.sm),
      decoration: BoxDecoration(
        color: tone.background,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: tone.foreground),
      ),
      child: Row(
        children: [
          Icon(Icons.error_outline, color: tone.foreground, size: 18),
          const SizedBox(width: AppSpacing.xs),
          Expanded(
            child: Text(message, style: TextStyle(color: tone.foreground)),
          ),
        ],
      ),
    );
  }
}

class _ButtonSpinner extends StatelessWidget {
  const _ButtonSpinner();

  @override
  Widget build(BuildContext context) => const SizedBox(
        width: 20,
        height: 20,
        child: CircularProgressIndicator(
          strokeWidth: 2.5,
          color: AppColors.onPrimary,
        ),
      );
}
