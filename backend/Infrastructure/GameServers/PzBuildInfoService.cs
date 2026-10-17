using System.Text.RegularExpressions;

namespace GamePanel.Infrastructure.GameServers;

public sealed class PzBuildInfoService
{
    private const string AppId = "380870";
    private static readonly Regex BuildIdPattern = new("\\\"buildid\\\"\\s+\\\"(?<id>[0-9]+)\\\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string _manifestPath;

    public PzBuildInfoService(string installPath)
    {
        _manifestPath = Path.Combine(installPath, "steamapps", $"appmanifest_{AppId}.acf");
    }

    public PzBuildInfo Read()
    {
        try
        {
            if (!File.Exists(_manifestPath))
                return new(null, null, "Steam app manifest not found; configure the PZ install path or run SteamCMD update.");

            var text = File.ReadAllText(_manifestPath);
            var match = BuildIdPattern.Match(text);
            if (!match.Success)
                return new(null, File.GetLastWriteTimeUtc(_manifestPath), "Steam app manifest does not contain a valid build ID.");

            return new(match.Groups["id"].Value, File.GetLastWriteTimeUtc(_manifestPath), null);
        }
        catch (IOException)
        {
            return new(null, null, "Steam app manifest is currently unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return new(null, null, "Panel cannot read the Steam app manifest.");
        }
    }
}

public sealed record PzBuildInfo(string? BuildId, DateTime? LastUpdatedUtc, string? Error);
