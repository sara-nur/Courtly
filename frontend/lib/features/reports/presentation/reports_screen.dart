import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import 'package:pdf/pdf.dart';
import 'package:printing/printing.dart';

import '../../../core/enums/reservation_status.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/date_time_picker_field.dart';
import '../../../core/widgets/db_dropdown.dart';
import '../../court_catalog/domain/court_models.dart';
import '../../reservations/application/reservation_providers.dart';
import '../application/reports_providers.dart';
import '../domain/report_filters.dart';
import '../domain/report_models.dart';

/// Reports (feature 20, mockup p.5 nav). Two reports — a **Reservations** report (date/court/status) and a
/// **Revenue & court-utilisation** report (month/court). Each renders its data **in-app as a table** behind a filter
/// row; a **Preview & print PDF** button opens the server-rendered PDF in a dialog where it can be saved or printed
/// (rubric §2.2: downloadable + printable). Dropdowns load from the DB; no raw ids are shown.
class ReportsScreen extends StatelessWidget {
  const ReportsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return DefaultTabController(
      length: 2,
      child: Padding(
        padding: AppSpacing.pagePadding,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Reports', style: theme.textTheme.headlineSmall),
            const SizedBox(height: AppSpacing.xxs),
            Text('View report data in-app, then download or print it as a PDF.', style: theme.textTheme.bodyMedium),
            const SizedBox(height: AppSpacing.md),
            const TabBar(
              isScrollable: true,
              tabAlignment: TabAlignment.start,
              tabs: [
                Tab(text: 'Reservations'),
                Tab(text: 'Revenue & utilisation'),
              ],
            ),
            const SizedBox(height: AppSpacing.md),
            const Expanded(
              child: TabBarView(
                children: [
                  _ReservationsReportTab(),
                  _RevenueReportTab(),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ============================================================================================
// Reservations report tab
// ============================================================================================

class _ReservationsReportTab extends ConsumerStatefulWidget {
  const _ReservationsReportTab();

  @override
  ConsumerState<_ReservationsReportTab> createState() => _ReservationsReportTabState();
}

class _ReservationsReportTabState extends ConsumerState<_ReservationsReportTab>
    with AutomaticKeepAliveClientMixin {
  ReservationsReportFilters _filters = const ReservationsReportFilters();

  @override
  bool get wantKeepAlive => true;

  void _update(ReservationsReportFilters filters) => setState(() => _filters = filters);

  void _openPdf() {
    final filters = _filters;
    showReportPdfDialog(
      context,
      title: 'Reservations report',
      fileName: 'reservations-report.pdf',
      buildPdf: (_) => ref.read(reportsRepositoryProvider).reservationsReport(
            fromUtc: filters.fromUtc,
            toUtc: filters.toUtc,
            courtId: filters.courtId,
            status: filters.status,
          ),
    );
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final courts = ref.watch(reservationCourtLookupProvider);
    final data = ref.watch(reservationsReportDataProvider(_filters));

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // Top breathing room so the fields' floating labels aren't clipped by the TabBarView edge.
        const SizedBox(height: AppSpacing.sm),
        Wrap(
          spacing: AppSpacing.md,
          runSpacing: AppSpacing.sm,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 220,
              child: DateTimePickerField(
                label: 'From date',
                value: _filters.fromUtc?.toLocal(),
                includeTime: false,
                onChanged: (value) => _update(_filters.copyWith(fromUtc: _dayStartUtc(value))),
              ),
            ),
            SizedBox(
              width: 220,
              child: DateTimePickerField(
                label: 'To date',
                value: _filters.toUtc?.toLocal().subtract(const Duration(days: 1)),
                includeTime: false,
                onChanged: (value) => _update(_filters.copyWith(toUtc: _dayEndUtc(value))),
              ),
            ),
            SizedBox(
              width: 240,
              child: DbDropdown<Court?>(
                label: 'Court',
                value: courts.maybeWhen(
                  data: (list) => _courtById(list, _filters.courtId),
                  orElse: () => null,
                ),
                items: [null, ...courts.maybeWhen(data: (list) => list, orElse: () => const <Court>[])],
                itemLabel: (court) => court?.name ?? 'All courts',
                onChanged: (court) => _update(
                  court == null ? _filters.copyWith(clearCourt: true) : _filters.copyWith(courtId: court.id),
                ),
              ),
            ),
            SizedBox(
              width: 220,
              child: DbDropdown<ReservationStatus?>(
                label: 'Status',
                value: _filters.status,
                items: const [null, ...ReservationStatus.values],
                itemLabel: (status) => status?.label ?? 'All statuses',
                onChanged: (status) => _update(
                  status == null ? _filters.copyWith(clearStatus: true) : _filters.copyWith(status: status),
                ),
              ),
            ),
            if (_filters.isActive)
              TextButton.icon(
                onPressed: () => _update(const ReservationsReportFilters()),
                icon: const Icon(Icons.clear_all, size: 18),
                label: const Text('Clear'),
              ),
          ],
        ),
        const SizedBox(height: AppSpacing.md),
        Row(
          children: [
            const Spacer(),
            FilledButton.icon(
              onPressed: _openPdf,
              icon: const Icon(Icons.picture_as_pdf_outlined, size: 18),
              label: const Text('Preview & print PDF'),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.sm),
        Expanded(
          child: AsyncValueView<ReservationsReportData>(
            value: data,
            onRetry: () => ref.invalidate(reservationsReportDataProvider(_filters)),
            data: (report) => _ReservationsTable(report: report),
          ),
        ),
      ],
    );
  }
}

class _ReservationsTable extends StatelessWidget {
  const _ReservationsTable({required this.report});

  final ReservationsReportData report;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Wrap(
          spacing: AppSpacing.lg,
          runSpacing: AppSpacing.xs,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            _SummaryStat(label: 'Total bookings', value: '${report.totalCount}'),
            _SummaryStat(label: 'Total amount', value: _money(report.totalAmount)),
            ...report.statusBreakdown.map(
              (s) => Text('${s.statusName}: ${s.count}', style: theme.textTheme.bodySmall),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.sm),
        if (report.rows.isEmpty)
          const Expanded(child: _EmptyState(message: 'No reservations match these filters.'))
        else
          Expanded(
            child: _ScrollableTable(
              table: DataTable(
                columns: const [
                  DataColumn(label: Text('Ref')),
                  DataColumn(label: Text('Customer')),
                  DataColumn(label: Text('Court')),
                  DataColumn(label: Text('Date & time')),
                  DataColumn(label: Text('Status')),
                  DataColumn(label: Text('Paid')),
                  DataColumn(label: Text('Amount'), numeric: true),
                ],
                rows: [
                  for (final r in report.rows)
                    DataRow(cells: [
                      DataCell(Text(r.reference)),
                      DataCell(_TwoLine(primary: r.customer, secondary: r.email)),
                      DataCell(Text(r.courtName)),
                      DataCell(_TwoLine(
                        primary: DateFormat('MMM d, y').format(r.slotStartUtc),
                        secondary: '${DateFormat('HH:mm').format(r.slotStartUtc)}'
                            '–${DateFormat('HH:mm').format(r.slotEndUtc)} UTC',
                      )),
                      DataCell(_StatusPill(r.statusName)),
                      DataCell(Icon(
                        r.isPaid ? Icons.check_circle : Icons.remove_circle_outline,
                        size: 18,
                        color: r.isPaid ? Colors.green.shade600 : theme.disabledColor,
                      )),
                      DataCell(Text(_money(r.amount))),
                    ]),
                ],
              ),
            ),
          ),
      ],
    );
  }
}

// ============================================================================================
// Revenue & utilisation report tab
// ============================================================================================

class _RevenueReportTab extends ConsumerStatefulWidget {
  const _RevenueReportTab();

  @override
  ConsumerState<_RevenueReportTab> createState() => _RevenueReportTabState();
}

class _RevenueReportTabState extends ConsumerState<_RevenueReportTab>
    with AutomaticKeepAliveClientMixin {
  static const int _monthChoices = 12;

  late final List<DateTime> _months;
  late RevenueReportFilters _filters;

  @override
  bool get wantKeepAlive => true;

  @override
  void initState() {
    super.initState();
    final now = DateTime.now();
    final current = DateTime(now.year, now.month);
    // The current month and the previous 11 (DateTime normalizes month underflow across year boundaries).
    _months = List<DateTime>.generate(_monthChoices, (i) => DateTime(current.year, current.month - i));
    _filters = RevenueReportFilters(month: current);
  }

  void _update(RevenueReportFilters filters) => setState(() => _filters = filters);

  void _openPdf() {
    final filters = _filters;
    showReportPdfDialog(
      context,
      title: 'Revenue & court utilisation',
      fileName: 'revenue-utilization.pdf',
      buildPdf: (_) => ref.read(reportsRepositoryProvider).revenueUtilizationReport(
            year: filters.year,
            month: filters.monthNumber,
            courtId: filters.courtId,
          ),
    );
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final courts = ref.watch(reservationCourtLookupProvider);
    final data = ref.watch(revenueReportDataProvider(_filters));

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // Top breathing room so the fields' floating labels aren't clipped by the TabBarView edge.
        const SizedBox(height: AppSpacing.sm),
        Wrap(
          spacing: AppSpacing.md,
          runSpacing: AppSpacing.sm,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 240,
              child: DbDropdown<DateTime>(
                label: 'Month',
                value: _filters.month,
                items: _months,
                itemLabel: (month) => DateFormat('MMMM yyyy').format(month),
                onChanged: (month) {
                  if (month != null) _update(_filters.copyWith(month: month));
                },
              ),
            ),
            SizedBox(
              width: 240,
              child: DbDropdown<Court?>(
                label: 'Court',
                value: courts.maybeWhen(
                  data: (list) => _courtById(list, _filters.courtId),
                  orElse: () => null,
                ),
                items: [null, ...courts.maybeWhen(data: (list) => list, orElse: () => const <Court>[])],
                itemLabel: (court) => court?.name ?? 'All courts',
                onChanged: (court) => _update(
                  court == null ? _filters.copyWith(clearCourt: true) : _filters.copyWith(courtId: court.id),
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.md),
        Row(
          children: [
            const Spacer(),
            FilledButton.icon(
              onPressed: _openPdf,
              icon: const Icon(Icons.picture_as_pdf_outlined, size: 18),
              label: const Text('Preview & print PDF'),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.sm),
        Expanded(
          child: AsyncValueView<RevenueUtilizationReportData>(
            value: data,
            onRetry: () => ref.invalidate(revenueReportDataProvider(_filters)),
            data: (report) => _RevenueTable(report: report),
          ),
        ),
      ],
    );
  }
}

class _RevenueTable extends StatelessWidget {
  const _RevenueTable({required this.report});

  final RevenueUtilizationReportData report;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Wrap(
          spacing: AppSpacing.lg,
          runSpacing: AppSpacing.xs,
          children: [
            _SummaryStat(label: 'Total bookings', value: '${report.totalBookings}'),
            _SummaryStat(label: 'Total revenue', value: _money(report.totalRevenue)),
            _SummaryStat(label: 'Overall utilisation', value: _percent(report.overallUtilizationPct)),
          ],
        ),
        const SizedBox(height: AppSpacing.sm),
        if (report.rows.isEmpty)
          const Expanded(child: _EmptyState(message: 'No activity for this month.'))
        else
          Expanded(
            child: _ScrollableTable(
              table: DataTable(
                columns: const [
                  DataColumn(label: Text('Court')),
                  DataColumn(label: Text('Bookings'), numeric: true),
                  DataColumn(label: Text('Revenue'), numeric: true),
                  DataColumn(label: Text('Utilisation'), numeric: true),
                ],
                rows: [
                  for (final r in report.rows)
                    DataRow(cells: [
                      DataCell(Text(r.courtName)),
                      DataCell(Text('${r.bookings}')),
                      DataCell(Text(_money(r.revenue))),
                      DataCell(Text(_percent(r.utilizationPct))),
                    ]),
                  DataRow(
                    cells: [
                      const DataCell(Text('Total', style: TextStyle(fontWeight: FontWeight.w700))),
                      DataCell(Text('${report.totalBookings}', style: const TextStyle(fontWeight: FontWeight.w700))),
                      DataCell(Text(_money(report.totalRevenue), style: const TextStyle(fontWeight: FontWeight.w700))),
                      DataCell(Text(_percent(report.overallUtilizationPct),
                          style: const TextStyle(fontWeight: FontWeight.w700))),
                    ],
                  ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}

// ============================================================================================
// Shared pieces
// ============================================================================================

/// Opens the server-rendered PDF in a dialog with a built-in **Print** action and a **Download** action.
void showReportPdfDialog(
  BuildContext context, {
  required String title,
  required String fileName,
  required LayoutCallback buildPdf,
}) {
  showDialog<void>(
    context: context,
    builder: (dialogContext) {
      final size = MediaQuery.of(dialogContext).size;
      return Dialog(
        child: SizedBox(
          width: size.width * 0.9 > 1000 ? 1000 : size.width * 0.9,
          height: size.height * 0.9,
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(AppSpacing.md, AppSpacing.sm, AppSpacing.sm, AppSpacing.sm),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(title, style: Theme.of(dialogContext).textTheme.titleMedium),
                    ),
                    IconButton(
                      tooltip: 'Close',
                      onPressed: () => Navigator.of(dialogContext).pop(),
                      icon: const Icon(Icons.close),
                    ),
                  ],
                ),
              ),
              const Divider(height: 1),
              Expanded(child: _ReportPdfPreview(buildPdf: buildPdf, fileName: fileName)),
            ],
          ),
        ),
      );
    },
  );
}

/// The in-dialog PDF preview: renders the server PDF with a built-in **Print** action and a **Download** action that
/// writes the bytes to a file the user picks. Sharing is off — Download is the explicit desktop save path.
class _ReportPdfPreview extends StatelessWidget {
  const _ReportPdfPreview({required this.buildPdf, required this.fileName});

  final LayoutCallback buildPdf;
  final String fileName;

  @override
  Widget build(BuildContext context) {
    return PdfPreview(
      build: buildPdf,
      pdfFileName: fileName,
      allowPrinting: true,
      allowSharing: false,
      canChangePageFormat: false,
      canChangeOrientation: false,
      canDebug: false,
      actions: [
        PdfPreviewAction(
          icon: const Icon(Icons.download),
          onPressed: (context, layout, pageFormat) {
            _download(context, layout, pageFormat);
          },
        ),
      ],
    );
  }

  Future<void> _download(BuildContext context, LayoutCallback layout, PdfPageFormat pageFormat) async {
    final messenger = ScaffoldMessenger.of(context);
    try {
      final bytes = await layout(pageFormat);

      final path = await FilePicker.platform.saveFile(
        dialogTitle: 'Save report',
        fileName: fileName,
        type: FileType.custom,
        allowedExtensions: const ['pdf'],
      );
      if (path == null) return; // user cancelled

      final outPath = path.toLowerCase().endsWith('.pdf') ? path : '$path.pdf';
      await File(outPath).writeAsBytes(bytes);
      messenger.showSnackBar(SnackBar(content: Text('Saved to $outPath')));
    } catch (e) {
      messenger.showSnackBar(SnackBar(content: Text('Could not save the report: $e')));
    }
  }
}

/// A two-line table cell: a primary line and a muted secondary line (e.g. name / email, date / time).
class _TwoLine extends StatelessWidget {
  const _TwoLine({required this.primary, this.secondary});

  final String primary;
  final String? secondary;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      mainAxisAlignment: MainAxisAlignment.center,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(primary),
        if (secondary != null && secondary!.isNotEmpty)
          Text(secondary!, style: theme.textTheme.bodySmall?.copyWith(color: theme.hintColor)),
      ],
    );
  }
}

/// A small coloured status pill for the reservations table.
class _StatusPill extends StatelessWidget {
  const _StatusPill(this.label);

  final String label;

  @override
  Widget build(BuildContext context) {
    final (Color fg, Color bg) = switch (label) {
      'Confirmed' => (Colors.green.shade700, Colors.green.shade50),
      'Pending' => (Colors.orange.shade800, Colors.orange.shade50),
      'Cancelled' => (Colors.red.shade700, Colors.red.shade50),
      'Completed' => (Colors.blue.shade700, Colors.blue.shade50),
      _ => (Colors.grey.shade700, Colors.grey.shade100),
    };
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(12)),
      child: Text(label, style: TextStyle(color: fg, fontSize: 12, fontWeight: FontWeight.w600)),
    );
  }
}

/// One labelled summary statistic shown above a report table.
class _SummaryStat extends StatelessWidget {
  const _SummaryStat({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text('$label: ', style: theme.textTheme.bodyMedium),
        Text(value, style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w700)),
      ],
    );
  }
}

/// Wraps a wide [DataTable] in vertical + horizontal scrolling so a long/wide report scrolls inside the page. The
/// vertical scroll uses an explicit controller so the [Scrollbar] attaches to it (not the nested horizontal scroller).
class _ScrollableTable extends StatefulWidget {
  const _ScrollableTable({required this.table});

  final Widget table;

  @override
  State<_ScrollableTable> createState() => _ScrollableTableState();
}

class _ScrollableTableState extends State<_ScrollableTable> {
  final ScrollController _vertical = ScrollController();

  @override
  void dispose() {
    _vertical.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scrollbar(
      controller: _vertical,
      child: SingleChildScrollView(
        controller: _vertical,
        child: SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: widget.table,
        ),
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Text(message, style: theme.textTheme.bodyMedium?.copyWith(color: theme.hintColor)),
    );
  }
}

final NumberFormat _moneyFormat = NumberFormat('#,##0.00');

String _money(double value) => '\$${_moneyFormat.format(value)}';

String _percent(double value) => '${value.toStringAsFixed(1)}%';

Court? _courtById(List<Court> courts, int? id) {
  if (id == null) return null;
  for (final court in courts) {
    if (court.id == id) return court;
  }
  return null;
}

/// Start of the picked local day, as a UTC instant (inclusive range start).
DateTime _dayStartUtc(DateTime localDay) => DateTime.utc(localDay.year, localDay.month, localDay.day);

/// Start of the day AFTER the picked local day, as UTC (exclusive range end, so the picked day is included).
DateTime _dayEndUtc(DateTime localDay) =>
    DateTime.utc(localDay.year, localDay.month, localDay.day).add(const Duration(days: 1));
