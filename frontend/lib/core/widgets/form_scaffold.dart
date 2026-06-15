import 'package:flutter/material.dart';

import '../theme/app_spacing.dart';

/// Consistent chrome for forms: a titled, constrained card with an optional
/// dismiss control, a scrollable body of [children], and a footer action row.
///
/// The content is centered and width-constrained so the form never fills the
/// entire screen. Pass [leading] (e.g. a back button) and [onClose] to control
/// navigation affordances without depending on other widgets.
class FormScaffold extends StatelessWidget {
  const FormScaffold({
    super.key,
    required this.title,
    this.subtitle,
    required this.children,
    this.actions,
    this.onClose,
    this.leading,
    this.maxWidth = 520,
    this.contentPadding = const EdgeInsets.all(24),
  });

  final String title;
  final String? subtitle;
  final List<Widget> children;
  final List<Widget>? actions;
  final VoidCallback? onClose;
  final Widget? leading;
  final double maxWidth;
  final EdgeInsetsGeometry contentPadding;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final body = <Widget>[];
    for (var i = 0; i < children.length; i++) {
      if (i > 0) {
        body.add(const SizedBox(height: AppSpacing.md));
      }
      body.add(children[i]);
    }

    final actionRow = <Widget>[];
    final actionList = actions;
    if (actionList != null) {
      for (var i = 0; i < actionList.length; i++) {
        if (i > 0) {
          actionRow.add(const SizedBox(width: AppSpacing.sm));
        }
        actionRow.add(actionList[i]);
      }
    }

    return Center(
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: maxWidth),
        child: Card(
          child: Padding(
            padding: contentPadding,
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      if (leading != null) leading!,
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Text(title, style: theme.textTheme.titleLarge),
                            if (subtitle != null)
                              Text(
                                subtitle!,
                                style: theme.textTheme.bodySmall,
                              ),
                          ],
                        ),
                      ),
                      if (onClose != null)
                        IconButton(
                          icon: const Icon(Icons.close),
                          onPressed: onClose,
                        ),
                    ],
                  ),
                  const SizedBox(height: AppSpacing.sm),
                  const Divider(),
                  const SizedBox(height: AppSpacing.md),
                  ...body,
                  if (actionList != null) ...[
                    const SizedBox(height: AppSpacing.lg),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.end,
                      children: actionRow,
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
