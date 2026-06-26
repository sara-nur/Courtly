import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/app_text_field.dart';

/// Captures the required cancellation reason for a reservation (rubric §7: a
/// cancellation must carry a reason). Returns the trimmed reason on confirm, or
/// null when dismissed. The reason is validated below the field (rubric §4) and
/// posted to the state-machine cancel endpoint by the caller.
Future<String?> showCancelReservationDialog(
  BuildContext context, {
  required String reference,
}) {
  return showDialog<String>(
    context: context,
    builder: (_) => _CancelReservationDialog(reference: reference),
  );
}

class _CancelReservationDialog extends StatefulWidget {
  const _CancelReservationDialog({required this.reference});

  final String reference;

  @override
  State<_CancelReservationDialog> createState() => _CancelReservationDialogState();
}

class _CancelReservationDialogState extends State<_CancelReservationDialog> {
  static const int _maxReason = 500;

  final _formKey = GlobalKey<FormState>();
  final _reason = TextEditingController();

  @override
  void dispose() {
    _reason.dispose();
    super.dispose();
  }

  void _submit() {
    if (_formKey.currentState?.validate() ?? false) {
      Navigator.of(context).pop(_reason.text.trim());
    }
  }

  String? _validate(String? value) {
    final text = (value ?? '').trim();
    if (text.isEmpty) return 'A cancellation reason is required.';
    if (text.length > _maxReason) {
      return 'The reason must be at most $_maxReason characters.';
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Row(
        children: [
          const Icon(Icons.cancel_outlined, color: AppColors.danger),
          const SizedBox(width: AppSpacing.sm),
          Expanded(child: Text('Cancel ${widget.reference}')),
          IconButton(
            icon: const Icon(Icons.close),
            onPressed: () => Navigator.of(context).pop(),
          ),
        ],
      ),
      content: SizedBox(
        width: 420,
        child: Form(
          key: _formKey,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'This cancels the booking and frees its slot. The reason is '
                'recorded in the audit trail and sent to the customer.',
              ),
              const SizedBox(height: AppSpacing.md),
              AppTextField(
                controller: _reason,
                label: 'Reason',
                hint: 'e.g. Customer requested cancellation',
                maxLines: 3,
                autovalidateMode: AutovalidateMode.onUserInteraction,
                validator: _validate,
              ),
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Keep booking'),
        ),
        ElevatedButton(
          onPressed: _submit,
          style: ElevatedButton.styleFrom(
            backgroundColor: AppColors.danger,
            foregroundColor: Colors.white,
          ),
          child: const Text('Cancel booking'),
        ),
      ],
    );
  }
}
