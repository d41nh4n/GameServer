namespace GamePanel.ClientUpdater;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

public sealed class PackageInstaller
{
    private const long MaxArchiveBytes = 256L * 1024 * 1024;
    private const long MaxUncompressedBytes = 1024L * 1024 * 1024;
    private const int MaxEntries = 8192;

    public async Task<UpdateResult> ApplyAsync(
        string gameDirectory,
        ClientUpdateManifest manifest,
        Func<ClientModPackage, CancellationToken, Task<Stream>> download,
        bool checkOnly,
        CancellationToken ct = default)
    {
        ManifestValidator.Validate(manifest);
        var gameRoot = Path.GetFullPath(gameDirectory);
        if (!Directory.Exists(gameRoot)) throw new DirectoryNotFoundException("Valheim game directory was not found.");

        var payloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            await using var source = await download(package, ct);
            var archiveBytes = await ReadBoundedAsync(source, MaxArchiveBytes, ct);
            RequireHash(archiveBytes, package.ArchiveSha256, "Package archive hash mismatch.");
            ExtractVerifiedPackage(package, archiveBytes, payloads);
        }

        var changed = new List<string>();
        foreach (var item in payloads)
        {
            var target = SafeTarget(gameRoot, item.Key);
            if (!File.Exists(target) || !await FileMatchesAsync(target, item.Value, ct)) changed.Add(item.Key);
        }
        if (checkOnly || changed.Count == 0)
            return new UpdateResult(manifest.Revision, checkOnly, changed, null);

        var updaterRoot = Path.Combine(gameRoot, ".gamepanel-updater");
        var backupRoot = Path.Combine(updaterRoot, "backups", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + manifest.Revision);
        var applied = new List<(string RelativePath, bool Existed)>();
        Directory.CreateDirectory(backupRoot);

        try
        {
            foreach (var relativePath in changed)
            {
                ct.ThrowIfCancellationRequested();
                var target = SafeTarget(gameRoot, relativePath);
                var existed = File.Exists(target);
                if (existed)
                {
                    var backup = SafeTarget(backupRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, overwrite: false);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + ".gamepanel-" + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temporary, payloads[relativePath], ct);
                File.Move(temporary, target, overwrite: true);
                applied.Add((relativePath, existed));
            }

            var statePath = Path.Combine(updaterRoot, "current-manifest.json");
            Directory.CreateDirectory(updaterRoot);
            var stateTemporary = statePath + ".tmp";
            await File.WriteAllTextAsync(
                stateTemporary,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }) + Environment.NewLine,
                ct);
            File.Move(stateTemporary, statePath, overwrite: true);
        }
        catch
        {
            foreach (var item in applied.AsEnumerable().Reverse())
            {
                var target = SafeTarget(gameRoot, item.RelativePath);
                if (item.Existed)
                {
                    var backup = SafeTarget(backupRoot, item.RelativePath);
                    if (File.Exists(backup)) File.Copy(backup, target, overwrite: true);
                }
                else
                {
                    File.Delete(target);
                }
            }
            throw;
        }

        return new UpdateResult(manifest.Revision, false, changed, backupRoot);
    }

    private static void ExtractVerifiedPackage(
        ClientModPackage package,
        byte[] archiveBytes,
        IDictionary<string, byte[]> payloads)
    {
        using var memory = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("Package archive contains too many entries.");

        var expected = package.Files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long uncompressed = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            ManifestValidator.ValidateRelativePath(entry.FullName);
            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000) throw new InvalidDataException("Package archive symlinks are not allowed.");
            if (!expected.TryGetValue(entry.FullName, out var declared))
                throw new InvalidDataException($"Undeclared package file: {entry.FullName}");
            if (!seen.Add(entry.FullName)) throw new InvalidDataException($"Duplicate archive entry: {entry.FullName}");
            uncompressed += entry.Length;
            if (uncompressed > MaxUncompressedBytes) throw new InvalidDataException("Package archive is too large after extraction.");

            using var input = entry.Open();
            var bytes = ReadBoundedAsync(input, MaxUncompressedBytes, CancellationToken.None).GetAwaiter().GetResult();
            RequireHash(bytes, declared.Sha256, $"File hash mismatch: {entry.FullName}");
            payloads.Add(entry.FullName, bytes);
        }
        if (seen.Count != expected.Count) throw new InvalidDataException("Package archive is missing declared files.");
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, long limit, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, ct);
            if (count == 0) break;
            total += count;
            if (total > limit) throw new InvalidDataException("Downloaded package exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return output.ToArray();
    }

    private static void RequireHash(byte[] bytes, string expected, string message)
    {
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(actual),
                System.Text.Encoding.ASCII.GetBytes(expected.ToLowerInvariant())))
            throw new InvalidDataException(message);
    }

    private static string SafeTarget(string root, string relativePath)
    {
        ManifestValidator.ValidateRelativePath(relativePath.Replace(Path.DirectorySeparatorChar, '/'));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Resolved package path escapes the target directory.");
        return target;
    }

    private static async Task<bool> FileMatchesAsync(string path, byte[] expected, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(expected));
    }
}
