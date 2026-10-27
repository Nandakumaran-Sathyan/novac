using System.Net;
using System.Net.Http.Json;
using SmartPOS.Application.Auth;
using SmartPOS.Core.Enums;

namespace SmartPOS.IntegrationTests;

public class AuthAndAuthorizationTests(SmartPosApiFactory factory) : IClassFixture<SmartPosApiFactory>
{
    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_returns_token_and_user()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "  ADMIN@test.local ", password = SmartPosApiFactory.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(SmartPosApiFactory.Json);
        Assert.False(string.IsNullOrEmpty(login!.AccessToken));
        Assert.Equal(UserRole.Admin, login.User.Role);
        Assert.True(login.ExpiresAt > DateTime.UtcNow);
    }

    [Theory]
    [InlineData(SmartPosApiFactory.AdminEmail, "wrong-password1")]
    [InlineData("nobody@test.local", "whatever123")]
    public async Task Bad_credentials_return_401_with_same_message(string email, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Invalid email or password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/users")]
    [InlineData("POST", "/api/users")]
    [InlineData("GET", "/api/reports/daily-sales")]
    [InlineData("POST", "/api/inventory/adjustments")]
    [InlineData("POST", "/api/products")]
    public async Task Cashier_cannot_use_admin_endpoints(string method, string path)
    {
        var cashier = await factory.CashierClientAsync();
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new { });

        var response = await cashier.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_cashier_who_can_then_log_in()
    {
        var admin = await factory.AdminClientAsync();
        var create = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "New Cashier", email = "new.cashier@test.local", password = "NewCashier123", role = "Cashier"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Again", email = "NEW.cashier@test.local", password = "NewCashier123", role = "Cashier"
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await factory.CreateClientAsAsync("new.cashier@test.local", "NewCashier123");
    }
}
