using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ShelterStack.Web.Auth;

/// <summary>
/// The single typed client every Gateway call goes through. It attaches the signed-in
/// session's access token as a <c>Bearer</c> header on every authorized request, so the
/// backend resolves the right tenant from the token's <c>tenant_id</c> claim and the existing
/// global query filters keep the caller scoped to their own organization.
/// </summary>
/// <remarks>
/// The token is attached here in the typed client rather than in an <c>HttpClient</c>
/// <c>DelegatingHandler</c> on purpose: <c>IHttpClientFactory</c> builds message handlers in a
/// scope separate from (and longer-lived than) the Blazor circuit, so a handler that read the
/// scoped <see cref="WebAuthenticationStateProvider"/> would capture a stale — and across
/// users, wrong — session. This class is resolved inside the circuit's scope, so the session
/// it reads is always the current user's.
/// </remarks>
public sealed class GatewayClient(HttpClient http, WebAuthenticationStateProvider auth)
{
    /// <summary>
    /// Posts credentials to the Gateway's <c>/identity/login</c> (proxied to the Identity API)
    /// and returns the issued access token, or <c>null</c> when the credentials are rejected.
    /// This is the one unauthenticated call, so it does not attach a Bearer header.
    /// </summary>
    public async Task<string?> LoginAsync(string email, string password, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            "/identity/login",
            new { email, password },
            ct
        );

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
        return body?.AccessToken;
    }

    /// <summary>
    /// Sends an authorized request through the Gateway with the session's access token attached
    /// as a <c>Bearer</c> header. The domain (Animals) screens build on this; it is the chokepoint
    /// that guarantees every Gateway call carries the token.
    /// </summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (auth.AccessToken is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return http.SendAsync(request, ct);
    }

    /// <summary>
    /// Issues an authorized <c>GET</c> and returns the raw response. Callers inspect the status
    /// code themselves — a <c>403</c> (e.g. a volunteer hitting a staff-only endpoint) and a
    /// <c>404</c> are meaningful outcomes the screens render distinct states for, not failures to
    /// hide behind a thrown exception.
    /// </summary>
    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, path), ct);

    /// <summary>
    /// Issues an authorized request with a JSON body (create/update) and returns the raw
    /// response, so callers can branch on <c>201/200</c>, <c>403</c>, and <c>400</c> validation.
    /// The body is serialized with <see cref="GatewayJson.Options"/> so enums go over as names.
    /// </summary>
    public Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        T body,
        CancellationToken ct
    )
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: GatewayJson.Options),
        };

        return SendAsync(request, ct);
    }

    private sealed record LoginResponse(string AccessToken);
}
