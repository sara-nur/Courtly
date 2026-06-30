using System.Net;
using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Browser password-reset page (feature 22). The clickable link emailed to the user opens
/// <c>GET /reset-password?email=…&amp;token=…</c>, which renders a small HTML form; the form POSTs back here
/// and the new password is set via the shared <see cref="IAuthService"/>. Anonymous — the single-use token
/// in the link is the credential. Returns HTML (not JSON) and is hidden from Swagger.
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class PasswordResetPageController : ControllerBase
{
    private readonly IAuthService _authService;

    public PasswordResetPageController(IAuthService authService) => _authService = authService;

    [HttpGet("/reset-password")]
    public IActionResult Show([FromQuery] string? email, [FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return Html(MessagePage(
                "This reset link is invalid or incomplete. Request a new one from the Courtly app.",
                isError: true));
        }

        return Html(FormPage(email, token, error: null));
    }

    [HttpPost("/reset-password")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Submit(
        [FromForm] string email,
        [FromForm] string token,
        [FromForm] string newPassword,
        [FromForm] string confirmPassword,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return Html(MessagePage(
                "This reset link is invalid or incomplete. Request a new one from the Courtly app.",
                isError: true));
        }

        if (newPassword != confirmPassword)
        {
            return Html(FormPage(email, token, "The passwords don't match."));
        }

        try
        {
            await _authService.ResetPasswordAsync(
                new ResetPasswordRequest(email, token, newPassword), ct);
        }
        catch (AppException ex)
        {
            // Expected, user-safe message (invalid/expired token, or the password policy).
            return Html(FormPage(email, token, ex.Message));
        }

        return Html(SuccessPage());
    }

    private ContentResult Html(string html) => Content(html, "text/html");

    // ── HTML builders ──────────────────────────────────────────────────────────
    private static string FormPage(string email, string token, string? error)
    {
        var e = WebUtility.HtmlEncode(email);
        var t = WebUtility.HtmlEncode(token);
        var errorBlock = error is null
            ? string.Empty
            : $"""<div class="banner error">{WebUtility.HtmlEncode(error)}</div>""";

        return Layout("Reset your password", $"""
            <h1>Reset your password</h1>
            <p class="muted">Choose a new password for <strong>{e}</strong>.</p>
            {errorBlock}
            <form method="post" action="/reset-password" autocomplete="off">
              <input type="hidden" name="email" value="{e}" />
              <input type="hidden" name="token" value="{t}" />
              <label for="newPassword">New password</label>
              <div class="pw-wrap">
                <input id="newPassword" name="newPassword" type="password" required minlength="8" />
                <button type="button" class="pw-toggle" data-target="newPassword" aria-label="Show password"></button>
              </div>
              <label for="confirmPassword">Confirm new password</label>
              <div class="pw-wrap">
                <input id="confirmPassword" name="confirmPassword" type="password" required minlength="8" />
                <button type="button" class="pw-toggle" data-target="confirmPassword" aria-label="Show password"></button>
              </div>
              <p class="hint">At least 8 characters, including an uppercase letter, a lowercase letter, and a number.</p>
              <button type="submit">Update password</button>
            </form>
            """);
    }

    private static string SuccessPage() => Layout("Password updated", """
        <div class="check">✓</div>
        <h1>Password updated</h1>
        <p class="muted">Your password has been changed. Return to the Courtly app and sign in with your new password.</p>
        """);

    private static string MessagePage(string message, bool isError) => Layout(
        isError ? "Reset link problem" : "Courtly",
        $"""<div class="banner {(isError ? "error" : "info")}">{WebUtility.HtmlEncode(message)}</div>""");

    // Shared page chrome (self-contained inline CSS — no external assets). A $$ raw string is used so the
    // CSS braces are literal and only {{ }} marks the interpolations.
    private static string Layout(string title, string body) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>{{WebUtility.HtmlEncode(title)}} · Courtly</title>
          <style>
            :root { color-scheme: light; }
            body { margin:0; background:#f4f6f5; font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif; color:#1a1a1a; }
            .wrap { min-height:100vh; display:flex; align-items:center; justify-content:center; padding:24px; box-sizing:border-box; }
            .card { background:#fff; width:100%; max-width:420px; border-radius:14px; box-shadow:0 8px 30px rgba(0,0,0,.08); padding:32px; box-sizing:border-box; }
            .brand { font-size:22px; font-weight:700; color:#2e7d32; margin-bottom:20px; }
            h1 { font-size:20px; margin:0 0 8px; }
            p { line-height:1.5; }
            .muted { color:#555; font-size:14px; margin:0 0 20px; }
            label { display:block; font-size:13px; font-weight:600; margin:14px 0 6px; }
            input[type=password], input[type=text] { width:100%; box-sizing:border-box; padding:11px 12px; font-size:15px; border:1px solid #d0d4d2; border-radius:8px; }
            input::-ms-reveal, input::-ms-clear { display:none; }
            input:focus { outline:none; border-color:#2e7d32; box-shadow:0 0 0 3px rgba(46,125,50,.15); }
            .hint { font-size:12px; color:#888; margin:8px 0 20px; }
            button { width:100%; padding:12px; font-size:15px; font-weight:600; color:#fff; background:#2e7d32; border:none; border-radius:8px; cursor:pointer; }
            button:hover { background:#276b2b; }
            .pw-wrap { position:relative; }
            .pw-wrap input { padding-right:46px; }
            .pw-toggle { position:absolute; top:50%; right:6px; transform:translateY(-50%); width:auto; padding:6px; background:none; border:none; color:#888; cursor:pointer; display:inline-flex; }
            .pw-toggle:hover { color:#2e7d32; background:none; }
            .pw-toggle svg { width:20px; height:20px; display:block; }
            .banner { font-size:13px; padding:10px 12px; border-radius:8px; margin:0 0 16px; }
            .banner.error { background:#fdecea; color:#b71c1c; }
            .banner.info { background:#e8f3ec; color:#2e7d32; }
            .check { width:56px; height:56px; line-height:56px; text-align:center; font-size:30px; color:#fff; background:#2e7d32; border-radius:50%; margin:0 auto 16px; }
          </style>
        </head>
        <body>
          <div class="wrap">
            <div class="card">
              <div class="brand">Courtly</div>
              {{body}}
            </div>
          </div>
          <script>
            (function () {
              var EYE = '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>';
              var EYE_OFF = '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>';
              document.querySelectorAll('.pw-toggle').forEach(function (btn) {
                btn.innerHTML = EYE;
                btn.addEventListener('click', function () {
                  var el = document.getElementById(btn.getAttribute('data-target'));
                  var show = el.type === 'password';
                  el.type = show ? 'text' : 'password';
                  btn.innerHTML = show ? EYE_OFF : EYE;
                  btn.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
                });
              });
            })();
          </script>
        </body>
        </html>
        """;
}
