using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class IncidentEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string SeedPassword =>
    Environment.GetEnvironmentVariable("SeedUsers__Password")
    ?? throw new InvalidOperationException(
        "SeedUsers__Password is not configured.");


    [Fact]
    public async Task GetList_ReturnsSeededIncidents()
    {
        using var client = await factory.LoginAsync(
            "morgan", SeedPassword);

        var incidents = await client.GetFromJsonAsync<
            List<IncidentListItemResponse>>("/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents,
            incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(incidents,
            incident => incident.Id == DbSeeder.BobIncidentId);
    }



    [Fact]
    public async Task GetDetails_ForUnknownId_ReturnsProblemDetails404()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword);

        using var response = await client.GetAsync(
            $"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }



    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword);

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.TryGetProperty("ownerUserId", out _));
        Assert.False(document.RootElement.TryGetProperty("email", out _));

        Assert.Equal(
            "Аліса Коваль",
            document.RootElement.GetProperty("ownerDisplayName").GetString());
    }


    [Fact]
    public async Task ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        var script = await _client.GetStringAsync("/app.js");

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
    }


    [Fact]
    public async Task GetSeveritySummary_WithoutStatus_ReturnsAllSeverityGroups()
    {
        await AssertSeveritySummaryMatchesDatabaseAsync(
            "/api/incidents/severity-summary", null);
    }

    [Fact]
    public async Task GetSeveritySummary_WithNewStatus_ReturnsOnlyMatchingIncidents()
    {
        await AssertSeveritySummaryMatchesDatabaseAsync(
            "/api/incidents/severity-summary?status=New", "New");
    }

    [Fact]
    public async Task GetSeveritySummary_WithUnknownStatus_ReturnsBadRequest()
    {
        using var response = await _client.GetAsync(
            "/api/incidents/severity-summary?status=WrongStatus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSeveritySummary_WithStatusWithoutIncidents_ReturnsEmptyList()
    {
        var summaries = await _client.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary?status=Resolved");

        Assert.NotNull(summaries);
        Assert.Equal(
    summaries.Count,
    summaries.Select(item => item.Severity).Distinct().Count());
        Assert.Empty(summaries);
    }


    [Fact]
    public async Task A02_OwnerCanReadOwnIncident()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword);

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            DbSeeder.AliceIncidentId,
            document.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A03_NonOwnerCannotReadForeignIncident()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword);

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Боб Мельник", body);
        Assert.DoesNotContain("Невідома спроба входу", body);
    }

    [Fact]
    public async Task A03R_AnalystCanReadForeignIncident()
    {
        using var client = await factory.LoginAsync("morgan", SeedPassword);

        using var response = await client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            DbSeeder.AliceIncidentId,
            document.RootElement.GetProperty("id").GetGuid());
    }


    [Fact]
    public async Task A05_ReporterSeesOnlyOwnIncidents()
    {
        using var client = await factory.LoginAsync(
            "alice", SeedPassword);

        var incidents = await client.GetFromJsonAsync<
            List<IncidentListItemResponse>>("/api/incidents");

        Assert.NotNull(incidents);

        Assert.Contains(incidents,
            incident => incident.Id == DbSeeder.AliceIncidentId);

        Assert.DoesNotContain(incidents,
            incident => incident.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task A01_AnonymousCannotReadIncidentList()
    {
        using var response = await _client.GetAsync(
            "/api/incidents");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A04_NonOwnerCannotMutateForeignIncident(
        string method)
    {
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var before = await db.Incidents
            .AsNoTracking()
            .Where(i => i.Id == DbSeeder.BobIncidentId)
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.Description,
                i.OwnerUserId,
                i.Severity,
                i.Status,
                i.UpdatedAtUtc
            })
            .SingleAsync();

        using var client = await factory.LoginAsync(
            "alice", SeedPassword);

        var url = $"/api/incidents/{DbSeeder.BobIncidentId}";

        using var response = method == "PUT"
            ? await client.PutAsJsonAsync(url, new
            {
                title = "Unauthorized update",
                description = "Modified by Alice"
            })
            : await client.DeleteAsync(url);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);

        var after = await db.Incidents
            .AsNoTracking()
            .Where(i => i.Id == DbSeeder.BobIncidentId)
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.Description,
                i.OwnerUserId,
                i.Severity,
                i.Status,
                i.UpdatedAtUtc
            })
            .SingleAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A08a_OverpostingCannotChangeProtectedFields()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var id = DbSeeder.AliceIncidentId;

        var before = await db.Incidents.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new
            {
                i.Title,
                i.Description,
                i.OwnerUserId,
                i.Status,
                i.Severity,
                i.UpdatedAtUtc
            })
            .SingleAsync();

        using var client = await factory.LoginAsync(
            "alice", SeedPassword);

        var newTitle = "A08a test " + Guid.NewGuid().ToString("N");

        try
        {
            using var response = await client.PutAsJsonAsync(
                $"/api/incidents/{id}",
                new
                {
                    title = newTitle,
                    description = "Authorized content update",
                    ownerUserId = Guid.NewGuid(),
                    role = "Administrator",
                    status = "Closed",
                    severity = "High"
                });

            Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode);

            var after = await db.Incidents.AsNoTracking()
                .Where(i => i.Id == id)
                .SingleAsync();

            Assert.Equal(newTitle, after.Title);
            Assert.Equal(
                "Authorized content update",
                after.Description);

            Assert.Equal(before.OwnerUserId, after.OwnerUserId);
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.Severity, after.Severity);

            using var freshClient = await factory.LoginAsync(
                "alice", SeedPassword);

            using var foreignResponse = await freshClient.GetAsync(
                $"/api/incidents/{DbSeeder.BobIncidentId}");

            Assert.Equal(
                HttpStatusCode.Forbidden,
                foreignResponse.StatusCode);
        }
        finally
        {
            db.ChangeTracker.Clear();

            var incident = await db.Incidents
                .SingleAsync(i => i.Id == id);

            incident.Title = before.Title;
            incident.Description = before.Description;
            incident.UpdatedAtUtc = before.UpdatedAtUtc;

            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A06_ReporterCannotPerformTriage()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var id = await CreateTriageTestIncidentAsync(db);

        try
        {
            using var client = await factory.LoginAsync(
                "alice", SeedPassword);

            using var response = await client.PostAsync(
                $"/api/incidents/{id}/triage", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var status = await db.Incidents.AsNoTracking()
                .Where(i => i.Id == id)
                .Select(i => i.Status)
                .SingleAsync();

            Assert.Equal(IncidentStatus.New, status);

            Assert.False(await db.IncidentStatusHistory
                .AnyAsync(h => h.IncidentId == id));
        }
        finally
        {
            await db.Incidents
                .Where(i => i.Id == id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task A07_AnalystCanTriageNewIncident()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var id = await CreateTriageTestIncidentAsync(db);

        try
        {
            using var client = await factory.LoginAsync(
                "morgan", SeedPassword);

            using var response = await client.PostAsync(
                $"/api/incidents/{id}/triage", null);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            var status = await db.Incidents.AsNoTracking()
                .Where(i => i.Id == id)
                .Select(i => i.Status)
                .SingleAsync();

            Assert.Equal(IncidentStatus.Triaged, status);

            var history = await db.IncidentStatusHistory
                .AsNoTracking()
                .SingleAsync(h => h.IncidentId == id);

            Assert.Equal(IncidentStatus.New, history.OldStatus);
            Assert.Equal(IncidentStatus.Triaged, history.NewStatus);
            Assert.Equal(DbSeeder.MorganId, history.ChangedByUserId);
        }
        finally
        {
            await db.Incidents
                .Where(i => i.Id == id)
                .ExecuteDeleteAsync();
        }
    }

    private static async Task<Guid> CreateTriageTestIncidentAsync(
        SecureLabDbContext db)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.Incidents.Add(new Incident
        {
            Id = id,
            OwnerUserId = DbSeeder.AliceId,
            Title = $"Lab04 triage test {id:N}",
            Description = "Temporary authorization test incident",
            Severity = IncidentSeverity.Low,
            Status = IncidentStatus.New,
            OccurredAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return id;
    }

    [Fact]
    public async Task A12_ChildCommentAndInternalVisibilityAreProtected()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var internalId = Guid.NewGuid();

        db.IncidentComments.Add(new IncidentComment
        {
            Id = internalId,
            IncidentId = DbSeeder.AliceIncidentId,
            AuthorUserId = DbSeeder.MorganId,
            Text = "A12 private analyst note",
            IsInternal = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        try
        {
            using var alice = await factory.LoginAsync(
                "alice", SeedPassword);

            using var morgan = await factory.LoginAsync(
                "morgan", SeedPassword);

            using var anonymous = factory.CreateClient();

            var internalUrl =
                $"/api/incident-comments/{internalId}";

            // Анонімний користувач не має доступу.
            using var anonymousResponse =
                await anonymous.GetAsync(internalUrl);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                anonymousResponse.StatusCode);

            // Reporter не бачить internal comment
            // навіть власного інциденту.
            using var aliceInternal =
                await alice.GetAsync(internalUrl);

            Assert.Equal(
                HttpStatusCode.Forbidden,
                aliceInternal.StatusCode);

            // Alice не може читати коментар Bob.
            using var aliceForeign = await alice.GetAsync(
                "/api/incident-comments/" +
                "30000000-0000-0000-0000-000000000002");

            Assert.Equal(
                HttpStatusCode.Forbidden,
                aliceForeign.StatusCode);

            // Alice може читати дозволений public comment.
            using var alicePublic = await alice.GetAsync(
                "/api/incident-comments/" +
                "30000000-0000-0000-0000-000000000001");

            Assert.Equal(
                HttpStatusCode.OK,
                alicePublic.StatusCode);

            // Analyst може читати internal comment.
            using var analystInternal =
                await morgan.GetAsync(internalUrl);

            Assert.Equal(
                HttpStatusCode.OK,
                analystInternal.StatusCode);

            var comment = await analystInternal.Content
                .ReadFromJsonAsync<IncidentCommentResponse>();

            Assert.NotNull(comment);
            Assert.Equal(internalId, comment.Id);

            // Reporter не бачить internal у details.
            var aliceDetails = await alice.GetFromJsonAsync<
                IncidentDetailsResponse>(
                $"/api/incidents/{DbSeeder.AliceIncidentId}");

            Assert.NotNull(aliceDetails);

            Assert.DoesNotContain(
                aliceDetails.Comments,
                c => c.Id == internalId);

            // Analyst бачить internal у details.
            var analystDetails = await morgan.GetFromJsonAsync<
                IncidentDetailsResponse>(
                $"/api/incidents/{DbSeeder.AliceIncidentId}");

            Assert.NotNull(analystDetails);

            Assert.Contains(
                analystDetails.Comments,
                c => c.Id == internalId);
        }
        finally
        {
            db.ChangeTracker.Clear();

            await db.IncidentComments
                .Where(c => c.Id == internalId)
                .ExecuteDeleteAsync();
        }
    }

    private async Task AssertSeveritySummaryMatchesDatabaseAsync(
    string endpoint, string? status)
    {
        var summaries = await _client.GetFromJsonAsync<
            List<IncidentSeveritySummaryResponse>>(endpoint);

        Assert.NotNull(summaries);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var incidents = await db.Incidents.AsNoTracking()
            .Select(i => new { i.Severity, i.Status })
            .ToListAsync();

        var expected = incidents
            .Where(i => status is null || i.Status.ToString() == status)
            .GroupBy(i => i.Severity.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(expected.Count, summaries.Count);

        foreach (var summary in summaries)
        {
            Assert.True(expected.TryGetValue(
                summary.Severity, out var count));

            Assert.Equal(count, summary.Count);
        }
    }
}
