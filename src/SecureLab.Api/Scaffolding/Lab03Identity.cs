using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Security.Claims;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (
    RegistrationInput input,
    UserManager<ApplicationUser> users) =>
{
    if (string.IsNullOrWhiteSpace(input.UserName) ||
        string.IsNullOrWhiteSpace(input.Email) ||
        string.IsNullOrWhiteSpace(input.DisplayName) ||
        string.IsNullOrEmpty(input.Password))
    {
        return Results.BadRequest(new
        {
            message = "Invalid registration data"
        });
    }

    var userName = input.UserName.Trim();
    var email = input.Email.Trim();
    var displayName = input.DisplayName.Trim();

    if (userName.Length > 256 ||
        email.Length > 256 ||
        displayName.Length > 120 ||
        !Regex.IsMatch(userName, @"^[A-Za-z0-9._@+-]+$") ||
        !new EmailAddressAttribute().IsValid(email))
    {
        return Results.BadRequest(new
        {
            message = "Invalid registration data"
        });
    }

    var user = new ApplicationUser
    {
        Id = Guid.NewGuid(),
        UserName = userName,
        Email = email,
        DisplayName = displayName
    };

    var result = await users.CreateAsync(user, input.Password);

    if (!result.Succeeded)
    {
        return Results.BadRequest(new
        {
            message = "Registration failed"
        });
    }

    var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);

    if (!role.Succeeded)
    {
        throw new InvalidOperationException(
            "Default role assignment failed.");
    }

    return Results.Created("/api/me",
        new RegistrationResponse(user.Id, userName, displayName));
});
        app.MapPost("/api/auth/login", async (LoginInput input, SignInManager<ApplicationUser> signIn) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
                return Results.Unauthorized();
            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });
        app.MapPost("/api/auth/logout", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        // Навмисно хибна навчальна довіра: ЛР 03 замінює заявлений id на verified principal.
        app.MapGet("/api/me", GetCurrentUser)
    .RequireAuthorization();
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }

    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        var userId = httpContext.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userId, out var verifiedUserId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var userName = httpContext.User
            .FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

        var role = httpContext.User
            .FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        return Results.Ok(new
        {
            UserId = verifiedUserId,
            UserName = userName,
            Role = role
        });
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegistrationResponse(
    Guid Id,
    string UserName,
    string DisplayName);
