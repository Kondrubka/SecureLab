using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using System.Security.Claims;
using SecureLab.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents");

        group.MapGet("/", GetListAsync)
    .WithName("GetIncidents")
    .Produces<IReadOnlyList<IncidentListItemResponse>>()
    .ProducesValidationProblem()
    .RequireAuthorization();

        group.MapGet("/{id:guid}", GetDetailsAsync)
    .WithName("GetIncidentDetails")
    .Produces<IncidentDetailsResponse>()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .RequireAuthorization();

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
                .WithName("GetIncidentSeveritySummary")
                .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>();

        group.MapPut("/{id:guid}", UpdateIncidentAsync)
            .WithName("UpdateIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .RequireAuthorization();

        group.MapDelete("/{id:guid}", DeleteIncidentAsync)
                .WithName("DeleteIncident")
                .Produces(StatusCodes.Status204NoContent)
                .RequireAuthorization();
        group.MapPost("/{id:guid}/triage", TriageIncidentAsync)
.WithName("TriageIncident")
.Produces(StatusCodes.Status204NoContent)
.RequireAuthorization();
        endpoints.MapGet(
            "/api/incident-comments/{id:guid}",
            GetCommentAsync)
            .WithName("GetIncidentComment")
            .Produces<IncidentCommentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        return endpoints;
    }
    private static async Task<IResult> UpdateIncidentAsync(
    Guid id,
    UpdateIncidentRequest request,
    HttpContext httpContext,
    SecureLabDbContext dbContext,
    CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!IncidentAccess.CanUpdate(
            httpContext.User, incident.OwnerUserId))
        {
            return Results.Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Title)
            || request.Title.Length > 160
            || string.IsNullOrWhiteSpace(request.Description)
            || request.Description.Length > 4000)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["content"] = ["Некоректна назва або опис інциденту."]
                });
        }

        incident.Title = request.Title.Trim();
        incident.Description = request.Description.Trim();
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteIncidentAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!IncidentAccess.CanDelete(
            httpContext.User,
            incident.OwnerUserId,
            incident.Status))
        {
            return Results.Forbid();
        }

        dbContext.Incidents.Remove(incident);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> TriageIncidentAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var userIdValue = httpContext.User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Results.Challenge();
        }

        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(
                i => i.Id == id, cancellationToken);

        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!IncidentAccess.CanTriage(
    httpContext.User, incident.Status))
        {
            var logger = loggerFactory.CreateLogger(
                "SecureLab.Authorization");

            var traceId = Activity.Current?.Id
                ?? httpContext.TraceIdentifier;

            logger.LogWarning(
                "authorization.denied SubjectId={SubjectId} Action={Action} ResourceId={ResourceId} Result={Result} OccurredAtUtc={OccurredAtUtc} TraceId={TraceId}",
                userId,
                "incident.triage",
                id,
                "denied",
                DateTimeOffset.UtcNow,
                traceId);

            return Results.Forbid();
        }

        var oldStatus = incident.Status;
        var now = DateTimeOffset.UtcNow;

        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = now;

        dbContext.IncidentStatusHistory.Add(
            new IncidentStatusHistory
            {
                Id = Guid.NewGuid(),
                IncidentId = incident.Id,
                ChangedByUserId = userId,
                OldStatus = oldStatus,
                NewStatus = IncidentStatus.Triaged,
                Note = "Triage performed by analyst.",
                CreatedAtUtc = now
            });

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }


    private static async Task<IResult> GetListAsync(
    string? status,
    HttpContext httpContext,
    IncidentQueries queries,
    CancellationToken cancellationToken)
    {
        var userIdValue = httpContext.User.FindFirstValue(
    ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Results.Challenge();
        }
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        var canReadAll = httpContext.User.IsInRole(
    DbSeeder.AnalystRole);

        return Results.Ok(await queries.GetListAsync(
            parsedStatus,
            userId,
            canReadAll,
            cancellationToken));
    }


    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var userIdValue = httpContext.User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Results.Challenge();
        }

        var ownerId = await queries.GetOwnerIdAsync(
            id, cancellationToken);

        if (ownerId is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: StatusCodes.Status404NotFound);
        }

        var isOwner = ownerId.Value == userId;
        var isAnalyst = httpContext.User.IsInRole(
            DbSeeder.AnalystRole);

        if (!isOwner && !isAnalyst)
        {
            return Results.Forbid();
        }

        var incident = await queries.GetDetailsAsync(
    id,
    cancellationToken,
    includeInternal: IncidentAccess.CanReadAll(httpContext.User));

        return incident is null
            ? Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(incident);
    }


    private static async Task<IResult> GetCommentAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var commentInfo = await dbContext.IncidentComments
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                OwnerUserId = c.Incident.OwnerUserId,
                c.IsInternal
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (commentInfo is null)
        {
            return Results.Problem(
                title: "Коментар не знайдено",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!IncidentAccess.CanRead(
            httpContext.User,
            commentInfo.OwnerUserId))
        {
            return Results.Forbid();
        }

        if (commentInfo.IsInternal &&
            !IncidentAccess.CanReadAll(httpContext.User))
        {
            return Results.Forbid();
        }

        var comment = await dbContext.IncidentComments
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new IncidentCommentResponse(
                c.Id,
                c.Author.DisplayName,
                c.Text,
                c.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return comment is null
            ? Results.NotFound()
            : Results.Ok(comment);
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
    string? status,
    IncidentQueries queries,
    CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;

        if (status is not null)
        {
            var allowedStatuses = new[]
            {
            IncidentStatus.New,
            IncidentStatus.Triaged,
            IncidentStatus.InProgress,
            IncidentStatus.Resolved,
            IncidentStatus.Closed
        };

            if (!Enum.TryParse<IncidentStatus>(
                    status,
                    ignoreCase: true,
                    out var candidate)
                || !allowedStatuses.Contains(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] =
                    [
                        "Допустимі значення: New, Triaged, InProgress, Resolved, Closed."
                    ]
                });
            }

            parsedStatus = candidate;
        }

        var summary = await queries.GetSeveritySummaryAsync(
            parsedStatus,
            cancellationToken);

        return Results.Ok(summary);
    }
}
