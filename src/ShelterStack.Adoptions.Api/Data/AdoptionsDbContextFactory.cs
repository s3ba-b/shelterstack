using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ShelterStack.Adoptions.Api.Tenancy;

namespace ShelterStack.Adoptions.Api.Data;

/// <summary>
/// Lets `dotnet ef migrations add` construct the context without Aspire/DI or a
/// live database — only used by EF Core tooling, never at runtime.
/// </summary>
public sealed class AdoptionsDbContextFactory : IDesignTimeDbContextFactory<AdoptionsDbContext>
{
    public AdoptionsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AdoptionsDbContext>().UseNpgsql(
            "Host=localhost;Database=adoptionsdb;Username=design-time"
        );

        return new AdoptionsDbContext(optionsBuilder.Options, new StaticTenantContext(Guid.Empty));
    }
}
