/// Immutable domain model for Feature 21 (news / announcements admin CRUD).
///
/// Mirrors the backend `NewsDto` with **camelCase** JSON keys. The row id is kept
/// for API calls (PUT/DELETE `/api/news/{id}`, and to compose the image URL) but
/// is **never shown in the UI** (rubric §6) — lists render the title, published
/// date and author name. [imageUrl] is the backend's **relative** path
/// (`/api/news/{id}/image`); the app prefixes [AppConfig.apiBaseUrl] at render
/// time via `absoluteImageUrl`, exactly like court images.
///
/// The shared [PagedResult] is reused from the reference_data feature (there is
/// no `core/` copy) to avoid a DRY violation — do not redefine it here.
library;

export '../../reference_data/domain/reference_models.dart' show PagedResult;

/// A published news article / announcement. [publishedAtUtc] is a UTC instant
/// (the JSON value ends in `Z`); render it with `.toLocal()`. [isActive] toggles
/// visibility — the client feed only shows active, already-published items.
class News {
  const News({
    required this.id,
    required this.title,
    required this.text,
    required this.imageUrl,
    required this.publishedAtUtc,
    required this.isActive,
    required this.authorName,
  });

  final int id;
  final String title;
  final String text;
  final String? imageUrl;
  final DateTime publishedAtUtc;
  final bool isActive;
  final String authorName;

  factory News.fromJson(Map<String, dynamic> json) => News(
        id: (json['id'] as num).toInt(),
        title: json['title'] as String,
        text: json['text'] as String,
        imageUrl: json['imageUrl'] as String?,
        publishedAtUtc: DateTime.parse(json['publishedAtUtc'] as String),
        isActive: json['isActive'] as bool? ?? true,
        authorName: json['authorName'] as String? ?? '',
      );
}
