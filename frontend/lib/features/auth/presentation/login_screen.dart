import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/shell/courtly_logo.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/app_text_field.dart';
import '../application/auth_controller.dart';

/// Admin sign-in screen, wired to the live `/api/auth/login`. On success the
/// router redirect (driven by the auth state) sends the user to the dashboard,
/// so this screen never navigates manually. Server validation messages render
/// **below** their field (rubric §4); a credential/role failure shows a banner.
class AdminLoginScreen extends ConsumerStatefulWidget {
  const AdminLoginScreen({super.key});

  @override
  ConsumerState<AdminLoginScreen> createState() => _AdminLoginScreenState();
}

class _AdminLoginScreenState extends ConsumerState<AdminLoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _identifierController = TextEditingController();
  final _passwordController = TextEditingController();

  // Backend field keys (lower-cased to match ApiException's casing).
  static const String _identifierField = 'usernameoremail';
  static const String _passwordField = 'password';

  bool _submitting = false;
  String? _formError;
  final Map<String, String> _serverErrors = {};

  @override
  void dispose() {
    _identifierController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _formError = null;
      _serverErrors.clear();
    });
    if (!_formKey.currentState!.validate()) return;

    setState(() => _submitting = true);
    try {
      await ref.read(authControllerProvider.notifier).login(
            _identifierController.text.trim(),
            _passwordController.text,
          );
      // Success: the router redirect takes over; this screen unmounts.
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        if (e.hasFieldErrors) {
          e.fieldErrors!.forEach((field, messages) {
            if (messages.isNotEmpty) _serverErrors[field] = messages.first;
          });
        }
        // Show the banner only when there is no field-specific message to point
        // at (e.g. "Invalid credentials." or a blocked role).
        _formError = _serverErrors.isEmpty ? e.message : null;
      });
      _formKey.currentState!.validate();
    } catch (_) {
      if (!mounted) return;
      setState(() => _formError = 'Something went wrong. Please try again.');
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  String? _validateRequired(String label, String fieldKey, String? value) {
    if (value == null || value.trim().isEmpty) return '$label is required.';
    return _serverErrors[fieldKey];
  }

  void _clearServerError(String fieldKey) {
    if (_serverErrors.remove(fieldKey) != null) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 400),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(AppSpacing.xl),
                child: Form(
                  key: _formKey,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const Center(child: CourtlyLogo(showAdminSuffix: true)),
                      const SizedBox(height: AppSpacing.lg),
                      Text(
                        'Welcome back',
                        textAlign: TextAlign.center,
                        style: theme.textTheme.headlineSmall,
                      ),
                      const SizedBox(height: AppSpacing.xxs),
                      Text(
                        'Sign in to manage courts and reservations.',
                        textAlign: TextAlign.center,
                        style: theme.textTheme.bodyMedium,
                      ),
                      const SizedBox(height: AppSpacing.lg),
                      if (_formError != null) ...[
                        _ErrorBanner(message: _formError!),
                        const SizedBox(height: AppSpacing.md),
                      ],
                      AppTextField(
                        controller: _identifierController,
                        label: 'Username or email',
                        prefixIcon: Icons.person_outline,
                        enabled: !_submitting,
                        textInputAction: TextInputAction.next,
                        autovalidateMode: AutovalidateMode.onUserInteraction,
                        onChanged: (_) => _clearServerError(_identifierField),
                        validator: (v) => _validateRequired(
                          'Username or email',
                          _identifierField,
                          v,
                        ),
                      ),
                      const SizedBox(height: AppSpacing.md),
                      AppTextField(
                        controller: _passwordController,
                        label: 'Password',
                        prefixIcon: Icons.lock_outline,
                        obscureText: true,
                        enabled: !_submitting,
                        textInputAction: TextInputAction.done,
                        autovalidateMode: AutovalidateMode.onUserInteraction,
                        onChanged: (_) => _clearServerError(_passwordField),
                        validator: (v) =>
                            _validateRequired('Password', _passwordField, v),
                      ),
                      const SizedBox(height: AppSpacing.lg),
                      SizedBox(
                        height: AppSpacing.inputHeight,
                        child: ElevatedButton(
                          onPressed: _submitting ? null : _submit,
                          child: _submitting
                              ? const SizedBox(
                                  width: 20,
                                  height: 20,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2.5,
                                    color: AppColors.onPrimary,
                                  ),
                                )
                              : const Text('Sign in'),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Inline banner for a non-field auth failure (bad credentials, blocked role,
/// or a connection problem). Shown above the fields, never as a dialog.
class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final tone = StatusToneColors.of(StatusTone.danger);
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.xs,
      ),
      decoration: BoxDecoration(
        color: tone.background,
        borderRadius: AppSpacing.brSm,
      ),
      child: Row(
        children: [
          Icon(Icons.error_outline, size: 18, color: tone.foreground),
          const SizedBox(width: AppSpacing.xs),
          Expanded(
            child: Text(
              message,
              style: TextStyle(color: tone.foreground, fontSize: 13),
            ),
          ),
        ],
      ),
    );
  }
}
