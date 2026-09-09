using System.Net;
using System.Threading.RateLimiting;
using GamePanel.Api;

/// <summary>
/// Testy hardening CR-00B: partitionowanie per-IP logina + throttling (6. próba odrzucona).
/// Nie dotyka realnego runtime / DB / endpoint — czysta konstrukcja polityki i limitera.
/// </summary>
public class LoginRateLimitPolicyTests
{
    private static IPAddress? Parsed(string? s) =>
        s == null ? null : (IPAddress.TryParse(s, out var a) ? a : null);

    [Fact]
    public void DifferentIps_ProduceDifferentPartitionKeys()
    {
        var a = LoginRateLimitPolicy.PartitionKeyFromIp(Parsed("10.0.0.1"));
        var b = LoginRateLimitPolicy.PartitionKeyFromIp(Parsed("10.0.0.2"));

        Assert.NotEqual(a, b);
        Assert.Equal("10.0.0.1", a);
        Assert.Equal("10.0.0.2", b);
    }

    [Fact]
    public void NullIp_FallsBackToUnknownKey()
    {
        Assert.Equal("unknown-ip", LoginRateLimitPolicy.PartitionKeyFromIp(null));
    }

    [Fact]
    public void SameIp_ProducesSameKey()
    {
        var k1 = LoginRateLimitPolicy.PartitionKeyFromIp(Parsed("192.168.1.7"));
        var k2 = LoginRateLimitPolicy.PartitionKeyFromIp(Parsed("192.168.1.7"));
        Assert.Equal(k1, k2);
    }

    [Fact]
    public void PartitionKey_IsStable_NotCultureSensitive()
    {
        // Klucz to IP (nie username) — deterministyczna reprezentacja adresu IPv6.
        var k1 = LoginRateLimitPolicy.PartitionKeyFromIp(Parsed("2001:db8::1"));
        var k2 = LoginRateLimitPolicy.PartitionKeyFromIp(Parsed("2001:db8::1"));
        Assert.NotNull(k1);
        Assert.Equal(k1, k2);
    }

    [Fact]
    public async Task SixthAttempt_IsRejected_WhileFirstFiveSucceed()
    {
        // Budujemy surowy limiter fixed-window zgodnie z policy (5/min), bez HttpContext.
        var partition = RateLimitPartition.GetFixedWindowLimiter("10.9.8.7", opts =>
        {
            var o = new FixedWindowRateLimiterOptions();
            o.PermitLimit = LoginRateLimitPolicy.PermitLimit;
            o.Window = LoginRateLimitPolicy.Window;
            o.AutoReplenishment = true;
            return o;
        });
        var limiter = partition.Factory(partition.PartitionKey);

        int acquired = 0;
        int rejected = 0;
        for (var i = 0; i < 6; i++)
        {
            var lease = await TryAcquire(limiter);
            if (lease) acquired++; else rejected++;
        }

        Assert.Equal(5, acquired);
        Assert.Equal(1, rejected);
    }

    [Fact]
    public void Ips_ArePartitioned_Independently()
    {
        // Dwa różne klucze IP → dwa niezależne liczniki (per-IP separation).
        var p1 = LoginRateLimitPolicy.PartitionKeyFromIp(Parsed("198.51.100.1"));
        var p2 = LoginRateLimitPolicy.PartitionKeyFromIp(Parsed("198.51.100.2"));

        var l1 = RateLimitPartition.GetFixedWindowLimiter(p1, opts => { var o = new FixedWindowRateLimiterOptions(); o.PermitLimit = LoginRateLimitPolicy.PermitLimit; o.Window = LoginRateLimitPolicy.Window; o.AutoReplenishment = true; return o; });
        var l2 = RateLimitPartition.GetFixedWindowLimiter(p2, opts => { var o = new FixedWindowRateLimiterOptions(); o.PermitLimit = LoginRateLimitPolicy.PermitLimit; o.Window = LoginRateLimitPolicy.Window; o.AutoReplenishment = true; return o; });
        var limiter1 = l1.Factory(l1.PartitionKey);
        var limiter2 = l2.Factory(l2.PartitionKey);

        // Wyczerpujemy IP1 (6 prób) — IP2 musi nadal mieć pełne 5 wolnych.
        for (var i = 0; i < LoginRateLimitPolicy.PermitLimit; i++)
        {
            var lease = limiter1.AttemptAcquire(1);
            Assert.True(lease != null && lease.IsAcquired, "IP1 dozwolony do limitu");
            lease.Dispose();
        }
        Assert.False(TryAcquireSync(limiter1), "IP1 poza limitem");

        // IP2 nienaruszony: pierwsze 5 wciąż dozwolone
        for (var i = 0; i < LoginRateLimitPolicy.PermitLimit; i++)
        {
            var lease = limiter2.AttemptAcquire(1);
            Assert.True(lease != null && lease.IsAcquired, "IP2 nie powinien być dotknięty przez IP1");
            lease.Dispose();
        }
    }

    private static bool TryAcquireSync(RateLimiter limiter)
    {
        var lease = limiter.AttemptAcquire(1);
        var ok = lease != null && lease.IsAcquired;
        if (lease != null) lease.Dispose();
        return ok;
    }

    private static async Task<bool> TryAcquire(RateLimiter limiter)
    {
        try
        {
            var lease = limiter.AttemptAcquire(1);
            var ok = lease != null && lease.IsAcquired;
            if (lease != null) lease.Dispose();
            return ok;
        }
        catch
        {
            return false;
        }
    }
}