using System.Security.Claims;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

public static class IncidentAccess
{
    public static bool IsOwner(
        ClaimsPrincipal user, Guid ownerId)
    {
        var value = user.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var userId)
            && userId == ownerId;
    }

    public static bool CanReadAll(ClaimsPrincipal user)
    {
        return user.IsInRole(DbSeeder.AnalystRole);
    }

    public static bool CanRead(
        ClaimsPrincipal user, Guid ownerId)
    {
        return IsOwner(user, ownerId)
            || CanReadAll(user);
    }

    public static bool CanUpdate(
        ClaimsPrincipal user, Guid ownerId)
    {
        return IsOwner(user, ownerId)
            && user.IsInRole(DbSeeder.ReporterRole);
    }

    public static bool CanDelete(
        ClaimsPrincipal user,
        Guid ownerId,
        IncidentStatus status)
    {
        return CanUpdate(user, ownerId)
            && status == IncidentStatus.New;
    }

    public static bool CanTriage(
        ClaimsPrincipal user,
        IncidentStatus status)
    {
        return user.IsInRole(DbSeeder.AnalystRole)
            && status == IncidentStatus.New;
    }
}
