using SmartPOS.API.Infrastructure;
using SmartPOS.Application;
using SmartPOS.Infrastructure;
using SmartPOS.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

// One-off maintenance command: `dotnet run --project src/SmartPOS.API -- seed-demo-history --email <email> [--days 90]`
if (args.Length > 0 && args[0] == "seed-demo-history")
{
    return await SeedDemoHistoryAsync(app, args);
}
if (args.Length > 0 && args[0] == "reset-password")
{
    return await ResetPasswordAsync(app, args);
}

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

app.Run();
return 0;

static async Task<int> SeedDemoHistoryAsync(WebApplication app, string[] args)
{
    if (!app.Environment.IsDevelopment())
    {
        Console.Error.WriteLine("seed-demo-history adds FABRICATED data and only runs in the Development environment.");
        return 1;
    }
    string? Option(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
    var email = Option("--email");
    if (string.IsNullOrWhiteSpace(email))
    {
        Console.Error.WriteLine("Usage: seed-demo-history --email <account email> [--days 90]");
        return 1;
    }
    var days = int.TryParse(Option("--days"), out var d) ? d : 90;
    try
    {
        using var scope = app.Services.CreateScope();
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<DemoHistoryCommand>().RunAsync(email, days, CancellationToken.None));
        return 0;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static async Task<int> ResetPasswordAsync(WebApplication app, string[] args)
{
    if (!app.Environment.IsDevelopment())
    {
        Console.Error.WriteLine("reset-password only runs in the Development environment.");
        return 1;
    }
    var email = args.SkipWhile(a => a != "--email").Skip(1).FirstOrDefault();
    if (string.IsNullOrWhiteSpace(email))
    {
        Console.Error.WriteLine("Usage: reset-password --email <account email>   (password from a hidden prompt, or the NEW_PASSWORD environment variable)");
        return 1;
    }
    var password = Environment.GetEnvironmentVariable("NEW_PASSWORD");
    if (string.IsNullOrEmpty(password))
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("No terminal to prompt on. Set the NEW_PASSWORD environment variable instead.");
            return 1;
        }
        password = ReadHidden("New password: ");
        if (password != ReadHidden("Repeat it:   "))
        {
            Console.Error.WriteLine("The two entries did not match; nothing was changed.");
            return 1;
        }
    }
    try
    {
        using var scope = app.Services.CreateScope();
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<ResetPasswordCommand>().RunAsync(email, password, CancellationToken.None));
        return 0;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static string ReadHidden(string prompt)
{
    Console.Write(prompt);
    var text = new System.Text.StringBuilder();
    for (var key = Console.ReadKey(intercept: true); key.Key != ConsoleKey.Enter; key = Console.ReadKey(intercept: true))
    {
        if (key.Key == ConsoleKey.Backspace && text.Length > 0)
            text.Length--;
        else if (!char.IsControl(key.KeyChar))
            text.Append(key.KeyChar);
    }
    Console.WriteLine();
    return text.ToString();
}

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
