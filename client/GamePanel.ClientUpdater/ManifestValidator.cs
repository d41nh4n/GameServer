namespace GamePanel.ClientUpdater;

using System.Text.RegularExpressions;

public static class ManifestValidator
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new("^[0-9A-Za-z][0-9A-Za-z.+_-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex HashPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> LoaderRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doorstop_version",
        "doorstop_config.ini",
        "winhttp.dll",
    };

    public static void Validate(ClientUpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported manifest schema version.");
        if (manifest.ProfileId != "valheim-main") throw new InvalidDataException("Manifest profile is not allowlisted.");
        if (!IdPattern.IsMatch(manifest.Revision)) throw new InvalidDataException("Invalid manifest revision.");
        if (manifest.Packages is null) throw new InvalidDataException("Manifest packages are required.");

        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            if (!IdPattern.IsMatch(package.PackageId) || !VersionPattern.IsMatch(package.Version))
                throw new InvalidDataException("Invalid package identity.");
            if (!packageIds.Add(package.PackageId)) throw new InvalidDataException("Duplicate package ID.");
            if (package.Target is not ("client-only" or "client-server"))
                throw new InvalidDataException("Server-only packages cannot be installed on clients.");
            if (string.IsNullOrWhiteSpace(package.DownloadPath) ||
                !package.DownloadPath.StartsWith("/api/client-updater/packages/", StringComparison.Ordinal) ||
                package.DownloadPath.Contains("..", StringComparison.Ordinal) ||
                package.DownloadPath.Contains('?') ||
                package.DownloadPath.Contains('#'))
                throw new InvalidDataException("Package download path is not allowlisted.");
            RequireHash(package.ArchiveSha256);
            if (package.Files is null || package.Files.Count == 0)
                throw new InvalidDataException("Package file list is empty.");

            foreach (var file in package.Files)
            {
                ValidateRelativePath(file.Path);
                if (!IsAllowedTarget(file.Path)) throw new InvalidDataException($"Client package path is not allowlisted: {file.Path}");
                RequireHash(file.Sha256);
                if (!paths.Add(file.Path)) throw new InvalidDataException($"Duplicate client target path: {file.Path}");
            }
        }
    }

    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains('\0') || path.StartsWith('/') ||
            (path.Length > 1 && path[1] == ':'))
            throw new InvalidDataException("Unsafe client package path.");
        var segments = path.Split('/');
        if (segments.Any(x => x.Length == 0 || x is "." or ".."))
            throw new InvalidDataException("Client package path traversal detected.");
    }

    private static bool IsAllowedTarget(string path) =>
        path.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("doorstop_libs/", StringComparison.OrdinalIgnoreCase) ||
        LoaderRootFiles.Contains(path);

    private static void RequireHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || !HashPattern.IsMatch(hash))
            throw new InvalidDataException("Invalid SHA-256 value.");
    }
}
