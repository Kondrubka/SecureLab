using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public sealed class UnknownApiRouteTests(
    SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task REG_API_404_01_UnknownApi_ReturnsProblemJson()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var missing = await client.GetAsync(
            "/api/nonexistent-lr06");

        Assert.Equal(
            HttpStatusCode.NotFound,
            missing.StatusCode);

        Assert.Equal(
            "application/problem+json",
            missing.Content.Headers.ContentType?.MediaType);

        var body = await missing.Content.ReadAsStringAsync();

        Assert.Contains("API endpoint not found", body);

        Assert.Equal(
            "nosniff",
            missing.Headers.GetValues(
                "X-Content-Type-Options").Single());

        Assert.Equal(
            "no-referrer",
            missing.Headers.GetValues(
                "Referrer-Policy").Single());

        using var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
