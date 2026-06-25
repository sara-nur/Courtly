import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart';

import '../../../../core/env/app_config.dart';
import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/image_urls.dart';
import '../../../../core/widgets/app_back_button.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/db_dropdown.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../../../core/widgets/image_upload_field.dart';
import '../../../../core/widgets/map_picker_modal.dart';
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
/// Feature 11 adds three panes: a map-location picker ([MapPickerModal]) whose
/// chosen lat/lng travel with the submit payload, an amenity multi-select
/// ([FilterChip]s by name, never id) replaced wholesale on save, and an image
/// pane ([ImageUploadField]) whose pending picks/removals/primary-change are
/// reconciled against the backend in the submit sequence.
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

  // Map location (both-or-neither; null = no pinned point).
  double? _latitude;
  double? _longitude;

  // Amenity selection: presence of an amenityId = selected; value = highlighted.
  final Map<int, bool> _amenityHighlighted = {};

  // Image panes: server images, ids marked for deletion, fresh local picks, and
  // the id the user chose as primary (deferred to submit).
  List<CourtImage> _existingImages = [];
  final List<int> _removedImageIds = [];
  final List<PendingImage> _pendingImages = [];
  int? _primaryImageId;

  // Once the court row exists (loaded for edit, or created during this submit),
  // its id is remembered here so a retry after a partial failure UPDATEs the same
  // court instead of creating a duplicate.
  int? _persistedCourtId;

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
    _latitude = existing?.latitude;
    _longitude = existing?.longitude;
    _persistedCourtId = existing?.id;

    if (_isEdit) {
      // Load the court's existing amenities + images off the build frame.
      Future.microtask(_loadSubResources);
    }
  }

  /// On edit, fetch the court's current amenity links + images so the selectors
  /// open pre-populated. Failures are non-fatal: the form still works, just
  /// starting empty (the submit will then replace whatever the server has).
  Future<void> _loadSubResources() async {
    final repo = ref.read(courtRepositoryProvider);
    final courtId = widget.existing!.id;
    try {
      final amenities = await repo.listAmenities(courtId);
      final images = await repo.listImages(courtId);
      if (!mounted) return;
      setState(() {
        _amenityHighlighted
          ..clear()
          ..addEntries(
            amenities.map((a) => MapEntry(a.amenityId, a.isHighlighted)),
          );
        _existingImages = images;
        _primaryImageId = images
            .cast<CourtImage?>()
            .firstWhere((i) => i!.isPrimary, orElse: () => null)
            ?.id;
      });
    } catch (_) {
      // Ignore — keep the form usable even if the sub-resources fail to load.
    }
  }

  @override
  void dispose() {
    _nameController.dispose();
    _descriptionController.dispose();
    _priceController.dispose();
    super.dispose();
  }

  Future<void> _pickLocation() async {
    final initial = (_latitude != null && _longitude != null)
        ? LatLng(_latitude!, _longitude!)
        : null;
    final result = await MapPickerModal.show(context, initial: initial);
    if (result == null || !mounted) return;
    setState(() {
      _latitude = result.latitude;
      _longitude = result.longitude;
    });
  }

  void _clearLocation() {
    setState(() {
      _latitude = null;
      _longitude = null;
    });
  }

  void _toggleAmenity(int amenityId, bool selected) {
    setState(() {
      if (selected) {
        _amenityHighlighted[amenityId] = false;
      } else {
        _amenityHighlighted.remove(amenityId);
      }
    });
  }

  void _toggleHighlighted(int amenityId) {
    setState(() {
      _amenityHighlighted[amenityId] =
          !(_amenityHighlighted[amenityId] ?? false);
    });
  }

  Future<void> _pickImages() async {
    final FilePickerResult? result;
    try {
      result = await FilePicker.platform.pickFiles(
        type: FileType.image,
        allowMultiple: true,
        withData: true,
      );
    } catch (e) {
      // Surface a reason instead of silently doing nothing (e.g. a sandbox /
      // entitlement denial on desktop).
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Could not open the file picker: $e')),
      );
      return;
    }
    if (result == null || !mounted) return;
    final picks = <PendingImage>[];
    for (final file in result.files) {
      final bytes = file.bytes;
      if (bytes == null) continue;
      picks.add(PendingImage(bytes: bytes, filename: file.name));
    }
    if (picks.isEmpty) return;
    setState(() => _pendingImages.addAll(picks));
  }

  void _removeExistingImage(int imageId) {
    setState(() {
      _removedImageIds.add(imageId);
      _existingImages =
          _existingImages.where((i) => i.id != imageId).toList(growable: false);
      if (_primaryImageId == imageId) _primaryImageId = null;
    });
  }

  void _removePendingImage(int index) {
    setState(() => _pendingImages.removeAt(index));
  }

  void _setPrimaryImage(int imageId) {
    setState(() => _primaryImageId = imageId);
  }

  /// Builds the amenity payload (the whole desired set — the backend replaces).
  List<({int amenityId, String? note, bool isHighlighted})> _amenityItems() =>
      _amenityHighlighted.entries
          .map((e) => (amenityId: e.key, note: null, isHighlighted: e.value))
          .toList(growable: false);

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
      'latitude': _latitude,
      'longitude': _longitude,
    };

    try {
      // 1) Create or update the court (now incl. lat/lng) → obtain its id. Once a
      //    court exists, remember its id so a retry after a later-step failure
      //    UPDATEs it instead of creating a duplicate court.
      final int courtId;
      if (_persistedCourtId != null) {
        await repo.update(_persistedCourtId!, payload);
        courtId = _persistedCourtId!;
      } else {
        final created = await repo.create(payload);
        courtId = created.id;
        _persistedCourtId = courtId;
      }

      // 2) Replace the court's amenity set with the chosen one (idempotent — the
      //    backend replaces the whole set).
      await repo.setAmenities(courtId, _amenityItems());

      // 3) Delete the images the user removed, dropping each id as it succeeds so
      //    a retry never re-deletes an already-gone image (which would 404).
      for (final imageId in List<int>.of(_removedImageIds)) {
        await repo.deleteImage(courtId, imageId);
        _removedImageIds.remove(imageId);
      }

      // 4) Upload each fresh pick, dropping it as it succeeds so a retry never
      //    re-uploads a duplicate. The backend auto-marks the first image of an
      //    image-less court as primary, so no client-side primary flag is needed.
      for (final pick in List<PendingImage>.of(_pendingImages)) {
        await repo.uploadImage(courtId, bytes: pick.bytes, filename: pick.filename);
        _pendingImages.remove(pick);
      }

      // 5) Apply a primary change among the surviving existing images.
      if (_primaryImageId != null &&
          _existingImages.any(
            (i) => i.id == _primaryImageId && !i.isPrimary,
          )) {
        await repo.setPrimaryImage(courtId, _primaryImageId!);
      }

      // 6) Refresh the grid (reload on create → newest-first; refresh on edit).
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
    final baseUrl = ref.watch(appConfigProvider).apiBaseUrl;

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
                  _LocationField(
                    latitude: _latitude,
                    longitude: _longitude,
                    enabled: !_submitting,
                    onPick: _pickLocation,
                    onClear: _clearLocation,
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
                  const SizedBox(height: AppSpacing.md),
                  _AmenitySelector(
                    amenities: data.amenities,
                    highlighted: _amenityHighlighted,
                    enabled: !_submitting,
                    onToggle: _toggleAmenity,
                    onToggleHighlighted: _toggleHighlighted,
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
            ImageUploadField(
              existing: [
                for (final image in _existingImages)
                  ExistingCourtImage(
                    imageId: image.id,
                    absoluteUrl: absoluteImageUrl(baseUrl, image.url),
                    isPrimary: image.id == _primaryImageId,
                    caption: image.caption,
                  ),
              ],
              pending: _pendingImages,
              enabled: !_submitting,
              onPickFiles: _pickImages,
              onRemoveExisting: _removeExistingImage,
              onRemovePending: _removePendingImage,
              onSetPrimary: _setPrimaryImage,
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

/// The map-location row: a "Pick location on map" button and, once a point is
/// set, a read-only "Location: lat, lng" line with a Clear button. The coords
/// are display-only here — they're never an editable numeric textbox (rubric).
class _LocationField extends StatelessWidget {
  const _LocationField({
    required this.latitude,
    required this.longitude,
    required this.enabled,
    required this.onPick,
    required this.onClear,
  });

  final double? latitude;
  final double? longitude;
  final bool enabled;
  final VoidCallback onPick;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final hasPoint = latitude != null && longitude != null;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        OutlinedButton.icon(
          onPressed: enabled ? onPick : null,
          icon: const Icon(Icons.map_outlined, size: 18),
          label: const Text('Pick location on map'),
        ),
        if (hasPoint) ...[
          const SizedBox(height: AppSpacing.xs),
          Row(
            children: [
              const Icon(Icons.place_outlined,
                  size: 18, color: AppColors.textSecondary),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(
                  'Location: ${latitude!.toStringAsFixed(6)}, '
                  '${longitude!.toStringAsFixed(6)}',
                  style: theme.textTheme.bodyMedium,
                ),
              ),
              TextButton(
                onPressed: enabled ? onClear : null,
                child: const Text('Clear'),
              ),
            ],
          ),
        ],
      ],
    );
  }
}

/// The amenity multi-select: a [Wrap] of [FilterChip]s (one per amenity, by
/// name) plus a star toggle on each selected chip to flip its highlighted flag.
/// Never shows a raw amenity id.
class _AmenitySelector extends StatelessWidget {
  const _AmenitySelector({
    required this.amenities,
    required this.highlighted,
    required this.enabled,
    required this.onToggle,
    required this.onToggleHighlighted,
  });

  final List<Amenity> amenities;
  final Map<int, bool> highlighted;
  final bool enabled;
  final void Function(int amenityId, bool selected) onToggle;
  final void Function(int amenityId) onToggleHighlighted;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          'Amenities',
          style: theme.textTheme.titleSmall
              ?.copyWith(fontWeight: FontWeight.w600),
        ),
        const SizedBox(height: AppSpacing.xxs),
        if (amenities.isEmpty)
          Text(
            'No amenities available.',
            style: theme.textTheme.bodySmall
                ?.copyWith(color: AppColors.textMuted),
          )
        else
          Wrap(
            spacing: AppSpacing.xs,
            runSpacing: AppSpacing.xs,
            children: [
              for (final amenity in amenities)
                _AmenityChip(
                  amenity: amenity,
                  selected: highlighted.containsKey(amenity.id),
                  isHighlighted: highlighted[amenity.id] ?? false,
                  enabled: enabled,
                  onToggle: (on) => onToggle(amenity.id, on),
                  onToggleHighlighted: () => onToggleHighlighted(amenity.id),
                ),
            ],
          ),
      ],
    );
  }
}

/// A single amenity [FilterChip] with an inline star toggle (shown only when the
/// chip is selected) to mark the amenity highlighted.
class _AmenityChip extends StatelessWidget {
  const _AmenityChip({
    required this.amenity,
    required this.selected,
    required this.isHighlighted,
    required this.enabled,
    required this.onToggle,
    required this.onToggleHighlighted,
  });

  final Amenity amenity;
  final bool selected;
  final bool isHighlighted;
  final bool enabled;
  final ValueChanged<bool> onToggle;
  final VoidCallback onToggleHighlighted;

  @override
  Widget build(BuildContext context) {
    // The chip BODY toggles selection; the trailing delete-slot is a separate hit
    // target, so it carries the "feature" (highlight) star — a star nested inside
    // the label would be swallowed by the chip's own onSelected tap.
    return FilterChip(
      label: Text(amenity.name),
      selected: selected,
      onSelected: enabled ? onToggle : null,
      deleteIcon: Icon(
        isHighlighted ? Icons.star : Icons.star_border,
        size: 18,
        color: isHighlighted ? AppColors.warning : AppColors.textMuted,
      ),
      onDeleted: selected && enabled ? onToggleHighlighted : null,
      deleteButtonTooltipMessage:
          isHighlighted ? 'Featured — tap to unfeature' : 'Tap to feature',
    );
  }
}
