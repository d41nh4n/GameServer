using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamePanel.Infrastructure.GameServers;

public sealed class PrivilegedBrokerClient
{
    private const string DefaultSocketPath = "/run/gamepanel/privileged.sock";
    private const int MaxFrameBytes = 65_536;
    private static readonly Regex DeploymentIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<string, string> Instances =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["valheim-main.service"] = "valheim-main",
            ["pzserver-game.service"] = "pz-main",
        };

    private readonly string _socketPath;

    public PrivilegedBrokerClient() : this(DefaultSocketPath)
    {
    }

    public PrivilegedBrokerClient(string socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
            throw new ArgumentException("Broker socket path is required.", nameof(socketPath));
        _socketPath = socketPath;
    }

    public Task<JsonElement> SendAsync(string unitName, string action, CancellationToken ct = default)
    {
        if (!Instances.TryGetValue(unitName, out var instance) ||
            action is not ("status" or "start" or "stop" or "restart"))
            throw new InvalidOperationException("Broker request is not allowlisted");

        return SendEnvelopeAsync(instance, action, "baseline", TimeSpan.FromMinutes(4), ct);
    }

    public Task<JsonElement> SendDeploymentAsync(
        string action,
        string deploymentId,
        CancellationToken ct = default)
    {
        if (action is not ("seal" or "deploy" or "rollback"))
            throw new ArgumentException("Deployment action is not allowlisted.", nameof(action));
        if (string.IsNullOrWhiteSpace(deploymentId) || !DeploymentIdPattern.IsMatch(deploymentId))
            throw new ArgumentException("Invalid deployment id.", nameof(deploymentId));

        var timeout = action == "seal" ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(5);
        return SendEnvelopeAsync("valheim-main", action, deploymentId, timeout, ct);
    }

    private async Task<JsonElement> SendEnvelopeAsync(
        string instance,
        string action,
        string deploymentId,
        TimeSpan requestTimeout,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(requestTimeout);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), timeout.Token);

        var requestId = Guid.NewGuid().ToString();
        var request = JsonSerializer.Serialize(new
        {
            requestVersion = 1,
            requestId,
            action,
            instanceId = instance,
            deploymentId,
        }) + "\n";
        var bytes = Encoding.UTF8.GetBytes(request);
        var sent = 0;
        while (sent < bytes.Length)
            sent += await socket.SendAsync(bytes.AsMemory(sent), SocketFlags.None, timeout.Token);

        var buffer = new byte[MaxFrameBytes];
        var received = 0;
        var newline = -1;
        while (received < buffer.Length)
        {
            var count = await socket.ReceiveAsync(
                buffer.AsMemory(received, buffer.Length - received),
                SocketFlags.None,
                timeout.Token);
            if (count == 0) break;
            var start = received;
            received += count;
            newline = Array.IndexOf(buffer, (byte)'\n', start, count);
            if (newline >= 0) break;
        }

        if (received == 0) throw new IOException("Privileged broker returned no response");
        if (newline < 0) throw new IOException("Privileged broker response was incomplete or oversized");
        if (newline != received - 1) throw new IOException("Privileged broker returned trailing response bytes");

        using var doc = JsonDocument.Parse(buffer.AsMemory(0, newline));
        var result = doc.RootElement.Clone();
        if (!result.TryGetProperty("requestId", out var responseId) || responseId.GetString() != requestId ||
            !result.TryGetProperty("action", out var responseAction) || responseAction.GetString() != action ||
            !result.TryGetProperty("instanceId", out var responseInstance) || responseInstance.GetString() != instance ||
            !result.TryGetProperty("deploymentId", out var responseDeployment) || responseDeployment.GetString() != deploymentId)
            throw new InvalidOperationException("Privileged broker response correlation failed");
        if (!result.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException(
                result.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                    ? error.GetString() ?? "Privileged broker request failed"
                    : "Privileged broker request failed");
        return result;
    }
}
