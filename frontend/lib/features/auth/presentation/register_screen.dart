import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/validators.dart';
import '../../../core/widgets/app_text_field.dart';
import '../application/auth_controller.dart';
import 'auth_scaffold.dart';

/// Client self-service registration, wired to `/api/auth/register`. Collects only
/// name + email + password (city is set later in Profile) and has **no role
/// field** — the server always grants `User`. On success it auto-logs-in and the
/// router redirect sends the user to Home. Validation messages render below each
/// field (rubric §4); the password rule matches the server policy.
class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key});

  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> {
  final _formKey = GlobalKey<FormState>();
  final _firstNameController = TextEditingController();
  final _lastNameController = TextEditingController();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  final _confirmController = TextEditingController();

  // Backend field keys (lower-cased to match ApiException's casing).
  static const String _firstNameField = 'firstname';
  static const String _lastNameField = 'lastname';
  static const String _emailField = 'email';
  static const String _passwordField = 'password';

  bool _submitting = false;
  bool _obscurePassword = true;
  bool _obscureConfirm = true;
  String? _formError;
  final Map<String, String> _serverErrors = {};

  @override
  void dispose() {
    _firstNameController.dispose();
    _lastNameController.dispose();
    _emailController.dispose();
    _passwordController.dispose();
    _confirmController.dispose();
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
      await ref.read(authControllerProvider.notifier).register(
            email: _emailController.text.trim(),
            password: _passwordController.text,
            firstName: _firstNameController.text.trim(),
            lastName: _lastNameController.text.trim(),
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

  void _clearServerError(String fieldKey) {
    if (_serverErrors.remove(fieldKey) != null) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    return AuthScaffold(
      title: 'Create your account',
      subtitle: 'Sign up to start booking courts.',
      child: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (_formError != null) ...[
              AuthBanner(message: _formError!),
              const SizedBox(height: AppSpacing.md),
            ],
            AppTextField(
              controller: _firstNameController,
              label: 'First name',
              prefixIcon: Icons.badge_outlined,
              enabled: !_submitting,
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
              enabled: !_submitting,
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
              enabled: !_submitting,
              textInputAction: TextInputAction.next,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_emailField),
              validator: (v) =>
                  Validators.email(v) ?? _serverErrors[_emailField],
            ),
            const SizedBox(height: AppSpacing.md),
            AppTextField(
              controller: _passwordController,
              label: 'Password',
              prefixIcon: Icons.lock_outline,
              obscureText: _obscurePassword,
              enabled: !_submitting,
              textInputAction: TextInputAction.next,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_passwordField),
              validator: (v) =>
                  Validators.password(v) ?? _serverErrors[_passwordField],
              suffixIcon: IconButton(
                icon: Icon(_obscurePassword
                    ? Icons.visibility_outlined
                    : Icons.visibility_off_outlined),
                tooltip: _obscurePassword ? 'Show password' : 'Hide password',
                onPressed: () =>
                    setState(() => _obscurePassword = !_obscurePassword),
              ),
            ),
            const SizedBox(height: AppSpacing.md),
            AppTextField(
              controller: _confirmController,
              label: 'Confirm password',
              prefixIcon: Icons.lock_outline,
              obscureText: _obscureConfirm,
              enabled: !_submitting,
              textInputAction: TextInputAction.done,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              validator: (v) =>
                  Validators.confirmPassword(v, _passwordController.text),
              suffixIcon: IconButton(
                icon: Icon(_obscureConfirm
                    ? Icons.visibility_outlined
                    : Icons.visibility_off_outlined),
                tooltip: _obscureConfirm ? 'Show password' : 'Hide password',
                onPressed: () =>
                    setState(() => _obscureConfirm = !_obscureConfirm),
              ),
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
                    : const Text('Create account'),
              ),
            ),
            const SizedBox(height: AppSpacing.sm),
            Wrap(
              alignment: WrapAlignment.center,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                const Text('Already have an account?'),
                TextButton(
                  onPressed: _submitting ? null : () => context.pop(),
                  child: const Text('Sign in'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
