namespace GamePanel.Infrastructure.GameServers;

using System.IO.Compression;
using Microsoft.Extensions.Configuration;

public sealed record ValheimMod(
    string Name,
    string FileName,
    string RelativePath,
    long SizeBytes,
    bool Enabled,
    DateTime LastModifiedUtc,
    bool HasConfig,
    string? ConfigFileName
);

public sealed class ValheimModService
{
    private readonly ISystemdRuntimeDriver _driver;
    private readonly string _bepInExRoot;
    private readonly string _pluginsDir;
    private readonly string _configDir;
    private const string Unit = "valheim-main.service";

    public ValheimModService(ISystemdRuntimeDriver driver, IConfiguration? config = null)
        : this(driver, config?["Valheim:BepInExRoot"] ?? "/srv/gamepanel/instances/valheim-main/server/BepInEx")
    {
    }

    public ValheimModService(ISystemdRuntimeDriver driver, string bepInExRoot)
    {
        _driver = driver;
        _bepInExRoot = bepInExRoot;
        _pluginsDir = Path.Combine(_bepInExRoot, "plugins");
        _configDir = Path.Combine(_bepInExRoot, "config");
    }

    public async Task EnsureServerStoppedAsync(CancellationToken ct = default)
    {
        var state = await _driver.GetStateAsync(Unit, ct);
        if (state.IsRunning)
        {
            throw new InvalidOperationException("Server must be stopped to modify mods or configuration.");
        }
    }

    public IReadOnlyList<ValheimMod> ListMods()
    {
        if (!Directory.Exists(_pluginsDir)) return [];

        var configFiles = Directory.Exists(_configDir)
            ? Directory.GetFiles(_configDir, "*.cfg").Select(Path.GetFileName).Where(x => x is not null).Cast<string>().ToList()
            : [];

        var result = new List<ValheimMod>();
        var files = Directory.GetFiles(_pluginsDir, "*.*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var isDll = fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            var isDisabled = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

            if (!isDll && !isDisabled) continue;

            var relativePath = ToApiRelativePath(Path.GetRelativePath(_pluginsDir, file));
            var fi = new FileInfo(file);

            var name = fileName;
            if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                name = name[..^".disabled".Length];
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                name = name[..^".dll".Length];

            var matchingConfig = configFiles.FirstOrDefault(c =>
                c.Equals($"{name}.cfg", StringComparison.OrdinalIgnoreCase) ||
                c.Contains(name, StringComparison.OrdinalIgnoreCase));

            result.Add(new ValheimMod(
                Name: name,
                FileName: fileName,
                RelativePath: relativePath,
                SizeBytes: fi.Length,
                Enabled: !isDisabled,
                LastModifiedUtc: fi.LastWriteTimeUtc,
                HasConfig: matchingConfig is not null,
                ConfigFileName: matchingConfig
            ));
        }

        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<ValheimMod> ToggleModAsync(string relativePath, CancellationToken ct = default)
    {
        await EnsureServerStoppedAsync(ct);
        var fullPath = GetSafePluginsPath(relativePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Mod file not found: {relativePath}");

        string newFullPath;
        if (fullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            newFullPath = fullPath[..^".disabled".Length];
        }
        else if (fullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            newFullPath = fullPath + ".disabled";
        }
        else
        {
            throw new InvalidOperationException("Only .dll or .disabled mod files can be toggled.");
        }

        if (File.Exists(newFullPath)) File.Delete(newFullPath);
        File.Move(fullPath, newFullPath);

        var fi = new FileInfo(newFullPath);
        var fileName = Path.GetFileName(newFullPath);
        var newRelPath = ToApiRelativePath(Path.GetRelativePath(_pluginsDir, newFullPath));
        var name = fileName;
        if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            name = name[..^".disabled".Length];
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            name = name[..^".dll".Length];

        var configExists = Directory.Exists(_configDir) &&
            Directory.GetFiles(_configDir, "*.cfg").Any(c => Path.GetFileName(c).Contains(name, StringComparison.OrdinalIgnoreCase));

        return new ValheimMod(
            Name: name,
            FileName: fileName,
            RelativePath: newRelPath,
            SizeBytes: fi.Length,
            Enabled: !newFullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
            LastModifiedUtc: fi.LastWriteTimeUtc,
            HasConfig: configExists,
            ConfigFileName: configExists ? $"{name}.cfg" : null
        );
    }

    public async Task DeleteModAsync(string relativePath, CancellationToken ct = default)
    {
        await EnsureServerStoppedAsync(ct);
        var fullPath = GetSafePluginsPath(relativePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Mod file not found: {relativePath}");

        File.Delete(fullPath);
    }

    public async Task<string> GetConfigAsync(string configFileName, CancellationToken ct = default)
    {
        var fullPath = GetSafeConfigPath(configFileName);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Config file not found: {configFileName}");

        return await File.ReadAllTextAsync(fullPath, ct);
    }

    public async Task SaveConfigAsync(string configFileName, string content, CancellationToken ct = default)
    {
        await EnsureServerStoppedAsync(ct);
        var fullPath = GetSafeConfigPath(configFileName);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(fullPath, content, ct);
    }

    public async Task<IReadOnlyList<string>> UploadModAsync(string fileName, Stream stream, CancellationToken ct = default)
    {
        await EnsureServerStoppedAsync(ct);
        var cleanFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(cleanFileName))
            throw new ArgumentException("Invalid file name.");

        var ext = Path.GetExtension(cleanFileName).ToLowerInvariant();
        if (ext is not ".dll" and not ".zip")
            throw new ArgumentException("Only .dll and .zip files are supported.");

        if (!Directory.Exists(_pluginsDir)) Directory.CreateDirectory(_pluginsDir);

        var createdFiles = new List<string>();

        if (ext == ".dll")
        {
            var targetPath = GetSafePluginsPath(cleanFileName);
            using (var fileStream = File.Create(targetPath))
            {
                await stream.CopyToAsync(fileStream, ct);
            }
            createdFiles.Add(cleanFileName);
        }
        else if (ext == ".zip")
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var normalizedPluginsDir = Path.GetFullPath(_pluginsDir);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var destinationPath = Path.GetFullPath(Path.Combine(_pluginsDir, entry.FullName));
                if (!destinationPath.StartsWith(normalizedPluginsDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Zip entry attempts path traversal: {entry.FullName}");
                }

                var entryDir = Path.GetDirectoryName(destinationPath);
                if (entryDir is not null && !Directory.Exists(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
                createdFiles.Add(Path.GetRelativePath(_pluginsDir, destinationPath).Replace('\\', '/'));
            }
        }

        return createdFiles;
    }

    public async Task<IReadOnlyList<string>> InstallThunderstoreModAsync(
        string downloadUrl,
        string packageFullName,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        await EnsureServerStoppedAsync(ct);

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            throw new ArgumentException("Invalid download URL.");
        }

        var host = uri.Host.ToLowerInvariant();
        if (!host.EndsWith("thunderstore.io", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Untrusted download host. Only thunderstore.io is supported.");
        }

        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > 100 * 1024 * 1024)
        {
            throw new InvalidOperationException("Mod package exceeds 100MB size limit.");
        }

        using var memoryStream = new MemoryStream();
        await response.Content.CopyToAsync(memoryStream, ct);
        memoryStream.Position = 0;

        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);
        var createdFiles = new List<string>();

        var normalizedPluginsDir = Path.GetFullPath(_pluginsDir);
        var normalizedConfigDir = Path.GetFullPath(_configDir);
        if (!Directory.Exists(_pluginsDir)) Directory.CreateDirectory(_pluginsDir);
        if (!Directory.Exists(_configDir)) Directory.CreateDirectory(_configDir);

        var fileEntries = archive.Entries
            .Where(entry => !IsArchiveDirectory(entry))
            .Select(entry => (Entry: entry, Path: NormalizeArchiveEntryPath(entry.FullName)))
            .ToList();
        var hasPluginsPrefix = fileEntries.Any(item =>
            item.Path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) ||
            item.Path.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase));

        var cleanPackageName = Path.GetFileName(packageFullName.Replace('\\', '/').Trim('/'));
        if (string.IsNullOrWhiteSpace(cleanPackageName)) cleanPackageName = "UnknownMod";

        var extractionPlan = new List<(ZipArchiveEntry Entry, string DestinationPath)>();
        var plannedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in fileEntries)
        {
            var entry = item.Entry;
            var entryPath = item.Path;
            var entryName = Path.GetFileName(entryPath);
            string destinationPath;

            if (hasPluginsPrefix)
            {
                if (entryPath.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase))
                {
                    var sub = entryPath["BepInEx/plugins/".Length..];
                    destinationPath = Path.GetFullPath(Path.Combine(_pluginsDir, sub));
                }
                else if (entryPath.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))
                {
                    var sub = entryPath["plugins/".Length..];
                    destinationPath = Path.GetFullPath(Path.Combine(_pluginsDir, sub));
                }
                else if (entryPath.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase))
                {
                    var sub = entryPath["BepInEx/config/".Length..];
                    destinationPath = Path.GetFullPath(Path.Combine(_configDir, sub));
                }
                else if (entryPath.StartsWith("config/", StringComparison.OrdinalIgnoreCase))
                {
                    var sub = entryPath["config/".Length..];
                    destinationPath = Path.GetFullPath(Path.Combine(_configDir, sub));
                }
                else if (entryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    destinationPath = Path.GetFullPath(Path.Combine(_pluginsDir, entryName));
                }
                else
                {
                    continue;
                }
            }
            else
            {
                var ext = Path.GetExtension(entryName).ToLowerInvariant();
                var lowerName = entryName.ToLowerInvariant();
                if (lowerName is "manifest.json" or "icon.png" or "readme.md" or "changelog.md")
                {
                    continue;
                }

                if (ext == ".cfg")
                {
                    destinationPath = Path.GetFullPath(Path.Combine(_configDir, entryName));
                }
                else
                {
                    destinationPath = Path.GetFullPath(Path.Combine(_pluginsDir, cleanPackageName, entryPath));
                }
            }

            if (!IsPathWithin(normalizedPluginsDir, destinationPath) &&
                !IsPathWithin(normalizedConfigDir, destinationPath))
            {
                throw new InvalidOperationException($"Zip entry attempts path traversal: {entryPath}");
            }
            if (!plannedDestinations.Add(destinationPath))
                throw new InvalidOperationException($"Duplicate normalized zip destination: {entryPath}");

            extractionPlan.Add((entry, destinationPath));
        }

        foreach (var (entry, destinationPath) in extractionPlan)
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (dir is not null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            entry.ExtractToFile(destinationPath, overwrite: true);
            createdFiles.Add(IsPathWithin(normalizedPluginsDir, destinationPath)
                ? ToApiRelativePath(Path.GetRelativePath(_pluginsDir, destinationPath))
                : ToApiRelativePath(Path.GetRelativePath(_configDir, destinationPath)));
        }

        return createdFiles;
    }

    public void ExportClientModpack(Stream outputStream)
    {
        using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);

        if (Directory.Exists(_pluginsDir))
        {
            var files = Directory.GetFiles(_pluginsDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = ToApiRelativePath(Path.GetRelativePath(_pluginsDir, file));
                archive.CreateEntryFromFile(file, $"BepInEx/plugins/{rel}", CompressionLevel.Optimal);
            }
        }

        if (Directory.Exists(_configDir))
        {
            var configFiles = Directory.GetFiles(_configDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in configFiles)
            {
                var rel = ToApiRelativePath(Path.GetRelativePath(_configDir, file));
                archive.CreateEntryFromFile(file, $"BepInEx/config/{rel}", CompressionLevel.Optimal);
            }
        }
    }

    private string GetSafePluginsPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("Path cannot be empty.");
        if (relativePath.Contains("..")) throw new ArgumentException("Path traversal detected.");

        var normalizedPluginsDir = Path.GetFullPath(_pluginsDir);
        var fullPath = Path.GetFullPath(Path.Combine(_pluginsDir, relativePath));

        if (!fullPath.StartsWith(normalizedPluginsDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Path is outside the plugins directory.");
        }

        return fullPath;
    }

    private static bool IsArchiveDirectory(ZipArchiveEntry entry) =>
        string.IsNullOrEmpty(entry.Name) ||
        entry.FullName.EndsWith('/') ||
        entry.FullName.EndsWith('\\');

    private static string NormalizeArchiveEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
            throw new InvalidOperationException("Zip entry path is invalid.");

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
            throw new InvalidOperationException($"Zip entry attempts path traversal: {path}");

        var segments = normalized.Split('/');
        if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".."))
            throw new InvalidOperationException($"Zip entry attempts path traversal: {path}");

        return string.Join('/', segments);
    }

    private static bool IsPathWithin(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    private static string ToApiRelativePath(string path) =>
        Path.DirectorySeparatorChar == '/' ? path : path.Replace(Path.DirectorySeparatorChar, '/');

    private string GetSafeConfigPath(string configFileName)
    {
        if (string.IsNullOrWhiteSpace(configFileName)) throw new ArgumentException("Config file name cannot be empty.");
        if (configFileName.Contains("..") || configFileName.Contains('/') || configFileName.Contains('\\'))
        {
            throw new ArgumentException("Invalid config file name.");
        }
        if (!configFileName.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
        {
            configFileName += ".cfg";
        }

        var normalizedConfigDir = Path.GetFullPath(_configDir);
        var fullPath = Path.GetFullPath(Path.Combine(_configDir, configFileName));

        if (!fullPath.StartsWith(normalizedConfigDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Path is outside the config directory.");
        }

        return fullPath;
    }
}
