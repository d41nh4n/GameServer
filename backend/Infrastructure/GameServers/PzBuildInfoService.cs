using System.Text.RegularExpressions;

namespace GamePanel.Infrastructure.GameServers;

public sealed class PzBuildInfoService
{
    private const string AppId = "380870";
    private static readonly Regex BuildIdPattern = new("\\\"buildid\\\"\\s+\\\"(?<id>[0-9]+)\\\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GameVersionPattern = new("version=(?<version>[0-9]+\\.[0-9]+\\.[0-9]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string _manifestPath;
    private readonly string _logsPath;

    public PzBuildInfoService(string installPath, string logsPath)
    {
        _manifestPath = Path.Combine(installPath, "steamapps", $"appmanifest_{AppId}.acf");
        _logsPath = logsPath;
    }

    private string? ReadGameVersion()
    {
        try
        {
            if (!Directory.Exists(_logsPath)) return null;
            foreach (var path in Directory.EnumerateFiles(_logsPath, "*DebugLog-server.txt")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var match = GameVersionPattern.Match(File.ReadAllText(path));
                if (match.Success) return match.Groups["version"].Value;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    public PzBuildInfo Read()
    {
        try
        {
            if (!File.Exists(_manifestPath))
                return new(null, null, null, "Steam app manifest not found; configure the PZ install path or run SteamCMD update.");

            var text = File.ReadAllText(_manifestPath);
            var match = BuildIdPattern.Match(text);
            if (!match.Success)
                return new(null, null, File.GetLastWriteTimeUtc(_manifestPath), "Steam app manifest does not contain a valid build ID.");

            return new(match.Groups["id"].Value, ReadGameVersion(), File.GetLastWriteTimeUtc(_manifestPath), null);
        }
        catch (IOException)
        {
            return new(null, null, null, "Steam app manifest is currently unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return new(null, null, null, "Panel cannot read the Steam app manifest.");
        }
    }
}

public sealed record PzBuildInfo(string? BuildId, string? GameVersion, DateTime? LastUpdatedUtc, string? Error);
