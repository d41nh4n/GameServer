namespace GamePanel.ContractTests;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GamePanel.Infrastructure.GameServers;
using Xunit;

public sealed class ValheimModStagingServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "valheim_stage_test_" + Guid.NewGuid().ToString("N"));

    public ValheimModStagingServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task StageAsync_AllowsPinnedLoaderRoutes()
    {
        var resolved = new ThunderstoreResolvedVersion("denikson-BepInExPack_Valheim", "denikson", "BepInExPack_Valheim", "5.4.2350", "https://gcdn.thunderstore.io/loader.zip", []);
        await using var archive = CreateArchive(
            ("manifest.json", "{\"name\":\"BepInExPack_Valheim\",\"version_number\":\"5.4.2350\",\"dependencies\":[]}"),
            ("BepInExPack_Valheim/BepInEx/core/BepInEx.dll", "core"),
            ("BepInExPack_Valheim/doorstop_libs/libdoorstop_x64.so", "native"),
            ("BepInExPack_Valheim/doorstop_config.ini", "doorstop"),
            ("BepInExPack_Valheim/winhttp.dll", "windows-only"));
        var service = new ValheimModStagingService(_root);

        var result = await service.StageAsync("loader-5.4.2350", resolved, "loader", "25253791", archive);

        Assert.Equal(3, result.FileCount);
        Assert.True(File.Exists(Path.Combine(_root, "loader-5.4.2350", "normalized", "BepInEx", "core", "BepInEx.dll")));
        Assert.True(File.Exists(Path.Combine(_root, "loader-5.4.2350", "normalized", "doorstop_config.ini")));
    }

    [Fact]
    public async Task StageAsync_NormalizesPinnedPackageWithoutTouchingProduction()
    {
        var resolved = new ThunderstoreResolvedVersion(
            "Author-TestMod", "Author", "TestMod", "1.2.3",
            "https://gcdn.thunderstore.io/live/repository/packages/Author-TestMod-1.2.3.zip",
            ["denikson-BepInExPack_Valheim-5.4.2202"]);
        await using var archive = CreateArchive(
            ("manifest.json", "{\"name\":\"TestMod\",\"version_number\":\"1.2.3\",\"dependencies\":[\"denikson-BepInExPack_Valheim-5.4.2202\"]}"),
            ("plugins/TestMod/TestMod.dll", "plugin"),
            ("config/Author.TestMod.cfg", "enabled=true"));
        var service = new ValheimModStagingService(_root);

        var result = await service.StageAsync("dep-001", resolved, "plugin", "25253791", archive);

        Assert.Equal("validated", result.State);
        Assert.True(File.Exists(Path.Combine(_root, "dep-001", "normalized", "BepInEx", "plugins", "TestMod", "TestMod.dll")));
        Assert.False(Directory.Exists(Path.Combine(_root, "server")));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "dep-001", "deployment-manifest.json")));
        Assert.Equal("validated", manifest.RootElement.GetProperty("state").GetString());
        Assert.Equal("Author-TestMod", manifest.RootElement.GetProperty("packageId").GetString());
        Assert.Equal("25253791", manifest.RootElement.GetProperty("testedGameBuild").GetString());
        Assert.Equal(2, manifest.RootElement.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task StageAsync_RejectsTraversalAndRemovesPartialDeployment()
    {
        var resolved = new ThunderstoreResolvedVersion("Author-TestMod", "Author", "TestMod", "1.2.3", "https://gcdn.thunderstore.io/mod.zip", []);
        await using var archive = CreateArchive(
            ("manifest.json", "{\"name\":\"TestMod\",\"version_number\":\"1.2.3\",\"dependencies\":[]}"),
            ("plugins/../../escape.dll", "bad"));
        var service = new ValheimModStagingService(_root);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.StageAsync("dep-bad", resolved, "plugin", "25253791", archive));

        Assert.False(Directory.Exists(Path.Combine(_root, "dep-bad")));
    }

    [Fact]
    public async Task StageAsync_RejectsManifestIdentityMismatch()
    {
        var resolved = new ThunderstoreResolvedVersion("Author-TestMod", "Author", "TestMod", "1.2.3", "https://gcdn.thunderstore.io/mod.zip", []);
        await using var archive = CreateArchive(
            ("manifest.json", "{\"name\":\"OtherMod\",\"version_number\":\"1.2.3\",\"dependencies\":[]}"),
            ("plugins/TestMod.dll", "plugin"));
        var service = new ValheimModStagingService(_root);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.StageAsync("dep-mismatch", resolved, "plugin", "25253791", archive));
    }

    private static MemoryStream CreateArchive(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var item = zip.CreateEntry(entry.Name);
                using var writer = new StreamWriter(item.Open(), Encoding.UTF8, leaveOpen: false);
                writer.Write(entry.Content);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
