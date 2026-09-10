using GamePanel.Infrastructure.Services;
using GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class ServerOperationQueueTests
{
    [Fact]
    public void Enqueue_ReturnsQueuedJobImmediately()
    {
        var queue = new ServerOperationQueue();
        var job = queue.Enqueue(Guid.NewGuid(), ServerOperationKind.Start, Guid.NewGuid(), "admin");

        Assert.Equal(ServerOperationStatus.Queued, job.Status);
        Assert.NotEqual(Guid.Empty, job.Id);
    }

    [Fact]
    public void Enqueue_RejectsConcurrentOperationForSameServer()
    {
        var queue = new ServerOperationQueue();
        var serverId = Guid.NewGuid();
        queue.Enqueue(serverId, ServerOperationKind.Start, Guid.NewGuid(), "admin");

        Assert.Throws<InvalidOperationException>(() =>
            queue.Enqueue(serverId, ServerOperationKind.Stop, Guid.NewGuid(), "admin"));
    }

    [Fact]
    public async Task Complete_ReleasesServerForNextOperation()
    {
        var queue = new ServerOperationQueue();
        var serverId = Guid.NewGuid();
        var first = queue.Enqueue(serverId, ServerOperationKind.Start, Guid.NewGuid(), "admin");
        var dequeued = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(first.Id, dequeued.Id);

        queue.Complete(first.Id, true, null);
        var next = queue.Enqueue(serverId, ServerOperationKind.Stop, Guid.NewGuid(), "admin");

        Assert.Equal(ServerOperationStatus.Queued, next.Status);
        Assert.Equal(ServerOperationStatus.Succeeded, queue.Get(first.Id)!.Status);
    }

    [Fact]
    public async Task Worker_RestartCallsStopBeforeStartAndCompletesJob()
    {
        var runtime = new RecordingRuntime();
        var (queue, worker, provider) = CreateWorker(runtime);
        await worker.StartAsync(CancellationToken.None);
        var job = queue.Enqueue(Guid.NewGuid(), ServerOperationKind.Restart, null, "admin");

        await WaitUntilAsync(() => queue.Get(job.Id)?.Status is ServerOperationStatus.Succeeded);

        Assert.Equal(new[] { "stop", "start" }, runtime.Calls);
        await worker.StopAsync(CancellationToken.None);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Worker_RuntimeFailureMarksJobFailedAndReleasesServerLock()
    {
        var runtime = new RecordingRuntime { StartResult = false };
        var (queue, worker, provider) = CreateWorker(runtime);
        await worker.StartAsync(CancellationToken.None);
        var serverId = Guid.NewGuid();
        var failed = queue.Enqueue(serverId, ServerOperationKind.Start, null, "admin");

        await WaitUntilAsync(() => queue.Get(failed.Id)?.Status is ServerOperationStatus.Failed);
        var retry = queue.Enqueue(serverId, ServerOperationKind.Stop, null, "admin");

        Assert.Equal(ServerOperationStatus.Queued, retry.Status);
        Assert.Equal("Start failed", queue.Get(failed.Id)!.Error);
        await worker.StopAsync(CancellationToken.None);
        await provider.DisposeAsync();
    }

    private static (ServerOperationQueue Queue, ServerOperationWorker Worker, ServiceProvider Provider) CreateWorker(RecordingRuntime runtime)
    {
        var services = new ServiceCollection();
        services.AddScoped<IGameServerRuntime>(_ => runtime);
        var provider = services.BuildServiceProvider();
        var queue = new ServerOperationQueue();
        return (queue, new ServerOperationWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ServerOperationWorker>.Instance), provider);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(predicate(), "Worker did not complete before timeout");
    }

    private sealed class RecordingRuntime : IGameServerRuntime
    {
        public bool StartResult { get; init; } = true;
        public List<string> Calls { get; } = [];
        public Task<IEnumerable<ServerInstance>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<ServerInstance>>([]);
        public Task<bool> StartAsync(Guid id, CancellationToken ct = default) { Calls.Add("start"); return Task.FromResult(StartResult); }
        public Task<bool> StopAsync(Guid id, CancellationToken ct = default) { Calls.Add("stop"); return Task.FromResult(true); }
    }
}
