using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

public sealed class PluginState
{
    private readonly object _gate = new();
    private readonly List<BridgePlayer> _players = new();
    private long _snapshotSequence;
    private DateTime? _snapshotAtUtc;
    private bool _gameReady;
    public bool PluginConnected { get; private set; }
    public bool PluginAuthenticated { get; private set; }
    public DateTime? LastHeartbeatAtUtc { get; private set; }
    public string? PluginVersion { get; private set; }
    public int ProtocolVersion { get; private set; }
    public string? ValheimBuildId { get; private set; }
    public string? UnityVersion { get; private set; }
    public string? InstanceId { get; private set; }
    public DateTime? ConnectedAtUtc { get; private set; }
    public bool Fresh => LastHeartbeatAtUtc.HasValue && DateTime.UtcNow - LastHeartbeatAtUtc.Value <= TimeSpan.FromSeconds(15);
    public void Connected() { lock (_gate) { PluginConnected = true; ConnectedAtUtc = DateTime.UtcNow; } }
    public void Authenticated() { lock (_gate) { PluginAuthenticated = true; } }
    public void Hello(JsonElement root) { lock (_gate) { PluginVersion = root.GetProperty("pluginVersion").GetString(); ProtocolVersion = root.GetProperty("protocolVersion").GetInt32(); ValheimBuildId = root.GetProperty("valheimBuildId").GetString(); UnityVersion = root.GetProperty("unityVersion").GetString(); InstanceId = root.GetProperty("instanceId").GetString(); } }
    public void Heartbeat() { lock (_gate) { LastHeartbeatAtUtc = DateTime.UtcNow; PluginConnected = true; } }
    public bool SnapshotFresh => Fresh && _snapshotAtUtc.HasValue && DateTime.UtcNow - _snapshotAtUtc.Value <= TimeSpan.FromSeconds(15);
    public bool OnlinePlayersAvailable => PluginAuthenticated && Fresh && SnapshotFresh && _gameReady && ValheimBuildId == "25253791" && UnityVersion == "6000.0.75f1";
    public IReadOnlyList<BridgePlayer> Players { get { lock (_gate) { return _players.ToArray(); } } }
    public bool TrySnapshot(JsonElement root)
    {
        if (root.GetProperty("protocolVersion").GetInt32() != 1) return false;
        if (root.TryGetProperty("collectorStatus", out var status) && status.GetString() != "compatible") return false;
        var sequence = root.GetProperty("sequence").GetInt64(); if (sequence <= _snapshotSequence) return false;
        var captured = root.GetProperty("capturedAtUtc").GetDateTime(); if (captured > DateTime.UtcNow.AddSeconds(30)) return false;
        if (root.GetProperty("instanceId").GetString() != InstanceId) return false;
        var arr = root.GetProperty("players"); if (arr.GetArrayLength() > 64) return false;
        var next = new List<BridgePlayer>(); foreach (var p in arr.EnumerateArray()) { var id=p.GetProperty("connectionId").GetString() ?? ""; var name=p.GetProperty("displayName").GetString() ?? ""; if(id.Length>128||name.Length>128||id.Length==0) return false; next.Add(new BridgePlayer(id,name,p.TryGetProperty("platformId",out var pid)?pid.GetString():null,p.TryGetProperty("connectedAtUtc",out var at)&&at.ValueKind!=JsonValueKind.Null?at.GetDateTime():null,true)); }
        lock (_gate) { _players.Clear(); _players.AddRange(next); _snapshotSequence=sequence; _snapshotAtUtc=captured; _gameReady=root.GetProperty("gameReady").GetBoolean(); }
        return true;
    }
    public void Disconnected() { lock (_gate) { PluginConnected = false; PluginAuthenticated = false; _players.Clear(); _snapshotAtUtc = null; _gameReady = false; } }
    public object Health() => new { bridgeRunning = true, pluginConnected = PluginConnected, pluginAuthenticated = PluginAuthenticated, lastPluginHeartbeatAt = LastHeartbeatAtUtc, heartbeatAgeSeconds = LastHeartbeatAtUtc.HasValue ? (double?)(DateTime.UtcNow - LastHeartbeatAtUtc.Value).TotalSeconds : null, snapshotAgeSeconds = _snapshotAtUtc.HasValue ? (double?)(DateTime.UtcNow - _snapshotAtUtc.Value).TotalSeconds : null, gameReady = _gameReady, snapshotSequence = _snapshotSequence, pluginVersion = PluginVersion, protocolVersion = ProtocolVersion, valheimBuildId = ValheimBuildId, unityVersion = UnityVersion, fresh = Fresh, onlinePlayers = OnlinePlayersAvailable };}

public sealed class PluginIpcService : BackgroundService
{
    private const int Port = 27667;
    private const int MaxFrame = 64 * 1024;
    private readonly PluginState _state;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PluginIpcService> _logger;
    private TcpListener? _listener;
    private int _active;
    public PluginIpcService(PluginState state, IConfiguration configuration, ILogger<PluginIpcService> logger) { _state = state; _configuration = configuration; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = _configuration["VALHEIM_PLUGIN_TOKEN"];
        if (string.IsNullOrWhiteSpace(token)) { _logger.LogWarning("Plugin IPC disabled: missing token"); return; }
        _listener = new TcpListener(IPAddress.Loopback, Port); _listener.Start();
        try { while (!stoppingToken.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(stoppingToken); _ = HandleAsync(client, token, stoppingToken); } }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _listener.Stop(); }
    }
    private async Task HandleAsync(TcpClient client, string expectedToken, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) { client.Close(); return; }
        _state.Connected();
        try
        {
            using (client) using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 3000; stream.WriteTimeout = 3000;
                var auth = await ReadJsonAsync(stream, ct); if (auth is null || auth.Value.GetProperty("type").GetString() != "PluginAuthenticate") return;
                var supplied = auth.Value.TryGetProperty("token", out var token) ? token.GetString() : null;
                if (string.IsNullOrEmpty(supplied) || !FixedEquals(expectedToken, supplied)) return;
                _state.Authenticated(); await WriteJsonAsync(stream, "{\"type\":\"BridgeAcknowledgement\",\"protocolVersion\":1}", ct);
                while (!ct.IsCancellationRequested)
                {
                    var root = await ReadJsonAsync(stream, ct); if (root is null) break;
                    var type = root.Value.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                    if (type == "PluginHello") { if (root.Value.GetProperty("protocolVersion").GetInt32() != 1) break; _state.Hello(root.Value); }
                    else if (type == "PluginHeartbeat") _state.Heartbeat();
                    else if (type == "PlayerListSnapshot") _state.TrySnapshot(root.Value);
                    else if (type == "PluginDisconnect") break;
                    else break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        finally { _state.Disconnected(); Interlocked.Exchange(ref _active, 0); }
    }
    private static bool FixedEquals(string a, string b) { var x=Encoding.UTF8.GetBytes(a); var y=Encoding.UTF8.GetBytes(b); return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x,y); }
    private static async Task<JsonElement?> ReadJsonAsync(NetworkStream s, CancellationToken ct)
    { var h=await ReadExactAsync(s,4,ct); var n=IPAddress.NetworkToHostOrder(BitConverter.ToInt32(h)); if(n<=0||n>MaxFrame)throw new InvalidDataException("Invalid frame"); var b=await ReadExactAsync(s,n,ct); using var d=JsonDocument.Parse(b); return d.RootElement.Clone(); }
    private static async Task WriteJsonAsync(NetworkStream s,string value,CancellationToken ct){var b=Encoding.UTF8.GetBytes(value);var h=BitConverter.GetBytes(IPAddress.HostToNetworkOrder(b.Length));await s.WriteAsync(h,ct);await s.WriteAsync(b,ct);await s.FlushAsync(ct);}
    private static async Task<byte[]> ReadExactAsync(Stream s,int n,CancellationToken ct){var b=new byte[n];var o=0;while(o<n){var r=await s.ReadAsync(b.AsMemory(o,n-o),ct);if(r==0)throw new EndOfStreamException();o+=r;}return b;}
}
