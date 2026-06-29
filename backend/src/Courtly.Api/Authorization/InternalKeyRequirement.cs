using Microsoft.AspNetCore.Authorization;

namespace Courtly.Api.Authorization;

/// <summary>
/// Marker requirement for the <c>"InternalKey"</c> policy (feature 18): a request satisfies it only by presenting the
/// shared internal push secret in the <c>X-Internal-Key</c> header. Carries no state — the matching
/// <see cref="InternalKeyAuthorizationHandler"/> holds all the verification logic. The policy deliberately does
/// <b>not</b> require an authenticated user, because the JWT-less Worker authenticates on the key alone.
/// </summary>
public sealed class InternalKeyRequirement : IAuthorizationRequirement;
