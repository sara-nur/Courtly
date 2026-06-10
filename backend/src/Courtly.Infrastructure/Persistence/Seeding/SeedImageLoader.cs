using Microsoft.Extensions.Logging;

namespace Courtly.Infrastructure.Persistence.Seeding;

/// <summary>
/// Reads the seed image files bundled under <c>Persistence/Seeding/SeedImages</c> (copied next to the assembly at
/// build time) and returns their bytes + content type, so the runtime seeder can store them as <c>bytea</c>.
/// If a file is missing (e.g. a stripped test output), it logs a warning and returns a 1×1 PNG so seeding still
/// completes rather than throwing.
/// </summary>
public sealed class SeedImageLoader
{
    private const string ImagesFolder = "Persistence/Seeding/SeedImages";

    // 1×1 transparent PNG — only used as a fallback when a bundled file cannot be found.
    private static readonly byte[] FallbackPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly ILogger<SeedImageLoader> _logger;

    public SeedImageLoader(ILogger<SeedImageLoader> logger) => _logger = logger;

    /// <summary>Loads <paramref name="fileName"/> from the seed images folder.</summary>
    public SeedImage Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, ImagesFolder, fileName);
        if (File.Exists(path))
        {
            return new SeedImage(File.ReadAllBytes(path), ContentTypeFor(fileName));
        }

        _logger.LogWarning("Seed image '{File}' not found at {Path}; using fallback placeholder.", fileName, path);
        return new SeedImage(FallbackPng, "image/png");
    }

    private static string ContentTypeFor(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png",
        };
    }
}

/// <summary>A loaded seed image: raw bytes plus its MIME type.</summary>
public readonly record struct SeedImage(byte[] Bytes, string ContentType);
