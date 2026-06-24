import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/app_back_button.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../application/reference_providers.dart';
import '../../domain/reference_models.dart';

/// Opens the CourtType create/edit modal (mirrors the Country golden template;
/// Name is required, Description is optional and multi-line).
Future<void> showCourtTypeForm(
  BuildContext context,
  WidgetRef ref, {
  CourtType? existing,
}) {
  return showDialog<void>(
    context: context,
    builder: (_) => _CourtTypeForm(existing: existing),
  );
}

class _CourtTypeForm extends ConsumerStatefulWidget {
  const _CourtTypeForm({this.existing});

  final CourtType? existing;

  @override
  ConsumerState<_CourtTypeForm> createState() => _CourtTypeFormState();
}

class _CourtTypeFormState extends ConsumerState<_CourtTypeForm> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _descriptionController;

  static const String _nameField = 'name';
  static const String _descriptionField = 'description';

  bool _submitting = false;
  final Map<String, String> _serverErrors = {};

  bool get _isEdit => widget.existing != null;

  @override
  void initState() {
    super.initState();
    _nameController = TextEditingController(text: widget.existing?.name ?? '');
    _descriptionController =
        TextEditingController(text: widget.existing?.description ?? '');
  }

  @override
  void dispose() {
    _nameController.dispose();
    _descriptionController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(_serverErrors.clear);
    if (!_formKey.currentState!.validate()) return;

    setState(() => _submitting = true);
    final repo = ref.read(referenceRepositoryProvider);
    final name = _nameController.text.trim();
    final description = _descriptionController.text.trim();
    final payload = <String, dynamic>{
      'name': name,
      'description': description.isEmpty ? null : description,
    };

    try {
      if (_isEdit) {
        await repo.updateCourtType(widget.existing!.id, payload);
      } else {
        await repo.createCourtType(payload);
      }

      final controller = ref.read(courtTypeListControllerProvider.notifier);
      if (_isEdit) {
        await controller.refresh();
      } else {
        await controller.reload();
      }

      if (!mounted) return;
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            _isEdit
                ? 'Court type "$name" updated.'
                : 'Court type "$name" created.',
          ),
        ),
      );
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        if (e.hasFieldErrors) {
          e.fieldErrors!.forEach((field, messages) {
            if (messages.isNotEmpty) _serverErrors[field] = messages.first;
          });
        } else {
          _serverErrors[_nameField] = e.message;
        }
        _submitting = false;
      });
      _formKey.currentState!.validate();
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _serverErrors[_nameField] = 'Something went wrong. Please try again.';
        _submitting = false;
      });
      _formKey.currentState!.validate();
    }
  }

  String? _validateName(String? value) {
    final v = value?.trim() ?? '';
    if (v.isEmpty) return 'Name is required.';
    if (v.length > 50) return 'Name must be 50 characters or fewer.';
    return _serverErrors[_nameField];
  }

  String? _validateDescription(String? value) {
    final v = value?.trim() ?? '';
    if (v.length > 500) {
      return 'Description must be 500 characters or fewer.';
    }
    return _serverErrors[_descriptionField];
  }

  void _clearServerError(String fieldKey) {
    if (_serverErrors.remove(fieldKey) != null) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    return Dialog(
      child: Form(
        key: _formKey,
        child: FormScaffold(
          title: _isEdit ? 'Edit Court Type' : 'New Court Type',
          leading: AppBackButton(
            onPressed: _submitting ? null : () => Navigator.of(context).pop(),
          ),
          onClose: _submitting ? null : () => Navigator.of(context).pop(),
          children: [
            AppTextField(
              controller: _nameController,
              label: 'Name',
              hint: 'e.g. Singles',
              enabled: !_submitting,
              textInputAction: TextInputAction.next,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_nameField),
              validator: _validateName,
            ),
            AppTextField(
              controller: _descriptionController,
              label: 'Description (optional)',
              hint: 'Short description',
              enabled: !_submitting,
              maxLines: 3,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_descriptionField),
              validator: _validateDescription,
            ),
          ],
          actions: [
            TextButton(
              onPressed: _submitting ? null : () => Navigator.of(context).pop(),
              child: const Text('Cancel'),
            ),
            SizedBox(
              height: AppSpacing.inputHeight,
              child: ElevatedButton(
                onPressed: _submitting ? null : _submit,
                child: _submitting
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2.5),
                      )
                    : Text(_isEdit ? 'Save changes' : 'Create'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
