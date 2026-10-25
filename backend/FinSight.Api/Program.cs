using System.Text.Json.Serialization;
using FinSight.Api.Middleware;
using FinSight.Application;
using FinSight.Domain.Rules;
using FinSight.Infrastructure;
using FinSight.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ---------------------------------------------------------------
// The connection string contains a password, so it never lives in appsettings.json.
// Development: `dotnet user-secrets` (stored outside the repo). Elsewhere: the environment
// variable ConnectionStrings__FinSightDb. Fail fast with a helpful message if neither is set.
var connectionString = builder.Configuration.GetConnectionString("FinSightDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Connection string 'FinSightDb' is not configured. Set it with " +
        "`dotnet user-secrets set ConnectionStrings:FinSightDb \"<value>\" --project FinSight.Api` " +
        "or the environment variable ConnectionStrings__FinSightDb. See README.md.");

builder.Services.AddOptions<LendingProductRules>()
    .Bind(builder.Configuration.GetSection(LendingProductRules.SectionName))
    .Validate(r => r.MinLoanAmount > 0 && r.MinLoanAmount <= r.MaxLoanAmount, "LoanAmount limits are invalid.")
    .Validate(r => r.MinTenureMonths > 0 && r.MinTenureMonths <= r.MaxTenureMonths, "Tenure limits are invalid.")
    .Validate(r => r.MinAnnualInterestRate >= 0 && r.MinAnnualInterestRate <= r.MaxAnnualInterestRate, "Interest rate limits are invalid.")
    .Validate(r => r.MaxDebtToIncomeRatio is > 0 and <= 1, "MaxDebtToIncomeRatio must be between 0 and 1.")
    .ValidateOnStart(); // a bad config stops the app at startup instead of on the first request

// ---- Services --------------------------------------------------------------------
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddApplication();
builder.Services.AddSingleton(sp => new LoanRuleEvaluator(sp.GetRequiredService<IOptions<LendingProductRules>>().Value));

builder.Services.AddControllers(o =>
        // DTOs mark required fields explicitly with [Required]; this stops a malformed body
        // from also producing a confusing "The request field is required." error.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Hide System.Text.Json's raw messages, which expose internal .NET type names.
        o.AllowInputFormatterExceptionMessages = false;
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<FinSightDbContext>("sqlserver");

const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .WithMethods("GET", "POST")));

// ---- HTTP pipeline (order matters) ---------------------------------------------------
var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(); // gives bare 404/405 responses a ProblemDetails body

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "FinSight API v1");
        o.RoutePrefix = "swagger"; // /swagger
    });
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").RequireCors(FrontendCorsPolicy);

app.Run();

// Exposes the implicit Program class to WebApplicationFactory-based integration tests.
public partial class Program;
