using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class Lab02GoodLevelTests : IClassFixture<SecureLabApiFactory>
{
    private readonly SecureLabApiFactory _factory;
    private readonly HttpClient _client;

    public Lab02GoodLevelTests(SecureLabApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task T02_InvalidSeverity_ReturnsBadRequest()
    {
        var request = new
        {
            title = $"Invalid-{Guid.NewGuid():N}",
            description = "Навчальний опис для перевірки некоректного значення severity.",
            severity = "Unknown",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        using var response = await _client.PostAsJsonAsync(
            "/api/incidents",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("severity", content);
        Assert.DoesNotContain("SELECT ", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection string", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack trace", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task T03_DuplicateActiveTitle_ReturnsConflict()
    {
        var title = $"Regression-{Guid.NewGuid():N}";

        var request = new
        {
            title,
            description = "Навчальний опис для автоматичної перевірки конфлікту однакових заголовків.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        Guid? createdId = null;

        try
        {
            using var firstResponse = await _client.PostAsJsonAsync(
                "/api/incidents",
                request);

            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

            var firstContent =
                await firstResponse.Content.ReadFromJsonAsync<CreatedIncidentTestResponse>();

            Assert.NotNull(firstContent);

            createdId = firstContent.Id;

            using var secondResponse = await _client.PostAsJsonAsync(
                "/api/incidents",
                request);

            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

            var content = await secondResponse.Content.ReadAsStringAsync();

            Assert.DoesNotContain("SELECT ", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("connection string", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("stack trace", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (createdId is not null)
            {
                using var scope = _factory.Services.CreateScope();

                var db = scope.ServiceProvider
                    .GetRequiredService<SecureLabDbContext>();

                var incident = await db.Incidents
                    .SingleOrDefaultAsync(i => i.Id == createdId.Value);

                if (incident is not null)
                {
                    db.Incidents.Remove(incident);
                    await db.SaveChangesAsync();
                }
            }
        }
    }

    private sealed record CreatedIncidentTestResponse(Guid Id);
}