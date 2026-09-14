namespace GamePanel.ContractTests;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GamePanel.Infrastructure.GameServers;
using Xunit;

public sealed class ClientModpackServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "client_modpack_test_" + Guid.NewGuid().ToString("N"));

    public ClientModpackServiceTests() => Directory.CreateDirectory(Path.Combine(_root, "current", "packages", "Advize-PlantEverything"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public async Task ReadsCurrentManifestAndOnlyServesHashMatchedPackage()
    {
        var archive = Zip(("BepInEx/plugins/Advize_PlantEverything.dll", "plugin"));
        var hash = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var packagePath = Path.Combine(_root, "current", "packages", "Advize-PlantEverything", "1.21.2.zip");
        await File.WriteAllBytesAsync(packagePath, archive);
        await WriteManifestAsync(hash);
        var service = new ClientModpackService(_root);

        var manifest = await service.GetManifestAsync();
        var package = await service.GetPackageAsync("Advize-PlantEverything", "1.21.2");

        Assert.Equal("plant-everything-1.21.2", manifest.Revision);
        Assert.Equal(archive, package.Bytes);
        Assert.Equal("Advize-PlantEverything-1.21.2.zip", package.DownloadName);
    }

    [Fact]
    public async Task RejectsPackageWhenArchiveHashDoesNotMatchManifest()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "current", "packages", "Advize-PlantEverything", "1.21.2.zip"), [1, 2, 3]);
        await WriteManifestAsync(new string('a', 64));
        var service = new ClientModpackService(_root);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.GetPackageAsync("Advize-PlantEverything", "1.21.2"));
    }

    [Fact]
    public async Task RejectsInvalidPackageIdentityBeforeFilesystemLookup()
    {
        var service = new ClientModpackService(_root);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetPackageAsync("../escape", "1.0.0"));
    }

    private async Task WriteManifestAsync(string archiveHash)
    {
        var manifest = new
        {
            schemaVersion = 1,
            profileId = "valheim-main",
            revision = "plant-everything-1.21.2",
            generatedAtUtc = DateTimeOffset.UtcNow,
            packages = new[]
            {
                new
                {
                    packageId = "Advize-PlantEverything",
                    version = "1.21.2",
                    target = "client-server",
                    downloadPath = "/api/client-updater/packages/Advize-PlantEverything/1.21.2",
                    archiveSha256 = archiveHash,
                    files = new[] { new { path = "BepInEx/plugins/Advize_PlantEverything.dll", sha256 = new string('b', 64) } },
                },
            },
        };
        await File.WriteAllTextAsync(Path.Combine(_root, "current", "manifest.json"), JsonSerializer.Serialize(manifest));
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Name);
                using var writer = new StreamWriter(zipEntry.Open());
                writer.Write(entry.Content);
            }
        return memory.ToArray();
    }
}
