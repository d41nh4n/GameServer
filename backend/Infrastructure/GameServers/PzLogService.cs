namespace GamePanel.Infrastructure.GameServers;

/// <summary>Reads PZ log files from Zomboid/Logs directory.</summary>
public sealed class PzLogService
{
    private readonly string _logsDir;

    public PzLogService(string? logsDir = null)
    {
        _logsDir = logsDir ?? "/home/pzserver/Zomboid/Logs";
    }

    public List<LogFileEntry> ListFiles()
    {
        if (!Directory.Exists(_logsDir)) return new();
        return Directory.GetFiles(_logsDir, "*.*")
            .Where(f => f.EndsWith(".txt") || f.EndsWith(".log"))
            .Select(f => new LogFileEntry
            {
                Name = Path.GetFileName(f),
                Size = new FileInfo(f).Length,
                Mtime = File.GetLastWriteTimeUtc(f),
            })
            .OrderByDescending(f => f.Mtime)
            .ToList();
    }

    public string ReadFile(string filename)
    {
        var safeName = Path.GetFileName(filename);
        var path = Path.Combine(_logsDir, safeName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Log file not found: {safeName}");
        return File.ReadAllText(path);
    }

    public List<string> ListRecentLogDates()
    {
        if (!Directory.Exists(_logsDir)) return new();
        return Directory.GetDirectories(_logsDir)
            .Select(d => Path.GetFileName(d))
            .OrderByDescending(d => d)
            .ToList();
    }
}

public sealed record LogFileEntry
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime Mtime { get; set; }
}