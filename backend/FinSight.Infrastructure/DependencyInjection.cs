using FinSight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinSight.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FinSightDbContext>(options => options
            .UseSqlServer(connectionString)
            .UseSeeding((context, _) => SeedData.Seed(context))
            .UseAsyncSeeding((context, _, ct) => SeedData.SeedAsync(context, ct)));

        return services;
    }
}
