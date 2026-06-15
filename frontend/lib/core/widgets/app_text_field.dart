import 'package:flutter/material.dart';

/// A reusable text input field wrapping [TextFormField].
///
/// Styling (borders, fill, error text placement) is supplied by the app
/// [InputDecorationTheme] so validation errors render below the field.
class AppTextField extends StatelessWidget {
  const AppTextField({
    super.key,
    this.controller,
    this.label,
    this.hint,
    this.validator,
    this.obscureText = false,
    this.keyboardType,
    this.enabled = true,
    this.prefixIcon,
    this.onChanged,
    this.maxLines = 1,
    this.textInputAction,
    this.autovalidateMode,
  });

  final TextEditingController? controller;
  final String? label;
  final String? hint;
  final String? Function(String?)? validator;
  final bool obscureText;
  final TextInputType? keyboardType;
  final bool enabled;
  final IconData? prefixIcon;
  final ValueChanged<String>? onChanged;
  final int maxLines;
  final TextInputAction? textInputAction;
  final AutovalidateMode? autovalidateMode;

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      controller: controller,
      validator: validator,
      obscureText: obscureText,
      keyboardType: keyboardType,
      enabled: enabled,
      onChanged: onChanged,
      maxLines: obscureText ? 1 : maxLines,
      textInputAction: textInputAction,
      autovalidateMode: autovalidateMode,
      decoration: InputDecoration(
        labelText: label,
        hintText: hint,
        prefixIcon: prefixIcon != null ? Icon(prefixIcon) : null,
      ),
    );
  }
}
