import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';

/// The booking calendar header (mockup p.9 §3.2.3): a month label with
/// prev/next chevrons above a horizontal, scrollable day strip. The window is a
/// rolling [dayCount] days starting at [firstDate] (today) — matching the F13
/// rolling-horizon slot generation, so we never offer days with no slots far in
/// the future. Selecting a day (tap or chevron) reports it via [onDateSelected];
/// the parent owns [selectedDate] and reloads that day's availability.
class BookingDateStrip extends StatefulWidget {
  const BookingDateStrip({
    super.key,
    required this.selectedDate,
    required this.firstDate,
    required this.dayCount,
    required this.onDateSelected,
  });

  /// The currently selected day (date-only, i.e. `DateTime(y, m, d)`).
  final DateTime selectedDate;

  /// The first selectable day — today (date-only).
  final DateTime firstDate;

  /// How many days the strip offers, starting at [firstDate].
  final int dayCount;

  final ValueChanged<DateTime> onDateSelected;

  @override
  State<BookingDateStrip> createState() => _BookingDateStripState();
}

class _BookingDateStripState extends State<BookingDateStrip> {
  static const double _cellWidth = 56;
  static const double _cellExtent = _cellWidth + AppSpacing.xs;

  final ScrollController _controller = ScrollController();

  DateTime get _lastDate => widget.firstDate.add(Duration(days: widget.dayCount - 1));

  int get _selectedIndex => widget.selectedDate.difference(widget.firstDate).inDays;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _scrollToSelected(animate: false));
  }

  @override
  void didUpdateWidget(BookingDateStrip oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!_isSameDay(oldWidget.selectedDate, widget.selectedDate)) {
      _scrollToSelected(animate: true);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _scrollToSelected({required bool animate}) {
    if (!_controller.hasClients) return;
    final target = (_selectedIndex * _cellExtent)
        .clamp(0.0, _controller.position.maxScrollExtent);
    if (animate) {
      _controller.animateTo(target,
          duration: const Duration(milliseconds: 250), curve: Curves.easeOut);
    } else {
      _controller.jumpTo(target);
    }
  }

  void _step(int days) {
    final next = widget.selectedDate.add(Duration(days: days));
    if (next.isBefore(widget.firstDate) || next.isAfter(_lastDate)) return;
    widget.onDateSelected(next);
  }

  static bool _isSameDay(DateTime a, DateTime b) =>
      a.year == b.year && a.month == b.month && a.day == b.day;

  @override
  Widget build(BuildContext context) {
    final canStepBack = widget.selectedDate.isAfter(widget.firstDate);
    final canStepForward = widget.selectedDate.isBefore(_lastDate);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Text(
              Formatters.monthYear(widget.selectedDate),
              style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
            ),
            const Spacer(),
            IconButton(
              icon: const Icon(Icons.chevron_left),
              onPressed: canStepBack ? () => _step(-1) : null,
              tooltip: 'Previous day',
            ),
            IconButton(
              icon: const Icon(Icons.chevron_right),
              onPressed: canStepForward ? () => _step(1) : null,
              tooltip: 'Next day',
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.xs),
        SizedBox(
          height: 72,
          child: ListView.separated(
            controller: _controller,
            scrollDirection: Axis.horizontal,
            itemCount: widget.dayCount,
            separatorBuilder: (_, __) => const SizedBox(width: AppSpacing.xs),
            itemBuilder: (context, index) {
              final date = widget.firstDate.add(Duration(days: index));
              return _DayCell(
                date: date,
                width: _cellWidth,
                isSelected: _isSameDay(date, widget.selectedDate),
                onTap: () => widget.onDateSelected(date),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _DayCell extends StatelessWidget {
  const _DayCell({
    required this.date,
    required this.width,
    required this.isSelected,
    required this.onTap,
  });

  final DateTime date;
  final double width;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final foreground = isSelected ? Colors.white : AppColors.textPrimary;
    final weekdayColor = isSelected ? Colors.white70 : AppColors.textMuted;

    return SizedBox(
      width: width,
      child: Material(
        color: isSelected ? AppColors.primary : AppColors.surface,
        borderRadius: AppSpacing.brMd,
        child: InkWell(
          borderRadius: AppSpacing.brMd,
          onTap: onTap,
          child: Container(
            decoration: BoxDecoration(
              borderRadius: AppSpacing.brMd,
              border: Border.all(
                color: isSelected ? AppColors.primary : AppColors.surfaceMuted,
              ),
            ),
            padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(
                  Formatters.weekdayShort(date),
                  style: theme.textTheme.labelSmall?.copyWith(color: weekdayColor),
                ),
                const SizedBox(height: AppSpacing.xxs),
                Text(
                  '${date.day}',
                  style: theme.textTheme.titleMedium?.copyWith(
                    color: foreground,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
