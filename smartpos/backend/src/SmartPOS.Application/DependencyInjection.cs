using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        return services;
    }
}
