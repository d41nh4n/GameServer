using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamePanel.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace GamePanel.Infrastructure.GameServers;

/// <summary>HTTP client for the future loopback-only bridge. Disabled unless explicitly configured.</summary>
public sealed class ValheimControlProtocol : IGameControlProtocol
{
    private readonly HttpClient _client;
    private readonly bool _enabled;
    public ValheimControlProtocol(HttpClient client, IConfiguration configuration)
    {
        _client = client;
        _enabled = configuration.GetValue<bool>("GameServers:Valheim:ControlProtocol:Enabled");
        var token = configuration["GameServers:Valheim:ControlProtocol:Token"];
        if (_enabled && !string.IsNullOrWhiteSpace(token)) _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<ValheimCapabilities> GetCapabilitiesAsync(Guid serverId, CancellationToken ct = default)
    {
        if (!_enabled) return Unavailable("Valheim control plugin unavailable");
        try
        {
            using var response = await _client.GetAsync("/capabilities", ct);
            if (!response.IsSuccessStatusCode) return Unavailable(response.StatusCode == HttpStatusCode.Unauthorized ? "Bridge authentication failed" : "Bridge unavailable");
            var body = await response.Content.ReadFromJsonAsync<BridgeCapabilities>(cancellationToken: ct);
            if (body is null) return Unavailable("Invalid bridge response");
            var supported = body.Supported.Select(ParseCapability).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
            return new ValheimCapabilities(supported, body.Connected, body.CheckedAtUtc, body.Reason);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Unavailable("Bridge request timed out"); }
        catch (HttpRequestException) { return Unavailable("Valheim control plugin unavailable"); }
    }

    public async Task<IReadOnlyList<GamePanel.Application.Interfaces.ValheimOnlinePlayer>> GetPlayersAsync(Guid serverId, CancellationToken ct = default)
    {
        if (!_enabled) return [];
        try { return await _client.GetFromJsonAsync<List<GamePanel.Application.Interfaces.ValheimOnlinePlayer>>("/players", ct) ?? []; }
        catch { return []; }
    }

    public Task<ValheimActionResult> ExecuteAsync(Guid serverId, ValheimCapability capability, ValheimControlRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ValheimActionResult(false, "PLUGIN_UNAVAILABLE", "Live action endpoints are not enabled in CR-02B"));

    private static ValheimCapabilities Unavailable(string reason) => new(new HashSet<ValheimCapability>(), false, DateTime.UtcNow, reason);
    private static ValheimCapability? ParseCapability(string value) => Enum.TryParse<ValheimCapability>(value, true, out var parsed) ? parsed : null;
    private sealed record BridgeCapabilities(bool Connected, string[] Supported, DateTime CheckedAtUtc, string? Reason);
}
