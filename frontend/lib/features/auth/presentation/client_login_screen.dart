import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/client_router.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/app_text_field.dart';
import '../application/auth_controller.dart';
import 'auth_scaffold.dart';

/// Client (mobile) sign-in screen, wired to the live `/api/auth/login`. On
/// success the router redirect sends the user to Home, so this screen never
/// navigates manually. Links out to register and forgot-password. Server
/// validation messages render **below** their field (rubric §4); a credential
/// failure shows a banner.
class ClientLoginScreen extends ConsumerStatefulWidget {
  const ClientLoginScreen({super.key});

  @override
  ConsumerState<ClientLoginScreen> createState() => _ClientLoginScreenState();
}

class _ClientLoginScreenState extends ConsumerState<ClientLoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _identifierController = TextEditingController();
  final _passwordController = TextEditingController();

  static const String _identifierField = 'usernameoremail';
  static const String _passwordField = 'password';

  bool _submitting = false;
  bool _obscurePassword = true;
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
    return AuthScaffold(
      title: 'Welcome to Courtly',
      subtitle: 'Sign in to book a court.',
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
              controller: _identifierController,
              label: 'Username or email',
              prefixIcon: Icons.person_outline,
              enabled: !_submitting,
              textInputAction: TextInputAction.next,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_identifierField),
              validator: (v) =>
                  _validateRequired('Username or email', _identifierField, v),
            ),
            const SizedBox(height: AppSpacing.md),
            AppTextField(
              controller: _passwordController,
              label: 'Password',
              prefixIcon: Icons.lock_outline,
              obscureText: _obscurePassword,
              enabled: !_submitting,
              textInputAction: TextInputAction.done,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_passwordField),
              validator: (v) =>
                  _validateRequired('Password', _passwordField, v),
              suffixIcon: IconButton(
                icon: Icon(_obscurePassword
                    ? Icons.visibility_outlined
                    : Icons.visibility_off_outlined),
                tooltip: _obscurePassword ? 'Show password' : 'Hide password',
                onPressed: () =>
                    setState(() => _obscurePassword = !_obscurePassword),
              ),
            ),
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(
                onPressed: _submitting
                    ? null
                    : () => context.push(ClientRoutes.forgotPassword),
                child: const Text('Forgot password?'),
              ),
            ),
            const SizedBox(height: AppSpacing.xs),
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
            const SizedBox(height: AppSpacing.sm),
            Wrap(
              alignment: WrapAlignment.center,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                const Text("Don't have an account?"),
                TextButton(
                  onPressed: _submitting
                      ? null
                      : () => context.push(ClientRoutes.register),
                  child: const Text('Create account'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
