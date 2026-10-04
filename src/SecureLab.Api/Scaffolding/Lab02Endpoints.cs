using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            var order = sortBy switch
            {
                null or "" or "createdAtUtc" => "created_at_utc DESC",
                "severity" => "severity", "status" => "status", _ => sortBy
            };
            var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "")
                + "%' OR description ILIKE '%" + (q ?? "") + "%' ORDER BY " + order + " LIMIT 50";
            var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
        });
        app.MapPost("/api/incidents", async (
    CreateIncidentRequest request,
    SecureLabDbContext db,
    CancellationToken ct) =>
{
    var now = DateTimeOffset.UtcNow;
    var errors = new Dictionary<string, string[]>();

    // title
    if (string.IsNullOrWhiteSpace(request.Title))
    {
        errors["title"] = ["Title є обов'язковим."];
    }
    else if (request.Title.Length > 160)
    {
        errors["title"] = ["Title не може перевищувати 160 символів."];
    }

    // description
    if (string.IsNullOrWhiteSpace(request.Description))
    {
        errors["description"] = ["Description є обов'язковим."];
    }
    else if (request.Description.Length > 4000)
    {
        errors["description"] = ["Description не може перевищувати 4000 символів."];
    }

    // severity
    var severityIsValid =
        Enum.TryParse<IncidentSeverity>(
            request.Severity,
            ignoreCase: true,
            out var severity)
        && Enum.IsDefined(severity);

    if (!severityIsValid)
    {
        errors["severity"] =
            ["Допустимі значення: Low, Medium, High, Critical."];
    }

    // occurredAtUtc
    if (request.OccurredAtUtc is null)
    {
        errors["occurredAtUtc"] =
            ["OccurredAtUtc є обов'язковим."];
    }
    else if (request.OccurredAtUtc.Value > now.AddMinutes(5))
    {
        errors["occurredAtUtc"] =
            ["OccurredAtUtc не може бути більш ніж на 5 хвилин у майбутньому."];
    }

    // Нормалізація текстових значень
    var title = request.Title?.Trim() ?? "";
    var description = request.Description?.Trim() ?? "";

    if (errors.Count == 0 &&
        (severity == IncidentSeverity.High ||
         severity == IncidentSeverity.Critical) &&
        description.Length < 40)
    {
        errors["description"] =
            ["Для High або Critical опис має містити щонайменше 40 символів."];
    }

    // До БД не звертаємося, якщо DTO некоректний
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var duplicateExists = await db.Incidents
        .AsNoTracking()
        .AnyAsync(i =>
            i.Title == title &&
            i.Status != IncidentStatus.Closed,
            ct);

    if (duplicateExists)
    {
        return Results.Conflict(new
        {
            title = "Інцидент з таким заголовком уже існує.",
            status = 409
        });
    }

    var occurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime();

    var incident = new Incident
    {
        Id = Guid.NewGuid(),
        OwnerUserId = DbSeeder.AliceId,
        Title = title,
        Description = description,
        Severity = severity,
        Status = IncidentStatus.New,
        OccurredAtUtc = occurredAtUtc,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    db.Incidents.Add(incident);
    await db.SaveChangesAsync(ct);

    var response = new CreatedIncidentResponse(
        incident.Id,
        incident.Title,
        incident.Severity.ToString(),
        incident.Status.ToString(),
        incident.OccurredAtUtc,
        incident.CreatedAtUtc,
        incident.UpdatedAtUtc);

    return Results.Created(
        $"/api/incidents/{incident.Id}",
        response);
});
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id,
    string Title,
    string Severity,
    string Status,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
