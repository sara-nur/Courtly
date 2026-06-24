import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../application/reference_providers.dart';
import '../domain/reference_models.dart';
import 'forms/amenity_form.dart';
import 'forms/city_form.dart';
import 'forms/country_form.dart';
import 'forms/court_type_form.dart';
import 'forms/surface_type_form.dart';
import 'widgets/reference_list_view.dart';

/// Reference-data admin (Feature 9): one screen, a tab per reference table
/// (Countries, Cities, Surfaces, Court Types, Amenities). Each tab body is a
/// [ReferenceListView] wired to that entity's list controller; add/edit open the
/// entity's form. The Cities tab disables "+ Add" with a reason when no country
/// exists yet (dependent forms are blocked when a prerequisite table is empty).
class SettingsScreen extends ConsumerStatefulWidget {
  const SettingsScreen({super.key});

  @override
  ConsumerState<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends ConsumerState<SettingsScreen>
    with SingleTickerProviderStateMixin {
  late final TabController _tabController;

  static const List<String> _tabs = [
    'Countries',
    'Cities',
    'Surfaces',
    'Court Types',
    'Amenities',
  ];

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: _tabs.length, vsync: this);
  }

  @override
  void dispose() {
    _tabController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.lg,
            AppSpacing.lg,
            AppSpacing.lg,
            0,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Reference Data', style: theme.textTheme.headlineSmall),
              const SizedBox(height: AppSpacing.xxs),
              Text(
                'Manage countries, cities, surfaces, court types and amenities.',
                style: theme.textTheme.bodyMedium,
              ),
            ],
          ),
        ),
        TabBar(
          controller: _tabController,
          isScrollable: true,
          tabAlignment: TabAlignment.start,
          labelColor: AppColors.primary,
          unselectedLabelColor: AppColors.textSecondary,
          indicatorColor: AppColors.primary,
          tabs: [for (final t in _tabs) Tab(text: t)],
        ),
        const Divider(height: 1),
        Expanded(
          child: TabBarView(
            controller: _tabController,
            children: const [
              _CountriesTab(),
              _CitiesTab(),
              _SurfacesTab(),
              _CourtTypesTab(),
              _AmenitiesTab(),
            ],
          ),
        ),
      ],
    );
  }
}

// --- Countries ---------------------------------------------------------------

class _CountriesTab extends ConsumerWidget {
  const _CountriesTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(countryListControllerProvider.notifier);
    final state = ref.watch(countryListControllerProvider);
    return ReferenceListView<Country>(
      state: state,
      titleOf: (c) => c.name,
      subtitleOf: (c) => c.isoCode,
      addLabel: 'Add country',
      searchHint: 'Search countries…',
      onAdd: () => showCountryForm(context, ref),
      onEdit: (c) => showCountryForm(context, ref, existing: c),
      onDelete: (c) =>
          ref.read(referenceRepositoryProvider).deleteCountry(c.id).then((_) {
        controller.refresh();
        ref.invalidate(countryLookupProvider);
      }),
      onSearch: controller.setSearch,
      onNextPage: controller.nextPage,
      onPrevPage: controller.prevPage,
      onRetry: controller.load,
    );
  }
}

// --- Cities ------------------------------------------------------------------

class _CitiesTab extends ConsumerWidget {
  const _CitiesTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(cityListControllerProvider.notifier);
    final state = ref.watch(cityListControllerProvider);

    // Cities require a country — block "+ Add" with a reason while none exist.
    final countries = ref.watch(countryLookupProvider);
    final addDisabledReason = countries.maybeWhen(
      data: (list) => list.isEmpty
          ? 'Add a country first — cities require a country.'
          : null,
      orElse: () => null,
    );

    return ReferenceListView<City>(
      state: state,
      titleOf: (c) => c.name,
      subtitleOf: (c) => c.countryName,
      addLabel: 'Add city',
      searchHint: 'Search cities…',
      addDisabledReason: addDisabledReason,
      onAdd: () => showCityForm(context, ref),
      onEdit: (c) => showCityForm(context, ref, existing: c),
      onDelete: (c) =>
          ref.read(referenceRepositoryProvider).deleteCity(c.id).then(
                (_) => controller.refresh(),
              ),
      onSearch: controller.setSearch,
      onNextPage: controller.nextPage,
      onPrevPage: controller.prevPage,
      onRetry: controller.load,
    );
  }
}

// --- Surfaces ----------------------------------------------------------------

class _SurfacesTab extends ConsumerWidget {
  const _SurfacesTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(surfaceTypeListControllerProvider.notifier);
    final state = ref.watch(surfaceTypeListControllerProvider);
    return ReferenceListView<SurfaceType>(
      state: state,
      titleOf: (s) => s.name,
      subtitleOf: (s) => s.description,
      addLabel: 'Add surface',
      searchHint: 'Search surfaces…',
      onAdd: () => showSurfaceTypeForm(context, ref),
      onEdit: (s) => showSurfaceTypeForm(context, ref, existing: s),
      onDelete: (s) =>
          ref.read(referenceRepositoryProvider).deleteSurfaceType(s.id).then(
                (_) => controller.refresh(),
              ),
      onSearch: controller.setSearch,
      onNextPage: controller.nextPage,
      onPrevPage: controller.prevPage,
      onRetry: controller.load,
    );
  }
}

// --- Court types -------------------------------------------------------------

class _CourtTypesTab extends ConsumerWidget {
  const _CourtTypesTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(courtTypeListControllerProvider.notifier);
    final state = ref.watch(courtTypeListControllerProvider);
    return ReferenceListView<CourtType>(
      state: state,
      titleOf: (t) => t.name,
      subtitleOf: (t) => t.description,
      addLabel: 'Add court type',
      searchHint: 'Search court types…',
      onAdd: () => showCourtTypeForm(context, ref),
      onEdit: (t) => showCourtTypeForm(context, ref, existing: t),
      onDelete: (t) =>
          ref.read(referenceRepositoryProvider).deleteCourtType(t.id).then(
                (_) => controller.refresh(),
              ),
      onSearch: controller.setSearch,
      onNextPage: controller.nextPage,
      onPrevPage: controller.prevPage,
      onRetry: controller.load,
    );
  }
}

// --- Amenities ---------------------------------------------------------------

class _AmenitiesTab extends ConsumerWidget {
  const _AmenitiesTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(amenityListControllerProvider.notifier);
    final state = ref.watch(amenityListControllerProvider);
    return ReferenceListView<Amenity>(
      state: state,
      titleOf: (a) => a.name,
      subtitleOf: (a) => a.iconKey,
      addLabel: 'Add amenity',
      searchHint: 'Search amenities…',
      onAdd: () => showAmenityForm(context, ref),
      onEdit: (a) => showAmenityForm(context, ref, existing: a),
      onDelete: (a) =>
          ref.read(referenceRepositoryProvider).deleteAmenity(a.id).then(
                (_) => controller.refresh(),
              ),
      onSearch: controller.setSearch,
      onNextPage: controller.nextPage,
      onPrevPage: controller.prevPage,
      onRetry: controller.load,
    );
  }
}
