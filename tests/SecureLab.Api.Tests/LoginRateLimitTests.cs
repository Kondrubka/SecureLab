using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public sealed class LoginRateLimitTests(
    SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task LoginRateLimit_Returns429_AndRecovers()
    {
        // Створюємо окремий сервер для незалежної перевірки.
        using var host = factory.WithWebHostBuilder(_ => { });

        using var client = host.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        var request = new
        {
            userName = "unknown_rate_limit_test",
            password = "IncorrectTestPassword_987!"
        };

        // Перші 12 спроб мають отримати звичайну відмову.
        for (var i = 0; i < 12; i++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/auth/login", request);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }

        // Наступна спроба перевищує ліміт.
        using var blocked = await client.PostAsJsonAsync(
            "/api/auth/login", request);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            blocked.StatusCode);

        // Очікуємо завершення 10-секундного вікна.
        await Task.Delay(TimeSpan.FromSeconds(11));

        // Після завершення вікна запити знову дозволені.
        using var recovered = await client.PostAsJsonAsync(
            "/api/auth/login", request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            recovered.StatusCode);
    }
}
