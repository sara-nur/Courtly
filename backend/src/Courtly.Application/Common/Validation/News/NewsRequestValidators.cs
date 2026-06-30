using Courtly.Contracts.News;
using FluentValidation;

namespace Courtly.Application.Common.Validation.News;

/// <summary>
/// Server-side validation for News create/update (feature 21). Messages spell out the exact limits so the Flutter
/// client can show them below the offending field. Discovered via the existing assembly scan
/// (<c>AddValidatorsFromAssemblyContaining&lt;RegisterRequestValidator&gt;</c>) — no per-validator registration. The
/// image itself is NOT validated here (it arrives as a multipart <c>IFormFile</c>, not a field on the record): the
/// controller enforces "required on create" and <c>ImageContentValidator</c> checks MIME + magic bytes + size. The
/// title/limit constants mirror <c>NewsConfiguration</c>.
/// </summary>
public sealed class CreateNewsRequestValidator : AbstractValidator<CreateNewsRequest>
{
    public CreateNewsRequestValidator()
    {
        RuleFor(r => r.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(NewsValidationLimits.MaxTitleLength)
            .WithMessage($"Title must be at most {NewsValidationLimits.MaxTitleLength} characters.");

        RuleFor(r => r.Text)
            .NotEmpty().WithMessage("Content is required.")
            .MaximumLength(NewsValidationLimits.MaxTextLength)
            .WithMessage($"Content must be at most {NewsValidationLimits.MaxTextLength} characters.");

        RuleFor(r => r.PublishedAtUtc)
            .NotEmpty().WithMessage("Published date is required.");
    }
}

public sealed class UpdateNewsRequestValidator : AbstractValidator<UpdateNewsRequest>
{
    public UpdateNewsRequestValidator()
    {
        RuleFor(r => r.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(NewsValidationLimits.MaxTitleLength)
            .WithMessage($"Title must be at most {NewsValidationLimits.MaxTitleLength} characters.");

        RuleFor(r => r.Text)
            .NotEmpty().WithMessage("Content is required.")
            .MaximumLength(NewsValidationLimits.MaxTextLength)
            .WithMessage($"Content must be at most {NewsValidationLimits.MaxTextLength} characters.");

        RuleFor(r => r.PublishedAtUtc)
            .NotEmpty().WithMessage("Published date is required.");
    }
}

/// <summary>Length limits shared by the create/update validators, mirroring the EF configuration.</summary>
internal static class NewsValidationLimits
{
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 4000;
}
