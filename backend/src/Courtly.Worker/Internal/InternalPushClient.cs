using System.Net.Http.Json;
using Courtly.Contracts.Notifications;

namespace Courtly.Worker.Internal;

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper over the API's internal push endpoint (feature 18). The <c>BaseAddress</c> and
/// the <c>X-Internal-Key</c> shared-secret header are configured once at registration (see the Worker host), so this
/// type only posts the payload to the relative path and asserts a success status.
/// <para>
/// The body is serialized with the default HTTP JSON conventions (web defaults — camelCase, <b>numeric</b> enums), which
/// match how the API's MVC model binder reads it. It deliberately does NOT use
/// <see cref="Courtly.Contracts.Messaging.MessagingTopology.SerializerOptions"/>: those add a <i>string</i>-enum
/// converter for inspectable RabbitMQ/DLQ payloads, and the API's numeric-enum binder would reject a string enum (e.g.
/// <c>"ReservationCreated"</c>) with a 400. This is an HTTP call to the API, not a message on the bus.
/// </para>
/// </summary>
public sealed class InternalPushClient : IInternalPushClient
{
    private const string PushPath = "/api/internal/push";

    private readonly HttpClient _http;

    public InternalPushClient(HttpClient http)
    {
        _http = http;
    }

    public async Task PushAsync(InternalPushRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(PushPath, request, ct);
        response.EnsureSuccessStatusCode();
    }
}
