using Dms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Dms.Infrastructure.Jobs;

public sealed class InfraDbContextFactory : IDesignTimeDbContextFactory<InfraDbContext>
{
    public InfraDbContext CreateDbContext(string[] args) =>
        new(DesignTimeSupport.CreateOptions<InfraDbContext>(InfraSchema.Name), DesignTimeSupport.CreateSession());
}
