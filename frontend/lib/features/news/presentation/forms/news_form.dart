import 'package:cached_network_image/cached_network_image.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/env/app_config.dart';
import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/image_urls.dart';
import '../../../../core/widgets/app_back_button.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/date_time_picker_field.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../../../core/widgets/image_upload_field.dart';
import '../../application/news_providers.dart';
import '../../domain/news_models.dart';

/// Opens the News create/edit modal.
///
/// Title + content are [AppTextField]s (errors below the field); the publish
/// date/time is a [DateTimePickerField] (never typed); the image is picked with
/// [ImageUploadField] (a single image — **required on create**, optional-replace
/// on edit); "Active" is a [SwitchListTile] toggle (never text). On success the
/// modal pops, a snackbar shows, and the list refreshes (reload on create →
/// newest-first; refresh on edit). Server-side validation messages are mapped
/// back to their fields.
Future<void> showNewsForm(
  BuildContext context,
  WidgetRef ref, {
  News? existing,
}) {
  return showDialog<void>(
    context: context,
    builder: (_) => _NewsForm(existing: existing),
  );
}

class _NewsForm extends ConsumerStatefulWidget {
  const _NewsForm({this.existing});

  final News? existing;

  @override
  ConsumerState<_NewsForm> createState() => _NewsFormState();
}

class _NewsFormState extends ConsumerState<_NewsForm> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _titleController;
  late final TextEditingController _textController;

  DateTime? _publishedAt;
  late bool _isActive;

  // Single image: a fresh local pick replaces (on submit) whatever the server
  // has. `_existingImageRemoved` lets the user drop the current image on edit —
  // they must then pick a new one (the image is required).
  PendingImage? _pendingImage;
  bool _existingImageRemoved = false;
  String? _imageError;

  bool _submitting = false;

  // Lower-cased keys to match ApiException.fieldErrors (case-insensitive) and the
  // server property names (Title/Text/PublishedAtUtc) + the image field ("file").
  static const String _titleField = 'title';
  static const String _textField = 'text';
  static const String _publishedField = 'publishedatutc';
  static const String _fileField = 'file';

  final Map<String, String> _serverErrors = {};

  bool get _isEdit => widget.existing != null;

  bool get _hasExistingImage =>
      _isEdit && !_existingImageRemoved && widget.existing!.imageUrl != null;

  bool get _hasImage => _pendingImage != null || _hasExistingImage;

  @override
  void initState() {
    super.initState();
    final existing = widget.existing;
    _titleController = TextEditingController(text: existing?.title ?? '');
    _textController = TextEditingController(text: existing?.text ?? '');
    // Show the publish time in local time; the API layer converts back to UTC.
    _publishedAt = existing?.publishedAtUtc.toLocal() ?? DateTime.now();
    _isActive = existing?.isActive ?? true;
  }

  @override
  void dispose() {
    _titleController.dispose();
    _textController.dispose();
    super.dispose();
  }

  Future<void> _pickImage() async {
    final FilePickerResult? result;
    try {
      result = await FilePicker.platform.pickFiles(
        type: FileType.image,
        withData: true,
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Could not open the file picker: $e')),
      );
      return;
    }
    if (result == null || !mounted) return;
    final file = result.files.isNotEmpty ? result.files.first : null;
    final bytes = file?.bytes;
    if (file == null || bytes == null) return;
    setState(() {
      _pendingImage = PendingImage(bytes: bytes, filename: file.name);
      _imageError = null;
    });
  }

  Future<void> _submit() async {
    setState(() {
      _serverErrors.clear();
      _imageError = null;
    });
    final formValid = _formKey.currentState!.validate();
    if (!_hasImage) {
      setState(() => _imageError = 'An image is required.');
    }
    if (!formValid || !_hasImage) return;

    setState(() => _submitting = true);
    final repo = ref.read(newsRepositoryProvider);
    final baseUrl = ref.read(appConfigProvider).apiBaseUrl;
    final title = _titleController.text.trim();
    final text = _textController.text.trim();
    final publishedAt = _publishedAt!;

    try {
      if (_isEdit) {
        await repo.update(
          widget.existing!.id,
          title: title,
          text: text,
          publishedAtUtc: publishedAt,
          isActive: _isActive,
          imageBytes: _pendingImage?.bytes,
          filename: _pendingImage?.filename,
        );
        // The image URL is stable per id, so a replaced image would otherwise
        // show its stale cached copy — evict it.
        if (_pendingImage != null && widget.existing!.imageUrl != null) {
          await CachedNetworkImage.evictFromCache(
            absoluteImageUrl(baseUrl, widget.existing!.imageUrl!),
          );
        }
      } else {
        await repo.create(
          title: title,
          text: text,
          publishedAtUtc: publishedAt,
          isActive: _isActive,
          imageBytes: _pendingImage!.bytes,
          filename: _pendingImage!.filename,
        );
      }

      final controller = ref.read(newsListControllerProvider.notifier);
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
            _isEdit ? 'Article "$title" updated.' : 'Article "$title" published.',
          ),
        ),
      );
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        if (e.hasFieldErrors) {
          e.fieldErrors!.forEach((field, messages) {
            if (messages.isEmpty) return;
            if (field == _fileField) {
              _imageError = messages.first;
            } else {
              _serverErrors[field] = messages.first;
            }
          });
        } else {
          _serverErrors[_titleField] = e.message;
        }
        _submitting = false;
      });
      _formKey.currentState!.validate();
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _serverErrors[_titleField] = 'Something went wrong. Please try again.';
        _submitting = false;
      });
      _formKey.currentState!.validate();
    }
  }

  String? _validateTitle(String? value) {
    final v = value?.trim() ?? '';
    if (v.isEmpty) return 'Title is required.';
    if (v.length > 200) return 'Title must be 200 characters or fewer.';
    return _serverErrors[_titleField];
  }

  String? _validateText(String? value) {
    final v = value?.trim() ?? '';
    if (v.isEmpty) return 'Content is required.';
    if (v.length > 4000) return 'Content must be 4000 characters or fewer.';
    return _serverErrors[_textField];
  }

  String? _validatePublished(DateTime? value) {
    if (value == null) return 'Published date is required.';
    return _serverErrors[_publishedField];
  }

  void _clearServerError(String fieldKey) {
    if (_serverErrors.remove(fieldKey) != null) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final baseUrl = ref.watch(appConfigProvider).apiBaseUrl;

    return Dialog(
      child: Form(
        key: _formKey,
        child: FormScaffold(
          title: _isEdit ? 'Edit article' : 'New article',
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
                    : Text(_isEdit ? 'Save changes' : 'Publish'),
              ),
            ),
          ],
          children: [
            AppTextField(
              controller: _titleController,
              label: 'Title',
              hint: 'e.g. Spring tournament sign-ups are open',
              enabled: !_submitting,
              textInputAction: TextInputAction.next,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_titleField),
              validator: _validateTitle,
            ),
            AppTextField(
              controller: _textController,
              label: 'Content',
              hint: 'Write the announcement…',
              enabled: !_submitting,
              maxLines: 6,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) => _clearServerError(_textField),
              validator: _validateText,
            ),
            DateTimePickerField(
              label: 'Published',
              value: _publishedAt,
              enabled: !_submitting,
              includeTime: true,
              firstDate: DateTime(2000),
              onChanged: (date) => setState(() {
                _publishedAt = date;
                _serverErrors.remove(_publishedField);
              }),
              validator: _validatePublished,
            ),
            ImageUploadField(
              existing: _hasExistingImage
                  ? [
                      ExistingCourtImage(
                        imageId: widget.existing!.id,
                        absoluteUrl: absoluteImageUrl(
                          baseUrl,
                          widget.existing!.imageUrl!,
                        ),
                        isPrimary: true,
                      ),
                    ]
                  : const [],
              pending: _pendingImage != null ? [_pendingImage!] : const [],
              enabled: !_submitting,
              onPickFiles: _pickImage,
              onRemoveExisting: (_) =>
                  setState(() => _existingImageRemoved = true),
              onRemovePending: (_) => setState(() => _pendingImage = null),
              onSetPrimary: (_) {},
            ),
            if (_imageError != null)
              Padding(
                padding: const EdgeInsets.only(top: AppSpacing.xs),
                child: Text(
                  _imageError!,
                  style: const TextStyle(color: AppColors.danger, fontSize: 12),
                ),
              ),
            SwitchListTile.adaptive(
              value: _isActive,
              title: const Text('Active'),
              subtitle: const Text('Visible in the app feed once published.'),
              contentPadding: EdgeInsets.zero,
              onChanged: _submitting ? null : (v) => setState(() => _isActive = v),
            ),
          ],
        ),
      ),
    );
  }
}
