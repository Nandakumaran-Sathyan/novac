using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPOS.Application.Auth;

namespace SmartPOS.Infrastructure.Email;

/// <summary>Bound from "Email:Smtp". The password belongs in user-secrets or an environment variable, never in a file.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>Sender address. Falls back to <see cref="User"/>.</summary>
    public string From { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>Sends mail through any SMTP server (a free option: Gmail with an app password, Brevo/Mailjet free tiers, or a local Mailpit).</summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var from = string.IsNullOrWhiteSpace(o.From) ? o.User : o.From;
        using var message = new MailMessage(from, to, subject, body);
        using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.UseSsl, Timeout = 15_000 };
        if (!string.IsNullOrWhiteSpace(o.User))
            client.Credentials = new NetworkCredential(o.User, o.Password);
        await client.SendMailAsync(message, cancellationToken);
    }
}

/// <summary>
/// Development stand-in used when no SMTP server is configured and Email:LogMessages is on: writes the whole
/// email, including its verification link, to the log. The link is a credential, so this is never enabled by default.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogWarning("EMAIL (not sent, no SMTP server configured)\nTo: {To}\nSubject: {Subject}\n\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}

/// <summary>Used when nothing is configured: says so in the log, without printing the message (it contains a credential).</summary>
public sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogWarning("An email was not sent because no SMTP server is configured (Email:Smtp:Host).");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Free, keyless domain check using Google Public DNS over HTTPS (https://dns.google/resolve). Only the domain
/// part of the address leaves the server, never the mailbox name. A domain is deliverable if it has MX records,
/// or, as RFC 5321 allows, an A/AAAA record to fall back to. Any network failure gives <see cref="EmailDomainStatus.Unknown"/>.
/// </summary>
public sealed class DnsOverHttpsEmailAddressChecker(ILogger<DnsOverHttpsEmailAddressChecker> logger) : IEmailAddressChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    // Definitive answers are cached for an hour, so a popular domain (gmail.com) costs one lookup, not one per sign-up.
    // A failed lookup (Unknown) is never cached: the next sign-up tries again.
    private static readonly ConcurrentDictionary<string, (EmailDomainStatus Status, DateTime ExpiresAt)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    public async Task<EmailDomainStatus> CheckDomainAsync(string domain, CancellationToken cancellationToken)
    {
        if (Cache.TryGetValue(domain, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached.Status;

        var status = await LookUpAsync(domain, cancellationToken);
        if (status != EmailDomainStatus.Unknown)
            Cache[domain] = (status, DateTime.UtcNow + CacheFor);
        return status;
    }

    private async Task<EmailDomainStatus> LookUpAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            var mx = await QueryAsync(domain, "MX", cancellationToken);
            if (mx.Status == 3) return EmailDomainStatus.CannotReceiveMail;          // NXDOMAIN: no such domain
            if (mx.Status != 0) return EmailDomainStatus.Unknown;                     // SERVFAIL etc.
            if (mx.HasAnswers) return EmailDomainStatus.CanReceiveMail;

            var a = await QueryAsync(domain, "A", cancellationToken);                 // no MX: mail goes to the host itself
            return a.Status == 0 && a.HasAnswers ? EmailDomainStatus.CanReceiveMail
                 : a.Status is 0 or 3 ? EmailDomainStatus.CannotReceiveMail
                 : EmailDomainStatus.Unknown;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Mail-server lookup for {Domain} failed", domain);
            return EmailDomainStatus.Unknown;
        }
    }

    private static async Task<(int Status, bool HasAnswers)> QueryAsync(string domain, string type, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync($"https://dns.google/resolve?name={Uri.EscapeDataString(domain)}&type={type}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        return (root.GetProperty("Status").GetInt32(), root.TryGetProperty("Answer", out var answers) && answers.GetArrayLength() > 0);
    }
}
