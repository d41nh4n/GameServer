namespace GamePanel.Infrastructure.GameServers;
using System.Diagnostics;
using System.Text.RegularExpressions;

/// <summary>Read-only monitoring and safe versioned backup for an adopted Valheim instance.</summary>
public sealed class ValheimMonitoringService
{
    private readonly ISystemdRuntimeDriver _driver;
    private const string DataRoot = "/srv/gamepanel/instances/valheim-main/data";
    private const string WorldRoot = DataRoot + "/worlds_local";
    private const string BackupRoot = "/home/nh4n/backups/game-server-panel/valheim";
    private const string Unit = "valheim-main.service";
    private const string ServerRoot = "/srv/gamepanel/instances/valheim-main/server";
    private const string ManifestPath = ServerRoot + "/steamapps/appmanifest_896660.acf";
    private const string RuntimeRoot = "/srv/gamepanel/instances/valheim-main/runtime";
    private const string SteamCmdBinary = "/usr/games/steamcmd";

    private static string? _cachedLatestBuildId;
    private static DateTime _lastLatestBuildCheck = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public ValheimMonitoringService(ISystemdRuntimeDriver driver) => _driver = driver;

    public async Task<ValheimMonitorSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var state = await _driver.GetStateAsync(Unit, ct);
        var members = ReadMembers();
        var backups = ListBackups();
        var connectedPlayers = await GetConnectedPlayersAsync(state.InvocationId, ct);
        var onlinePlayers = connectedPlayers.Where(x => x.Online && x.Name is not null).Select(x => x.Name!).ToList();
        var installedBuild = GetInstalledBuildId();
        var latestBuild = await GetLatestBuildIdAsync(ct);
        var updateAvailable = !string.IsNullOrEmpty(installedBuild) &&
                              !string.IsNullOrEmpty(latestBuild) &&
                              installedBuild != latestBuild;
        return new ValheimMonitorSnapshot
        {
            ActiveState = state.ActiveState,
            SubState = state.SubState,
            MainPid = state.MainPid > 0 ? state.MainPid : null,
            InvocationId = state.InvocationId,
            Ready = state.IsRunning,
            Members = members,
            OnlinePlayers = onlinePlayers,
            ConnectedPlayers = connectedPlayers,
            Backups = backups,
            WorldPath = WorldRoot,
            InstalledBuildId = installedBuild,
            LatestBuildId = latestBuild,
            UpdateAvailable = updateAvailable,
        };
    }

    private static async Task<List<ValheimOnlinePlayer>> GetConnectedPlayersAsync(string invocationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(invocationId)) return [];
        var psi = new ProcessStartInfo { FileName = "/usr/bin/journalctl", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add($"_SYSTEMD_INVOCATION_ID={invocationId}");
        psi.ArgumentList.Add("-n"); psi.ArgumentList.Add("5000");
        psi.ArgumentList.Add("--no-pager"); psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("cat");
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start journalctl");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0 ? ValheimOnlinePlayerParser.ParseConnections(output.Split('\n')).ToList() : [];
    }

    public async Task<string> GetLogsAsync(int lines = 200, CancellationToken ct = default)
    {
        lines = Math.Clamp(lines, 1, 1000);
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/journalctl",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-u"); psi.ArgumentList.Add(Unit);
        psi.ArgumentList.Add("-n"); psi.ArgumentList.Add(lines.ToString());
        psi.ArgumentList.Add("--no-pager"); psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("short-iso");
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start journalctl");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException("journalctl failed");
        return output;
    }

    public List<ValheimMember> ReadMembers()
    {
        return [
            .. ReadMemberFile("adminlist.txt", "Admin"),
            .. ReadMemberFile("permittedlist.txt", "Permitted"),
            .. ReadMemberFile("bannedlist.txt", "Banned"),
        ];
    }

    public void AddMember(string id, string role)
    {
        var file = RoleFile(role);
        ValidateId(id);
        var path = Path.Combine(DataRoot, file);
        BackupFile(path);
        var entries = File.Exists(path) ? File.ReadAllLines(path).Select(x => x.Trim()).Where(x => x.Length > 0).ToHashSet() : new();
        entries.Add(id);
        File.WriteAllLines(path, entries.Order(StringComparer.Ordinal));
    }

    public void RemoveMember(string id, string role)
    {
        var file = RoleFile(role);
        ValidateId(id);
        var path = Path.Combine(DataRoot, file);
        if (!File.Exists(path)) return;
        BackupFile(path);
        File.WriteAllLines(path, File.ReadAllLines(path).Where(x => x.Trim() != id));
    }

    /// <summary>Creates a versioned world backup only while the service is inactive.</summary>
    public async Task<ValheimBackup> CreateBackupAsync(CancellationToken ct = default)
    {
        var state = await _driver.GetStateAsync(Unit, ct);
        if (state.ActiveState == "active")
            throw new InvalidOperationException("Stop Valheim before creating a consistent world backup");
        if (!Directory.Exists(WorldRoot)) throw new DirectoryNotFoundException(WorldRoot);

        Directory.CreateDirectory(BackupRoot);
        var name = $"world-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var destination = Path.Combine(BackupRoot, name);
        CopyDirectory(WorldRoot, destination);
        var info = new DirectoryInfo(destination);
        return new ValheimBackup { Name = name, Path = destination, CreatedAt = info.CreationTimeUtc, SizeBytes = DirectorySize(info) };
    }

    public async Task<ValheimBackup> RollbackAsync(string version, CancellationToken ct = default)
    {
        ValidateVersion(version);
        var state = await _driver.GetStateAsync(Unit, ct);
        if (state.ActiveState == "active") throw new InvalidOperationException("Stop Valheim before rollback");
        var source = Path.Combine(BackupRoot, version);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        var head = await CreateBackupAsync(ct);
        var staging = WorldRoot + ".rollback-staging";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        CopyDirectory(source, staging);
        if (Directory.Exists(WorldRoot)) Directory.Delete(WorldRoot, true);
        Directory.Move(staging, WorldRoot);
        return head;
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version != Path.GetFileName(version) || version.Contains(".."))
            throw new ArgumentException("Invalid backup version", nameof(version));
    }

    public List<ValheimBackup> ListBackups()
    {
        if (!Directory.Exists(BackupRoot)) return [];
        return Directory.GetDirectories(BackupRoot)
            .Select(path =>
            {
                var info = new DirectoryInfo(path);
                return new ValheimBackup { Name = info.Name, Path = path, CreatedAt = info.CreationTimeUtc, SizeBytes = DirectorySize(info) };
            })
            .OrderByDescending(x => x.CreatedAt).ToList();
    }

    private static string RoleFile(string role) => role switch
    {
        "Admin" => "adminlist.txt",
        "Permitted" => "permittedlist.txt",
        "Banned" => "bannedlist.txt",
        _ => throw new ArgumentException("Role must be Admin, Permitted or Banned", nameof(role)),
    };

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(char.IsWhiteSpace) || id.Any(c => c is '/' or '\\'))
            throw new ArgumentException("Invalid Valheim member ID", nameof(id));
    }

    private static void BackupFile(string path)
    {
        if (!File.Exists(path)) return;
        Directory.CreateDirectory(BackupRoot);
        File.Copy(path, Path.Combine(BackupRoot, $"{Path.GetFileName(path)}.{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak"));
    }

    private static List<ValheimMember> ReadMemberFile(string file, string role)
    {
        var path = Path.Combine(DataRoot, file);
        if (!File.Exists(path)) return [];
        return File.ReadLines(path)
            .Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#') && !x.StartsWith("//", StringComparison.Ordinal))
            .Select(id => new ValheimMember { Id = id, Role = role, Source = file }).ToList();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static long DirectorySize(DirectoryInfo directory) =>
        directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(x => x.Length);

    public static string? ParseBuildIdFromManifest(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var match = Regex.Match(content, @"""buildid""\s+""(?<buildid>\d+)""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["buildid"].Value : null;
    }

    public string? GetInstalledBuildId(string? customManifestPath = null)
    {
        try
        {
            var path = customManifestPath ?? ManifestPath;
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            return ParseBuildIdFromManifest(text);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string?> GetLatestBuildIdAsync(CancellationToken ct = default, bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedLatestBuildId != null && DateTime.UtcNow - _lastLatestBuildCheck < CacheDuration)
        {
            return _cachedLatestBuildId;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.steamcmd.net/v1/info/896660");
            using var res = await _httpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return _cachedLatestBuildId;

            using var doc = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("896660", out var app) &&
                app.TryGetProperty("depots", out var depots) &&
                depots.TryGetProperty("branches", out var branches) &&
                branches.TryGetProperty("public", out var pub) &&
                pub.TryGetProperty("buildid", out var bid))
            {
                var idStr = bid.GetString();
                if (!string.IsNullOrEmpty(idStr))
                {
                    _cachedLatestBuildId = idStr;
                    _lastLatestBuildCheck = DateTime.UtcNow;
                    return idStr;
                }
            }
        }
        catch
        {
            // fallback gracefully
        }
        return _cachedLatestBuildId;
    }

    public async Task<ValheimUpdateResult> UpdateServerAsync(CancellationToken ct = default)
    {
        var state = await _driver.GetStateAsync(Unit, ct);
        if (state.ActiveState == "active")
        {
            throw new InvalidOperationException("Stop Valheim before updating via SteamCMD");
        }

        if (!File.Exists(SteamCmdBinary))
        {
            throw new FileNotFoundException("SteamCMD executable not found", SteamCmdBinary);
        }

        var psi = new ProcessStartInfo
        {
            FileName = SteamCmdBinary,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["HOME"] = RuntimeRoot;
        psi.ArgumentList.Add("+force_install_dir");
        psi.ArgumentList.Add(ServerRoot);
        psi.ArgumentList.Add("+login");
        psi.ArgumentList.Add("anonymous");
        psi.ArgumentList.Add("+app_update");
        psi.ArgumentList.Add("896660");
        psi.ArgumentList.Add("validate");
        psi.ArgumentList.Add("+quit");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start SteamCMD");
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var output = await outputTask;
        var error = await errorTask;
        var combined = $"{output}\n{error}".Trim();

        return new ValheimUpdateResult
        {
            Success = process.ExitCode == 0,
            ExitCode = process.ExitCode,
            Output = combined,
            InstalledBuildId = GetInstalledBuildId(),
        };
    }
}

public sealed record ValheimMonitorSnapshot
{
    public string ActiveState { get; init; } = "unknown";
    public string SubState { get; init; } = "unknown";
    public int? MainPid { get; init; }
    public string InvocationId { get; init; } = "";
    public bool Ready { get; init; }
    public string WorldPath { get; init; } = "";
    public List<ValheimMember> Members { get; init; } = [];
    public List<string> OnlinePlayers { get; init; } = [];
    public List<ValheimOnlinePlayer> ConnectedPlayers { get; init; } = [];
    public List<ValheimBackup> Backups { get; init; } = [];
    public string? InstalledBuildId { get; init; }
    public string? LatestBuildId { get; init; }
    public bool UpdateAvailable { get; init; }
}

public sealed record ValheimUpdateResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string? InstalledBuildId { get; init; }
}
public sealed record ValheimMember { public string Id { get; init; } = ""; public string Role { get; init; } = ""; public string Source { get; init; } = ""; }
public sealed record ValheimBackup { public string Name { get; init; } = ""; public string Path { get; init; } = ""; public DateTime CreatedAt { get; init; } public long SizeBytes { get; init; } }

public sealed record ValheimOnlinePlayer(string SteamId, string? Name, bool Online);

public static partial class ValheimOnlinePlayerParser
{
    [GeneratedRegex(@"Got character ZDOID from (?<name>[^:]+?)\s*:\s*(?<owner>\d+):(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CharacterState();

    [GeneratedRegex(@"Got connection SteamID (?<steam>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SteamConnection();

    [GeneratedRegex(@"Connections\s+(?<count>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionHeartbeat();

    public static IReadOnlyList<ValheimOnlinePlayer> ParseConnections(IEnumerable<string> lines)
    {
        var players = new List<ValheimOnlinePlayer>();
        foreach (var line in lines)
        {
            var count = ConnectionHeartbeat().Match(line);
            if (count.Success && int.TryParse(count.Groups["count"].Value, out var connected))
            {
                if (connected == 0) players.Clear();
                continue;
            }
            var steam = SteamConnection().Match(line);
            if (steam.Success)
            {
                var id = steam.Groups["steam"].Value;
                if (!players.Any(x => x.SteamId == id)) players.Add(new(id, null, false));
                continue;
            }
            var match = CharacterState().Match(line);
            if (!match.Success) continue;
            var name = match.Groups["name"].Value.Trim();
            if (name.Length is 0 or > 64) continue;
            var online = match.Groups["owner"].Value != "0" || match.Groups["id"].Value != "0";
            var index = players.FindLastIndex(x => x.Name is null);
            if (index >= 0) players[index] = players[index] with { Name = name, Online = online };
            else
            {
                var known = players.FindIndex(x => x.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
                if (known >= 0) players[known] = players[known] with { Online = online };
            }
        }
        return players;
    }

    public static IReadOnlyList<string> Parse(IEnumerable<string> lines)
    {
        var online = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var match = CharacterState().Match(line);
            if (!match.Success) continue;
            var name = match.Groups["name"].Value.Trim();
            if (name.Length is 0 or > 64) continue;
            online[name] = match.Groups["owner"].Value != "0" || match.Groups["id"].Value != "0";
        }
        return online.Where(x => x.Value).Select(x => x.Key).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
