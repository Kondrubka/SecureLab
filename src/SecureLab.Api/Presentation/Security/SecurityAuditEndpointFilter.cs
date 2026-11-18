using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace SecureLab.Api.Presentation.Security;

public sealed class SecurityAuditEndpointFilter(
    ILoggerFactory loggerFactory) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var result = await next(context);

        if (result is IStatusCodeHttpResult statusResult &&
            statusResult.StatusCode is int statusCode &&
            statusCode >= 200 &&
            statusCode < 300)
        {
            var http = context.HttpContext;
            var path = http.Request.Path.Value ?? "";

            var action = (http.Request.Method, path) switch
            {
                ("POST", "/api/auth/logout") => "auth.logout",
                ("POST", "/api/incidents") => "incident.create",
                ("PUT", _) => "incident.update",
                ("DELETE", _) => "incident.delete",
                ("POST", var p) when p.EndsWith(
                    "/triage", StringComparison.OrdinalIgnoreCase)
                    => "incident.triage",
                _ => "unknown"
            };

            var subjectId = http.User.FindFirstValue(
                ClaimTypes.NameIdentifier) ?? "unknown";

            var resourceId = http.Request.RouteValues.TryGetValue(
                "id", out var id)
                ? id?.ToString() ?? "-"
                : "-";

            var logger = loggerFactory.CreateLogger(
                "SecureLab.Audit");

            logger.LogInformation(
                "audit.event SubjectId={SubjectId} Action={Action} ResourceId={ResourceId} Result={Result} OccurredAtUtc={OccurredAtUtc} TraceId={TraceId}",
                subjectId,
                action,
                resourceId,
                "success",
                DateTimeOffset.UtcNow,
                http.TraceIdentifier);
        }

        return result;
    }
}
