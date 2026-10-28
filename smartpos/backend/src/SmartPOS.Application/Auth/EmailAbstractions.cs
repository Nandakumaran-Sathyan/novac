namespace SmartPOS.Application.Auth;

/// <summary>Sends an email. Implemented by SMTP, or by a logger when no mail server is configured (development).</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

public enum EmailDomainStatus
{
    /// <summary>The domain publishes mail servers (or at least an address to deliver to).</summary>
    CanReceiveMail,
    /// <summary>The domain does not exist, or exists but cannot receive mail.</summary>
    CannotReceiveMail,
    /// <summary>The lookup failed (offline, timeout). Not the user's fault, so it must not block them.</summary>
    Unknown
}

/// <summary>Checks that the domain part of an email address can receive mail.</summary>
public interface IEmailAddressChecker
{
    Task<EmailDomainStatus> CheckDomainAsync(string domain, CancellationToken cancellationToken);
}

/// <summary>
/// Throwaway-mailbox domains that we refuse at sign-up. A short built-in list of the best-known ones;
/// it catches casual abuse, not a determined attacker (a DNS check alone cannot, because these domains
/// do have mail servers). Extend it, or replace it with a maintained list, if abuse becomes a problem.
/// </summary>
public static class DisposableEmailDomains
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailinator.com", "guerrillamail.com", "guerrillamail.net", "guerrillamail.org", "sharklasers.com", "grr.la",
        "10minutemail.com", "10minutemail.net", "temp-mail.org", "tempmail.com", "tempmail.net", "temp-mail.io",
        "yopmail.com", "yopmail.net", "throwawaymail.com", "trashmail.com", "trashmail.net", "getnada.com",
        "dispostable.com", "maildrop.cc", "fakeinbox.com", "mintemail.com", "mohmal.com", "emailondeck.com",
        "burnermail.io", "spamgourmet.com", "mailnesia.com", "tempinbox.com", "discard.email", "moakt.com"
    };

    public static bool IsDisposable(string domain) => Known.Contains(domain.Trim());
}
