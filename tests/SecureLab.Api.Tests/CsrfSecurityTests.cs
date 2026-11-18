using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

/// <summary>
/// REG-CSRF-01: missing/invalid antiforgery tokens are rejected without
/// modifying protected state; a real valid token permits the intended action.
/// </summary>
public sealed class CsrfSecurityTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set SeedUsers__Password outside Git.");

    private static readonly HttpStatusCode BadRequest = HttpStatusCode.BadRequest;

    [Fact]
    public async Task REG_CSRF_01_Logout_MissingInvalidAndValidToken()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword);

        using var before = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var missing = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(BadRequest, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);

        using var afterMissing = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, afterMissing.StatusCode);

        // Issue the real antiforgery cookie, but intentionally send a false header.
        _ = await client.GetCsrfTokenAsync();
        using var wrongRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        wrongRequest.Headers.Add("X-SecureLab-CSRF", "not-a-valid-token");
        using var wrong = await client.SendAsync(wrongRequest);
        Assert.Equal(BadRequest, wrong.StatusCode);
        Assert.Equal("application/problem+json", wrong.Content.Headers.ContentType?.MediaType);

        using var afterWrong = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, afterWrong.StatusCode);

        using var valid = await client.PostWithCsrfAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);

        using var afterValid = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterValid.StatusCode);
    }

    [Fact]
    public async Task REG_CSRF_02_Create_MissingInvalidAndValidToken_PreservesDatabase()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var title = $"REG-CSRF-01 create {Guid.NewGuid():N}";
        var body = new
        {
            title,
            description = "Antiforgery integration regression test",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        Guid? createdId = null;
        try
        {
            using var missing = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(BadRequest, missing.StatusCode);
            Assert.False(await db.Incidents.AsNoTracking().AnyAsync(i => i.Title == title));

            _ = await client.GetCsrfTokenAsync();
            using var wrongRequest = new HttpRequestMessage(HttpMethod.Post, "/api/incidents")
            {
                Content = JsonContent.Create(body)
            };
            wrongRequest.Headers.Add("X-SecureLab-CSRF", "not-a-valid-token");
            using var wrong = await client.SendAsync(wrongRequest);
            Assert.Equal(BadRequest, wrong.StatusCode);
            Assert.False(await db.Incidents.AsNoTracking().AnyAsync(i => i.Title == title));

            using var valid = await client.PostAsJsonWithCsrfAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
            using var json = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
            createdId = json.RootElement.GetProperty("id").GetGuid();

            Assert.True(await db.Incidents.AsNoTracking().AnyAsync(i => i.Id == createdId));
        }
        finally
        {
            if (createdId.HasValue)
            {
                db.ChangeTracker.Clear();
                await db.Incidents.Where(i => i.Id == createdId.Value).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task REG_CSRF_03_UpdateDelete_MissingInvalidAndValidToken_PreservesDatabase()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var title = $"REG-CSRF-01 state {Guid.NewGuid():N}";
        var createBody = new
        {
            title,
            description = "unchanged",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        using var created = await client.PostAsJsonWithCsrfAsync("/api/incidents", createBody);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        var url = $"/api/incidents/{id}";
        var updateBody = new { title = title + " updated", description = "changed" };

        try
        {
            using var missingPut = await client.PutAsJsonAsync(url, updateBody);
            Assert.Equal(BadRequest, missingPut.StatusCode);
            Assert.Equal(title, await db.Incidents.AsNoTracking()
                .Where(i => i.Id == id).Select(i => i.Title).SingleAsync());

            using var wrongPutRequest = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = JsonContent.Create(updateBody)
            };
            wrongPutRequest.Headers.Add("X-SecureLab-CSRF", "not-a-valid-token");
            using var wrongPut = await client.SendAsync(wrongPutRequest);
            Assert.Equal(BadRequest, wrongPut.StatusCode);
            Assert.Equal(title, await db.Incidents.AsNoTracking()
                .Where(i => i.Id == id).Select(i => i.Title).SingleAsync());

            using var validPut = await client.PutAsJsonWithCsrfAsync(url, updateBody);
            Assert.Equal(HttpStatusCode.NoContent, validPut.StatusCode);
            Assert.Equal(updateBody.title, await db.Incidents.AsNoTracking()
                .Where(i => i.Id == id).Select(i => i.Title).SingleAsync());

            using var missingDelete = await client.DeleteAsync(url);
            Assert.Equal(BadRequest, missingDelete.StatusCode);
            Assert.True(await db.Incidents.AsNoTracking().AnyAsync(i => i.Id == id));

            using var wrongDeleteRequest = new HttpRequestMessage(HttpMethod.Delete, url);
            wrongDeleteRequest.Headers.Add("X-SecureLab-CSRF", "not-a-valid-token");
            using var wrongDelete = await client.SendAsync(wrongDeleteRequest);
            Assert.Equal(BadRequest, wrongDelete.StatusCode);
            Assert.True(await db.Incidents.AsNoTracking().AnyAsync(i => i.Id == id));

            using var validDelete = await client.DeleteWithCsrfAsync(url);
            Assert.Equal(HttpStatusCode.NoContent, validDelete.StatusCode);
            Assert.False(await db.Incidents.AsNoTracking().AnyAsync(i => i.Id == id));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Incidents.Where(i => i.Id == id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task REG_CSRF_04_Triage_MissingInvalidAndValidToken_PreservesStatusHistory()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Incidents.Add(new Incident
        {
            Id = id,
            OwnerUserId = DbSeeder.AliceId,
            Title = $"REG-CSRF-01 triage {id:N}",
            Description = "Temporary incident for antiforgery test",
            Severity = IncidentSeverity.Low,
            Status = IncidentStatus.New,
            OccurredAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        try
        {
            using var client = await factory.LoginAsync("morgan", SeedPassword);
            var url = $"/api/incidents/{id}/triage";

            using var missing = await client.PostAsync(url, null);
            Assert.Equal(BadRequest, missing.StatusCode);
            Assert.Equal(IncidentStatus.New, await db.Incidents.AsNoTracking()
                .Where(i => i.Id == id).Select(i => i.Status).SingleAsync());
            Assert.False(await db.IncidentStatusHistory.AsNoTracking()
                .AnyAsync(h => h.IncidentId == id));

            _ = await client.GetCsrfTokenAsync();
            using var wrongRequest = new HttpRequestMessage(HttpMethod.Post, url);
            wrongRequest.Headers.Add("X-SecureLab-CSRF", "not-a-valid-token");
            using var wrong = await client.SendAsync(wrongRequest);
            Assert.Equal(BadRequest, wrong.StatusCode);
            Assert.Equal(IncidentStatus.New, await db.Incidents.AsNoTracking()
                .Where(i => i.Id == id).Select(i => i.Status).SingleAsync());
            Assert.False(await db.IncidentStatusHistory.AsNoTracking()
                .AnyAsync(h => h.IncidentId == id));

            using var valid = await client.PostWithCsrfAsync(url);
            Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
            Assert.Equal(IncidentStatus.Triaged, await db.Incidents.AsNoTracking()
                .Where(i => i.Id == id).Select(i => i.Status).SingleAsync());
            Assert.True(await db.IncidentStatusHistory.AsNoTracking()
                .AnyAsync(h => h.IncidentId == id &&
                    h.OldStatus == IncidentStatus.New &&
                    h.NewStatus == IncidentStatus.Triaged &&
                    h.ChangedByUserId == DbSeeder.MorganId));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Incidents.Where(i => i.Id == id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task REG_CSRF_05_AnonymousCannotObtainToken()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/auth/csrf-token");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
