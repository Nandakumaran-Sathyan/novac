using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartPOS.Application.AI;
using SmartPOS.Application.AI.Tools;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Categories;
using SmartPOS.Application.Common;
using SmartPOS.Application.Customers;
using SmartPOS.Application.Dashboard;
using SmartPOS.Application.Inventory;
using SmartPOS.Application.Products;
using SmartPOS.Application.Reports;
using SmartPOS.Application.Sales;

namespace SmartPOS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StoreOptions>()
            .Bind(configuration.GetSection(StoreOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZoneId, out _), "Store:TimeZoneId is not a known time zone.")
            .ValidateOnStart();
        services.AddOptions<PosOptions>()
            .Bind(configuration.GetSection(PosOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LoyaltyOptions>()
            .Bind(configuration.GetSection(LoyaltyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<StoreCalendar>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IInsightsService, InsightsService>();

        services.AddOptions<AssistantOptions>()
            .Bind(configuration.GetSection(AssistantOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<AIConcurrencyGate>();
        services.AddScoped<IAIService, AIOrchestrationService>();
        services.AddScoped<IStoreAITools, StoreAITools>();
        services.AddScoped<StoreToolRegistry>();
        return services;
    }
}
