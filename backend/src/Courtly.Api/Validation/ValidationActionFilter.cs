using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using ValidationException = Courtly.Application.Common.Exceptions.ValidationException;

namespace Courtly.Api.Validation;

/// <summary>
/// The single server-side validation gate (rubric §3.4/§4). Runs after model binding, so it sees both the
/// bound DTOs and ModelState. FluentValidation <c>IValidator&lt;T&gt;</c> messages (which spell out the
/// format) take precedence per field; DataAnnotations / model-binding errors fill in for fields no validator
/// covers. Any failure becomes the app's <see cref="ValidationException"/> → the exception middleware emits a
/// standardized 400 <c>ErrorResponse</c>. <c>[ApiController]</c>'s automatic 400 is suppressed in
/// <c>Program.cs</c> so every validation error flows through this one path.
/// </summary>
public sealed class ValidationActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        // 1. FluentValidation — richer, conditional rules; wins per field.
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var group in result.Errors.GroupBy(f => f.PropertyName))
            {
                errors[group.Key] = group.Select(f => f.ErrorMessage).Distinct().ToArray();
            }
        }

        // 2. DataAnnotations / model-binding backstop for any field a validator didn't already cover.
        foreach (var (key, entry) in context.ModelState)
        {
            if (entry is null || entry.Errors.Count == 0 || errors.ContainsKey(key))
            {
                continue;
            }

            errors[key] = entry.Errors.Select(e => e.ErrorMessage).ToArray();
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("One or more validation errors occurred.", errors);
        }

        await next();
    }
}
