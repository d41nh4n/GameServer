namespace GamePanel.Infrastructure.GameServers;
using System.Net.Sockets;
using System.Text;

/// <summary>Source RCON client compatible with Project Zomboid's auth handshake.</summary>
public interface IRconClient
{
    Task<string> SendCommandAsync(string command, CancellationToken ct = default);
}

public sealed class RconClient : IRconClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly TimeSpan _timeout;

    private const int AuthType = 3;
    private const int CommandType = 2;

    public RconClient(string host, int port, string password, TimeSpan? timeout = null)
    {
        _host = host;
        _port = port;
        _password = password;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        await tcp.ConnectAsync(_host, _port, timeout.Token);
        await using var stream = tcp.GetStream();

        await stream.WriteAsync(BuildPacket(1, AuthType, _password), timeout.Token);

        // PZ emits an empty SERVERDATA_RESPONSE_VALUE before the actual auth response.
        RconPacket auth;
        do
        {
            auth = await ReadPacketAsync(stream, timeout.Token);
            if (auth.RequestId == -1)
                throw new InvalidOperationException("RCON authentication failed");
        } while (auth.Type != 2);

        await stream.WriteAsync(BuildPacket(2, CommandType, command), timeout.Token);

        // Return the command response matching request ID 2. Ignore handshake noise.
        while (true)
        {
            var response = await ReadPacketAsync(stream, timeout.Token);
            if (response.RequestId != 2 && response.RequestId != -1) continue;
            if (response.RequestId == -1) throw new InvalidOperationException("RCON command rejected");
            return response.Body;
        }
    }

    private static byte[] BuildPacket(int requestId, int type, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var length = bodyBytes.Length + 10; // id + type + body + two null bytes
        var packet = new byte[length + 4];
        BitConverter.GetBytes(length).CopyTo(packet, 0);
        BitConverter.GetBytes(requestId).CopyTo(packet, 4);
        BitConverter.GetBytes(type).CopyTo(packet, 8);
        bodyBytes.CopyTo(packet, 12);
        // packet[length+2] and packet[length+3] remain null
        return packet;
    }

    private static async Task<RconPacket> ReadPacketAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        var length = BitConverter.ToInt32(header);
        if (length < 10 || length > 8192)
            throw new InvalidOperationException($"Invalid RCON packet length: {length}");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, ct);
        var requestId = BitConverter.ToInt32(payload, 0);
        var type = BitConverter.ToInt32(payload, 4);
        var bodyLength = length - 10;
        var body = bodyLength > 0 ? Encoding.UTF8.GetString(payload, 8, bodyLength) : "";
        return new RconPacket(requestId, type, body.TrimEnd('\0'));
    }

    private sealed record RconPacket(int RequestId, int Type, string Body);
}