using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);
var token = builder.Configuration["VALHEIM_CONTROL_TOKEN"];
if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Missing VALHEIM_CONTROL_TOKEN");
builder.WebHost.UseUrls("http://127.0.0.1:27666");
builder.Services.AddAuthentication("BridgeToken").AddScheme<AuthenticationSchemeOptions, BridgeTokenHandler>("BridgeToken", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<PluginState>();
builder.Services.AddHostedService<PluginIpcService>();
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", (PluginState state) => Results.Ok(state.Health())).RequireAuthorization();
app.MapGet("/capabilities", (PluginState state) => Results.Ok(new { connected = state.OnlinePlayersAvailable, supported = state.OnlinePlayersAvailable ? new[] { "OnlinePlayers" } : Array.Empty<string>(), checkedAtUtc = DateTime.UtcNow, reason = state.OnlinePlayersAvailable ? "Fresh read-only player snapshot" : "Plugin unavailable, collector not ready, or snapshot stale" })).RequireAuthorization();
app.MapGet("/players", (PluginState state) => state.OnlinePlayersAvailable ? Results.Ok(state.Players) : Results.Problem("Player snapshot unavailable or stale", statusCode: 503)).RequireAuthorization();
app.Run();

public sealed record BridgeHealth(string Service, string Status, DateTime CheckedAtUtc);
public sealed record BridgeCapabilities(bool Connected, string[] Supported, DateTime CheckedAtUtc, string? Reason);
public sealed record BridgePlayer(string ConnectionId, string DisplayName, string? PlatformId, DateTime? ConnectedAtUtc, bool IsConnected);

public sealed class BridgeTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;
    public BridgeTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration) : base(options, logger, encoder) => _configuration = configuration;
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = _configuration["VALHEIM_CONTROL_TOKEN"];
        var header = Request.Headers.Authorization.ToString();
        var supplied = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : null;
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied) || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(supplied)))
            return Task.FromResult(AuthenticateResult.Fail("Invalid bridge authentication"));
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "gamepanel")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
