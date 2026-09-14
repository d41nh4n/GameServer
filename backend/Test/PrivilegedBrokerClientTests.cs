namespace GamePanel.ContractTests;

using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GamePanel.Infrastructure.GameServers;
using Xunit;

public sealed class PrivilegedBrokerClientTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "broker_client_test_" + Guid.NewGuid().ToString("N"));

    public PrivilegedBrokerClientTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task SendDeploymentAsync_RejectsInvalidDeploymentIdBeforeConnecting()
    {
        var client = new PrivilegedBrokerClient(Path.Combine(_tempDir, "missing.sock"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SendDeploymentAsync("seal", "../escape"));
    }

    [Fact]
    public async Task SendDeploymentAsync_UsesFixedValheimInstanceAndCorrelatesResponse()
    {
        var socketPath = Path.Combine(_tempDir, "broker.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);

        JsonElement captured = default;
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptAsync();
            var requestBytes = await ReadFrameAsync(connection);
            using var requestDoc = JsonDocument.Parse(requestBytes);
            captured = requestDoc.RootElement.Clone();
            var response = JsonSerializer.Serialize(new
            {
                ok = true,
                state = "sealed",
                requestId = captured.GetProperty("requestId").GetString(),
                action = captured.GetProperty("action").GetString(),
                instanceId = captured.GetProperty("instanceId").GetString(),
                deploymentId = captured.GetProperty("deploymentId").GetString(),
            }) + "\n";
            await connection.SendAsync(Encoding.UTF8.GetBytes(response), SocketFlags.None);
        });

        var client = new PrivilegedBrokerClient(socketPath);
        var result = await client.SendDeploymentAsync("seal", "dep-001");
        await server;

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("seal", captured.GetProperty("action").GetString());
        Assert.Equal("valheim-main", captured.GetProperty("instanceId").GetString());
        Assert.Equal("dep-001", captured.GetProperty("deploymentId").GetString());
        Assert.Equal(5, captured.EnumerateObject().Count());
    }

    [Fact]
    public async Task SendDeploymentAsync_RejectsMismatchedResponseCorrelation()
    {
        var socketPath = Path.Combine(_tempDir, "mismatch.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);

        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptAsync();
            var requestBytes = await ReadFrameAsync(connection);
            using var requestDoc = JsonDocument.Parse(requestBytes);
            var request = requestDoc.RootElement;
            var response = JsonSerializer.Serialize(new
            {
                ok = true,
                requestId = Guid.NewGuid().ToString(),
                action = request.GetProperty("action").GetString(),
                instanceId = request.GetProperty("instanceId").GetString(),
                deploymentId = request.GetProperty("deploymentId").GetString(),
            }) + "\n";
            await connection.SendAsync(Encoding.UTF8.GetBytes(response), SocketFlags.None);
        });

        var client = new PrivilegedBrokerClient(socketPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendDeploymentAsync("deploy", "dep-002"));
        await server;
    }

    private static async Task<byte[]> ReadFrameAsync(Socket socket)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1024];
        while (bytes.Count < 65536)
        {
            var read = await socket.ReceiveAsync(buffer, SocketFlags.None);
            if (read == 0) break;
            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            if (bytes[^1] == (byte)'\n') break;
        }
        return bytes.ToArray();
    }
}
