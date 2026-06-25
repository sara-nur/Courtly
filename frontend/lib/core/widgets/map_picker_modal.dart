import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../theme/app_colors.dart';
import '../theme/app_spacing.dart';

/// A dialog that lets an admin pick a court's map location (Feature 11).
///
/// Shows an OpenStreetMap [FlutterMap] with a draggable-by-tap pin, a search box
/// that geocodes free text via Nominatim and recenters on the first hit, and a
/// read-only "lat, lng" line (display only — never an editable numeric box, per
/// rubric). [show] resolves to the chosen [LatLng] on "Use this location" or
/// `null` on Cancel/dismiss.
///
/// The chosen point is internal state initialised from `initial`, so a widget
/// test can pass `initial`, tap "Use this location", and get that coord back
/// without any network or tile load.
class MapPickerModal extends StatefulWidget {
  const MapPickerModal({super.key, this.initial});

  /// The starting pin + map center; null means "no pin yet, default center".
  final LatLng? initial;

  /// Default map center when no point is supplied — Sarajevo.
  static const LatLng defaultCenter = LatLng(43.8563, 18.4131);

  /// Opens the picker and returns the chosen [LatLng], or null when cancelled.
  static Future<LatLng?> show(BuildContext context, {LatLng? initial}) {
    return showDialog<LatLng>(
      context: context,
      builder: (_) => MapPickerModal(initial: initial),
    );
  }

  @override
  State<MapPickerModal> createState() => _MapPickerModalState();
}

class _MapPickerModalState extends State<MapPickerModal> {
  /// Bare Dio (NOT the app client) so our JWT/interceptors are never attached to
  /// a third-party request, and a descriptive User-Agent is sent (Nominatim
  /// usage policy).
  final Dio _geocoder = Dio(
    BaseOptions(headers: {'User-Agent': 'Courtly/1.0 (admin)'}),
  );

  final MapController _mapController = MapController();
  final TextEditingController _searchController = TextEditingController();

  LatLng? _picked;
  bool _searching = false;
  String? _searchError;
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    _picked = widget.initial;
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _searchController.dispose();
    _geocoder.close(force: true);
    super.dispose();
  }

  LatLng get _center => _picked ?? MapPickerModal.defaultCenter;

  void _onSearchChanged(String value) {
    _debounce?.cancel();
    final query = value.trim();
    if (query.isEmpty) return;
    // Debounce so each keystroke does not hit Nominatim (usage policy).
    _debounce = Timer(const Duration(milliseconds: 600), () => _geocode(query));
  }

  Future<void> _geocode(String query) async {
    setState(() {
      _searching = true;
      _searchError = null;
    });
    try {
      final response = await _geocoder.get<dynamic>(
        'https://nominatim.openstreetmap.org/search',
        queryParameters: {'format': 'json', 'q': query, 'limit': 5},
      );
      // The picker may have been dismissed while the request was in flight — bail
      // before any setState / map-controller call to avoid use-after-dispose.
      if (!mounted) return;
      final results = (response.data as List?) ?? const <dynamic>[];
      if (results.isEmpty) {
        setState(() {
          _searching = false;
          _searchError = 'No matches for "$query".';
        });
        return;
      }
      final first = (results.first as Map).cast<String, dynamic>();
      final lat = double.tryParse('${first['lat']}');
      final lon = double.tryParse('${first['lon']}');
      if (lat == null || lon == null) {
        setState(() {
          _searching = false;
          _searchError = 'Could not read that location.';
        });
        return;
      }
      final point = LatLng(lat, lon);
      setState(() {
        _picked = point;
        _searching = false;
      });
      _mapController.move(point, 14);
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _searching = false;
        _searchError = 'Search failed. Check your connection and try again.';
      });
    }
  }

  void _onMapTap(TapPosition _, LatLng point) {
    setState(() => _picked = point);
  }

  String get _coordLabel => _picked == null
      ? 'No location selected — tap the map or search.'
      : '${_picked!.latitude.toStringAsFixed(6)}, '
          '${_picked!.longitude.toStringAsFixed(6)}';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Dialog(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 640),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      'Pick location',
                      style: theme.textTheme.titleLarge,
                    ),
                  ),
                  IconButton(
                    icon: const Icon(Icons.close),
                    onPressed: () => Navigator.of(context).pop(),
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.sm),
              TextField(
                controller: _searchController,
                onChanged: _onSearchChanged,
                onSubmitted: (v) => _geocode(v.trim()),
                decoration: InputDecoration(
                  hintText: 'Search a city, street or place…',
                  prefixIcon: const Icon(Icons.search, size: 18),
                  suffixIcon: _searching
                      ? const Padding(
                          padding: EdgeInsets.all(AppSpacing.sm),
                          child: SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          ),
                        )
                      : null,
                  isDense: true,
                ),
              ),
              if (_searchError != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  _searchError!,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: AppColors.danger),
                ),
              ],
              const SizedBox(height: AppSpacing.sm),
              SizedBox(
                height: 320,
                child: ClipRRect(
                  borderRadius: AppSpacing.brMd,
                  child: FlutterMap(
                    mapController: _mapController,
                    options: MapOptions(
                      initialCenter: _center,
                      initialZoom: _picked == null ? 11 : 14,
                      onTap: _onMapTap,
                    ),
                    children: [
                      TileLayer(
                        urlTemplate:
                            'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                        userAgentPackageName: 'com.courtly.admin',
                      ),
                      if (_picked != null)
                        MarkerLayer(
                          markers: [
                            Marker(
                              point: _picked!,
                              width: 40,
                              height: 40,
                              child: const Icon(
                                Icons.location_on,
                                color: AppColors.primary,
                                size: 40,
                              ),
                            ),
                          ],
                        ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: AppSpacing.sm),
              Row(
                children: [
                  const Icon(
                    Icons.place_outlined,
                    size: 18,
                    color: AppColors.textSecondary,
                  ),
                  const SizedBox(width: AppSpacing.xs),
                  Expanded(
                    child: Text(
                      _coordLabel,
                      style: theme.textTheme.bodyMedium,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.lg),
              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  TextButton(
                    onPressed: () => Navigator.of(context).pop(),
                    child: const Text('Cancel'),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  ElevatedButton(
                    onPressed: _picked == null
                        ? null
                        : () => Navigator.of(context).pop(_picked),
                    child: const Text('Use this location'),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
