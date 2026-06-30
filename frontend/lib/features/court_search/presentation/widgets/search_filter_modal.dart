import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/db_dropdown.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../../court_catalog/application/court_providers.dart';
import '../../../reference_data/domain/reference_models.dart';
import '../../application/search_controller.dart';

/// Bottom-sheet filter modal for the client search. Pre-fills its controls from
/// the passed-in [initial] filters, lets the user adjust them, and returns the
/// new [CourtSearchFilters] via [onApply] (the caller pops the sheet). "Clear
/// all" resets everything back to an empty set.
///
/// Surface + court types load from the catalog FK lookups
/// ([surfaceTypeLookupProvider]/[courtTypeLookupProvider]); each [DbDropdown]
/// renders disabled when its list is empty. Validation/helper messages render
/// BELOW their field (rubric §4).
class SearchFilterModal extends ConsumerStatefulWidget {
  const SearchFilterModal({
    super.key,
    required this.initial,
    required this.onApply,
  });

  final CourtSearchFilters initial;
  final ValueChanged<CourtSearchFilters> onApply;

  /// Price slider bounds (hourly price band). The upper bound doubles as the
  /// "no upper limit" sentinel — see [_resolvedMaxPrice].
  static const double _minPriceBound = 0;
  static const double _maxPriceBound = 100;

  @override
  ConsumerState<SearchFilterModal> createState() => _SearchFilterModalState();
}

/// The indoor/outdoor tri-state. `any` clears the filter; `indoor`/`outdoor`
/// map to `indoorOnly = true/false`.
enum _IndoorChoice { any, indoor, outdoor }

/// First element matching [test], or null when none match (avoids depending on
/// the `package:collection` `firstWhereOrNull` extension).
T? _firstWhereOrNull<T>(Iterable<T> items, bool Function(T) test) {
  for (final item in items) {
    if (test(item)) return item;
  }
  return null;
}

class _SearchFilterModalState extends ConsumerState<SearchFilterModal> {
  late int? _surfaceTypeId;
  late int? _courtTypeId;
  late RangeValues _priceRange;
  late _IndoorChoice _indoor;
  late int _minRating; // 0 = Any, 1..5 = floor

  @override
  void initState() {
    super.initState();
    _resetFrom(widget.initial);
  }

  void _resetFrom(CourtSearchFilters filters) {
    _surfaceTypeId = filters.surfaceTypeId;
    _courtTypeId = filters.courtTypeId;
    _priceRange = RangeValues(
      filters.minPrice ?? SearchFilterModal._minPriceBound,
      filters.maxPrice ?? SearchFilterModal._maxPriceBound,
    );
    _indoor = filters.indoorOnly == null
        ? _IndoorChoice.any
        : (filters.indoorOnly! ? _IndoorChoice.indoor : _IndoorChoice.outdoor);
    _minRating = filters.minRating?.round() ?? 0;
  }

  /// A minPrice of exactly the lower bound means "no lower limit" → null.
  double? get _resolvedMinPrice =>
      _priceRange.start <= SearchFilterModal._minPriceBound
          ? null
          : _priceRange.start;

  /// A maxPrice at the upper bound means "no upper limit" → null.
  double? get _resolvedMaxPrice =>
      _priceRange.end >= SearchFilterModal._maxPriceBound
          ? null
          : _priceRange.end;

  bool? get _resolvedIndoorOnly {
    switch (_indoor) {
      case _IndoorChoice.any:
        return null;
      case _IndoorChoice.indoor:
        return true;
      case _IndoorChoice.outdoor:
        return false;
    }
  }

  /// Builds the filter set from the current controls, preserving the inbound
  /// free-text [CourtSearchFilters.query] (the modal does not edit it — the
  /// search bar owns the query).
  CourtSearchFilters _build() => CourtSearchFilters(
        query: widget.initial.query,
        surfaceTypeId: _surfaceTypeId,
        courtTypeId: _courtTypeId,
        minPrice: _resolvedMinPrice,
        maxPrice: _resolvedMaxPrice,
        indoorOnly: _resolvedIndoorOnly,
        minRating: _minRating == 0 ? null : _minRating.toDouble(),
      );

  void _clearAll() {
    setState(() => _resetFrom(const CourtSearchFilters()));
  }

  void _apply() {
    widget.onApply(_build());
  }

  @override
  Widget build(BuildContext context) {
    final surfacesAsync = ref.watch(surfaceTypeLookupProvider);
    final courtTypesAsync = ref.watch(courtTypeLookupProvider);

    final surfaces = surfacesAsync.valueOrNull ?? const <SurfaceType>[];
    final courtTypes = courtTypesAsync.valueOrNull ?? const <CourtType>[];

    // Drop a stale selection that isn't present in the loaded list so the
    // dropdown never holds a value with no matching item.
    final SurfaceType? selectedSurface =
        _firstWhereOrNull(surfaces, (s) => s.id == _surfaceTypeId);
    final CourtType? selectedCourtType =
        _firstWhereOrNull(courtTypes, (c) => c.id == _courtTypeId);

    return SafeArea(
      top: false,
      child: Padding(
        // Lift the sheet above the keyboard when a control opens one.
        padding: EdgeInsets.only(
          bottom: MediaQuery.of(context).viewInsets.bottom,
        ),
        child: FormScaffold(
          title: 'Filters',
          subtitle: 'Narrow down courts to match what you want.',
          onClose: () => Navigator.of(context).pop(),
          contentPadding: const EdgeInsets.all(AppSpacing.lg),
          actions: [
            TextButton(
              onPressed: _clearAll,
              child: const Text('Clear all'),
            ),
            ElevatedButton(
              onPressed: _apply,
              child: const Text('Apply'),
            ),
          ],
          children: [
            DbDropdown<SurfaceType>(
              label: 'Surface',
              hint: 'Any',
              value: selectedSurface,
              items: surfaces,
              itemLabel: (s) => s.name,
              onChanged: (s) => setState(() => _surfaceTypeId = s?.id),
            ),
            DbDropdown<CourtType>(
              label: 'Court type',
              hint: 'Any',
              value: selectedCourtType,
              items: courtTypes,
              itemLabel: (c) => c.name,
              onChanged: (c) => setState(() => _courtTypeId = c?.id),
            ),
            _PriceRangeField(
              range: _priceRange,
              min: SearchFilterModal._minPriceBound,
              max: SearchFilterModal._maxPriceBound,
              onChanged: (r) => setState(() => _priceRange = r),
            ),
            _IndoorField(
              value: _indoor,
              onChanged: (c) => setState(() => _indoor = c),
            ),
            _MinRatingField(
              value: _minRating,
              onChanged: (r) => setState(() => _minRating = r),
            ),
          ],
        ),
      ),
    );
  }
}

/// A small label above a control, matching the form field tone.
class _FieldLabel extends StatelessWidget {
  const _FieldLabel(this.label, {this.trailing});

  final String label;
  final String? trailing;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      children: [
        Text(
          label,
          style: theme.textTheme.labelLarge
              ?.copyWith(color: AppColors.textSecondary),
        ),
        if (trailing != null) ...[
          const Spacer(),
          Text(
            trailing!,
            style: theme.textTheme.labelLarge
                ?.copyWith(color: AppColors.textPrimary),
          ),
        ],
      ],
    );
  }
}

/// Hourly-price band selector. The label shows the live band; a value at a
/// bound reads as "Any" on that end (no limit).
class _PriceRangeField extends StatelessWidget {
  const _PriceRangeField({
    required this.range,
    required this.min,
    required this.max,
    required this.onChanged,
  });

  final RangeValues range;
  final double min;
  final double max;
  final ValueChanged<RangeValues> onChanged;

  String _edge(double value, {required bool isStart}) {
    if (isStart && value <= min) return 'Any';
    if (!isStart && value >= max) return 'Any';
    return '\$${value.round()}';
  }

  @override
  Widget build(BuildContext context) {
    final label =
        '${_edge(range.start, isStart: true)} – ${_edge(range.end, isStart: false)}';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _FieldLabel('Price per hour', trailing: label),
        RangeSlider(
          values: range,
          min: min,
          max: max,
          divisions: (max - min).round(),
          labels: RangeLabels(
            _edge(range.start, isStart: true),
            _edge(range.end, isStart: false),
          ),
          activeColor: AppColors.primary,
          onChanged: onChanged,
        ),
        Text(
          'Drag both ends to set a price band.',
          style: Theme.of(context)
              .textTheme
              .bodySmall
              ?.copyWith(color: AppColors.textMuted),
        ),
      ],
    );
  }
}

/// Indoor / outdoor tri-state segmented control.
class _IndoorField extends StatelessWidget {
  const _IndoorField({required this.value, required this.onChanged});

  final _IndoorChoice value;
  final ValueChanged<_IndoorChoice> onChanged;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _FieldLabel('Indoor / outdoor'),
        const SizedBox(height: AppSpacing.xs),
        SegmentedButton<_IndoorChoice>(
          segments: const [
            ButtonSegment(value: _IndoorChoice.any, label: Text('Any')),
            ButtonSegment(
              value: _IndoorChoice.indoor,
              label: Text('Indoor'),
            ),
            ButtonSegment(
              value: _IndoorChoice.outdoor,
              label: Text('Outdoor'),
            ),
          ],
          selected: {value},
          showSelectedIcon: false,
          onSelectionChanged: (set) => onChanged(set.first),
        ),
      ],
    );
  }
}

/// Minimum-rating floor selector: tapping a star sets the floor; tapping the
/// current floor clears it back to "Any".
class _MinRatingField extends StatelessWidget {
  const _MinRatingField({required this.value, required this.onChanged});

  /// 0 = Any, 1..5 = "this many stars and up".
  final int value;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    final label = value == 0 ? 'Any' : '$value+ stars';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _FieldLabel('Minimum rating', trailing: label),
        const SizedBox(height: AppSpacing.xs),
        Row(
          children: [
            for (var star = 1; star <= 5; star++)
              IconButton(
                onPressed: () => onChanged(value == star ? 0 : star),
                visualDensity: VisualDensity.compact,
                icon: Icon(
                  star <= value ? Icons.star : Icons.star_border,
                  color: star <= value ? AppColors.warning : AppColors.textMuted,
                ),
              ),
          ],
        ),
        Text(
          'Show courts rated this many stars or higher.',
          style: Theme.of(context)
              .textTheme
              .bodySmall
              ?.copyWith(color: AppColors.textMuted),
        ),
      ],
    );
  }
}
