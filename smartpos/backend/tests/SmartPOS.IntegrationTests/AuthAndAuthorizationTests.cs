using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        // The token works, and once the email is verified the new shop starts empty and separate from the others.
        var client = await factory.CreateClientAsAsync("fresh.owner@test.local", "Registered123");
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me", SmartPosApiFactory.Json);
        Assert.Equal(login.User.ShopId, me!.ShopId);
        Assert.NotEqual(factory.ShopId, me.ShopId);
        await VerifyAsync(client, "fresh.owner@test.local");
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

    private static string CodeFrom((string To, string Subject, string Body) mail) =>
        Regex.Match(mail.Body, @"\b(\d{6})\b").Groups[1].Value;

    private (string To, string Subject, string Body) LastMailTo(string email) =>
        factory.Mail.Sent.Last(m => m.To == email);

    private async Task<HttpClient> RegisterAsync(string email)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop(email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await factory.CreateClientAsAsync(email, "Registered123");
    }

    private async Task VerifyAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/verify-email", new { code = CodeFrom(LastMailTo(email)) });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static Task<HttpResponseMessage> TryCodeAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/auth/verify-email", new { code });

    /// <summary>Edits the user's row directly, to stand in for time passing.</summary>
    private async Task UpdateUserAsync(string email, Action<SmartPOS.Core.Entities.User> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartPOS.Infrastructure.Persistence.SmartPosDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
        change(user);
        await db.SaveChangesAsync();
    }

    private async Task<SmartPOS.Core.Entities.User> UserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartPOS.Infrastructure.Persistence.SmartPosDbContext>();
        return await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Email == email);
    }

    [Fact]
    public async Task Registering_emails_a_six_digit_code_and_stores_only_a_keyed_hash()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", NewShop("verify.me@test.local"));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(SmartPosApiFactory.Json))!;
        Assert.False(login.User.EmailVerified);

        var code = CodeFrom(LastMailTo("verify.me@test.local"));
        Assert.Matches(@"^\d{6}$", code);
        var user = await UserAsync("verify.me@test.local");
        Assert.Equal(64, user.EmailVerificationCodeHash!.Length);
        Assert.DoesNotContain(code, user.EmailVerificationCodeHash);
        // Not a plain SHA-256 of the code either: that could be reversed by trying all million codes.
        Assert.NotEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code))),
            user.EmailVerificationCodeHash);
        Assert.InRange(user.EmailVerificationExpiresAt!.Value - DateTime.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task An_unverified_account_can_only_reach_its_profile_and_the_verification_endpoints()
    {
        var client = await RegisterAsync("blocked@test.local");

        foreach (var path in new[] { "/api/products", "/api/sales", "/api/customers", "/api/dashboard/summary", "/api/settings/pos", "/api/forecasting/status", "/api/ai/status" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("Email not verified", await response.Content.ReadAsStringAsync());
        }
        var sale = await client.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = 1, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Forbidden, sale.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        // The same token works as soon as the code is entered: the check reads the database on each request.
        await VerifyAsync(client, "blocked@test.local");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products")).StatusCode);
        Assert.True((await client.GetFromJsonAsync<UserDto>("/api/auth/me", SmartPosApiFactory.Json))!.EmailVerified);
    }

    [Fact]
    public async Task The_code_works_once()
    {
        var client = await RegisterAsync("once@test.local");
        var code = CodeFrom(LastMailTo("once@test.local"));

        Assert.Equal(HttpStatusCode.NoContent, (await TryCodeAsync(client, code)).StatusCode);
        var user = await UserAsync("once@test.local");
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.Null(user.EmailVerificationCodeHash);
        // A repeat (double click) is harmless.
        Assert.Equal(HttpStatusCode.NoContent, (await TryCodeAsync(client, code)).StatusCode);
    }

    [Fact]
    public async Task Five_tries_then_even_the_right_code_is_refused()
    {
        var client = await RegisterAsync("guesser@test.local");
        var code = CodeFrom(LastMailTo("guesser@test.local"));
        var wrong = code == "000000" ? "111111" : "000000";

        var first = await TryCodeAsync(client, wrong);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Contains("4 tries left", await first.Content.ReadAsStringAsync());
        for (var i = 0; i < 3; i++)
            await TryCodeAsync(client, wrong);
        Assert.Contains("last try", await (await TryCodeAsync(client, wrong)).Content.ReadAsStringAsync());

        var right = await TryCodeAsync(client, code);
        Assert.Equal(HttpStatusCode.BadRequest, right.StatusCode);
        Assert.Contains("Too many wrong codes", await right.Content.ReadAsStringAsync());
        Assert.Null((await UserAsync("guesser@test.local")).EmailVerifiedAt);
        Assert.Equal(5, (await UserAsync("guesser@test.local")).EmailVerificationAttempts);
    }

    [Fact]
    public async Task Parallel_guesses_cannot_get_past_five_tries()
    {
        var client = await RegisterAsync("parallel@test.local");
        var code = CodeFrom(LastMailTo("parallel@test.local"));
        var wrong = code == "000000" ? "111111" : "000000";

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => TryCodeAsync(client, wrong)));

        Assert.Equal(5, (await UserAsync("parallel@test.local")).EmailVerificationAttempts);
        Assert.Equal(HttpStatusCode.BadRequest, (await TryCodeAsync(client, code)).StatusCode);
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var client = await RegisterAsync("late@test.local");
        var code = CodeFrom(LastMailTo("late@test.local"));
        await UpdateUserAsync("late@test.local", u => u.EmailVerificationExpiresAt = DateTime.UtcNow.AddSeconds(-1));

        var response = await TryCodeAsync(client, code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expired", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("")]
    public async Task A_code_must_be_six_digits(string code)
    {
        var client = await RegisterAsync($"format{code.Length}{code}@test.local");
        Assert.Equal(HttpStatusCode.BadRequest, (await TryCodeAsync(client, code)).StatusCode);
        Assert.Equal(0, (await UserAsync($"format{code.Length}{code}@test.local")).EmailVerificationAttempts);
    }

    [Fact]
    public async Task One_users_code_cannot_verify_another()
    {
        await RegisterAsync("alice@test.local");
        var bob = await RegisterAsync("bob@test.local");
        var alices = CodeFrom(LastMailTo("alice@test.local"));
        if (alices == CodeFrom(LastMailTo("bob@test.local")))
            return; // one in a million: the same code by chance proves nothing either way

        Assert.Equal(HttpStatusCode.BadRequest, (await TryCodeAsync(bob, alices)).StatusCode);
        Assert.Null((await UserAsync("bob@test.local")).EmailVerifiedAt);
    }

    [Fact]
    public async Task Resending_waits_a_minute_then_replaces_the_code_and_resets_the_tries()
    {
        var client = await RegisterAsync("resend@test.local");
        var first = CodeFrom(LastMailTo("resend@test.local"));
        var wrong = first == "000000" ? "111111" : "000000";
        await TryCodeAsync(client, wrong);

        var tooSoon = await client.PostAsync("/api/auth/resend-verification", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        Assert.Contains("seconds", await tooSoon.Content.ReadAsStringAsync());

        await UpdateUserAsync("resend@test.local", u => u.EmailVerificationSentAt = DateTime.UtcNow.AddMinutes(-2));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/resend-verification", null)).StatusCode);
        var second = CodeFrom(LastMailTo("resend@test.local"));
        Assert.Equal(0, (await UserAsync("resend@test.local")).EmailVerificationAttempts);

        if (second != first)
            Assert.Equal(HttpStatusCode.BadRequest, (await TryCodeAsync(client, first)).StatusCode);
        await VerifyAsync(client, "resend@test.local");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/resend-verification", null)).StatusCode);
    }

    [Fact]
    public async Task Verifying_and_resending_need_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/auth/resend-verification", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TryCodeAsync(factory.CreateClient(), "123456")).StatusCode);
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

    // ---- AI (Phase 2: status only) ------------------------------------------------------------------------

    [Fact]
    public async Task AI_status_needs_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/ai/status")).StatusCode);
        var user = await factory.UserClientAsync();
        var status = await user.GetFromJsonAsync<SmartPOS.Application.AI.ChatModelStatus>("/api/ai/status", SmartPosApiFactory.Json);
        Assert.True(status!.Ready);
    }

    [Fact]
    public async Task Health_stays_up_but_reports_the_AI_as_degraded_when_the_model_is_down()
    {
        var original = factory.Model.Status;
        factory.Model.Status = new SmartPOS.Application.AI.ChatModelStatus(false, "fake-model", false, false, null, "Ollama is not running.");
        try
        {
            var response = await factory.CreateClient().GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // selling still works
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"Degraded\"", body);
            Assert.Contains("Ollama is not running.", body);
        }
        finally
        {
            factory.Model.Status = original;
        }
    }
}
