using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

/// <summary>
/// REG-RATE-SEARCH-01: search quota, 429, recovery and partition isolation.
/// Uses isolated test hosts so counters do not leak between tests.
/// </summary>
public sealed class SearchRateLimitTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const string SearchUrl = "/api/incidents/search?q=zz-no-match";

    private static WebApplicationFactoryClientOptions ClientOptions => new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    };

    [Fact]
    public async Task REG_RATE_SEARCH_01_FiveAllowed_Sixth429_ThenRecovers()
    {
        using var host = factory.WithWebHostBuilder(_ => { });
        using var client = host.CreateClient(ClientOptions);

        for (var i = 0; i < 5; i++)
        {
            using var allowed = await client.GetAsync(SearchUrl);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // A caller-supplied identity header must not bypass a server-side partition.
        using var forged = new HttpRequestMessage(HttpMethod.Get, SearchUrl);
        forged.Headers.Add("X-Demo-UserId", Guid.NewGuid().ToString());
        using var blocked = await client.SendAsync(forged);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        // Rate limiting the search must not block unrelated endpoints.
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(11));

        using var recovered = await client.GetAsync(SearchUrl);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public async Task REG_RATE_SEARCH_02_AliceAndBob_UseIndependentPartitions()
    {
        var password = Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException(
                "Set SeedUsers__Password locally outside Git.");

        using var host = factory.WithWebHostBuilder(_ => { });
        using var alice = host.CreateClient(ClientOptions);
        using var bob = host.CreateClient(ClientOptions);

        using var aliceLogin = await alice.PostAsJsonAsync(
            "/api/auth/login", new { userName = "alice", password });
        Assert.True(aliceLogin.IsSuccessStatusCode,
            $"Alice login returned HTTP {(int)aliceLogin.StatusCode}");

        using var bobLogin = await bob.PostAsJsonAsync(
            "/api/auth/login", new { userName = "bob", password });
        Assert.True(bobLogin.IsSuccessStatusCode,
            $"Bob login returned HTTP {(int)bobLogin.StatusCode}");

        // Confirm the clients actually have independent authenticated sessions.
        using var aliceMe = await alice.GetAsync("/api/me");
        using var bobMe = await bob.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, aliceMe.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobMe.StatusCode);

        for (var i = 0; i < 5; i++)
        {
            using var response = await alice.GetAsync(SearchUrl);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var aliceBlocked = await alice.GetAsync(SearchUrl);
        Assert.Equal(HttpStatusCode.TooManyRequests, aliceBlocked.StatusCode);

        // Alice exhausted her quota; Bob must still have his own full quota.
        for (var i = 0; i < 5; i++)
        {
            using var response = await bob.GetAsync(SearchUrl);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var bobBlocked = await bob.GetAsync(SearchUrl);
        Assert.Equal(HttpStatusCode.TooManyRequests, bobBlocked.StatusCode);
    }
}
