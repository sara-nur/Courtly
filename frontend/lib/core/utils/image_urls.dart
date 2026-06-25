/// Helpers for turning the backend's **relative** image paths into absolute
/// URLs the app can actually fetch.
///
/// Court image DTOs carry a relative `url` like `/api/images/{id}` (the backend
/// never hardcodes a host). The Flutter app composes the absolute URL by
/// prefixing its configured base URL (`AppConfig.apiBaseUrl`) at render time —
/// see `court_card` and `ImageUploadField`.
library;

/// Joins [baseUrl] (no trailing slash, e.g. `http://localhost:5000`) and a
/// [relative] path (e.g. `/api/images/12`) into one absolute URL.
///
/// Returns [relative] unchanged when it is already absolute (`http(s)://…`),
/// and an empty string when [relative] is empty so callers can keep their
/// existing "no image" fallback.
String absoluteImageUrl(String baseUrl, String relative) {
  if (relative.isEmpty) return '';
  if (relative.startsWith('http://') || relative.startsWith('https://')) {
    return relative;
  }
  final base = baseUrl.endsWith('/')
      ? baseUrl.substring(0, baseUrl.length - 1)
      : baseUrl;
  final path = relative.startsWith('/') ? relative : '/$relative';
  return '$base$path';
}
