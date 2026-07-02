import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/app_back_button.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../application/reference_providers.dart';
import '../../domain/reference_models.dart';

/// Opens the Country create/edit modal.
///
/// GOLDEN TEMPLATE for the other four entity forms — mirror this exactly:
///  - a [FormScaffold] (top-right close + leading [AppBackButton]) inside a
///    [showDialog];
///  - client validation with errors rendered **below** the field;
///  - server field errors merged in via [ApiException.errorFor] using the same
///    `_serverErrors` pattern as the login screen;
///  - a `_submitting` guard;
///  - on success: pop, refresh/reload the entity's list controller (newest row
///    on top after a create — no manual refresh), and show a **specific**
///    SnackBar ("Country 'France' created." / "updated.").
///
/// Pass [existing] to edit; omit it to create.
Future<void> showCountryForm(
  BuildContext context,
  WidgetRef ref, {
  Country? existing,
}) {
  return showDialog<void>(
    context: context,
    builder: (_) => _CountryForm(existing: existing),
  );
}

class _CountryForm extends ConsumerStatefulWidget {
  const _CountryForm({this.existing});

  final Country? existing;

  @override
  ConsumerState<_CountryForm> createState() => _CountryFormState();
}

class _CountryFormState extends ConsumerState<_CountryForm> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _isoController;

  // Backend field keys (lower-cased to match ApiException's casing).
  static const String _nameField = 'name';
  static const String _isoField = 'isocode';

  bool _submitting = false;
  final Map<String, String> _serverErrors = {};

  bool get _isEdit => widget.existing != null;

  @override
  void initState() {
    super.initState();
    _nameController = TextEditingController(text: widget.existing?.name ?? '');
    _isoController =
        TextEditingController(text: widget.existing?.isoCode ?? '');
  }

  @override
  void dispose() {
    _nameController.dispose();
    _isoController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(_serverErrors.clear);
    if (!_formKey.currentState!.validate()) return;

    setState(() => _submitting = true);
    final repo = ref.read(referenceRepositoryProvider);
    final name = _nameController.text.trim();
    final payload = <String, dynamic>{
      'name': name,
      'isoCode': _isoController.text.trim().toUpperCase(),
    };

    try {
      if (_isEdit) {
        await repo.updateCountry(widget.existing!.id, payload);
      } else {
        await repo.createCountry(payload);
      }

      // Create → reload from page 1 so the newest row (server OrderByDescending
      // (Id)) shows on top; edit → refresh the current page in place.
      final controller = ref.read(countryListControllerProvider.notifier);
      if (_isEdit) {
        await controller.refresh();
      } else {
        await controller.reload();
      }
      // The country lookup feeds the City form dropdown — keep it fresh.
      ref.invalidate(countryLookupProvider);

      if (!mounted) return;
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            _isEdit
                ? 'Country "$name" updated.'
                : 'Country "$name" created.',
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
          // No field-specific message (e.g. a duplicate-name rule) — attach it
          // to the name field so it still renders below a field, not in a box.
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
    if (value == null || value.trim().isEmpty) return 'Name is required.';
    return _serverErrors[_nameField];
  }

  String? _validateIso(String? value) {
    final v = value?.trim() ?? '';
    if (v.isEmpty) return 'ISO code is required.';
    if (!RegExp(r'^[A-Za-z]{3}$').hasMatch(v)) {
      return 'ISO code must be exactly 3 letters, e.g. BIH';
    }
    return _serverErrors[_isoField];
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
          title: _isEdit ? 'Edit Country' : 'New Country',
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
              hint: 'e.g. France',
              enabled: !_submitting,
              textInputAction: TextInputAction.next,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_nameField),
              validator: _validateName,
            ),
            AppTextField(
              controller: _isoController,
              label: 'ISO code',
              hint: 'e.g. FRA',
              enabled: !_submitting,
              textInputAction: TextInputAction.done,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_isoField),
              validator: _validateIso,
            ),
          ],
        ),
      ),
    );
  }
}
