using System.Diagnostics;
using System.Text.RegularExpressions;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GamePanel.Infrastructure.Services;

public sealed record LogDetailItem(DateTime? TimestampUtc, string Severity, string Message);

public sealed class LogDetailService
{
    private static readonly Regex Fatal = new(@"\bFATAL\b|\bCRITICAL\b|Out of memory", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Error = new(@"\bERROR\b|\bEXCEPTION\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Warning = new(@"\bWARN(?:ING)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly AppDbContext _db;

    public LogDetailService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<LogDetailItem>> QueryAsync(Guid serverId, DateTime fromUtc, DateTime toUtc, string? severity, int limit, CancellationToken ct = default)
    {
        var server = await _db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == serverId, ct)
            ?? throw new KeyNotFoundException("Server not found");
        var unit = server.RuntimeId;
        if (string.IsNullOrWhiteSpace(unit) || !unit.EndsWith(".service", StringComparison.Ordinal) || unit.Contains('/') || unit.Contains(' '))
            throw new InvalidOperationException("Server has no safe systemd journal unit");

        var psi = new ProcessStartInfo { FileName = "/usr/bin/journalctl", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-u"); psi.ArgumentList.Add(unit);
        psi.ArgumentList.Add("--since"); psi.ArgumentList.Add(fromUtc.ToUniversalTime().ToString("O"));
        psi.ArgumentList.Add("--until"); psi.ArgumentList.Add(toUtc.ToUniversalTime().ToString("O"));
        psi.ArgumentList.Add("--no-pager"); psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("short-iso");
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start journalctl");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException("journalctl failed");

        var wanted = severity?.Trim().ToLowerInvariant();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Classify(line))
            .Where(x => x is not null && (wanted is null or "all" || x!.Severity.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
            .Take(Math.Clamp(limit, 1, 500))
            .Cast<LogDetailItem>()
            .ToList();
    }

    private static LogDetailItem? Classify(string line)
    {
        var severity = Fatal.IsMatch(line) ? "fatal" : Error.IsMatch(line) ? "error" : Warning.IsMatch(line) ? "warning" : null;
        if (severity is null) return null;
        DateTime? timestamp = null;
        var prefix = line.Length >= 25 ? line[..25] : "";
        if (DateTime.TryParse(prefix, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)) timestamp = parsed.ToUniversalTime();
        var message = line.Length > 25 ? line[25..].Trim() : line.Trim();
        return new LogDetailItem(timestamp, severity, message.Length > 1000 ? message[..1000] : message);
    }
}
