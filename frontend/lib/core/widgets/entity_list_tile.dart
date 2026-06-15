import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_spacing.dart';

/// A list row that displays an entity's image beside its name, with an
/// optional subtitle and trailing widget. Used wherever entities are
/// presented in a list (rubric §6).
class EntityListTile extends StatelessWidget {
  /// Creates an [EntityListTile].
  const EntityListTile({
    super.key,
    required this.title,
    this.subtitle,
    this.imageUrl,
    this.trailing,
    this.onTap,
    this.leadingFallbackIcon = Icons.image_outlined,
  });

  /// Primary label shown for the entity.
  final String title;

  /// Optional secondary label rendered below the title.
  final String? subtitle;

  /// Optional image URL for the entity thumbnail.
  final String? imageUrl;

  /// Optional widget rendered at the end of the row.
  final Widget? trailing;

  /// Called when the row is tapped.
  final VoidCallback? onTap;

  /// Icon shown when no image is available or an image fails to load.
  final IconData leadingFallbackIcon;

  Widget _buildFallback() {
    return Container(
      width: AppSpacing.entityThumb,
      height: AppSpacing.entityThumb,
      color: AppColors.surfaceMuted,
      child: Center(
        child: Icon(leadingFallbackIcon, color: AppColors.textMuted),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final url = imageUrl;
    final hasImage = url != null && url.isNotEmpty;

    final Widget leadingContent = hasImage
        ? CachedNetworkImage(
            imageUrl: url,
            fit: BoxFit.cover,
            width: AppSpacing.entityThumb,
            height: AppSpacing.entityThumb,
            placeholder: (context, _) => const Center(
              child: SizedBox(
                width: AppSpacing.xl,
                height: AppSpacing.xl,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            ),
            errorWidget: (context, _, __) => _buildFallback(),
          )
        : _buildFallback();

    return ListTile(
      leading: ClipRRect(
        borderRadius: AppSpacing.brSm,
        child: leadingContent,
      ),
      title: Text(
        title,
        style: const TextStyle(fontWeight: FontWeight.w600),
      ),
      subtitle: subtitle != null ? Text(subtitle!) : null,
      trailing: trailing,
      onTap: onTap,
    );
  }
}
