using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SecureLab.Api.Tests;

public sealed class IncidentEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetList_ReturnsSeededIncidents()
    {
        var incidents = await _client.GetFromJsonAsync<List<IncidentListItemResponse>>(
            "/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task GetDetails_ForUnknownId_ReturnsProblemDetails404()
    {
        using var response = await _client.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        using var response = await _client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(document.RootElement.TryGetProperty("ownerUserId", out _));
        Assert.False(document.RootElement.TryGetProperty("email", out _));
        Assert.Equal("Аліса Коваль", document.RootElement.GetProperty("ownerDisplayName").GetString());
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
