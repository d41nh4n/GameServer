namespace GamePanel.Domain.ValueObjects;

/// <summary>
/// Một JWT đã ký: claims sub=userId, role, iat (issued-at), exp (expiry).
/// </summary>
public sealed record JwtToken(string Value, DateTime ExpiresAt)
{
    public bool Expired(DateTime utcNow) => utcNow >= ExpiresAt;
}