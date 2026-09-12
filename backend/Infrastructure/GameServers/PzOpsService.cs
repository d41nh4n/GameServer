using Microsoft.Extensions.Logging;

namespace GamePanel.Infrastructure.GameServers;

/// <summary>System health metrics for PZ host: CPU, memory, disk, process status.</summary>
public sealed class PzOpsService
{
    private readonly ILogger<PzOpsService> _logger;
    public PzOpsService(ILogger<PzOpsService> logger) => _logger = logger;
    /// <summary>Returns aggregate host + process metrics for ops dashboard.</summary>
    public async Task<OpsMetrics> GetMetricsAsync(int pid = 0, CancellationToken ct = default)
    {
        var metrics = new OpsMetrics();

        // Host CPU
        try
        {
            var prevTotal = await ReadCpuTotalAsync(ct);
            var prevIdle = await ReadCpuIdleAsync(ct);
            await Task.Delay(1000, ct);
            var currTotal = await ReadCpuTotalAsync(ct);
            var currIdle = await ReadCpuIdleAsync(ct);

            var totalDiff = currTotal - prevTotal;
            var idleDiff = currIdle - prevIdle;
            metrics.CpuPercent = totalDiff > 0 ? Math.Round(100.0 * (totalDiff - idleDiff) / totalDiff, 1) : 0;
        }
        catch { metrics.CpuPercent = 0; }

        // Host memory
        try
        {
            var memInfo = File.ReadAllText("/proc/meminfo");
            metrics.HostMemTotalKb = ParseMemValue(memInfo, "MemTotal:");
            metrics.HostMemAvailableKb = ParseMemValue(memInfo, "MemAvailable:");
            metrics.HostMemUsedKb = metrics.HostMemTotalKb - metrics.HostMemAvailableKb;
            metrics.HostMemPercent = metrics.HostMemTotalKb > 0
                ? Math.Round(100.0 * metrics.HostMemUsedKb / metrics.HostMemTotalKb, 1) : 0;
        }
        catch { }

        // Disk
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                metrics.Disks.Add(new DiskInfo
                {
                    Mount = drive.Name,
                    TotalGb = Math.Round(drive.TotalSize / 1073741824.0, 1),
                    AvailableGb = Math.Round(drive.AvailableFreeSpace / 1073741824.0, 1),
                    Percent = drive.TotalSize > 0
                        ? Math.Round(100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize, 1) : 0,
                });
            }
        }
        catch { }

        // Process details
        if (pid > 0)
        {
            try
            {
                var status = File.ReadAllText($"/proc/{pid}/status");
                metrics.ProcessThreads = ParseInt(status, "Threads:");
                metrics.ProcessVmRssKb = ParseInt(status, "VmRSS:");
                metrics.ProcessVmSizeKb = ParseInt(status, "VmSize:");
                if (Directory.Exists($"/proc/{pid}/fd"))
                    metrics.ProcessFds = Directory.GetFiles($"/proc/{pid}/fd").Length;
            }
            catch { }
        }

        _logger.LogInformation("PZ metrics snapshot {MetricEvent} {MetricType} {ProcessId} {CpuPercent} {HostMemPercent} {ProcessThreads} {ProcessVmRssKb} {ProcessVmSizeKb} {ProcessFds} {DiskCount}", true, "pz_process", pid, metrics.CpuPercent, metrics.HostMemPercent, metrics.ProcessThreads, metrics.ProcessVmRssKb, metrics.ProcessVmSizeKb, metrics.ProcessFds, metrics.Disks.Count);
        return metrics;
    }

    private static long ParseMemValue(string text, string prefix)
    {
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith(prefix) && long.TryParse(line.AsSpan(prefix.Length).Trim().ToString()
                    .Replace("kB", ""), out var val))
                return val;
        }
        return 0;
    }

    private static long ParseInt(string text, string prefix)
    {
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith(prefix) && long.TryParse(line.AsSpan(prefix.Length).Trim().ToString(), out var val))
                return val;
        }
        return 0;
    }

    private static async Task<long> ReadCpuTotalAsync(CancellationToken ct)
    {
        var line = (await File.ReadAllTextAsync("/proc/stat", ct)).Split('\n').FirstOrDefault(s => s.StartsWith("cpu "));
        if (line == null) return 0;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Skip(1).Sum(p => long.TryParse(p, out var v) ? v : 0);
    }

    private static async Task<long> ReadCpuIdleAsync(CancellationToken ct)
    {
        var line = (await File.ReadAllTextAsync("/proc/stat", ct)).Split('\n').FirstOrDefault(s => s.StartsWith("cpu "));
        if (line == null) return 0;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 4 && long.TryParse(parts[4], out var idle) ? idle : 0;
    }
}

public sealed record OpsMetrics
{
    public double CpuPercent { get; set; }
    public long HostMemTotalKb { get; set; }
    public long HostMemAvailableKb { get; set; }
    public long HostMemUsedKb { get; set; }
    public double HostMemPercent { get; set; }
    public long ProcessThreads { get; set; }
    public long ProcessVmRssKb { get; set; }
    public long ProcessVmSizeKb { get; set; }
    public long ProcessFds { get; set; }
    public List<DiskInfo> Disks { get; set; } = new();
}
public sealed record DiskInfo { public string Mount { get; set; } = ""; public double TotalGb { get; set; } public double AvailableGb { get; set; } public double Percent { get; set; } }