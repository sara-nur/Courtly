using Courtly.Application.Reviews;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Court reviews (feature 24). The controller is thin: it model-binds, calls <see cref="IReviewService"/>, and returns
/// the DTO — no business logic or DbContext here. Everything is authenticated (the client is auth-gated and the write
/// derives its owner from the JWT inside the service). The service throws the app's custom exceptions (403 for a
/// non-owner, 409 for "not completed" / "already reviewed", 404 for a missing reservation) and the exception middleware
/// maps them to a standardized <see cref="ErrorResponse"/>. The read list and the eligibility probe are nested under the
/// court; the write posts to <c>api/reviews</c> with the reservation id (the court is derived from it).
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;

    public ReviewsController(IReviewService reviews)
    {
        _reviews = reviews;
    }

    /// <summary>One page of a court's reviews, newest-first.</summary>
    [HttpGet("courts/{courtId:long}/reviews")]
    public async Task<ActionResult<PagedResult<ReviewDto>>> GetForCourt(
        long courtId, [FromQuery] PaginationQuery pagination, CancellationToken ct)
        => Ok(await _reviews.GetForCourtAsync(courtId, pagination, ct));

    /// <summary>Whether the signed-in user may review this court, and which reservation to post against.</summary>
    [HttpGet("courts/{courtId:long}/review-eligibility")]
    public async Task<ActionResult<ReviewEligibilityDto>> GetEligibility(long courtId, CancellationToken ct)
        => Ok(await _reviews.GetEligibilityAsync(courtId, ct));

    /// <summary>Posts the caller's review for one of their completed reservations.</summary>
    [HttpPost("reviews")]
    public async Task<ActionResult<ReviewDto>> Create([FromBody] CreateReviewRequest request, CancellationToken ct)
        => Ok(await _reviews.CreateAsync(request, ct));
}
