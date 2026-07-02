import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/app_back_button.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../application/reference_providers.dart';
import '../../domain/reference_models.dart';

/// Opens the Amenity create/edit modal (mirrors the Country golden template;
/// Name is required, Icon key is optional — a stable string key, never an id).
Future<void> showAmenityForm(
  BuildContext context,
  WidgetRef ref, {
  Amenity? existing,
}) {
  return showDialog<void>(
    context: context,
    builder: (_) => _AmenityForm(existing: existing),
  );
}

class _AmenityForm extends ConsumerStatefulWidget {
  const _AmenityForm({this.existing});

  final Amenity? existing;

  @override
  ConsumerState<_AmenityForm> createState() => _AmenityFormState();
}

class _AmenityFormState extends ConsumerState<_AmenityForm> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _iconKeyController;

  static const String _nameField = 'name';
  static const String _iconKeyField = 'iconkey';

  bool _submitting = false;
  final Map<String, String> _serverErrors = {};

  bool get _isEdit => widget.existing != null;

  @override
  void initState() {
    super.initState();
    _nameController = TextEditingController(text: widget.existing?.name ?? '');
    _iconKeyController =
        TextEditingController(text: widget.existing?.iconKey ?? '');
  }

  @override
  void dispose() {
    _nameController.dispose();
    _iconKeyController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(_serverErrors.clear);
    if (!_formKey.currentState!.validate()) return;

    setState(() => _submitting = true);
    final repo = ref.read(referenceRepositoryProvider);
    final name = _nameController.text.trim();
    final iconKey = _iconKeyController.text.trim();
    final payload = <String, dynamic>{
      'name': name,
      'iconKey': iconKey.isEmpty ? null : iconKey,
    };

    try {
      if (_isEdit) {
        await repo.updateAmenity(widget.existing!.id, payload);
      } else {
        await repo.createAmenity(payload);
      }

      final controller = ref.read(amenityListControllerProvider.notifier);
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
                ? 'Amenity "$name" updated.'
                : 'Amenity "$name" created.',
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

  String? _validateIconKey(String? value) {
    final v = value?.trim() ?? '';
    if (v.length > 100) return 'Icon key must be 100 characters or fewer.';
    return _serverErrors[_iconKeyField];
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
          title: _isEdit ? 'Edit Amenity' : 'New Amenity',
          leading: AppBackButton(
            onPressed: _submitting ? null : () => Navigator.of(context).pop(),
          ),
          onClose: _submitting ? null : () => Navigator.of(context).pop(),
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
          children: [
            AppTextField(
              controller: _nameController,
              label: 'Name',
              hint: 'e.g. Parking',
              enabled: !_submitting,
              textInputAction: TextInputAction.next,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_nameField),
              validator: _validateName,
            ),
            AppTextField(
              controller: _iconKeyController,
              label: 'Icon key (optional)',
              hint: 'e.g. parking',
              enabled: !_submitting,
              textInputAction: TextInputAction.done,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_iconKeyField),
              validator: _validateIconKey,
            ),
          ],
        ),
      ),
    );
  }
}
