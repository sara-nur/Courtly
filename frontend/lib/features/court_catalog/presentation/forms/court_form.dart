import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/app_back_button.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/db_dropdown.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../../reference_data/domain/reference_models.dart';
import '../../application/court_providers.dart';
import '../../domain/court_models.dart';

/// Opens the Court create/edit modal.
///
/// The City / SurfaceType / CourtType FKs are chosen via [DbDropdown]s (by
/// **name**, never a textbox or a raw id) loaded together from
/// [courtFormLookupsProvider]. Indoor/Active/Featured are [SwitchListTile]
/// toggles (never text). The hourly price is validated to a number > 0.
///
/// On success the modal pops, a specific snackbar shows, and the list refreshes
/// automatically (reload on create → newest-first; refresh on edit).
Future<void> showCourtForm(
  BuildContext context,
  WidgetRef ref, {
  Court? existing,
}) {
  return showDialog<void>(
    context: context,
    builder: (_) => _CourtForm(existing: existing),
  );
}

class _CourtForm extends ConsumerStatefulWidget {
  const _CourtForm({this.existing});

  final Court? existing;

  @override
  ConsumerState<_CourtForm> createState() => _CourtFormState();
}

class _CourtFormState extends ConsumerState<_CourtForm> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _descriptionController;
  late final TextEditingController _priceController;

  int? _cityId;
  int? _surfaceTypeId;
  int? _courtTypeId;
  late bool _isIndoor;
  late bool _isActive;
  late bool _isFeatured;

  // Lower-case keys to match ApiException.fieldErrors (case-insensitive) and the
  // server-error lookups below — exactly like the City form's `_countryField`.
  static const String _nameField = 'name';
  static const String _descriptionField = 'description';
  static const String _cityField = 'cityid';
  static const String _surfaceTypeField = 'surfacetypeid';
  static const String _courtTypeField = 'courttypeid';
  static const String _priceField = 'hourlyprice';

  bool _submitting = false;
  final Map<String, String> _serverErrors = {};

  bool get _isEdit => widget.existing != null;

  @override
  void initState() {
    super.initState();
    final existing = widget.existing;
    _nameController = TextEditingController(text: existing?.name ?? '');
    _descriptionController =
        TextEditingController(text: existing?.description ?? '');
    _priceController = TextEditingController(
      text: existing == null ? '' : existing.hourlyPrice.toString(),
    );
    _cityId = existing?.cityId;
    _surfaceTypeId = existing?.surfaceTypeId;
    _courtTypeId = existing?.courtTypeId;
    _isIndoor = existing?.isIndoor ?? false;
    _isActive = existing?.isActive ?? true;
    _isFeatured = existing?.isFeatured ?? false;
  }

  @override
  void dispose() {
    _nameController.dispose();
    _descriptionController.dispose();
    _priceController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(_serverErrors.clear);
    if (!_formKey.currentState!.validate()) return;

    setState(() => _submitting = true);
    final repo = ref.read(courtRepositoryProvider);
    final name = _nameController.text.trim();
    final description = _descriptionController.text.trim();
    final payload = <String, dynamic>{
      'name': name,
      'description': description.isEmpty ? null : description,
      'cityId': _cityId,
      'surfaceTypeId': _surfaceTypeId,
      'courtTypeId': _courtTypeId,
      'isIndoor': _isIndoor,
      'isActive': _isActive,
      'isFeatured': _isFeatured,
      'hourlyPrice': double.parse(_priceController.text.trim()),
    };

    try {
      if (_isEdit) {
        await repo.update(widget.existing!.id, payload);
      } else {
        await repo.create(payload);
      }

      final controller = ref.read(courtListControllerProvider.notifier);
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
            _isEdit ? 'Court "$name" updated.' : 'Court "$name" created.',
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
    if (v.length > 200) return 'Name must be 200 characters or fewer.';
    return _serverErrors[_nameField];
  }

  String? _validateDescription(String? value) {
    final v = value?.trim() ?? '';
    if (v.length > 2000) {
      return 'Description must be 2000 characters or fewer.';
    }
    return _serverErrors[_descriptionField];
  }

  String? _validatePrice(String? value) {
    final v = value?.trim() ?? '';
    if (v.isEmpty) return 'Price is required.';
    final parsed = double.tryParse(v);
    if (parsed == null || parsed <= 0) {
      return 'Enter a price greater than 0.';
    }
    return _serverErrors[_priceField];
  }

  // Validate against the stored FK ids (not the resolved dropdown instance): on
  // edit, an option removed from the lookup leaves `value` null while the id is
  // still valid to submit (mirrors the City form).
  String? _validateCity(City? value) {
    if (_cityId == null) return 'City is required.';
    return _serverErrors[_cityField];
  }

  String? _validateSurfaceType(SurfaceType? value) {
    if (_surfaceTypeId == null) return 'Surface type is required.';
    return _serverErrors[_surfaceTypeField];
  }

  String? _validateCourtType(CourtType? value) {
    if (_courtTypeId == null) return 'Court type is required.';
    return _serverErrors[_courtTypeField];
  }

  void _clearServerError(String fieldKey) {
    if (_serverErrors.remove(fieldKey) != null) setState(() {});
  }

  /// Resolves the [items] entry whose id == [id], or null when none matches
  /// (e.g. the option was removed from the lookup since this court was saved).
  T? _selected<T>(List<T> items, int? id, int Function(T) idOf) {
    if (id == null) return null;
    for (final item in items) {
      if (idOf(item) == id) return item;
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final lookups = ref.watch(courtFormLookupsProvider);

    return Dialog(
      child: Form(
        key: _formKey,
        child: FormScaffold(
          title: _isEdit ? 'Edit Court' : 'New Court',
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
              hint: 'e.g. Center Court',
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
            AsyncValueView<CourtFormLookups>(
              value: lookups,
              loading: () => const Padding(
                padding: EdgeInsets.symmetric(vertical: AppSpacing.sm),
                child: LinearProgressIndicator(),
              ),
              onRetry: () => ref.invalidate(courtFormLookupsProvider),
              data: (data) => Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                mainAxisSize: MainAxisSize.min,
                children: [
                  DbDropdown<City>(
                    value: _selected(data.cities, _cityId, (c) => c.id),
                    items: data.cities,
                    itemLabel: (c) => c.name,
                    label: 'City',
                    hint: 'Select a city',
                    enabled: !_submitting,
                    validator: _validateCity,
                    onChanged: (c) => setState(() {
                      _cityId = c?.id;
                      _serverErrors.remove(_cityField);
                    }),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  DbDropdown<SurfaceType>(
                    value: _selected(
                        data.surfaceTypes, _surfaceTypeId, (s) => s.id),
                    items: data.surfaceTypes,
                    itemLabel: (s) => s.name,
                    label: 'Surface type',
                    hint: 'Select a surface',
                    enabled: !_submitting,
                    validator: _validateSurfaceType,
                    onChanged: (s) => setState(() {
                      _surfaceTypeId = s?.id;
                      _serverErrors.remove(_surfaceTypeField);
                    }),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  DbDropdown<CourtType>(
                    value:
                        _selected(data.courtTypes, _courtTypeId, (t) => t.id),
                    items: data.courtTypes,
                    itemLabel: (t) => t.name,
                    label: 'Court type',
                    hint: 'Select a court type',
                    enabled: !_submitting,
                    validator: _validateCourtType,
                    onChanged: (t) => setState(() {
                      _courtTypeId = t?.id;
                      _serverErrors.remove(_courtTypeField);
                    }),
                  ),
                ],
              ),
            ),
            AppTextField(
              controller: _priceController,
              label: 'Hourly price',
              hint: 'e.g. 45.00',
              enabled: !_submitting,
              keyboardType:
                  const TextInputType.numberWithOptions(decimal: true),
              prefixIcon: Icons.attach_money,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_priceField),
              validator: _validatePrice,
            ),
            SwitchListTile.adaptive(
              value: _isIndoor,
              title: const Text('Indoor'),
              contentPadding: EdgeInsets.zero,
              onChanged:
                  _submitting ? null : (v) => setState(() => _isIndoor = v),
            ),
            SwitchListTile.adaptive(
              value: _isActive,
              title: const Text('Active'),
              contentPadding: EdgeInsets.zero,
              onChanged:
                  _submitting ? null : (v) => setState(() => _isActive = v),
            ),
            SwitchListTile.adaptive(
              value: _isFeatured,
              title: const Text('Featured'),
              contentPadding: EdgeInsets.zero,
              onChanged:
                  _submitting ? null : (v) => setState(() => _isFeatured = v),
            ),
          ],
        ),
      ),
    );
  }
}
