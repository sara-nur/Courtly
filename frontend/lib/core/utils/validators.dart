/// Reusable form-field validators for the auth screens. Messages state the
/// expected format/constraints (rubric §4), and the password rule mirrors the
/// API's ASP.NET Identity policy so the client never shows a false positive the
/// server would accept (or rejects something the server allows).
abstract final class Validators {
  // A pragmatic email shape check (one @, a dot in the domain). The server is
  // the authority; this just gives immediate, format-specific feedback.
  static final RegExp _email = RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$');

  static String? required(String label, String? value) =>
      (value == null || value.trim().isEmpty) ? '$label is required.' : null;

  static String? email(String? value) {
    if (value == null || value.trim().isEmpty) return 'Email is required.';
    return _email.hasMatch(value.trim())
        ? null
        : 'Enter a valid email address (e.g. name@example.com).';
  }

  /// Mirrors the API Identity policy: at least 8 characters including an
  /// uppercase letter, a lowercase letter, and a number.
  static String? password(String? value) {
    final v = value ?? '';
    if (v.isEmpty) return 'Password is required.';
    final ok = v.length >= 8 &&
        v.contains(RegExp(r'[A-Z]')) &&
        v.contains(RegExp(r'[a-z]')) &&
        v.contains(RegExp(r'\d'));
    return ok
        ? null
        : 'Password must be at least 8 characters and include an uppercase '
            'letter, a lowercase letter, and a number.';
  }

  static String? confirmPassword(String? value, String original) {
    if (value == null || value.isEmpty) return 'Please confirm your password.';
    return value == original ? null : 'Passwords do not match.';
  }
}
