import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/app_back_button.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/db_dropdown.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../application/reference_providers.dart';
import '../../domain/reference_models.dart';

/// Opens the City create/edit modal.
///
/// Mirrors the Country golden template, with one addition: the country is chosen
/// via a [DbDropdown] (FK by **name**, never a textbox or a raw id). The dropdown
/// is fed by [countryLookupProvider]; when the country list is empty the form is
/// blocked (the Settings "+ Add" is already disabled-with-reason, and the
/// dropdown renders "No options available").
Future<void> showCityForm(
  BuildContext context,
  WidgetRef ref, {
  City? existing,
}) {
  return showDialog<void>(
    context: context,
    builder: (_) => _CityForm(existing: existing),
  );
}

class _CityForm extends ConsumerStatefulWidget {
  const _CityForm({this.existing});

  final City? existing;

  @override
  ConsumerState<_CityForm> createState() => _CityFormState();
}

class _CityFormState extends ConsumerState<_CityForm> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  int? _countryId;

  static const String _nameField = 'name';
  static const String _countryField = 'countryid';

  bool _submitting = false;
  final Map<String, String> _serverErrors = {};

  bool get _isEdit => widget.existing != null;

  @override
  void initState() {
    super.initState();
    _nameController = TextEditingController(text: widget.existing?.name ?? '');
    _countryId = widget.existing?.countryId;
  }

  @override
  void dispose() {
    _nameController.dispose();
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
      'countryId': _countryId,
    };

    try {
      if (_isEdit) {
        await repo.updateCity(widget.existing!.id, payload);
      } else {
        await repo.createCity(payload);
      }

      final controller = ref.read(cityListControllerProvider.notifier);
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
            _isEdit ? 'City "$name" updated.' : 'City "$name" created.',
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
    if (value == null || value.trim().isEmpty) return 'Name is required.';
    return _serverErrors[_nameField];
  }

  String? _validateCountry(Country? value) {
    // Validate against the stored FK (_countryId), not the dropdown's resolved instance: on edit, a country
    // that was removed from the lookup leaves `value` null while _countryId still holds a valid FK to submit.
    if (_countryId == null) return 'Country is required.';
    return _serverErrors[_countryField];
  }

  void _clearServerError(String fieldKey) {
    if (_serverErrors.remove(fieldKey) != null) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final countries = ref.watch(countryLookupProvider);

    return Dialog(
      child: Form(
        key: _formKey,
        child: FormScaffold(
          title: _isEdit ? 'Edit City' : 'New City',
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
              hint: 'e.g. Paris',
              enabled: !_submitting,
              textInputAction: TextInputAction.next,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_nameField),
              validator: _validateName,
            ),
            AsyncValueView<List<Country>>(
              value: countries,
              loading: () => const Padding(
                padding: EdgeInsets.symmetric(vertical: AppSpacing.sm),
                child: LinearProgressIndicator(),
              ),
              onRetry: () => ref.invalidate(countryLookupProvider),
              data: (list) {
                final selected = _countryId == null
                    ? null
                    : list
                        .where((c) => c.id == _countryId)
                        .cast<Country?>()
                        .firstWhere((_) => true, orElse: () => null);
                return DbDropdown<Country>(
                  value: selected,
                  items: list,
                  itemLabel: (c) => c.name,
                  label: 'Country',
                  hint: 'Select a country',
                  enabled: !_submitting,
                  validator: _validateCountry,
                  onChanged: (c) => setState(() {
                    _countryId = c?.id;
                    _serverErrors.remove(_countryField);
                  }),
                );
              },
            ),
          ],
        ),
      ),
    );
  }
}
