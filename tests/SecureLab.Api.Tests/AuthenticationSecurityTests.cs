
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class AuthenticationSecurityTests(
    SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException(
            "Set local SeedUsers__Password outside Git.");

    // A-01: анонімний користувач не отримує профіль.
    [Fact]
    public async Task A01_AnonymousMe_Returns401()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    // A-02: після входу профіль відповідає cookie Alice.
    [Fact]
    public async Task A02_LoginAlice_MeReturnsVerifiedProfile()
    {
        using var client = await factory.LoginAsync(
            "alice", StudyPassword);

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var profile = document.RootElement;

        Assert.Equal(
            DbSeeder.AliceId,
            profile.GetProperty("userId").GetGuid());

        Assert.Equal(
            "alice",
            profile.GetProperty("userName").GetString());

        Assert.Equal(
            "Reporter",
            profile.GetProperty("role").GetString());

        // Перевіряємо відсутність чутливих полів.
        Assert.Equal(3, profile.EnumerateObject().Count());
        Assert.False(profile.TryGetProperty("passwordHash", out _));
        Assert.False(profile.TryGetProperty("securityStamp", out _));
    }

    // A-05: підроблений ownerUserId не змінює власника.
    [Fact]
    public async Task A05_ForgedOwnerId_DoesNotChangeVerifiedOwner()
    {
        // Входимо як Bob.
        using var client = await factory.LoginAsync(
            "bob", StudyPassword);

        var title = $"Auth A05 {Guid.NewGuid():N}";

        // Намагаємося підставити ідентифікатор Alice.
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/incidents")
        {
            Content = JsonContent.Create(new
            {
                title,
                description = "Навчальний тест перевірки власника інциденту.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                ownerUserId = DbSeeder.AliceId
            })
        };

        request.Headers.Add(
            "X-Demo-UserId", DbSeeder.AliceId.ToString());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var incidentId = document.RootElement
            .GetProperty("id").GetGuid();

        // Відповідь не розкриває внутрішнього власника.
        Assert.False(document.RootElement.TryGetProperty(
            "ownerUserId", out _));

        // Перевіряємо фактичний запис у PostgreSQL.
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents
            .SingleAsync(i => i.Id == incidentId);

        try
        {
            // Власником повинен бути Bob, а не Alice.
            Assert.Equal(DbSeeder.BobId, incident.OwnerUserId);
            Assert.NotEqual(DbSeeder.AliceId, incident.OwnerUserId);
        }
        finally
        {
            // Видаляємо створений тестовий інцидент.
            db.Incidents.Remove(incident);
            await db.SaveChangesAsync();
        }
    }
}
