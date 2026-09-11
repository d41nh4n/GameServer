using System.Collections.Concurrent;
using GamePanel.Application.Interfaces;

namespace GamePanel.Infrastructure.Services;

public sealed class ValheimControlActionService
{
    private readonly ConcurrentDictionary<string, byte> _idempotency = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly IGameControlProtocol _protocol;

    public ValheimControlActionService(IGameControlProtocol protocol) => _protocol = protocol;

    public async Task<(int Status, ValheimActionResult Result)> ExecuteAsync(Guid serverId, ValheimCapability capability, ValheimControlRequest request, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            return (400, new(false, "INVALID_IDEMPOTENCY_KEY", "Idempotency-Key is required"));
        if (!_idempotency.TryAdd($"{serverId}:{idempotencyKey}", 0))
            return (409, new(false, "DUPLICATE_IDEMPOTENCY_KEY", "Action was already submitted"));
        var gate = _locks.GetOrAdd(serverId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var caps = await _protocol.GetCapabilitiesAsync(serverId, ct);
            if (!caps.Connected) return (503, new(false, "PLUGIN_UNAVAILABLE", caps.Reason));
            if (!caps.Supported.Contains(capability)) return (501, new(false, "CAPABILITY_UNSUPPORTED", $"Capability {capability} is not supported"));
            return (200, await _protocol.ExecuteAsync(serverId, capability, request, ct));
        }
        finally { gate.Release(); }
    }
}
