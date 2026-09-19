using Dms.SharedKernel;

namespace Dms.Identity.Domain;

/// <summary>
/// A refresh-token session. Only the SHA-256 of the token is stored: the token itself has 256 bits
/// of entropy, so a fast hash is the right choice, and a database leak cannot yield usable tokens.
/// </summary>
public sealed class UserSession : AggregateRoot<SessionId>
{
    private UserSession()
    {
    }

    private UserSession(
        SessionId id,
        UserId userId,
        byte[] tokenHash,
        DateTimeOffset expiresAt,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CreatedAt = now;
    }

    public UserId UserId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionId? ReplacedBySessionId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public static UserSession Start(
        UserId userId,
        byte[] tokenHash,
        DateTimeOffset expiresAt,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now) =>
        new(SessionId.New(), userId, tokenHash, expiresAt, ipAddress, userAgent, now);

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, SessionId? replacedBy = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        ReplacedBySessionId = replacedBy;
    }
}
