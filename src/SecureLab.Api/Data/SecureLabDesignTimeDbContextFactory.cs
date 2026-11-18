using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SecureLab.Api.Data;

public sealed class SecureLabDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<SecureLabDbContext>
{
    public SecureLabDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__SecureLab");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__SecureLab before using EF Core tooling.");
        }

        var options = new DbContextOptionsBuilder<SecureLabDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SecureLabDbContext(options);
    }
}
