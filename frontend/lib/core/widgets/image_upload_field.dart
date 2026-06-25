import 'dart:typed_data';

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_spacing.dart';

/// An already-uploaded court image as the form needs it: the server [imageId],
/// the composed [absoluteUrl] (relative `/api/images/{id}` already prefixed with
/// the configured base URL), the [isPrimary] flag and an optional [caption].
class ExistingCourtImage {
  const ExistingCourtImage({
    required this.imageId,
    required this.absoluteUrl,
    required this.isPrimary,
    this.caption,
  });

  final int imageId;
  final String absoluteUrl;
  final bool isPrimary;
  final String? caption;
}

/// A locally picked, not-yet-uploaded image: its raw [bytes] (rendered with
/// [Image.memory]) and the original [filename] used on upload.
class PendingImage {
  const PendingImage({required this.bytes, required this.filename});

  final Uint8List bytes;
  final String filename;
}

/// A compact, horizontally-scrolling thumbnail strip for a court's images
/// (Feature 11). Purely callback-driven — the owning form holds the lists and
/// performs the actual file picking / API calls, so this widget is unit-testable
/// without the OS file dialog.
///
/// Existing images render via [CachedNetworkImage] (absolute URL) and show a
/// star to set primary (filled when primary); pending picks render via
/// [Image.memory]. Each thumbnail has a remove (X) button. The strip is bounded
/// to ~110px tall so it never dominates the dialog (rubric — image ≤ 50%).
class ImageUploadField extends StatelessWidget {
  const ImageUploadField({
    super.key,
    required this.existing,
    required this.pending,
    required this.onPickFiles,
    required this.onRemoveExisting,
    required this.onRemovePending,
    required this.onSetPrimary,
    this.enabled = true,
  });

  /// Already-uploaded images for this court.
  final List<ExistingCourtImage> existing;

  /// Locally picked images not yet sent to the server.
  final List<PendingImage> pending;

  /// Opens the file picker (the form owns the actual `file_picker` call).
  final VoidCallback onPickFiles;

  /// Marks an existing image (by server id) for removal.
  final void Function(int imageId) onRemoveExisting;

  /// Drops a pending pick at [index].
  final void Function(int index) onRemovePending;

  /// Promotes an existing image (by server id) to primary.
  final void Function(int imageId) onSetPrimary;

  /// Disables every affordance while the form is submitting.
  final bool enabled;

  static const double _thumbStripHeight = 110;
  static const double _thumbSize = 96;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final hasAny = existing.isNotEmpty || pending.isNotEmpty;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          'Images',
          style: theme.textTheme.titleSmall
              ?.copyWith(fontWeight: FontWeight.w600),
        ),
        const SizedBox(height: AppSpacing.xxs),
        Text(
          'The starred image is the primary one shown on the court card.',
          style: theme.textTheme.bodySmall
              ?.copyWith(color: AppColors.textSecondary),
        ),
        const SizedBox(height: AppSpacing.xs),
        if (hasAny)
          SizedBox(
            height: _thumbStripHeight,
            child: ListView(
              scrollDirection: Axis.horizontal,
              children: [
                for (final image in existing)
                  Padding(
                    padding: const EdgeInsets.only(right: AppSpacing.xs),
                    child: _ExistingThumb(
                      image: image,
                      enabled: enabled,
                      onRemove: () => onRemoveExisting(image.imageId),
                      onSetPrimary: () => onSetPrimary(image.imageId),
                    ),
                  ),
                for (var i = 0; i < pending.length; i++)
                  Padding(
                    padding: const EdgeInsets.only(right: AppSpacing.xs),
                    child: _PendingThumb(
                      image: pending[i],
                      enabled: enabled,
                      onRemove: () => onRemovePending(i),
                    ),
                  ),
              ],
            ),
          )
        else
          Text(
            'No images yet.',
            style: theme.textTheme.bodySmall
                ?.copyWith(color: AppColors.textMuted),
          ),
        const SizedBox(height: AppSpacing.xs),
        OutlinedButton.icon(
          onPressed: enabled ? onPickFiles : null,
          icon: const Icon(Icons.add_photo_alternate_outlined, size: 18),
          label: const Text('Add images'),
        ),
      ],
    );
  }
}

/// One existing-image thumbnail: a cached network image with a star (primary)
/// and a remove (X) overlay.
class _ExistingThumb extends StatelessWidget {
  const _ExistingThumb({
    required this.image,
    required this.enabled,
    required this.onRemove,
    required this.onSetPrimary,
  });

  final ExistingCourtImage image;
  final bool enabled;
  final VoidCallback onRemove;
  final VoidCallback onSetPrimary;

  @override
  Widget build(BuildContext context) {
    return _ThumbFrame(
      isPrimary: image.isPrimary,
      overlay: [
        _ThumbIconButton(
          tooltip: image.isPrimary ? 'Primary image' : 'Set as primary',
          icon: image.isPrimary ? Icons.star : Icons.star_border,
          color: image.isPrimary ? AppColors.warning : AppColors.onPrimary,
          onPressed: enabled ? onSetPrimary : null,
          alignment: Alignment.bottomLeft,
        ),
        _ThumbIconButton(
          tooltip: 'Remove',
          icon: Icons.close,
          color: AppColors.onPrimary,
          onPressed: enabled ? onRemove : null,
          alignment: Alignment.topRight,
        ),
      ],
      child: CachedNetworkImage(
        imageUrl: image.absoluteUrl,
        fit: BoxFit.cover,
        width: ImageUploadField._thumbSize,
        height: ImageUploadField._thumbSize,
        placeholder: (context, _) => const ColoredBox(
          color: AppColors.surfaceMuted,
          child: Center(
            child: SizedBox(
              width: 18,
              height: 18,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
          ),
        ),
        errorWidget: (context, _, __) => const ColoredBox(
          color: AppColors.surfaceMuted,
          child: Center(
            child: Icon(Icons.broken_image_outlined,
                color: AppColors.textMuted, size: 20),
          ),
        ),
      ),
    );
  }
}

/// One pending (local) thumbnail: an in-memory image with a remove (X) overlay.
class _PendingThumb extends StatelessWidget {
  const _PendingThumb({
    required this.image,
    required this.enabled,
    required this.onRemove,
  });

  final PendingImage image;
  final bool enabled;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    return _ThumbFrame(
      isPrimary: false,
      overlay: [
        _ThumbIconButton(
          tooltip: 'Remove',
          icon: Icons.close,
          color: AppColors.onPrimary,
          onPressed: enabled ? onRemove : null,
          alignment: Alignment.topRight,
        ),
      ],
      child: Image.memory(
        image.bytes,
        fit: BoxFit.cover,
        width: ImageUploadField._thumbSize,
        height: ImageUploadField._thumbSize,
        errorBuilder: (context, _, __) => const ColoredBox(
          color: AppColors.surfaceMuted,
          child: Center(
            child: Icon(Icons.broken_image_outlined,
                color: AppColors.textMuted, size: 20),
          ),
        ),
      ),
    );
  }
}

/// Shared rounded frame: clips the [child] image, draws a primary outline, and
/// stacks the [overlay] affordances on top.
class _ThumbFrame extends StatelessWidget {
  const _ThumbFrame({
    required this.child,
    required this.isPrimary,
    required this.overlay,
  });

  final Widget child;
  final bool isPrimary;
  final List<Widget> overlay;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: ImageUploadField._thumbSize,
      height: ImageUploadField._thumbSize,
      child: Stack(
        fit: StackFit.expand,
        children: [
          DecoratedBox(
            decoration: BoxDecoration(
              borderRadius: AppSpacing.brSm,
              border: Border.all(
                color: isPrimary ? AppColors.primary : AppColors.border,
                width: isPrimary ? 2 : 1,
              ),
            ),
            child: ClipRRect(
              borderRadius: AppSpacing.brSm,
              child: child,
            ),
          ),
          ...overlay,
        ],
      ),
    );
  }
}

/// A small circular icon button overlaid on a thumbnail corner.
class _ThumbIconButton extends StatelessWidget {
  const _ThumbIconButton({
    required this.tooltip,
    required this.icon,
    required this.color,
    required this.onPressed,
    required this.alignment,
  });

  final String tooltip;
  final IconData icon;
  final Color color;
  final VoidCallback? onPressed;
  final Alignment alignment;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: alignment,
      child: Padding(
        padding: const EdgeInsets.all(2),
        child: Material(
          color: Colors.black45,
          shape: const CircleBorder(),
          child: InkWell(
            customBorder: const CircleBorder(),
            onTap: onPressed,
            child: Tooltip(
              message: tooltip,
              child: Padding(
                padding: const EdgeInsets.all(2),
                child: Icon(icon, size: 16, color: color),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
