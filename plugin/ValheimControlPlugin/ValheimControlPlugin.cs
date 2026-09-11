using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace ValheimControlPlugin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ValheimControlPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.gamepanel.valheim-control";
    public const string PluginName = "GamePanel Valheim Control";
    public const string PluginVersion = "0.1.0";
    private CancellationFlag _stop;
    private PluginConnectionWorker _worker;
    private readonly PlayerSnapshotPublisher _snapshotPublisher = new PlayerSnapshotPublisher();
    private PlayerSnapshotCollector _collector;

    private void Awake()
    {
        _stop = new CancellationFlag();
        _collector = new PlayerSnapshotCollector(_snapshotPublisher, Logger);
        _worker = new PluginConnectionWorker(new PluginProtocolOptions(), Logger, _stop, _snapshotPublisher);
        _worker.Start();
        Logger.LogInfo("ValheimControlPlugin lifecycle started");
    }

    private void Update() { _collector?.Tick(); }

    private void OnDestroy()
    {
        if (_stop == null) return;
        _stop.Cancel();
        _worker?.Dispose();
    }
}

public sealed class PluginProtocolOptions
{
    public string Host = "127.0.0.1";
    public int Port = 27667;
    public int ProtocolVersion = 1;
    public string PluginVersion = ValheimControlPlugin.PluginVersion;
    public string ValheimBuildId = "25253791";
    public string UnityVersion = "6000.0.75f1";
    public string InstanceId = "valheim-main-lab";
    public string TokenFile = "BepInEx/config/ValheimControlPlugin.token";
    public int FrameLimit = 64 * 1024;
}

public sealed class CancellationFlag
{
    private volatile bool _cancelled;
    public bool IsCancellationRequested { get { return _cancelled; } }
    public void Cancel() { _cancelled = true; }
}

public enum PluginLifecycleState { Disconnected, Authenticating, Connected, Stopping }

public sealed class PluginConnectionWorker : IDisposable
{
    private readonly PluginProtocolOptions _options;
    private readonly ManualLogSource _log;
    private readonly CancellationFlag _stop;
    private readonly PlayerSnapshotPublisher _snapshotPublisher;
    private Thread _thread;
    private TcpClient _client;
    private PluginLifecycleState _state;

    public PluginConnectionWorker(PluginProtocolOptions options, ManualLogSource log, CancellationFlag stop, PlayerSnapshotPublisher snapshotPublisher) { _options = options; _log = log; _stop = stop; _snapshotPublisher = snapshotPublisher; _state = PluginLifecycleState.Disconnected; }
    public void Start() { _thread = new Thread(Run) { IsBackground = true, Name = "ValheimControlPlugin.Connection" }; _thread.Start(); }
    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { ConnectOnce(); }
            catch (Exception ex) { _log.LogWarning("Control bridge connection failed: " + ex.GetType().Name); }
            for (var i = 0; i < 50 && !_stop.IsCancellationRequested; i++) Thread.Sleep(100);
        }
        _state = PluginLifecycleState.Stopping;
    }
    private void ConnectOnce()
    {
        _state = PluginLifecycleState.Authenticating;
        using (var client = new TcpClient())
        {
            _client = client;
            client.ReceiveTimeout = 3000; client.SendTimeout = 3000;
            client.Connect(_options.Host, _options.Port);
            using (var stream = client.GetStream())
            {
                var token = File.ReadAllText(_options.TokenFile).Trim();
                PluginMessageSerializer.Write(stream, PluginMessageSerializer.Authenticate(token), _options.FrameLimit);
                var ack = PluginMessageSerializer.Read(stream, _options.FrameLimit);
                if (ack.IndexOf("BridgeAcknowledgement", StringComparison.Ordinal) < 0) throw new InvalidDataException("Bridge authentication rejected");
                PluginMessageSerializer.Write(stream, PluginMessageSerializer.Hello(_options), _options.FrameLimit);
                _state = PluginLifecycleState.Connected;
                while (!_stop.IsCancellationRequested)
                {
                    PluginMessageSerializer.Write(stream, PluginMessageSerializer.Heartbeat(_options), _options.FrameLimit);
                    var snapshot = _snapshotPublisher.Current;
                    if (snapshot != null) PluginMessageSerializer.Write(stream, PluginMessageSerializer.PlayerList(snapshot, _options), _options.FrameLimit);
                    for (var i = 0; i < 50 && !_stop.IsCancellationRequested; i++) Thread.Sleep(100);
                }
            }
        }
        _client = null;
    }
    public void Dispose()
    {
        try { _client?.Close(); } catch { }
        try { if (_thread != null && _thread.IsAlive) _thread.Join(3000); } catch { }
    }
}

public sealed class ImmutablePlayerSnapshot
{
    public long Sequence { get; private set; }
    public DateTime CapturedAtUtc { get; private set; }
    public bool GameReady { get; private set; }
    public IList<ImmutablePlayer> Players { get; private set; }
    public ImmutablePlayerSnapshot(long sequence, DateTime capturedAtUtc, bool gameReady, List<ImmutablePlayer> players) { Sequence = sequence; CapturedAtUtc = capturedAtUtc; GameReady = gameReady; Players = players.AsReadOnly(); }
}
public sealed class ImmutablePlayer { public string ConnectionId; public string DisplayName; public string PlatformId; public DateTime? ConnectedAtUtc; public bool IsConnected; }
public sealed class PlayerSnapshotPublisher
{
    private ImmutablePlayerSnapshot _current;
    public ImmutablePlayerSnapshot Current { get { return System.Threading.Interlocked.CompareExchange(ref _current, null, null); } }
    public void Publish(ImmutablePlayerSnapshot snapshot) { System.Threading.Interlocked.Exchange(ref _current, snapshot); }
}
public sealed class PlayerApiCompatibilityState { public bool Compatible; public string Reason; }
public sealed class PlayerSnapshotCollector
{
    private readonly PlayerSnapshotPublisher _publisher; private readonly ManualLogSource _log; private float _next; private long _sequence;
    public PlayerSnapshotCollector(PlayerSnapshotPublisher publisher, ManualLogSource log) { _publisher = publisher; _log = log; }
    public void Tick()
    {
        if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + 3f;
        try
        {
            var net = ZNet.instance; var players = new List<ImmutablePlayer>(); var ready = net != null && net.IsServer() && net.IsDedicated();
            if (ready) foreach (var peer in net.GetConnectedPeers()) if (peer != null && peer.IsReady()) players.Add(new ImmutablePlayer { ConnectionId = peer.m_uid.ToString(), DisplayName = BoundName(peer.m_playerName), PlatformId = null, ConnectedAtUtc = null, IsConnected = true });
            _publisher.Publish(new ImmutablePlayerSnapshot(++_sequence, DateTime.UtcNow, ready, players));
        }
        catch (Exception ex) { _log.LogWarning("Player snapshot collector failed: " + ex.GetType().Name); }
    }
    private static string BoundName(string value) { if (value == null) return ""; value = value.Replace("\r", " ").Replace("\n", " "); return value.Length <= 128 ? value : value.Substring(0, 128); }
}

public static class PluginMessageSerializer
{
    public static string Authenticate(string token) { return "{\"type\":\"PluginAuthenticate\",\"token\":\"" + Escape(token) + "\"}"; }
    public static string Hello(PluginProtocolOptions o) { return "{\"type\":\"PluginHello\",\"protocolVersion\":" + o.ProtocolVersion + ",\"pluginVersion\":\"" + Escape(o.PluginVersion) + "\",\"valheimBuildId\":\"" + Escape(o.ValheimBuildId) + "\",\"unityVersion\":\"" + Escape(o.UnityVersion) + "\",\"instanceId\":\"" + Escape(o.InstanceId) + "\"}"; }
    public static string Heartbeat(PluginProtocolOptions o) { return "{\"type\":\"PluginHeartbeat\",\"protocolVersion\":" + o.ProtocolVersion + ",\"pluginVersion\":\"" + Escape(o.PluginVersion) + "\",\"instanceId\":\"" + Escape(o.InstanceId) + "\"}"; }
    public static string PlayerList(ImmutablePlayerSnapshot s, PluginProtocolOptions o)
    {
        var b = new StringBuilder("{\"type\":\"PlayerListSnapshot\",\"protocolVersion\":"); b.Append(o.ProtocolVersion).Append(",\"instanceId\":\"").Append(Escape(o.InstanceId)).Append("\",\"sequence\":").Append(s.Sequence).Append(",\"capturedAtUtc\":\"").Append(s.CapturedAtUtc.ToString("o")).Append("\",\"gameReady\":").Append(s.GameReady ? "true" : "false").Append(",\"collectorStatus\":\"compatible\",\"players\":[");
        for (var i=0;i<s.Players.Count;i++) { if (i>0)b.Append(','); var p=s.Players[i]; b.Append("{\"connectionId\":\"").Append(Escape(p.ConnectionId)).Append("\",\"displayName\":\"").Append(Escape(p.DisplayName)).Append("\",\"platformId\":null,\"isConnected\":true}"); }
        return b.Append("]}").ToString();
    }
    public static void Write(Stream stream, string message, int max)
    {
        var bytes = Encoding.UTF8.GetBytes(message); if (bytes.Length == 0 || bytes.Length > max) throw new InvalidDataException("Invalid frame size");
        var header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length)); stream.Write(header, 0, 4); stream.Write(bytes, 0, bytes.Length); stream.Flush();
    }
    public static string Read(Stream stream, int max)
    {
        var h = ReadExact(stream, 4); var len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(h, 0)); if (len <= 0 || len > max) throw new InvalidDataException("Invalid frame size"); return Encoding.UTF8.GetString(ReadExact(stream, len));
    }
    private static byte[] ReadExact(Stream stream, int length) { var b = new byte[length]; var offset = 0; while (offset < length) { var read = stream.Read(b, offset, length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; } return b; }
    private static string Escape(string value) { return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n"); }
}
