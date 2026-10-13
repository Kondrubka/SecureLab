
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data.Entities;

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

    [Fact]
    public async Task A03_ForeignHeader_DoesNotChangeAuthenticatedUser()
    {
        using var client = await factory.LoginAsync(
            "alice", StudyPassword);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/me");

        request.Headers.Add(
            "X-Demo-UserId", DbSeeder.BobId.ToString());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            DbSeeder.AliceId,
            document.RootElement.GetProperty("userId").GetGuid());
    }


    // A-04: власник інциденту визначається сервером.
    [Fact]
    public async Task A04_CreateIncident_UsesAuthenticatedOwner()
    {
        // Входимо як Bob.
        using var client = await factory.LoginAsync(
            "bob", StudyPassword);

        var title = $"Auth A04 {Guid.NewGuid():N}";

        // Створюємо інцидент без ownerUserId.
        using var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                title,
                description = "Перевірка автоматичного призначення власника.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var incidentId = document.RootElement
            .GetProperty("id").GetGuid();

        // Перевіряємо фактичного власника у базі даних.
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents
            .SingleAsync(i => i.Id == incidentId);

        try
        {
            Assert.Equal(DbSeeder.BobId, incident.OwnerUserId);
            Assert.NotEqual(DbSeeder.AliceId, incident.OwnerUserId);
        }
        finally
        {
            // Видаляємо створений тестовий запис.
            db.Incidents.Remove(incident);
            await db.SaveChangesAsync();
        }
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


    // A-06: після logout користувач втрачає доступ.
    [Fact]
    public async Task A06_Logout_ProtectedEndpointReturns401()
    {
        // Входимо як Alice.
        using var client = await factory.LoginAsync(
            "alice", StudyPassword);

        // Перевіряємо доступ перед виходом.
        using var before = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Виконуємо logout.
        using var logout = await client.PostAsync(
            "/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Перевіряємо доступ після виходу.
        using var after = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Null(after.Headers.Location);
    }


    // A-07: невдалі входи не розкривають існування акаунта.
    [Fact]
    public async Task A07_LoginFailures_HaveSameExternalResponse()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        var invalidPassword = Guid.NewGuid().ToString("N");

        // Існуючий користувач із неправильним паролем.
        using var knownResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "alice",
                password = invalidPassword
            });

        // Неіснуючий користувач із неправильним паролем.
        using var unknownResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = $"unknown_{Guid.NewGuid():N}",
                password = invalidPassword
            });

        // Перевіряємо однакові HTTP-статуси.
        Assert.Equal(HttpStatusCode.Unauthorized,
            knownResponse.StatusCode);

        Assert.Equal(knownResponse.StatusCode,
            unknownResponse.StatusCode);

        // Перевіряємо однаковий тип відповіді.
        Assert.Equal(
            knownResponse.Content.Headers.ContentType?.MediaType,
            unknownResponse.Content.Headers.ContentType?.MediaType);

        Assert.Equal(
            "application/problem+json",
            knownResponse.Content.Headers.ContentType?.MediaType);

        // Порівнюємо JSON без діагностичного traceId.
        using var knownJson = JsonDocument.Parse(
            await knownResponse.Content.ReadAsStringAsync());

        using var unknownJson = JsonDocument.Parse(
            await unknownResponse.Content.ReadAsStringAsync());

        var knownFields = knownJson.RootElement
            .EnumerateObject()
            .Where(p => p.Name != "traceId")
            .ToDictionary(p => p.Name, p => p.Value.GetRawText());

        var unknownFields = unknownJson.RootElement
            .EnumerateObject()
            .Where(p => p.Name != "traceId")
            .ToDictionary(p => p.Name, p => p.Value.GetRawText());

        Assert.Equal(knownFields.Count, unknownFields.Count);

        foreach (var field in knownFields)
        {
            Assert.True(
                unknownFields.TryGetValue(field.Key, out var value));

            Assert.Equal(field.Value, value);
        }
    }


    // Добрий рівень: повторна реєстрація повертає 409.
    [Fact]
    public async Task DuplicateRegistration_Returns409Conflict()
    {
        using var client = factory.CreateClient();

        var userName = $"reg{Guid.NewGuid():N}";

        var registration = new
        {
            userName,
            email = $"{userName}@example.test",
            displayName = "Тестовий користувач",
            password = StudyPassword
        };

        try
        {
            // Перша реєстрація повинна бути успішною.
            using var first = await client.PostAsJsonAsync(
                "/api/auth/register", registration);

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            // Повторна реєстрація з такими самими даними.
            using var second = await client.PostAsJsonAsync(
                "/api/auth/register", registration);

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

            // Перевіряємо безпечну відповідь.
            using var json = JsonDocument.Parse(
                await second.Content.ReadAsStringAsync());

            Assert.Equal(
                "Registration failed",
                json.RootElement.GetProperty("message").GetString());

            Assert.Single(json.RootElement.EnumerateObject());
        }
        finally
        {
            // Видаляємо створений навчальний акаунт.
            using var scope = factory.Services.CreateScope();

            var users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var user = await users.FindByNameAsync(userName);

            if (user is not null)
            {
                var deletion = await users.DeleteAsync(user);
                Assert.True(deletion.Succeeded);
            }
        }
    }


    // Добрий рівень: перевірка атрибутів authentication cookie.
    [Fact]
    public async Task CookieFlags_HttpsLogin_HasSecureAttributes()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "alice",
                password = StudyPassword
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Перевіряємо наявність Set-Cookie.
        Assert.True(response.Headers.TryGetValues(
            "Set-Cookie", out var headers));

        const string cookieName = ".AspNetCore.Identity.Application=";

        var cookies = headers!
            .Where(value => value.StartsWith(
                cookieName, StringComparison.Ordinal))
            .ToArray();

        // Не виводимо значення cookie у повідомлення тесту.
        Assert.Single(cookies);

        // Відкидаємо частину з назвою та значенням cookie.
        var attributes = cookies[0]
            .Split(';')
            .Skip(1)
            .Select(value => value.Trim().ToLowerInvariant())
            .ToArray();

        Assert.True(attributes.Contains("httponly"),
            "HttpOnly attribute is missing.");

        Assert.True(attributes.Contains("secure"),
            "Secure attribute is missing.");

        Assert.True(attributes.Contains("samesite=lax"),
            "SameSite=Lax attribute is missing.");
    }

}
