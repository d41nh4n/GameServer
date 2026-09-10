using System.Collections.Concurrent;
using System.Threading.Channels;
using GamePanel.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GamePanel.Infrastructure.Services;

public enum ServerOperationKind { Start, Stop, Restart }
public enum ServerOperationStatus { Queued, Running, Succeeded, Failed }

public sealed class ServerOperationJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ServerId { get; init; }
    public ServerOperationKind Kind { get; init; }
    public Guid? UserId { get; init; }
    public string Username { get; init; } = "system";
    public ServerOperationStatus Status { get; internal set; } = ServerOperationStatus.Queued;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; internal set; }
    public DateTime? CompletedAtUtc { get; internal set; }
    public string? Error { get; internal set; }
}

public sealed class ServerOperationQueue
{
    private readonly Channel<ServerOperationJob> _channel = Channel.CreateUnbounded<ServerOperationJob>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, ServerOperationJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, Guid> _activeByServer = new();

    public ServerOperationJob Enqueue(Guid serverId, ServerOperationKind kind, Guid? userId, string username)
    {
        var job = new ServerOperationJob { ServerId = serverId, Kind = kind, UserId = userId, Username = username };
        if (!_activeByServer.TryAdd(serverId, job.Id)) throw new InvalidOperationException("A server operation is already queued or running");
        _jobs[job.Id] = job;
        if (!_channel.Writer.TryWrite(job)) { _activeByServer.TryRemove(serverId, out _); _jobs.TryRemove(job.Id, out _); throw new InvalidOperationException("Cannot queue server operation"); }
        return job;
    }

    public async ValueTask<ServerOperationJob> DequeueAsync(CancellationToken ct)
    {
        var job = await _channel.Reader.ReadAsync(ct);
        job.Status = ServerOperationStatus.Running; job.StartedAtUtc = DateTime.UtcNow;
        return job;
    }

    public void Complete(Guid jobId, bool success, string? error)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;
        job.Status = success ? ServerOperationStatus.Succeeded : ServerOperationStatus.Failed;
        job.Error = error; job.CompletedAtUtc = DateTime.UtcNow;
        _activeByServer.TryRemove(job.ServerId, out _);
    }

    public ServerOperationJob? Get(Guid id) => _jobs.TryGetValue(id, out var job) ? job : null;
}

public sealed class ServerOperationWorker : BackgroundService
{
    private readonly ServerOperationQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ServerOperationWorker> _logger;
    public ServerOperationWorker(ServerOperationQueue queue, IServiceScopeFactory scopes, ILogger<ServerOperationWorker> logger) { _queue = queue; _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ServerOperationJob job;
            try { job = await _queue.DequeueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            try
            {
                using var scope = _scopes.CreateScope();
                var runtime = scope.ServiceProvider.GetRequiredService<IGameServerRuntime>();
                var success = job.Kind switch
                {
                    ServerOperationKind.Start => await runtime.StartAsync(job.ServerId, stoppingToken),
                    ServerOperationKind.Stop => await runtime.StopAsync(job.ServerId, stoppingToken),
                    ServerOperationKind.Restart => await RestartAsync(runtime, job.ServerId, stoppingToken),
                    _ => false,
                };
                _queue.Complete(job.Id, success, success ? null : $"{job.Kind} failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Server operation {JobId} failed", job.Id);
                _queue.Complete(job.Id, false, ex.Message);
            }
        }
    }

    private static async Task<bool> RestartAsync(IGameServerRuntime runtime, Guid id, CancellationToken ct) =>
        await runtime.StopAsync(id, ct) && await runtime.StartAsync(id, ct);
}
