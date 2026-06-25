import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/enums/maintenance_status.dart';
import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/confirm_dialog.dart';
import '../../../../core/widgets/date_time_picker_field.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../../../core/widgets/status_badge.dart';
import '../../application/court_providers.dart';
import '../../domain/court_models.dart';

/// Opens the court status &amp; maintenance modal (Feature 12).
///
/// Shows the court's current availability, a form to open a maintenance window
/// (immediately or scheduled for a future date), and the **status history** — a
/// newest-first list of every window with its status, reason, who opened it and
/// when. Each open window offers the state-machine actions (Start / **Fix** /
/// Cancel) behind a [ConfirmDialog]; terminal windows show none. After any
/// change the history and the court grid refresh automatically.
Future<void> showCourtMaintenance(
  BuildContext context,
  WidgetRef ref,
  Court court,
) {
  return showDialog<void>(
    context: context,
    builder: (_) => _MaintenanceModal(court: court),
  );
}

class _MaintenanceModal extends ConsumerStatefulWidget {
  const _MaintenanceModal({required this.court});

  final Court court;

  @override
  ConsumerState<_MaintenanceModal> createState() => _MaintenanceModalState();
}

class _MaintenanceModalState extends ConsumerState<_MaintenanceModal> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _reasonController;

  // Optional planned window. A null start means "put under maintenance now".
  DateTime? _startUtc;
  DateTime? _endUtc;

  bool _submitting = false;
  static const String _reasonField = 'reason';
  final Map<String, String> _serverErrors = {};

  // Bumped after a successful create so the input fields get fresh keys and reset
  // cleanly — clearing them in place would re-trigger "required" validation on the
  // now-empty reason even though the window WAS created.
  int _formVersion = 0;

  int get _courtId => widget.court.id;

  @override
  void initState() {
    super.initState();
    _reasonController = TextEditingController();
  }

  @override
  void dispose() {
    _reasonController.dispose();
    super.dispose();
  }

  /// Refreshes both the maintenance history and the court grid (so the card's
  /// Available/Maintenance badge updates without a manual refresh).
  void _refreshAll() {
    ref.invalidate(maintenanceHistoryProvider(_courtId));
    ref.read(courtListControllerProvider.notifier).refresh();
  }

  Future<void> _submit() async {
    setState(_serverErrors.clear);
    if (!_formKey.currentState!.validate()) return;

    setState(() => _submitting = true);
    // Capture the scheduling intent BEFORE the success path clears the form — otherwise the message below would
    // always read the just-cleared (null) start.
    final wasScheduled = _startUtc != null;
    try {
      await ref.read(courtRepositoryProvider).createMaintenance(
            _courtId,
            reason: _reasonController.text.trim(),
            startUtc: _startUtc,
            endUtc: _endUtc,
          );
      _refreshAll();
      if (!mounted) return;
      _reasonController.clear();
      setState(() {
        _submitting = false;
        _startUtc = null;
        _endUtc = null;
        _formVersion++; // fresh field keys → cleared inputs, no stale "required" error
      });
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            wasScheduled
                ? 'Maintenance scheduled.'
                : 'Maintenance window added.',
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
          // Overlap / illegal-window business errors surface on the reason field.
          _serverErrors[_reasonField] = e.message;
        }
        _submitting = false;
      });
      _formKey.currentState!.validate();
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _serverErrors[_reasonField] = 'Something went wrong. Please try again.';
        _submitting = false;
      });
      _formKey.currentState!.validate();
    }
  }

  /// Runs a state-machine transition (start / fix / cancel) behind a confirm
  /// dialog, then refreshes. Backend guard messages (e.g. an illegal transition)
  /// surface in a SnackBar.
  Future<void> _runTransition(
    CourtMaintenanceLog log, {
    required String title,
    required String message,
    required String confirmLabel,
    required bool destructive,
    required IconData icon,
    required Future<CourtMaintenanceLog> Function() action,
    required String successMessage,
  }) async {
    final confirmed = await ConfirmDialog.show(
      context,
      title: title,
      message: message,
      confirmLabel: confirmLabel,
      destructive: destructive,
      icon: icon,
    );
    if (!confirmed) return;

    try {
      await action();
      _refreshAll();
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(successMessage)));
    } on ApiException catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(e.message), backgroundColor: AppColors.danger),
      );
    } catch (_) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Something went wrong. Please try again.'),
          backgroundColor: AppColors.danger,
        ),
      );
    }
  }

  String? _validateReason(String? value) {
    final v = value?.trim() ?? '';
    if (v.isEmpty) return 'A maintenance reason is required.';
    if (v.length > 500) return 'The reason must be at most 500 characters.';
    return _serverErrors[_reasonField];
  }

  /// Midnight today — the earliest selectable date (maintenance is scheduled
  /// forward; "now" is the blank-start case, not a past date).
  DateTime get _todayStart {
    final now = DateTime.now();
    return DateTime(now.year, now.month, now.day);
  }

  /// The end must be after the start (or, when the start is blank = now, in the
  /// future). Surfaced below the End field, not after a backend round-trip.
  String? _validateEnd(DateTime? value) {
    if (value == null) return null;
    final start = _startUtc;
    if (start != null) {
      if (!value.isAfter(start)) return 'End must be after the start.';
    } else if (!value.isAfter(DateTime.now())) {
      return 'End must be in the future.';
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final history = ref.watch(maintenanceHistoryProvider(_courtId));

    return Dialog(
      child: Form(
        key: _formKey,
        child: FormScaffold(
          title: 'Maintenance — ${widget.court.name}',
          subtitle: 'Status, scheduling and history',
          onClose: _submitting ? null : () => Navigator.of(context).pop(),
          actions: [
            TextButton(
              onPressed: _submitting ? null : () => Navigator.of(context).pop(),
              child: const Text('Done'),
            ),
          ],
          children: [
            _CurrentStatus(court: widget.court),
            const Divider(),
            Text(
              'Add a maintenance window',
              style: Theme.of(context)
                  .textTheme
                  .titleSmall
                  ?.copyWith(fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: AppSpacing.xs),
            AppTextField(
              key: ValueKey('reason-$_formVersion'),
              controller: _reasonController,
              label: 'Reason',
              hint: 'e.g. Resurfacing the clay',
              enabled: !_submitting,
              maxLines: 2,
              autovalidateMode: AutovalidateMode.onUserInteraction,
              onChanged: (_) {
                if (_serverErrors.remove(_reasonField) != null) setState(() {});
              },
              validator: _validateReason,
            ),
            _WindowField(
              key: ValueKey('start-$_formVersion'),
              label: 'Start — leave blank to start now',
              value: _startUtc,
              enabled: !_submitting,
              firstDate: _todayStart,
              onChanged: (v) => setState(() => _startUtc = v),
              onClear: () => setState(() => _startUtc = null),
            ),
            _WindowField(
              key: ValueKey('end-$_formVersion'),
              label: 'End (optional)',
              value: _endUtc,
              enabled: !_submitting,
              firstDate: _todayStart,
              validator: _validateEnd,
              onChanged: (v) => setState(() => _endUtc = v),
              onClear: () => setState(() => _endUtc = null),
            ),
            Align(
              alignment: Alignment.centerRight,
              child: SizedBox(
                height: AppSpacing.inputHeight,
                child: ElevatedButton.icon(
                  onPressed: _submitting ? null : _submit,
                  icon: _submitting
                      ? const SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(strokeWidth: 2.5),
                        )
                      : const Icon(Icons.build_outlined, size: 18),
                  label: Text(
                      _startUtc == null ? 'Put under maintenance' : 'Schedule'),
                ),
              ),
            ),
            const Divider(),
            Text(
              'Status history',
              style: Theme.of(context)
                  .textTheme
                  .titleSmall
                  ?.copyWith(fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: AppSpacing.xs),
            SizedBox(
              height: 240,
              child: AsyncValueView<List<CourtMaintenanceLog>>(
                value: history,
                onRetry: () =>
                    ref.invalidate(maintenanceHistoryProvider(_courtId)),
                data: (logs) => logs.isEmpty
                    ? const Center(child: Text('No maintenance history yet.'))
                    : ListView.separated(
                        itemCount: logs.length,
                        separatorBuilder: (_, __) =>
                            const Divider(height: AppSpacing.md),
                        itemBuilder: (context, i) => _HistoryRow(
                          log: logs[i],
                          busy: _submitting,
                          onStart: () => _start(logs[i]),
                          onFix: () => _fix(logs[i]),
                          onCancel: () => _cancel(logs[i]),
                        ),
                      ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _start(CourtMaintenanceLog log) => _runTransition(
        log,
        title: 'Start maintenance',
        message: 'Start this maintenance now? The court becomes unavailable.',
        confirmLabel: 'Start',
        destructive: false,
        icon: Icons.play_arrow_outlined,
        action: () =>
            ref.read(courtRepositoryProvider).startMaintenance(_courtId, log.id),
        successMessage: 'Maintenance started.',
      );

  Future<void> _fix(CourtMaintenanceLog log) => _runTransition(
        log,
        title: 'Fix court',
        message:
            'Mark this maintenance complete? The court becomes available for reservations again.',
        confirmLabel: 'Fix',
        destructive: false,
        icon: Icons.check_circle_outline,
        action: () =>
            ref.read(courtRepositoryProvider).fixMaintenance(_courtId, log.id),
        successMessage: 'Court fixed — available again.',
      );

  Future<void> _cancel(CourtMaintenanceLog log) => _runTransition(
        log,
        title: 'Cancel maintenance',
        message: 'Cancel this maintenance window?',
        confirmLabel: 'Cancel window',
        destructive: true,
        icon: Icons.cancel_outlined,
        action: () =>
            ref.read(courtRepositoryProvider).cancelMaintenance(_courtId, log.id),
        successMessage: 'Maintenance cancelled.',
      );
}

/// The court's current availability line: a Maintenance/Available badge and, when
/// under maintenance, the active reason + "Unavailable for reservations".
class _CurrentStatus extends StatelessWidget {
  const _CurrentStatus({required this.court});

  final Court court;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final down = court.isUnderMaintenance;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Row(
          children: [
            StatusBadge(
              label: down ? 'Maintenance' : 'Available',
              tone: down ? StatusTone.warning : StatusTone.success,
            ),
          ],
        ),
        if (down) ...[
          const SizedBox(height: AppSpacing.xs),
          Text(
            'Unavailable for reservations',
            style:
                theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
          ),
          if (court.maintenanceReason != null &&
              court.maintenanceReason!.isNotEmpty)
            Text(court.maintenanceReason!, style: theme.textTheme.bodyMedium),
        ],
      ],
    );
  }
}

/// One status-history row: status badge + reason + window + actor, plus the
/// state-machine actions allowed from this window's status.
class _HistoryRow extends StatelessWidget {
  const _HistoryRow({
    required this.log,
    required this.busy,
    required this.onStart,
    required this.onFix,
    required this.onCancel,
  });

  final CourtMaintenanceLog log;
  final bool busy;
  final VoidCallback onStart;
  final VoidCallback onFix;
  final VoidCallback onCancel;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheduled = log.status == MaintenanceStatus.scheduled;
    final inProgress = log.status == MaintenanceStatus.inProgress;

    final window = StringBuffer(Formatters.dateTime(log.startUtc.toLocal()));
    if (log.endUtc != null) {
      window.write(' → ${Formatters.dateTime(log.endUtc!.toLocal())}');
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Row(
          children: [
            StatusBadge(label: log.status.label, tone: log.status.tone),
            const Spacer(),
            if (scheduled)
              TextButton(
                onPressed: busy ? null : onStart,
                child: const Text('Start'),
              ),
            if (inProgress)
              TextButton(
                onPressed: busy ? null : onFix,
                child: const Text('Fix'),
              ),
            if (scheduled || inProgress)
              TextButton(
                onPressed: busy ? null : onCancel,
                style: TextButton.styleFrom(foregroundColor: AppColors.danger),
                child: const Text('Cancel'),
              ),
          ],
        ),
        if (log.reason.isNotEmpty)
          Text(log.reason, style: theme.textTheme.bodyMedium),
        Text(
          window.toString(),
          style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textSecondary),
        ),
        if (log.performedByName != null && log.performedByName!.isNotEmpty)
          Text(
            'By ${log.performedByName}',
            style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
          ),
      ],
    );
  }
}

/// A labelled [DateTimePickerField] with a "Clear" affordance so the optional
/// planned start/end can be set back to null (start null = "now").
///
/// The label is rendered ABOVE the field rather than passed as the field's own
/// label: an [InputDecorator] floating label sits on top of the "Select date and
/// time" placeholder while the field is empty, which reads as overlapping text.
class _WindowField extends StatelessWidget {
  const _WindowField({
    super.key,
    required this.label,
    required this.value,
    required this.enabled,
    required this.onChanged,
    required this.onClear,
    this.firstDate,
    this.validator,
  });

  final String label;
  final DateTime? value;
  final bool enabled;
  final ValueChanged<DateTime> onChanged;
  final VoidCallback onClear;
  final DateTime? firstDate;
  final String? Function(DateTime?)? validator;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          label,
          style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textSecondary),
        ),
        const SizedBox(height: AppSpacing.xxs),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: DateTimePickerField(
                value: value,
                enabled: enabled,
                firstDate: firstDate,
                validator: validator,
                onChanged: onChanged,
              ),
            ),
            if (value != null)
              TextButton(
                onPressed: enabled ? onClear : null,
                child: const Text('Clear'),
              ),
          ],
        ),
      ],
    );
  }
}
