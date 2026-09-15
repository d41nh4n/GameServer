namespace GamePanel.ClientUpdater;

using System.Net;

public sealed record UpdaterOptions(
    Uri ApiBase,
    string GameDirectory,
    bool CheckOnly,
    bool Launch,
    bool ShowHelp)
{
    public static UpdaterOptions Parse(string[] args)
    {
        string apiBase = "http://100.82.102.38:5001";
        string? gameDirectory = null;
        var checkOnly = false;
        var launch = false;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--api-base" when i + 1 < args.Length: apiBase = args[++i]; break;
                case "--game-dir" when i + 1 < args.Length: gameDirectory = args[++i]; break;
                case "--check-only": checkOnly = true; break;
                case "--launch": launch = true; break;
                case "--help" or "-h": showHelp = true; break;
                default: throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("API base must be an absolute HTTP(S) URL.");
        if (uri.Scheme == "http" && !IsTrustedPlainHttpHost(uri.Host))
            throw new ArgumentException("Plain HTTP is allowed only over loopback or Tailscale CGNAT addresses.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("API base must not contain credentials, query, or fragment.");

        var game = showHelp ? gameDirectory ?? "" : SteamGameLocator.Resolve(gameDirectory);
        return new UpdaterOptions(new Uri(uri.ToString().TrimEnd('/') + "/"), game, checkOnly, launch, showHelp);
    }

    private static bool IsTrustedPlainHttpHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }
}
