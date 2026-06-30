using Courtly.Application.SearchHistory;
using Courtly.Contracts.Errors;
using Courtly.Contracts.SearchHistory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Search-history capture (feature 23 recommender signal). The controller is thin: it model-binds the search filters,
/// calls <see cref="ISearchHistoryService"/>, and returns 204 — no business logic or DbContext here. Authenticated:
/// the owner is taken from the JWT inside the service (never the body), and an unauthenticated caller is rejected by
/// <c>[Authorize]</c>. The capture is fire-and-forget from the client's perspective, so there is no response body.
/// </summary>
[ApiController]
[Authorize]
[Route("api/search-history")]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class SearchHistoryController : ControllerBase
{
    private readonly ISearchHistoryService _service;

    public SearchHistoryController(ISearchHistoryService service)
    {
        _service = service;
    }

    /// <summary>Records the search the user just performed for the recommender. Returns 204 (no body).</summary>
    [HttpPost("")]
    public async Task<IActionResult> Record([FromBody] RecordSearchRequest request, CancellationToken ct)
    {
        await _service.RecordAsync(request, ct);
        return NoContent();
    }
}
