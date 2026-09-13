using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamePanel.Infrastructure.GameServers;

public sealed class PrivilegedBrokerClient
{
    private const string SocketPath = "/run/gamepanel/privileged.sock";
    private static readonly IReadOnlyDictionary<string, string> Instances = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["valheim-main.service"] = "valheim-main",
        ["pzserver-game.service"] = "pz-main",
    };

    public async Task<JsonElement> SendAsync(string unitName, string action, CancellationToken ct)
    {
        if (!Instances.TryGetValue(unitName, out var instance) || action is not ("status" or "start" or "stop" or "restart")) throw new InvalidOperationException("Broker request is not allowlisted");
        if (!Regex.IsMatch("baseline", "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")) throw new InvalidOperationException("Invalid deployment id");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), timeout.Token);
        var requestId = Guid.NewGuid().ToString();
        var request = JsonSerializer.Serialize(new { requestVersion = 1, requestId, action, instanceId = instance, deploymentId = "baseline" }) + "\n";
        var bytes = Encoding.UTF8.GetBytes(request); var sent = 0;
        while (sent < bytes.Length) sent += await socket.SendAsync(bytes.AsMemory(sent), SocketFlags.None, timeout.Token);
        var buffer = new byte[65536]; var received = 0;
        while (received < buffer.Length)
        {
            var count = await socket.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None, timeout.Token);
            if (count == 0) break; received += count;
            if (buffer[received - 1] == (byte)'\n') break;
        }
        if (received == 0) throw new IOException("Privileged broker returned no response");
        using var doc = JsonDocument.Parse(buffer[..received].ToArray());
        var result = doc.RootElement.Clone();
        if (!result.TryGetProperty("requestId", out var responseId) || responseId.GetString() != requestId || !result.TryGetProperty("action", out var responseAction) || responseAction.GetString() != action || !result.TryGetProperty("instanceId", out var responseInstance) || responseInstance.GetString() != instance || !result.TryGetProperty("deploymentId", out var responseDeployment) || responseDeployment.GetString() != "baseline")
            throw new InvalidOperationException("Privileged broker response correlation failed");
        if (!result.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) throw new InvalidOperationException(result.TryGetProperty("error", out var error) ? error.GetString() : "Privileged broker request failed");
        return result;
    }
}
