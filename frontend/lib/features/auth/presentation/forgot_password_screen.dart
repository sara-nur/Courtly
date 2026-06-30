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

/// Requests a password-reset link by email. The server never reveals whether the
/// address exists (anti-enumeration), so on success we always show the same
/// neutral confirmation. The reset link arrives by email and opens the app's
/// reset screen via a deep link — there is no token to copy here.
class ForgotPasswordScreen extends ConsumerStatefulWidget {
  const ForgotPasswordScreen({super.key});

  @override
  ConsumerState<ForgotPasswordScreen> createState() =>
      _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends ConsumerState<ForgotPasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _emailController = TextEditingController();

  bool _submitting = false;
  bool _sent = false;
  String? _formError;

  @override
  void dispose() {
    _emailController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() => _formError = null);
    if (!_formKey.currentState!.validate()) return;

    setState(() => _submitting = true);
    try {
      await ref
          .read(authControllerProvider.notifier)
          .forgotPassword(_emailController.text.trim());
      if (!mounted) return;
      setState(() => _sent = true);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _formError = e.message);
    } catch (_) {
      if (!mounted) return;
      setState(() => _formError = 'Something went wrong. Please try again.');
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AuthScaffold(
      title: 'Reset your password',
      subtitle: _sent
          ? 'Check your inbox for the reset link.'
          : 'Enter your email and we\'ll send you a reset link.',
      child: _sent ? _buildSent(context) : _buildForm(),
    );
  }

  Widget _buildSent(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const AuthBanner(
          message: 'If an account exists for that email, a reset link is on '
              'its way. Open it on this device to set a new password.',
          tone: StatusTone.success,
          icon: Icons.mark_email_read_outlined,
        ),
        const SizedBox(height: AppSpacing.lg),
        SizedBox(
          height: AppSpacing.inputHeight,
          child: ElevatedButton(
            onPressed: () => context.pop(),
            child: const Text('Back to sign in'),
          ),
        ),
      ],
    );
  }

  Widget _buildForm() {
    return Form(
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
            controller: _emailController,
            label: 'Email',
            prefixIcon: Icons.email_outlined,
            keyboardType: TextInputType.emailAddress,
            enabled: !_submitting,
            textInputAction: TextInputAction.done,
            autovalidateMode: AutovalidateMode.onUserInteraction,
            validator: Validators.email,
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
                  : const Text('Send reset link'),
            ),
          ),
          const SizedBox(height: AppSpacing.sm),
          TextButton(
            onPressed: _submitting ? null : () => context.pop(),
            child: const Text('Back to sign in'),
          ),
        ],
      ),
    );
  }
}
