using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace GamePanel.Api;

/// <summary>
/// Rate-limit policy dla POST /api/auth/login, partitionowana per client IP.
/// Każdy RemoteIpAddress dostaje WŁASNY FixedWindowRateLimiter (5 dozwolonych
/// prób / 1 minuta). Gdy RemoteIpAddress jest null (np. nietypowy proxy), używa
/// wspólnej fallback partition "unknown-ip", żeby żaden request nie przeszedł
/// bez limitu. Odrzucenie obsługuje middleware (RejectionStatusCode=429).
/// </summary>
public sealed class LoginRateLimitPolicy : IRateLimiterPolicy<string>
{
    public const int PermitLimit = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Klucz partition z surowego adresu IP. null → fallback "unknown-ip".</summary>
    public static string PartitionKeyFromIp(IPAddress? ip) =>
        ip == null ? "unknown-ip" : ip.ToString();

    /// <summary>Klucz partition z adresu IP requestu. null → fallback "unknown-ip".</summary>
    public static string PartitionKey(HttpContext context)
    {
        var ip = context?.Connection?.RemoteIpAddress;
        return PartitionKeyFromIp(ip);
    }

    public System.Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected =>
        (rej, ct) => ValueTask.CompletedTask;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            (key) =>
            {
                var o = new FixedWindowRateLimiterOptions();
                o.PermitLimit = PermitLimit;
                o.Window = Window;
                o.AutoReplenishment = true;
                return o;
            });
}