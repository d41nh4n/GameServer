using System.Net;
using Microsoft.Extensions.Options;

namespace GamePanel.Api.Middleware;

/// <summary>
/// (Tuỳ chọn) Chặn request không đến từ subnet Tailscale (100.64.0.0/10).
/// Bật khi cấu hình "Tailscale:Require" = true, mặc định false để dev local vẫn dùng được.
/// </summary>
public class TailscaleMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TailscaleSettings _settings;
    private readonly IPNetwork? _network;
    private readonly ILogger<TailscaleMiddleware> _logger;

    public TailscaleMiddleware(RequestDelegate next, IOptions<TailscaleSettings> options, ILogger<TailscaleMiddleware> logger)
    {
        _next = next;
        _settings = options.Value;
        _network = IPNetwork.TryParse(_settings.Subnet, out var n) ? n : null;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_settings.Require && _network is IPNetwork net)
        {
            var ip = context.Connection.RemoteIpAddress;
            if (ip is null || !net.Contains(ip.MapToIPv6()))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                _logger.LogWarning("Tailscale request rejected {HttpMethod} {RequestPath} {StatusCode}", context.Request.Method, context.Request.Path.Value ?? "", context.Response.StatusCode);
                await context.Response.WriteAsync("Forbidden: request must come from Tailscale subnet.");
                return;
            }
        }
        await _next(context);
    }
}

/// <summary>IP + mask đơn giản để kiểm tra IP có nằm trong subnet không (hỗ trợ IPv4/IPv6-mapped).</summary>
public readonly record struct IPNetwork(IPAddress BaseAddress, int PrefixLength)
{
    public static bool TryParse(string input, out IPNetwork network)
    {
        network = default;
        var parts = input.Split('/');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var addr) ||
            !int.TryParse(parts[1], out var prefix))
            return false;

        var bytes = addr.GetAddressBytes();
        var bitLen = bytes.Length * 8;
        if (prefix < 0 || prefix > bitLen) return false;

        // Che mask: zero các bit thấp phía sau prefix
        for (var i = 0; i < bytes.Length; i++)
        {
            var bitsLeft = bitLen - i * 8;
            var cleanBits = Math.Min(prefix - i * 8, 8);
            if (cleanBits <= 0)
                bytes[i] = 0;
            else if (cleanBits < 8)
                bytes[i] = (byte)(bytes[i] & (0xFF << (8 - cleanBits)));
        }
        network = new IPNetwork(new IPAddress(bytes), prefix);
        return true;
    }

    public bool Contains(IPAddress ip)
    {
        var ipBytes = ip.MapToIPv4().GetAddressBytes();
        var baseBytes = BaseAddress.MapToIPv4().GetAddressBytes();
        if (ipBytes.Length != baseBytes.Length) return false;

        var fullBytes = PrefixLength / 8;
        var remainingBits = PrefixLength % 8;
        for (var i = 0; i < fullBytes; i++)
            if (ipBytes[i] != baseBytes[i])
                return false;

        if (remainingBits > 0)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((ipBytes[fullBytes] & mask) != (baseBytes[fullBytes] & mask))
                return false;
        }
        return true;
    }
}