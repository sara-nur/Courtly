using Courtly.Application.Abstractions;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Client-facing recommender (feature 29). Both endpoints act on the authenticated caller (owner from the JWT, never
/// the route/body): <c>GET</c> returns their explainable, ranked recommendations (paginated); <c>POST feedback</c>
/// records the "Are these recommendations helpful?" Yes/No, which re-ranks the next fetch. Thin controller — all
/// logic lives in <see cref="IRecommendationService"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/recommendations")]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class RecommendationsController : ControllerBase
{
    private readonly IRecommendationService _recommendations;

    public RecommendationsController(IRecommendationService recommendations)
    {
        _recommendations = recommendations;
    }

    /// <summary>One ranked, paginated page of explainable recommendations for the current user, plus a summary.</summary>
    [HttpGet("")]
    public async Task<ActionResult<RecommendationsResponse>> Get(
        [FromQuery] PaginationQuery pagination, CancellationToken ct)
        => Ok(await _recommendations.GetAsync(pagination, ct));

    /// <summary>Records Yes/No feedback on the currently-shown courts and returns <c>204</c>.</summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> RecordFeedback(
        [FromBody] RecommendationFeedbackRequest request, CancellationToken ct)
    {
        await _recommendations.RecordFeedbackAsync(request, ct);
        return NoContent();
    }
}
