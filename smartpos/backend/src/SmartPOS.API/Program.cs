using SmartPOS.API.Infrastructure;
using SmartPOS.Application;
using SmartPOS.Infrastructure;
using SmartPOS.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(); // problem+json bodies for bare 401/403/404 responses

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocumentTitle = "SmartPOS API");
}
else
{
    app.UseHsts();
}

app.UseCors(ApiServiceCollectionExtensions.CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health", ApiServiceCollectionExtensions.HealthCheckOptions).AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);
}

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
