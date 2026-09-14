namespace GamePanel.Infrastructure.GameServers;

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed record ClientModpackManifest(
    int SchemaVersion,
    string ProfileId,
    string Revision,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ClientModpackPackage> Packages);

public sealed record ClientModpackPackage(
    string PackageId,
    string Version,
    string Target,
    string DownloadPath,
    string ArchiveSha256,
    IReadOnlyList<ClientModpackFile> Files);

public sealed record ClientModpackFile(string Path, string Sha256);
public sealed record ClientModpackDownload(byte[] Bytes, string DownloadName);

public sealed class ClientModpackService
{
    private const int MaxManifestBytes = 1024 * 1024;
    private const int MaxPackageBytes = 256 * 1024 * 1024;
    private static readonly Regex IdPattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new("^[0-9A-Za-z][0-9A-Za-z.+_-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex HashPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly string _root;

    public ClientModpackService(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Client modpack root is required.", nameof(root));
        _root = Path.GetFullPath(root);
    }

    public async Task<ClientModpackManifest> GetManifestAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(_root, "current", "manifest.json");
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("No approved client modpack is published.");
        if (info.Length <= 0 || info.Length > MaxManifestBytes) throw new InvalidDataException("Client modpack manifest size is invalid.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var manifest = await JsonSerializer.DeserializeAsync<ClientModpackManifest>(stream, JsonOptions, ct)
            ?? throw new InvalidDataException("Client modpack manifest is invalid.");
        ValidateManifest(manifest);
        return manifest;
    }

    public async Task<ClientModpackDownload> GetPackageAsync(string packageId, string version, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(version);
        if (!IdPattern.IsMatch(packageId) || !VersionPattern.IsMatch(version))
            throw new ArgumentException("Invalid package identity.");
        var manifest = await GetManifestAsync(ct);
        var package = manifest.Packages.SingleOrDefault(x => x.PackageId == packageId && x.Version == version)
            ?? throw new FileNotFoundException("Package is not part of the current approved client modpack.");
        var expectedPath = $"/api/client-updater/packages/{packageId}/{version}";
        if (package.DownloadPath != expectedPath) throw new InvalidDataException("Package download path does not match its identity.");

        var path = Path.Combine(_root, "current", "packages", packageId, version + ".zip");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > MaxPackageBytes)
            throw new FileNotFoundException("Approved client package file is unavailable.");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var actual = SHA256.HashData(bytes);
        var expected = Convert.FromHexString(package.ArchiveSha256);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            throw new InvalidDataException("Approved client package hash mismatch.");
        return new ClientModpackDownload(bytes, $"{packageId}-{version}.zip");
    }

    private static void ValidateManifest(ClientModpackManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.ProfileId != "valheim-main" || !IdPattern.IsMatch(manifest.Revision ?? ""))
            throw new InvalidDataException("Client modpack manifest identity is invalid.");
        if (manifest.Packages is null) throw new InvalidDataException("Client modpack package list is missing.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            var packageId = package.PackageId;
            if (packageId is null || !IdPattern.IsMatch(packageId) || !VersionPattern.IsMatch(package.Version ?? "") ||
                package.Target is not ("client-only" or "client-server") || !HashPattern.IsMatch(package.ArchiveSha256 ?? ""))
                throw new InvalidDataException("Client modpack package metadata is invalid.");
            if (!ids.Add(packageId)) throw new InvalidDataException("Duplicate client package.");
            if (package.Files is null || package.Files.Count == 0) throw new InvalidDataException("Client package has no files.");
            foreach (var file in package.Files)
            {
                var filePath = file.Path;
                if (filePath is null || string.IsNullOrWhiteSpace(filePath) || filePath.Contains("..") || filePath.Contains('\\') || filePath.StartsWith('/') ||
                    !HashPattern.IsMatch(file.Sha256 ?? "") || !paths.Add(filePath))
                    throw new InvalidDataException("Client modpack file metadata is invalid.");
            }
        }
    }
}
