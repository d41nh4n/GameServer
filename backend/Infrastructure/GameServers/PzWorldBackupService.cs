using System.IO.Compression;

namespace GamePanel.Infrastructure.GameServers;

/// <summary>Versioned Project Zomboid world backups. Live backups use RCON save first.</summary>
public sealed class PzWorldBackupService
{
    private const string WorldRoot = "/home/pzserver/Zomboid/Saves/Multiplayer/servertest_new";
    private const string BackupRoot = "/home/nh4n/backups/game-server-panel/pz";
    private readonly IRconClient _rcon;
    private readonly ISystemdRuntimeDriver _systemd;
    private const string Unit = "pzserver-game.service";

    public PzWorldBackupService(IRconClient rcon, ISystemdRuntimeDriver systemd) { _rcon = rcon; _systemd = systemd; }

    public async Task<PzWorldBackup> CreateAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(WorldRoot)) throw new DirectoryNotFoundException(WorldRoot);
        var state = await _systemd.GetStateAsync(Unit, ct);
        if (state.ActiveState == "active")
        {
            var output = await _rcon.SendCommandAsync("save", ct);
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("PZ save returned no confirmation");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        return CopyToVersion("pz");
    }

    public async Task<PzWorldBackup> RollbackAsync(string version, CancellationToken ct = default)
    {
        ValidateVersion(version);
        var state = await _systemd.GetStateAsync(Unit, ct);
        if (state.ActiveState == "active") throw new InvalidOperationException("Stop PZ before rollback");
        var source = Path.Combine(BackupRoot, version);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        var head = CopyToVersion("pz-head-before-rollback");
        ReplaceDirectory(source, WorldRoot);
        return head;
    }

    public IReadOnlyList<PzWorldBackup> List() => ListVersions();

    private PzWorldBackup CopyToVersion(string prefix)
    {
        Directory.CreateDirectory(BackupRoot);
        var name = $"{prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var destination = Path.Combine(BackupRoot, name);
        CopyDirectory(WorldRoot, destination);
        return Describe(destination);
    }

    private IReadOnlyList<PzWorldBackup> ListVersions() => Directory.Exists(BackupRoot)
        ? Directory.GetDirectories(BackupRoot).Select(Describe).OrderByDescending(x => x.CreatedAt).ToList()
        : [];

    private static PzWorldBackup Describe(string path)
    {
        var info = new DirectoryInfo(path);
        return new PzWorldBackup(info.Name, path, info.CreationTimeUtc, DirectorySize(info));
    }

    private static void ReplaceDirectory(string source, string destination)
    {
        var staging = destination + ".rollback-staging";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        CopyDirectory(source, staging);
        if (Directory.Exists(destination)) Directory.Delete(destination, true);
        Directory.Move(staging, destination);
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version != Path.GetFileName(version) || version.Contains(".."))
            throw new ArgumentException("Invalid backup version", nameof(version));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static long DirectorySize(DirectoryInfo dir) => dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(x => x.Length);
}

public sealed record PzWorldBackup(string Name, string Path, DateTime CreatedAt, long SizeBytes);
