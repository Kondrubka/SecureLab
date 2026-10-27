using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(SecureLabDbContext dbContext, ILogger<IncidentQueries> logger)
{
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        var query = dbContext.Incidents.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }


    public Task<Guid?> GetOwnerIdAsync(
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == incidentId)
            .Select(incident => (Guid?)incident.OwnerUserId)
            .SingleOrDefaultAsync(cancellationToken);
    }


    public Task<IncidentDetailsResponse?> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }


    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSeveritySummaryAsync(
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        var incidents = dbContext.Incidents
            .AsNoTracking();

        if (status.HasValue)
        {
            incidents = incidents.Where(incident => incident.Status == status.Value);
        }

        var summaries = await incidents
            .GroupBy(incident => incident.Severity)
            .Select(group => new
            {
                Severity = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var result = summaries
            .OrderBy(summary => summary.Severity)
            .Select(summary => new IncidentSeveritySummaryResponse(
                summary.Severity.ToString(),
                summary.Count))
            .ToList();

        logger.LogInformation(
            "Loaded incident severity summary with {GroupCount} groups for status {Status}",
            result.Count,
            status?.ToString() ?? "All");

        return result;
    }
}
