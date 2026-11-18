using System.Net.Http.Json;

namespace SecureLab.Api.Tests;

/// <summary>
/// Sends state-changing requests with a real ASP.NET Core antiforgery token.
/// The same HttpClient must retain its authentication and antiforgery cookies.
/// </summary>
public static class CsrfTestClient
{
    private const string TokenHeader = "X-SecureLab-CSRF";

    public static async Task<string> GetCsrfTokenAsync(this HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf-token");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        if (string.IsNullOrWhiteSpace(payload?.Token))
        {
            throw new InvalidOperationException("CSRF request token was not issued.");
        }

        return payload.Token;
    }

    public static async Task<HttpResponseMessage> SendWithCsrfAsync(
        this HttpClient client, HttpRequestMessage request)
    {
        var token = await client.GetCsrfTokenAsync();
        request.Headers.Add(TokenHeader, token);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PostAsJsonWithCsrfAsync<T>(
        this HttpClient client, string url, T data)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(data)
        };
        return await client.SendWithCsrfAsync(request);
    }

    public static async Task<HttpResponseMessage> PutAsJsonWithCsrfAsync<T>(
        this HttpClient client, string url, T data)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(data)
        };
        return await client.SendWithCsrfAsync(request);
    }

    public static async Task<HttpResponseMessage> PostWithCsrfAsync(
        this HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        return await client.SendWithCsrfAsync(request);
    }

    public static async Task<HttpResponseMessage> DeleteWithCsrfAsync(
        this HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        return await client.SendWithCsrfAsync(request);
    }

    private sealed record CsrfTokenResponse(string Token);
}
