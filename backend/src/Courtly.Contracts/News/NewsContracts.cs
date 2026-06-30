namespace Courtly.Contracts.News;

/// <summary>
/// News / announcement contracts (feature 21). DTOs only on the wire — the service projects the entity to
/// <see cref="NewsDto"/> and never returns the entity or the raw image bytes. <see cref="NewsDto.ImageUrl"/> is a
/// relative URL (<c>/api/news/{id}/image</c>) the client resolves against the API base; the bytes are streamed by
/// that dedicated endpoint, never base64-inlined here. The image itself arrives on create/update as a multipart
/// <c>IFormFile</c> (handled in the controller), so it is not a field on the request records. Server-side validation
/// (NotEmpty, length, published-date) lives in the FluentValidation validators, not here.
/// </summary>
public sealed record NewsDto(
    long Id,
    string Title,
    string Text,
    string? ImageUrl,
    DateTime PublishedAtUtc,
    bool IsActive,
    string AuthorName);

public sealed record CreateNewsRequest(string Title, string Text, DateTime PublishedAtUtc, bool IsActive);

public sealed record UpdateNewsRequest(string Title, string Text, DateTime PublishedAtUtc, bool IsActive);
