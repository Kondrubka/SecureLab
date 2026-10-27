using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using System.Security.Claims;
using SecureLab.Api.Data;

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
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
    .WithName("GetIncidentDetails")
    .Produces<IncidentDetailsResponse>()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .RequireAuthorization();

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>();

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
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

        return Results.Ok(await queries.GetListAsync(parsedStatus, cancellationToken));
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
            id, cancellationToken);

        return incident is null
            ? Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(incident);
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
