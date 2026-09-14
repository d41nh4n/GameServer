namespace GamePanel.Infrastructure.GameServers;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed record ValheimModStagingResult(
    string DeploymentId,
    string State,
    string PackageId,
    string Version,
    string ArchiveSha256,
    int FileCount);

public sealed class ValheimModStagingService
{
    private const long MaxArchiveBytes = 100L * 1024 * 1024;
    private const long MaxUncompressedBytes = 512L * 1024 * 1024;
    private const int MaxEntries = 4096;
    private static readonly Regex DeploymentIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BuildPattern = new(
        "^[0-9]{1,20}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> LoaderRootFiles = new(StringComparer.Ordinal)
    {
        ".doorstop_version",
        "doorstop_config.ini",
        "libdoorstop.so",
        "start_game_bepinex.sh",
        "start_server_bepinex.sh",
        "changelog.txt",
    };

    private readonly string _stagingRoot;

    public ValheimModStagingService(string stagingRoot)
    {
        if (string.IsNullOrWhiteSpace(stagingRoot))
            throw new ArgumentException("Staging root is required.", nameof(stagingRoot));
        _stagingRoot = Path.GetFullPath(stagingRoot);
    }

    public async Task<ValheimModStagingResult> StageAsync(
        string deploymentId,
        ThunderstoreResolvedVersion package,
        string packageType,
        string testedGameBuild,
        Stream archiveStream,
        CancellationToken ct = default)
    {
        ValidateInputs(deploymentId, package, packageType, testedGameBuild, archiveStream);
        Directory.CreateDirectory(_stagingRoot);
        SetDirectoryMode(_stagingRoot);

        var finalRoot = SafeChild(_stagingRoot, deploymentId);
        if (Directory.Exists(finalRoot) || File.Exists(finalRoot))
            throw new InvalidOperationException("Deployment ID already exists.");

        var tempRoot = SafeChild(_stagingRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        SetDirectoryMode(tempRoot);

        try
        {
            var archivePath = Path.Combine(tempRoot, "archive.zip");
            var archiveHash = await CopyArchiveAsync(archiveStream, archivePath, ct);
            var normalizedRoot = Path.Combine(tempRoot, "normalized");
            Directory.CreateDirectory(normalizedRoot);
            SetDirectoryMode(normalizedRoot);

            List<StagedFile> files;
            List<ExcludedFile> excluded;
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                (files, excluded) = await ValidateAndExtractAsync(
                    archive,
                    normalizedRoot,
                    package,
                    packageType,
                    ct);
            }

            var manifest = new DeploymentManifest(
                "valheim-main",
                deploymentId,
                package.PackageId,
                package.Version,
                packageType,
                "linux-x64",
                testedGameBuild,
                "validated",
                archiveHash,
                files,
                package.Dependencies,
                excluded);
            var manifestPath = Path.Combine(tempRoot, "deployment-manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }) + "\n",
                ct);
            SetFileMode(manifestPath);
            Directory.Move(tempRoot, finalRoot);

            return new ValheimModStagingResult(
                deploymentId,
                "validated",
                package.PackageId,
                package.Version,
                archiveHash,
                files.Count);
        }
        catch
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
            throw;
        }
    }

    private static void ValidateInputs(
        string deploymentId,
        ThunderstoreResolvedVersion package,
        string packageType,
        string testedGameBuild,
        Stream archiveStream)
    {
        if (string.IsNullOrWhiteSpace(deploymentId) || !DeploymentIdPattern.IsMatch(deploymentId))
            throw new ArgumentException("Invalid deployment ID.", nameof(deploymentId));
        if (packageType is not ("loader" or "plugin" or "mod"))
            throw new ArgumentException("Package type must be loader, plugin, or mod.", nameof(packageType));
        if (string.IsNullOrWhiteSpace(testedGameBuild) || !BuildPattern.IsMatch(testedGameBuild))
            throw new ArgumentException("Invalid tested game build.", nameof(testedGameBuild));
        if (!archiveStream.CanRead)
            throw new ArgumentException("Archive stream is not readable.", nameof(archiveStream));
    }

    private static async Task<string> CopyArchiveAsync(Stream source, string target, CancellationToken ct)
    {
        await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, ct);
            if (count == 0) break;
            total += count;
            if (total > MaxArchiveBytes) throw new InvalidDataException("Archive exceeds 100MB size limit.");
            hash.AppendData(buffer, 0, count);
            await destination.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        await destination.FlushAsync(ct);
        SetFileMode(target);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<(List<StagedFile> Files, List<ExcludedFile> Excluded)> ValidateAndExtractAsync(
        ZipArchive archive,
        string normalizedRoot,
        ThunderstoreResolvedVersion package,
        string packageType,
        CancellationToken ct)
    {
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidDataException("Archive exceeds file-count limit.");

        var manifestEntries = archive.Entries.Where(x => x.FullName == "manifest.json" && !IsDirectory(x)).ToList();
        if (manifestEntries.Count != 1)
            throw new InvalidDataException("Archive must contain exactly one root manifest.json.");

        await ValidateNativeManifestAsync(manifestEntries[0], package, ct);
        var files = new List<StagedFile>();
        var excluded = new List<ExcludedFile>();
        var destinations = new HashSet<string>(StringComparer.Ordinal);
        long totalUncompressed = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var source = ValidateEntryPath(entry);
            totalUncompressed += entry.Length;
            if (totalUncompressed > MaxUncompressedBytes)
                throw new InvalidDataException("Archive uncompressed size exceeds limit.");
            if (IsDirectory(entry) || source == "manifest.json") continue;

            var destination = MapDestination(source, package.PackageId, package.Name, packageType);
            if (destination is null)
            {
                if (IsExcludedMetadata(source, packageType))
                {
                    excluded.Add(new ExcludedFile(source, "package metadata or platform-specific bootstrap"));
                    continue;
                }
                throw new InvalidDataException($"Archive route is not allowed: {source}");
            }
            if (!destinations.Add(destination))
                throw new InvalidDataException($"Duplicate normalized destination: {destination}");

            var outputPath = SafeChild(normalizedRoot, destination.Replace('/', Path.DirectorySeparatorChar));
            var outputDirectory = Path.GetDirectoryName(outputPath)!;
            Directory.CreateDirectory(outputDirectory);
            SetDirectoryModeChain(normalizedRoot, outputDirectory);
            await using (var input = entry.Open())
            await using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                await input.CopyToAsync(output, ct);
            SetFileMode(outputPath);
            files.Add(new StagedFile(destination, destination, await ComputeSha256Async(outputPath, ct)));
        }

        if (files.Count == 0)
            throw new InvalidDataException("Package contains no allowlisted runtime files.");
        return (files, excluded);
    }

    private static async Task ValidateNativeManifestAsync(
        ZipArchiveEntry entry,
        ThunderstoreResolvedVersion package,
        CancellationToken ct)
    {
        if (entry.Length > 1024 * 1024)
            throw new InvalidDataException("Thunderstore manifest is too large.");
        await using var stream = entry.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
        var version = root.TryGetProperty("version_number", out var versionValue) ? versionValue.GetString() : null;
        if (!string.Equals(name, package.Name, StringComparison.Ordinal) ||
            !string.Equals(version, package.Version, StringComparison.Ordinal))
            throw new InvalidDataException("Thunderstore manifest identity does not match the pinned package.");

        if (!root.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Thunderstore manifest dependencies are missing.");
        var manifestDependencies = dependencies.EnumerateArray().Select(x => x.GetString()).ToList();
        if (manifestDependencies.Any(x => string.IsNullOrWhiteSpace(x)) ||
            !manifestDependencies.Cast<string>().Order(StringComparer.Ordinal).SequenceEqual(package.Dependencies.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Thunderstore manifest dependencies do not match resolved metadata.");
    }

    private static string ValidateEntryPath(ZipArchiveEntry entry)
    {
        var name = entry.FullName.TrimEnd('/');
        if (string.IsNullOrEmpty(name)) return "";
        if (entry.FullName.Contains('\\') || entry.FullName.Contains('\0') || entry.FullName.StartsWith('/') ||
            (entry.FullName.Length > 1 && entry.FullName[1] == ':'))
            throw new InvalidDataException("Archive contains an unsafe path.");
        var segments = name.Split('/');
        if (segments.Any(x => x.Length == 0 || x is "." or ".."))
            throw new InvalidDataException("Archive contains path traversal or a non-canonical path.");
        var fileType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (fileType == 0xA000)
            throw new InvalidDataException("Archive symlinks are not allowed.");
        return name;
    }

    private static string? MapDestination(string source, string packageId, string packageName, string packageType)
    {
        var wrapper = packageName + "/";
        if (source.StartsWith(wrapper, StringComparison.Ordinal))
            source = source[wrapper.Length..];
        if (packageType == "loader")
        {
            if (source.StartsWith("BepInEx/plugins/", StringComparison.Ordinal) ||
                source.StartsWith("BepInEx/config/", StringComparison.Ordinal) ||
                source.StartsWith("BepInEx/core/", StringComparison.Ordinal) ||
                source.StartsWith("BepInEx/patchers/", StringComparison.Ordinal) ||
                source.StartsWith("BepInEx/monomod/", StringComparison.Ordinal) ||
                source.StartsWith("doorstop_libs/", StringComparison.Ordinal) ||
                LoaderRootFiles.Contains(source))
                return source;
            return null;
        }
        if (source.StartsWith("BepInEx/plugins/", StringComparison.Ordinal) ||
            source.StartsWith("BepInEx/config/", StringComparison.Ordinal))
            return source;
        if (source.StartsWith("plugins/", StringComparison.Ordinal) ||
            source.StartsWith("config/", StringComparison.Ordinal))
            return "BepInEx/" + source;
        if (!source.Contains('/') && source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return $"BepInEx/plugins/{packageId}/{source}";
        return null;
    }

    private static bool IsExcludedMetadata(string source, string packageType)
    {
        var fileName = Path.GetFileName(source);
        return fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("icon.png", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectory(ZipArchiveEntry entry) => entry.FullName.EndsWith('/');

    private static string SafeChild(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, StringComparison.Ordinal))
            throw new InvalidDataException("Resolved path is outside the staging root.");
        return full;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void SetDirectoryModeChain(string root, string leaf)
    {
        var current = new DirectoryInfo(leaf);
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        while (current.FullName.StartsWith(rootPath, StringComparison.Ordinal))
        {
            SetDirectoryMode(current.FullName);
            if (current.FullName == rootPath || current.Parent is null) break;
            current = current.Parent;
        }
    }

    private static void SetDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
    }

    private static void SetFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
    }

    private sealed record StagedFile(string Source, string Destination, string Sha256);
    private sealed record ExcludedFile(string Source, string Reason);
    private sealed record DeploymentManifest(
        string InstanceId,
        string DeploymentId,
        string PackageId,
        string Version,
        string PackageType,
        string TargetPlatform,
        string TestedGameBuild,
        string State,
        string ArchiveSha256,
        IReadOnlyList<StagedFile> Files,
        IReadOnlyList<string> Dependencies,
        IReadOnlyList<ExcludedFile> PlatformExcluded);
}
