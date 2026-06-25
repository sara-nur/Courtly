using Courtly.Application.Common.Exceptions;

namespace Courtly.Application.Courts.Media;

/// <summary>
/// Pure (no DbContext) content guard for uploaded court images (feature 11). Verifies the declared content-type is
/// one we accept, that the magic bytes match a supported format, that the declared type is CONSISTENT with the
/// sniffed type (no spoofing a PNG that is really a JPEG), and that the payload is non-empty and within the 5 MB
/// cap. On any failure throws the app <see cref="ValidationException"/> (400) keyed on the upload field "file" so
/// the client renders the reason inline. Returns the normalized content-type (e.g. <c>image/jpeg</c>) on success.
/// Kept pure so it is trivially unit-testable without a request pipeline.
/// </summary>
public static class ImageContentValidator
{
    /// <summary>Hard upload cap: 5 MB. The HTTP layer also enforces this via request-size limits.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    private const string Png = "image/png";
    private const string Jpeg = "image/jpeg";
    private const string Webp = "image/webp";

    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Riff = { 0x52, 0x49, 0x46, 0x46 };  // "RIFF"
    private static readonly byte[] WebpTag = { 0x57, 0x45, 0x42, 0x50 }; // "WEBP" (offset 8)

    /// <summary>
    /// Validates the upload and returns the normalized content-type. Throws <see cref="ValidationException"/>
    /// (field "file") when the bytes are empty/oversize, the format is unsupported, or the declared content-type
    /// does not match the actual bytes.
    /// </summary>
    public static string Validate(string? declaredContentType, byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            throw FileError("The uploaded file is empty.");
        }

        if (bytes.Length > MaxBytes)
        {
            throw FileError($"The image must be at most {MaxBytes / (1024 * 1024)} MB.");
        }

        var sniffed = Sniff(bytes)
            ?? throw FileError("Unsupported image format. Allowed: PNG, JPEG, WEBP.");

        var declared = Normalize(declaredContentType);
        if (declared is null)
        {
            throw FileError("Unsupported content type. Allowed: image/png, image/jpeg, image/webp.");
        }

        if (declared != sniffed)
        {
            throw FileError($"The file content ({sniffed}) does not match the declared content type ({declared}).");
        }

        return sniffed;
    }

    /// <summary>Identifies the format from the leading magic bytes, or null when none match.</summary>
    private static string? Sniff(byte[] bytes)
    {
        if (StartsWith(bytes, PngMagic))
        {
            return Png;
        }

        if (StartsWith(bytes, JpegMagic))
        {
            return Jpeg;
        }

        // WEBP = "RIFF" then 4-byte size then "WEBP" at offset 8.
        if (bytes.Length >= 12 && StartsWith(bytes, Riff) && HasTagAt(bytes, WebpTag, 8))
        {
            return Webp;
        }

        return null;
    }

    /// <summary>Maps a declared MIME string to its normalized form, or null when it is not one we accept.</summary>
    private static string? Normalize(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var value = contentType.Trim().ToLowerInvariant();
        return value switch
        {
            Png => Png,
            Jpeg => Jpeg,
            "image/jpg" => Jpeg, // common (non-standard) alias browsers occasionally send
            Webp => Webp,
            _ => null,
        };
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasTagAt(byte[] bytes, byte[] tag, int offset)
    {
        if (bytes.Length < offset + tag.Length)
        {
            return false;
        }

        for (var i = 0; i < tag.Length; i++)
        {
            if (bytes[offset + i] != tag[i])
            {
                return false;
            }
        }

        return true;
    }

    private static ValidationException FileError(string message) =>
        new(message, new Dictionary<string, string[]> { ["file"] = new[] { message } });
}
