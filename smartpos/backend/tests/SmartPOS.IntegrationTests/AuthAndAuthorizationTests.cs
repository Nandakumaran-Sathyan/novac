using System.Net;
using System.Net.Http.Json;
using SmartPOS.Application.Auth;

namespace SmartPOS.IntegrationTests;

public class AuthAndAuthorizationTests(SmartPosApiFactory factory) : IClassFixture<SmartPosApiFactory>
{
    private static object NewShop(string email, string password = "Registered123", string shopName = "Fresh Shop") =>
        new { fullName = "New Owner", email, password, shopName, shopAddress = "5 New Road" };

    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_returns_token_and_user_with_shop()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "  OWNER@test.local ", password = SmartPosApiFactory.UserPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(SmartPosApiFactory.Json);
        Assert.False(string.IsNullOrEmpty(login!.AccessToken));
        Assert.Equal("Test Shop", login.User.ShopName);
        Assert.True(login.ExpiresAt > DateTime.UtcNow);
    }

    [Theory]
    [InlineData(SmartPosApiFactory.UserEmail, "wrong-password1")]
    [InlineData("nobody@test.local", "whatever123")]
    public async Task Bad_credentials_return_401_with_same_message(string email, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Invalid email or password", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/products")]
    [InlineData("/api/customers")]
    [InlineData("/api/reports/daily-sales")]
    [InlineData("/api/auth/me")]
    public async Task Protected_endpoint_without_token_returns_401(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registering_creates_a_shop_and_signs_the_user_in()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("Fresh.Owner@test.local"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(SmartPosApiFactory.Json))!;
        Assert.Equal("Fresh Shop", login.User.ShopName);
        Assert.Equal("fresh.owner@test.local", login.User.Email); // stored lower-case

        // The token works, and the new shop starts empty and separate from the others.
        var client = await factory.CreateClientAsAsync("fresh.owner@test.local", "Registered123");
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me", SmartPosApiFactory.Json);
        Assert.Equal(login.User.ShopId, me!.ShopId);
        Assert.NotEqual(factory.ShopId, me.ShopId);
        var products = await client.GetStringAsync("/api/products");
        Assert.Contains("\"totalCount\":0", products);
    }

    [Fact]
    public async Task Registering_with_an_existing_email_is_rejected_whatever_the_case()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("OWNER@test.local"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("nodigitshere")]
    [InlineData("12345678")]
    public async Task Registering_with_a_weak_password_is_rejected(string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("weak@test.local", password));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registering_without_a_shop_name_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { fullName = "No Shop", email = "noshop@test.local", password = "Registered123", shopName = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_endpoints_no_longer_exist()
    {
        var user = await factory.UserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/users")).StatusCode);
    }
}
