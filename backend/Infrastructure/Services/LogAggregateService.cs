namespace GamePanel.Infrastructure.Services;
using System.Text.RegularExpressions;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public sealed record LogAggregateDelta(
    int WarningCount,
    int ErrorCount,
    int FatalCount,
    int PlayerJoinCount,
    int PlayerLeaveCount,
    string? LastErrorMessage);

public static class LogAggregateParser
{
    private static readonly Regex Warning = new(@"\bWARN(?:ING)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Error = new(@"\bERROR\b|\bEXCEPTION\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Fatal = new(@"\bFATAL\b|\bCRITICAL\b|Out of memory", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Join = new(@"joined the server|player connected|login", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Leave = new(@"left the server|player disconnected|logout", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static LogAggregateDelta Parse(IEnumerable<string> lines)
    {
        var warning = 0; var error = 0; var fatal = 0; var joins = 0; var leaves = 0; string? lastError = null;
        foreach (var line in lines)
        {
            if (Warning.IsMatch(line)) warning++;
            if (Error.IsMatch(line)) { error++; lastError = line.Length > 255 ? line[..255] : line; }
            if (Fatal.IsMatch(line)) fatal++;
            if (Join.IsMatch(line)) joins++;
            if (Leave.IsMatch(line)) leaves++;
        }
        return new(warning, error, fatal, joins, leaves, lastError);
    }
}

public sealed class LogAggregateService
{
    private readonly AppDbContext _db;
    public LogAggregateService(AppDbContext db) => _db = db;

    public async Task UpsertAsync(Guid serverId, DateTime windowStartUtc, DateTime windowEndUtc, LogAggregateDelta delta, CancellationToken ct = default)
    {
        var row = await _db.LogAggregates.FirstOrDefaultAsync(x =>
            x.ServerInstanceId == serverId && x.WindowStartUtc == windowStartUtc, ct);
        if (row == null)
        {
            row = new LogAggregate { ServerInstanceId = serverId, WindowStartUtc = windowStartUtc, WindowEndUtc = windowEndUtc };
            _db.LogAggregates.Add(row);
        }
        row.WindowEndUtc = windowEndUtc;
        row.WarningCount = delta.WarningCount;
        row.ErrorCount = delta.ErrorCount;
        row.FatalCount = delta.FatalCount;
        row.PlayerJoinCount = delta.PlayerJoinCount;
        row.PlayerLeaveCount = delta.PlayerLeaveCount;
        row.LastErrorMessage = delta.LastErrorMessage;
        await _db.SaveChangesAsync(ct);
    }

    public IQueryable<LogAggregate> Query(Guid? serverId = null, DateTime? fromUtc = null, DateTime? toUtc = null)
    {
        var query = _db.LogAggregates.AsNoTracking().OrderByDescending(x => x.WindowStartUtc).AsQueryable();
        if (serverId.HasValue) query = query.Where(x => x.ServerInstanceId == serverId.Value).OrderByDescending(x => x.WindowStartUtc);
        if (fromUtc.HasValue) query = query.Where(x => x.WindowStartUtc >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.WindowStartUtc < toUtc.Value);
        return query;
    }
}