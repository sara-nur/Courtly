using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts.Media;
using Xunit;

namespace Courtly.Tests.Courts;

/// <summary>
/// Feature 11 DoD (auto): the pure upload guard accepts only PNG/JPEG/WEBP whose declared content-type matches the
/// magic bytes, and rejects empty, oversize, unsupported, and spoofed payloads — always throwing the app
/// <see cref="ValidationException"/> keyed on the upload field "file" so the client renders the reason inline. The
/// validator has no DbContext, so it is exercised directly with hand-built byte arrays.
/// </summary>
public class ImageContentValidatorTests
{
    private static byte[] Png() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0x03 };

    private static byte[] Jpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

    private static byte[] Webp()
    {
        // "RIFF" + 4-byte size + "WEBP" + a little payload.
        var bytes = new byte[16];
        new byte[] { 0x52, 0x49, 0x46, 0x46 }.CopyTo(bytes, 0); // RIFF
        new byte[] { 0x57, 0x45, 0x42, 0x50 }.CopyTo(bytes, 8); // WEBP at offset 8
        return bytes;
    }

    [Fact]
    public void Accepts_png_and_returns_normalized_type()
    {
        Assert.Equal("image/png", ImageContentValidator.Validate("image/png", Png()));
    }

    [Fact]
    public void Accepts_jpeg_and_returns_normalized_type()
    {
        Assert.Equal("image/jpeg", ImageContentValidator.Validate("image/jpeg", Jpeg()));
    }

    [Fact]
    public void Accepts_webp_and_returns_normalized_type()
    {
        Assert.Equal("image/webp", ImageContentValidator.Validate("image/webp", Webp()));
    }

    [Fact]
    public void Rejects_declared_png_with_jpeg_bytes_as_a_mismatch()
    {
        var ex = Assert.Throws<ValidationException>(() => ImageContentValidator.Validate("image/png", Jpeg()));
        Assert.True(ex.Errors!.ContainsKey("file"));
    }

    [Fact]
    public void Rejects_unsupported_content_type()
    {
        // application/pdf with a "%PDF" header — neither the declared type nor the sniffed format is allowed.
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var ex = Assert.Throws<ValidationException>(() => ImageContentValidator.Validate("application/pdf", pdf));
        Assert.True(ex.Errors!.ContainsKey("file"));
    }

    [Fact]
    public void Rejects_oversize_payload()
    {
        var tooBig = new byte[ImageContentValidator.MaxBytes + 1];
        // Valid PNG header so the size check (not the format check) is what fails.
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(tooBig, 0);

        var ex = Assert.Throws<ValidationException>(() => ImageContentValidator.Validate("image/png", tooBig));
        Assert.True(ex.Errors!.ContainsKey("file"));
    }

    [Fact]
    public void Rejects_empty_payload()
    {
        var ex = Assert.Throws<ValidationException>(() => ImageContentValidator.Validate("image/png", Array.Empty<byte>()));
        Assert.True(ex.Errors!.ContainsKey("file"));
    }
}
