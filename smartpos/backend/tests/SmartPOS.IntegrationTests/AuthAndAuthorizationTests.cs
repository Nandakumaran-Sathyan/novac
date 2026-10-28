using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
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

    // ---- email validation and verification ------------------------------------------------------------

    private static string TokenFrom((string To, string Subject, string Body) mail) =>
        Regex.Match(mail.Body, @"token=(\S+)").Groups[1].Value;

    private (string To, string Subject, string Body) LastMailTo(string email) =>
        factory.Mail.Sent.Last(m => m.To == email);

    [Fact]
    public async Task Registering_sends_a_verification_link_and_the_account_starts_unverified()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("verify.me@test.local"));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(SmartPosApiFactory.Json))!;
        Assert.False(login.User.EmailVerified);

        var mail = LastMailTo("verify.me@test.local");
        Assert.StartsWith("https://pos.test/verify-email?token=" + login.User.Id + ".", mail.Body.Split('\n').First(l => l.StartsWith("https://")));
    }

    [Fact]
    public async Task The_emailed_token_verifies_the_address_once_and_only_once()
    {
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("once@test.local"));
        var token = TokenFrom(LastMailTo("once@test.local"));

        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().PostAsJsonAsync("/api/auth/verify-email", new { token })).StatusCode);
        var client = await factory.CreateClientAsAsync("once@test.local", "Registered123");
        Assert.True((await client.GetFromJsonAsync<UserDto>("/api/auth/me", SmartPosApiFactory.Json))!.EmailVerified);

        // The same link cannot be used again.
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().PostAsJsonAsync("/api/auth/verify-email", new { token })).StatusCode);
    }

    [Theory]
    [InlineData("nonsense-token-that-is-long-enough")]
    [InlineData("1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("999999.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_wrong_token_does_not_verify_anything(string token)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_one_user_cannot_verify_another()
    {
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("alice@test.local"));
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("bob@test.local"));
        var alice = TokenFrom(LastMailTo("alice@test.local"));
        var bobId = alice.Split('.')[0] == TokenFrom(LastMailTo("bob@test.local")).Split('.')[0] ? "x" : TokenFrom(LastMailTo("bob@test.local")).Split('.')[0];

        // Bob's id with Alice's secret.
        var forged = bobId + "." + alice.Split('.')[1];
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().PostAsJsonAsync("/api/auth/verify-email", new { token = forged })).StatusCode);
    }

    [Fact]
    public async Task Resending_replaces_the_old_link_and_stops_once_verified()
    {
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("resend@test.local"));
        var first = TokenFrom(LastMailTo("resend@test.local"));
        var client = await factory.CreateClientAsAsync("resend@test.local", "Registered123");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/resend-verification", null)).StatusCode);
        var second = TokenFrom(LastMailTo("resend@test.local"));
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().PostAsJsonAsync("/api/auth/verify-email", new { token = first })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().PostAsJsonAsync("/api/auth/verify-email", new { token = second })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/resend-verification", null)).StatusCode);
    }

    [Fact]
    public async Task Resending_needs_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/auth/resend-verification", null)).StatusCode);
    }

    [Theory]
    [InlineData("someone@mailinator.com", "temporary")]
    [InlineData("someone@nomail.invalid", "doesn't appear to accept email")]
    public async Task Disposable_and_undeliverable_addresses_are_refused(string email, string expectedMessage)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop(email));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedMessage, await response.Content.ReadAsStringAsync());

        // ...and no account was created for it.
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "Registered123" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task A_failed_domain_lookup_does_not_block_sign_up()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("owner@unknowable.test"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
