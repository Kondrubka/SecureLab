using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SecureLab.Api.Tests;

[CollectionDefinition("AuditTrailSerial", DisableParallelization = true)]
public sealed class AuditTrailSerialCollection;

[Collection("AuditTrailSerial")]
public sealed class AuditTrailTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException(
            "Set SeedUsers__Password outside Git.");

    [Fact]
    public async Task REG_AUDIT_02_Logout_LogsOnlySuccessfulAction()
    {
        using var client = await factory.LoginAsync(
            "alice", SeedPassword);

        var capture = new AuditCaptureProvider();

        factory.Services
            .GetRequiredService<ILoggerFactory>()
            .AddProvider(capture);

        using var rejected = await client.PostAsync(
            "/api/auth/logout", null);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            rejected.StatusCode);

        Assert.DoesNotContain(
            capture.Entries,
            entry => entry.Contains(
                "Action=auth.logout",
                StringComparison.Ordinal));

        using var accepted = await client.PostWithCsrfAsync(
            "/api/auth/logout");

        Assert.Equal(
            HttpStatusCode.NoContent,
            accepted.StatusCode);

        Assert.Contains(
            capture.Entries,
            entry =>
                entry.Contains(
                    "Action=auth.logout",
                    StringComparison.Ordinal) &&
                entry.Contains(
                    "Result=success",
                    StringComparison.Ordinal) &&
                entry.Contains(
                    "SubjectId=",
                    StringComparison.Ordinal) &&
                entry.Contains(
                    "TraceId=",
                    StringComparison.Ordinal));

        using var afterLogout = await client.GetAsync("/api/me");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            afterLogout.StatusCode);
    }

    [Fact]
    public async Task REG_AUDIT_03_CreateUpdateDelete_LogOnlySuccess()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword);
        var capture = new AuditCaptureProvider();

        factory.Services.GetRequiredService<ILoggerFactory>()
            .AddProvider(capture);

        var title = $"REG-AUDIT-03-{Guid.NewGuid():N}";
        var createBody = new
        {
            title,
            description = "Audit trail integration test",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        var id = Guid.Empty;

        try
        {
            using var rejectedCreate =
                await client.PostAsJsonAsync("/api/incidents", createBody);

            Assert.Equal(HttpStatusCode.BadRequest, rejectedCreate.StatusCode);
            Assert.DoesNotContain(capture.Entries,
                e => e.Contains("Action=incident.create"));

            using var created =
                await client.PostAsJsonWithCsrfAsync("/api/incidents", createBody);

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            using var document = JsonDocument.Parse(
                await created.Content.ReadAsStringAsync());

            id = document.RootElement.GetProperty("id").GetGuid();

            Assert.Contains(capture.Entries,
                e => e.Contains("Action=incident.create") &&
                     e.Contains("Result=success"));

            var url = $"/api/incidents/{id}";
            var updateBody = new
            {
                title = title + "-updated",
                description = "Audit trail updated"
            };

            using var rejectedUpdate =
                await client.PutAsJsonAsync(url, updateBody);

            Assert.Equal(HttpStatusCode.BadRequest, rejectedUpdate.StatusCode);
            Assert.DoesNotContain(capture.Entries,
                e => e.Contains("Action=incident.update"));

            using var updated =
                await client.PutAsJsonWithCsrfAsync(url, updateBody);

            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

            Assert.Contains(capture.Entries,
                e => e.Contains("Action=incident.update") &&
                     e.Contains($"ResourceId={id}") &&
                     e.Contains("Result=success"));

            using var rejectedDelete = await client.DeleteAsync(url);

            Assert.Equal(HttpStatusCode.BadRequest, rejectedDelete.StatusCode);
            Assert.DoesNotContain(capture.Entries,
                e => e.Contains("Action=incident.delete"));

            using var deleted = await client.DeleteWithCsrfAsync(url);

            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

            Assert.Contains(capture.Entries,
                e => e.Contains("Action=incident.delete") &&
                     e.Contains($"ResourceId={id}") &&
                     e.Contains("Result=success"));
        }
        finally
        {
            if (id != Guid.Empty)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider
                    .GetRequiredService<SecureLabDbContext>();

                await db.Incidents
                    .Where(i => i.Id == id)
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task REG_AUDIT_04_Triage_LogsOnlySuccess()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword);
        using var morgan = await factory.LoginAsync("morgan", SeedPassword);

        var capture = new AuditCaptureProvider();

        factory.Services.GetRequiredService<ILoggerFactory>()
            .AddProvider(capture);

        var createBody = new
        {
            title = $"REG-AUDIT-04-{Guid.NewGuid():N}",
            description = "Audit trail triage integration test",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        var id = Guid.Empty;

        try
        {
            using var created =
                await alice.PostAsJsonWithCsrfAsync("/api/incidents", createBody);

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            using var document = JsonDocument.Parse(
                await created.Content.ReadAsStringAsync());

            id = document.RootElement.GetProperty("id").GetGuid();
            var url = $"/api/incidents/{id}/triage";

            using var rejected = await morgan.PostAsync(url, null);

            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.DoesNotContain(capture.Entries,
                e => e.Contains("Action=incident.triage"));

            using var accepted = await morgan.PostWithCsrfAsync(url);

            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

            Assert.Contains(capture.Entries,
                e => e.Contains("Action=incident.triage") &&
                     e.Contains($"ResourceId={id}") &&
                     e.Contains("Result=success") &&
                     e.Contains("TraceId="));

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider
                .GetRequiredService<SecureLabDbContext>();

            Assert.True(await db.IncidentStatusHistory
                .AsNoTracking()
                .AnyAsync(h => h.IncidentId == id));
        }
        finally
        {
            if (id != Guid.Empty)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider
                    .GetRequiredService<SecureLabDbContext>();

                await db.IncidentStatusHistory
                    .Where(h => h.IncidentId == id)
                    .ExecuteDeleteAsync();

                await db.Incidents
                    .Where(i => i.Id == id)
                    .ExecuteDeleteAsync();
            }
        }
    }
    private sealed class AuditCaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
            => new AuditCaptureLogger(categoryName, Entries);

        public void Dispose() { }
    }

    private sealed class AuditCaptureLogger(
        string categoryName,
        ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (categoryName == "SecureLab.Audit" &&
                logLevel == LogLevel.Information)
            {
                entries.Enqueue(formatter(state, exception));
            }
        }
    }
}
