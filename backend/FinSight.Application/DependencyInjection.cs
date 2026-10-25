using FinSight.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FinSight.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Scoped = one instance per HTTP request, matching the DbContext's lifetime.
        services.AddScoped<LoanApplicationService>();
        services.AddScoped<AssessmentService>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
