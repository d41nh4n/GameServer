using System.Globalization;
using System.Text.RegularExpressions;
using GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;

namespace GamePanel.Infrastructure.Services;

public sealed class ResourceMetricsService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ResourceMetricsService> _logger;
    public ResourceMetricsService(AppDbContext db, ILogger<ResourceMetricsService> logger) { _db = db; _logger = logger; }

    public async Task<ResourceOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var host = await ReadHostAsync(ct);
        var servers = await _db.ServerInstances.AsNoTracking().ToListAsync(ct);
        var resources = new List<ServerResourceMetric>();
        foreach (var server in servers)
        {
            if (server.ProcessId is not > 0) { resources.Add(ServerResourceMetric.Offline(server.Id, server.Name, server.Status)); continue; }
            resources.Add(await ReadProcessAsync(server.Id, server.Name, server.Status, server.ProcessId.Value, ct));
        }
        foreach (var metric in resources)
            _logger.LogInformation("Resource metric {MetricEvent} {MetricType} {ServerId} {Status} {ProcessId} {Online} {CpuPercent} {MemoryRssKb} {Threads} {FileDescriptors}", true, "resource_server", metric.ServerId, metric.Status, metric.Pid, metric.Online, metric.CpuPercent, metric.MemoryRssKb, metric.Threads, metric.FileDescriptors);
        _logger.LogInformation("Host metric {MetricEvent} {MetricType} {CpuPercent} {MemoryTotalKb} {MemoryUsedKb} {DiskCount} {GeneratedAtUtc}", true, "resource_host", host.CpuPercent, host.MemoryTotalKb, host.MemoryUsedKb, host.Disks.Count, DateTime.UtcNow);
        return new ResourceOverview(host, resources, DateTime.UtcNow);
    }

    private static async Task<HostResourceMetric> ReadHostAsync(CancellationToken ct)
    {
        var first = await ReadCpuAsync(ct); await Task.Delay(250, ct); var second = await ReadCpuAsync(ct);
        var total = second.total - first.total; var idle = second.idle - first.idle;
        var cpu = total > 0 ? Math.Round(100 * (1 - idle / (double)total), 1) : 0;
        var mem = File.ReadAllLines("/proc/meminfo");
        var totalKb = Value(mem, "MemTotal:"); var availableKb = Value(mem, "MemAvailable:");
        var disks = DriveInfo.GetDrives().Where(x => x.IsReady).Select(x => new DiskResourceMetric(x.Name, Math.Round(x.TotalSize / 1073741824d, 1), Math.Round(x.AvailableFreeSpace / 1073741824d, 1))).ToList();
        return new HostResourceMetric(cpu, totalKb, totalKb - availableKb, disks);
    }

    private static async Task<ServerResourceMetric> ReadProcessAsync(Guid id, string name, Domain.Entities.ServerStatus status, int pid, CancellationToken ct)
    {
        try
        {
            var systemBefore = await ReadCpuAsync(ct); var before = await ReadProcessCpuAsync(pid, ct);
            await Task.Delay(250, ct);
            var after = await ReadProcessCpuAsync(pid, ct); var systemAfter = await ReadCpuAsync(ct);
            var processDelta = after - before; var systemDelta = systemAfter.total - systemBefore.total;
            var cpu = systemDelta > 0 ? Math.Round(100 * processDelta / (double)systemDelta, 1) : 0;
            var text = await File.ReadAllTextAsync($"/proc/{pid}/status", ct);
            var rss = Value(text.Split('\n'), "VmRSS:"); var threads = Value(text.Split('\n'), "Threads:");
            var fds = 0;
            try { if (Directory.Exists($"/proc/{pid}/fd")) fds = Directory.GetFiles($"/proc/{pid}/fd").Length; } catch (UnauthorizedAccessException) { }
            return new ServerResourceMetric(id, name, status, pid, true, cpu, rss, threads, fds);
        }
        catch { return ServerResourceMetric.Offline(id, name, status, pid); }
    }

    private static async Task<long> ReadProcessCpuAsync(int pid, CancellationToken ct)
    {
        var line = await File.ReadAllTextAsync($"/proc/{pid}/stat", ct);
        var end = line.LastIndexOf(')'); var fields = line[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(fields[11], CultureInfo.InvariantCulture) + long.Parse(fields[12], CultureInfo.InvariantCulture);
    }
    private static async Task<(long total, long idle)> ReadCpuAsync(CancellationToken ct)
    {
        var line = (await File.ReadAllTextAsync("/proc/stat", ct)).Split('\n').First(x => x.StartsWith("cpu "));
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
        return (p.Sum(), p[3] + (p.Length > 4 ? p[4] : 0));
    }
    private static long Value(IEnumerable<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
        if (line is null) return 0;
        var value = Regex.Match(line[prefix.Length..], @"\d+").Value;
        return long.TryParse(value, out var parsed) ? parsed : 0;
    }
}

public sealed record ResourceOverview(HostResourceMetric Host, IReadOnlyList<ServerResourceMetric> Servers, DateTime GeneratedAtUtc);
public sealed record HostResourceMetric(double CpuPercent, long MemoryTotalKb, long MemoryUsedKb, IReadOnlyList<DiskResourceMetric> Disks);
public sealed record DiskResourceMetric(string Mount, double TotalGb, double AvailableGb);
public sealed record ServerResourceMetric(Guid ServerId, string Name, Domain.Entities.ServerStatus Status, int? Pid, bool Online, double CpuPercent, long MemoryRssKb, long Threads, int FileDescriptors)
{
    public static ServerResourceMetric Offline(Guid id, string name, Domain.Entities.ServerStatus status, int? pid = null) => new(id, name, status, pid, false, 0, 0, 0, 0);
}
