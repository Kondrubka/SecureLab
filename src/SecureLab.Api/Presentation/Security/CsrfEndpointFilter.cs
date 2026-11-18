using Microsoft.AspNetCore.Antiforgery;

namespace SecureLab.Api.Presentation.Security;

public sealed class CsrfEndpointFilter : IEndpointFilter
{
    private readonly IAntiforgery _antiforgery;

    public CsrfEndpointFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                title: "Invalid antiforgery token",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }
}
