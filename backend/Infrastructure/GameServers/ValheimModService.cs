namespace GamePanel.Infrastructure.GameServers;

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

public sealed record ValheimMod(
    string Name,
    string FileName,
    string RelativePath,
    long SizeBytes,
    bool Enabled,
    DateTime LastModifiedUtc,
    bool HasConfig,
    string? ConfigFileName,
    string? PackageFullName = null,
    string? InstalledVersion = null,
    string? LatestVersion = null,
    bool HasUpdate = false,
    string? UpdateDownloadUrl = null
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

            var (packageFullName, installedVersion) = ResolveModMetadata(file, relativePath);

            result.Add(new ValheimMod(
                Name: name,
                FileName: fileName,
                RelativePath: relativePath,
                SizeBytes: fi.Length,
                Enabled: !isDisabled,
                LastModifiedUtc: fi.LastWriteTimeUtc,
                HasConfig: matchingConfig is not null,
                ConfigFileName: matchingConfig,
                PackageFullName: packageFullName,
                InstalledVersion: installedVersion
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

        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                break;
            }
            catch (Exception) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                response?.Dispose();
                response = null;
                await Task.Delay(1000 * attempt, ct);
            }
        }
        if (response is null)
        {
            response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        using var finalResponse = response;

        var contentLength = finalResponse.Content.Headers.ContentLength;
        const long maxPackageSizeBytes = 300L * 1024 * 1024;
        if (contentLength.HasValue && contentLength.Value > maxPackageSizeBytes)
        {
            throw new InvalidOperationException("Mod package exceeds 300MB size limit.");
        }

        using var memoryStream = new MemoryStream();
        await finalResponse.Content.CopyToAsync(memoryStream, ct);
        if (memoryStream.Length > maxPackageSizeBytes)
        {
            throw new InvalidOperationException("Mod package exceeds 300MB size limit.");
        }
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
                if (lowerName is "icon.png" or "readme.md" or "changelog.md")
                {
                    continue;
                }

                if (lowerName == "manifest.json")
                {
                    destinationPath = Path.GetFullPath(Path.Combine(_pluginsDir, cleanPackageName, "manifest.json"));
                }
                else if (ext == ".cfg")
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

            if (destinationPath.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) && File.Exists(destinationPath))
            {
                continue;
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
            createdFiles.Add(IsPathWithin(normalizedPluginsDir, destinationPath)
                ? ToApiRelativePath(Path.GetRelativePath(_pluginsDir, destinationPath))
                : ToApiRelativePath(Path.GetRelativePath(_configDir, destinationPath)));
        }

        SavePackageMetadata(archive, downloadUrl, packageFullName, cleanPackageName);

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

    private void SavePackageMetadata(ZipArchive archive, string downloadUrl, string packageFullName, string cleanPackageName)
    {
        string? manifestVersion = null;
        string? manifestName = null;
        var manifestEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is not null)
        {
            try
            {
                using var s = manifestEntry.Open();
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.TryGetProperty("version_number", out var vProp))
                    manifestVersion = vProp.GetString();
                if (doc.RootElement.TryGetProperty("name", out var nProp))
                    manifestName = nProp.GetString();
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(manifestVersion))
        {
            var match = Regex.Match(downloadUrl, @"/(\d+\.\d+(?:\.\d+)?(?:[.\d]+)?)/?$");
            if (match.Success) manifestVersion = match.Groups[1].Value;
        }

        var metaDir = Path.Combine(_pluginsDir, cleanPackageName);
        if (Directory.Exists(metaDir))
        {
            try
            {
                var metaPath = Path.Combine(metaDir, ".tsmeta.json");
                var metaContent = JsonSerializer.Serialize(new
                {
                    packageFullName,
                    name = manifestName ?? cleanPackageName,
                    versionNumber = manifestVersion ?? "",
                    installedAtUtc = DateTime.UtcNow,
                    downloadUrl
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(metaPath, metaContent);
            }
            catch { }
        }
    }

    public (string? PackageFullName, string? InstalledVersion) ResolveModMetadata(string fullPath, string relativePath)
    {
        string? packageFullName = null;
        string? installedVersion = null;

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Contains('/'))
        {
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var topDir = parts[0];
            var topDirPath = Path.Combine(_pluginsDir, topDir);

            // 1. Check .tsmeta.json
            var tsmetaPath = Path.Combine(topDirPath, ".tsmeta.json");
            if (File.Exists(tsmetaPath))
            {
                try
                {
                    using var stream = File.OpenRead(tsmetaPath);
                    using var doc = JsonDocument.Parse(stream);
                    if (doc.RootElement.TryGetProperty("packageFullName", out var pkgProp))
                        packageFullName = pkgProp.GetString();
                    if (doc.RootElement.TryGetProperty("versionNumber", out var vProp))
                        installedVersion = vProp.GetString();
                }
                catch { }
            }

            // 2. Check manifest.json
            if (string.IsNullOrWhiteSpace(installedVersion))
            {
                var manifestPath = Path.Combine(topDirPath, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        using var stream = File.OpenRead(manifestPath);
                        using var doc = JsonDocument.Parse(stream);
                        if (doc.RootElement.TryGetProperty("version_number", out var vProp))
                            installedVersion = vProp.GetString();
                    }
                    catch { }
                }
            }

            // 3. Infer package name from directory if contains '-'
            if (string.IsNullOrWhiteSpace(packageFullName) && topDir.Contains('-'))
            {
                packageFullName = topDir;
            }
        }

        // 4. Try reading version from DLL header via PEReader
        if (string.IsNullOrWhiteSpace(installedVersion) && File.Exists(fullPath))
        {
            installedVersion = TryGetAssemblyVersion(fullPath);
        }

        return (packageFullName, installedVersion);
    }

    public static string? TryGetAssemblyVersion(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            if (peReader.HasMetadata)
            {
                var reader = peReader.GetMetadataReader();
                if (reader.IsAssembly)
                {
                    var assemblyDef = reader.GetAssemblyDefinition();
                    var v = assemblyDef.Version;
                    if (v.Major != 0 || v.Minor != 0 || v.Build != 0 || v.Revision != 0)
                    {
                        return v.Revision > 0 ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}" : $"{v.Major}.{v.Minor}.{v.Build}";
                    }
                }
            }
        }
        catch { }

        try
        {
            var fvi = FileVersionInfo.GetVersionInfo(dllPath);
            if (!string.IsNullOrWhiteSpace(fvi.ProductVersion) && fvi.ProductVersion != "0.0.0.0")
            {
                var clean = fvi.ProductVersion.Split('+')[0].Trim();
                if (!string.IsNullOrWhiteSpace(clean)) return clean;
            }
            if (!string.IsNullOrWhiteSpace(fvi.FileVersion) && fvi.FileVersion != "0.0.0.0")
            {
                var clean = fvi.FileVersion.Split('+')[0].Trim();
                if (!string.IsNullOrWhiteSpace(clean)) return clean;
            }
        }
        catch { }

        return null;
    }

    public static bool IsNewerVersion(string latestVersion, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(latestVersion) || string.IsNullOrWhiteSpace(currentVersion))
            return false;

        var cleanLatest = latestVersion.TrimStart('v', 'V').Trim();
        var cleanCurrent = currentVersion.TrimStart('v', 'V').Trim();

        if (string.Equals(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Version.TryParse(cleanLatest, out var vLatest) && Version.TryParse(cleanCurrent, out var vCurrent))
        {
            return vLatest > vCurrent;
        }

        var partsL = cleanLatest.Split('.');
        var partsC = cleanCurrent.Split('.');
        var maxLen = Math.Max(partsL.Length, partsC.Length);
        for (var i = 0; i < maxLen; i++)
        {
            var pL = i < partsL.Length && int.TryParse(partsL[i], out var numL) ? numL : 0;
            var pC = i < partsC.Length && int.TryParse(partsC[i], out var numC) ? numC : 0;
            if (pL > pC) return true;
            if (pL < pC) return false;
        }

        return false;
    }

    public IReadOnlyList<ValheimMod> CheckModUpdates(IReadOnlyList<ThunderstorePackageSummary> thunderstorePackages)
    {
        var installedMods = ListMods();
        if (installedMods.Count == 0 || thunderstorePackages.Count == 0)
            return installedMods;

        var tsMap = new Dictionary<string, ThunderstorePackageSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in thunderstorePackages)
        {
            tsMap.TryAdd(p.FullName, p);
        }

        var updatedList = new List<ValheimMod>(installedMods.Count);
        foreach (var mod in installedMods)
        {
            var pkgName = mod.PackageFullName;
            var installedVer = mod.InstalledVersion;

            if (string.IsNullOrWhiteSpace(pkgName))
            {
                var match = thunderstorePackages.FirstOrDefault(p =>
                    p.Name.Equals(mod.Name, StringComparison.OrdinalIgnoreCase) ||
                    p.FullName.EndsWith($"-{mod.Name}", StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    pkgName = match.FullName;
                }
            }

            if (!string.IsNullOrWhiteSpace(pkgName) && tsMap.TryGetValue(pkgName, out var tsPkg))
            {
                var latestVer = tsPkg.VersionNumber;
                var hasUpdate = !string.IsNullOrWhiteSpace(installedVer) && IsNewerVersion(latestVer, installedVer);
                updatedList.Add(mod with
                {
                    PackageFullName = pkgName,
                    LatestVersion = latestVer,
                    HasUpdate = hasUpdate,
                    UpdateDownloadUrl = tsPkg.DownloadUrl
                });
            }
            else
            {
                updatedList.Add(mod);
            }
        }

        return updatedList;
    }
}
