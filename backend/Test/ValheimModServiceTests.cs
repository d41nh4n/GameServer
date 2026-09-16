namespace GamePanel.ContractTests;

using System.IO.Compression;
using GamePanel.Infrastructure.GameServers;
using Xunit;

public sealed class ValheimModServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _pluginsDir;
    private readonly string _configDir;
    private readonly MockSystemdDriver _driver;

    public ValheimModServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "valheim_mods_test_" + Guid.NewGuid().ToString("N"));
        _pluginsDir = Path.Combine(_tempDir, "plugins");
        _configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(_pluginsDir);
        Directory.CreateDirectory(_configDir);
        _driver = new MockSystemdDriver(isRunning: false);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private sealed class MockSystemdDriver(bool isRunning = false) : ISystemdRuntimeDriver
    {
        public bool IsRunning { get; set; } = isRunning;

        public Task<SystemdUnitState> GetStateAsync(string unitName, CancellationToken ct = default)
        {
            return Task.FromResult(new SystemdUnitState(
                true,
                IsRunning ? "active" : "inactive",
                IsRunning ? "running" : "dead",
                IsRunning ? 1234 : 0,
                "inv-1"
            ));
        }

        public Task<bool> ControlAsync(string unitName, SystemdControlAction action, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    [Fact]
    public void ListMods_ReturnsPluginFiles_WithConfigMatch()
    {
        File.WriteAllText(Path.Combine(_pluginsDir, "ValheimPlus.dll"), "dummy dll");
        File.WriteAllText(Path.Combine(_pluginsDir, "ServerCharacters.dll.disabled"), "dummy dll 2");
        File.WriteAllText(Path.Combine(_configDir, "ValheimPlus.cfg"), "[General]\nEnabled=true");

        var service = new ValheimModService(_driver, _tempDir);
        var mods = service.ListMods();

        Assert.Equal(2, mods.Count);

        var vplus = mods.First(m => m.Name == "ValheimPlus");
        Assert.True(vplus.Enabled);
        Assert.Equal("ValheimPlus.dll", vplus.FileName);
        Assert.True(vplus.HasConfig);
        Assert.Equal("ValheimPlus.cfg", vplus.ConfigFileName);

        var sc = mods.First(m => m.Name == "ServerCharacters");
        Assert.False(sc.Enabled);
        Assert.Equal("ServerCharacters.dll.disabled", sc.FileName);
    }

    [Fact]
    public async Task ToggleMod_RenamesDllToDisabled_AndBack()
    {
        var modPath = Path.Combine(_pluginsDir, "TestMod.dll");
        File.WriteAllText(modPath, "dummy");

        var service = new ValheimModService(_driver, _tempDir);

        // Toggle to disabled
        var toggled = await service.ToggleModAsync("TestMod.dll");
        Assert.False(toggled.Enabled);
        Assert.Equal("TestMod.dll.disabled", toggled.FileName);
        Assert.False(File.Exists(modPath));
        Assert.True(File.Exists(Path.Combine(_pluginsDir, "TestMod.dll.disabled")));

        // Toggle back to enabled
        var toggledBack = await service.ToggleModAsync("TestMod.dll.disabled");
        Assert.True(toggledBack.Enabled);
        Assert.Equal("TestMod.dll", toggledBack.FileName);
        Assert.True(File.Exists(modPath));
    }

    [Fact]
    public async Task ToggleMod_Throws_WhenServerRunning()
    {
        _driver.IsRunning = true;
        File.WriteAllText(Path.Combine(_pluginsDir, "TestMod.dll"), "dummy");

        var service = new ValheimModService(_driver, _tempDir);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ToggleModAsync("TestMod.dll"));
    }

    [Fact]
    public async Task DeleteMod_RemovesFile()
    {
        var modPath = Path.Combine(_pluginsDir, "ToDelete.dll");
        File.WriteAllText(modPath, "dummy");

        var service = new ValheimModService(_driver, _tempDir);
        await service.DeleteModAsync("ToDelete.dll");

        Assert.False(File.Exists(modPath));
    }

    [Fact]
    public async Task GetConfig_And_SaveConfig_WorkCorrectly()
    {
        var service = new ValheimModService(_driver, _tempDir);

        await service.SaveConfigAsync("CustomMod.cfg", "[Config]\nKey=Value");
        Assert.True(File.Exists(Path.Combine(_configDir, "CustomMod.cfg")));

        var content = await service.GetConfigAsync("CustomMod.cfg");
        Assert.Contains("Key=Value", content);
    }

    [Fact]
    public async Task PathTraversal_IsRejected()
    {
        var service = new ValheimModService(_driver, _tempDir);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ToggleModAsync("../evil.dll"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteModAsync("../../etc/passwd"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetConfigAsync("../secret.cfg"));
    }

    [Fact]
    public async Task UploadMod_DllAndZip_ExtractCorrectly()
    {
        var service = new ValheimModService(_driver, _tempDir);

        // Test DLL upload
        using var dllStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("dll contents"));
        var files1 = await service.UploadModAsync("Uploaded.dll", dllStream);
        Assert.Contains("Uploaded.dll", files1);
        Assert.True(File.Exists(Path.Combine(_pluginsDir, "Uploaded.dll")));

        // Test Zip upload
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("ZipMod/ZipMod.dll");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("zip dll");
        }
        zipStream.Position = 0;

        var files2 = await service.UploadModAsync("bundle.zip", zipStream);
        Assert.Contains("ZipMod/ZipMod.dll", files2);
        Assert.True(File.Exists(Path.Combine(_pluginsDir, "ZipMod", "ZipMod.dll")));
    }

    [Fact]
    public void ExportClientModpack_IncludesEnabledPluginsAndConfigs_ExcludesDisabled()
    {
        File.WriteAllText(Path.Combine(_pluginsDir, "Active.dll"), "active mod");
        File.WriteAllText(Path.Combine(_pluginsDir, "Disabled.dll.disabled"), "disabled mod");
        File.WriteAllText(Path.Combine(_configDir, "Active.cfg"), "setting=1");

        var service = new ValheimModService(_driver, _tempDir);
        using var memoryStream = new MemoryStream();
        service.ExportClientModpack(memoryStream);

        memoryStream.Position = 0;
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        var entryNames = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("BepInEx/plugins/Active.dll", entryNames);
        Assert.Contains("BepInEx/config/Active.cfg", entryNames);
        Assert.DoesNotContain("BepInEx/plugins/Disabled.dll.disabled", entryNames);
    }

    [Fact]
    public async Task InstallThunderstoreMod_RejectsUntrustedHost()
    {
        var service = new ValheimModService(_driver, _tempDir);
        using var client = new HttpClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.InstallThunderstoreModAsync("https://evil.com/fake.zip", "evil-pack", client));
    }

    [Fact]
    public async Task InstallThunderstoreMod_ExtractsZipSafely()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("plugins/TestTs/TestTs.dll");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("ts mod dll");
        }
        zipStream.Position = 0;

        var handler = new MockHttpMessageHandler(zipStream.ToArray());
        using var client = new HttpClient(handler);

        var service = new ValheimModService(_driver, _tempDir);
        var files = await service.InstallThunderstoreModAsync(
            "https://valheim.thunderstore.io/package/download/author/testts/1.0.0/",
            "author-testts",
            client);

        Assert.Contains("TestTs/TestTs.dll", files);
        Assert.True(File.Exists(Path.Combine(_pluginsDir, "TestTs", "TestTs.dll")));
    }

    [Fact]
    public async Task InstallThunderstoreMod_NormalizesWindowsArchiveSeparators()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("plugins\\AutoRepair.dll");
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync("auto repair"u8.ToArray());
        }
        zipStream.Position = 0;

        var handler = new MockHttpMessageHandler(zipStream.ToArray());
        using var client = new HttpClient(handler);
        var service = new ValheimModService(_driver, _tempDir);

        var files = await service.InstallThunderstoreModAsync(
            "https://valheim.thunderstore.io/package/download/Tekla/AutoRepair/5.4.1602/",
            "Tekla-AutoRepair",
            client);

        Assert.Contains("AutoRepair.dll", files);
        Assert.True(File.Exists(Path.Combine(_pluginsDir, "AutoRepair.dll")));
        Assert.False(File.Exists(Path.Combine(_pluginsDir, "Tekla-AutoRepair", "plugins\\AutoRepair.dll")));
    }

    [Fact]
    public async Task InstallThunderstoreMod_RejectsPathsThatCollideAfterNormalization()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var first = archive.CreateEntry("plugins\\Duplicate.dll").Open())
                await first.WriteAsync("first"u8.ToArray());
            await using (var second = archive.CreateEntry("plugins/Duplicate.dll").Open())
                await second.WriteAsync("second"u8.ToArray());
        }
        zipStream.Position = 0;

        var handler = new MockHttpMessageHandler(zipStream.ToArray());
        using var client = new HttpClient(handler);
        var service = new ValheimModService(_driver, _tempDir);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallThunderstoreModAsync(
            "https://valheim.thunderstore.io/package/download/Author/Duplicate/1.0.0/",
            "Author-Duplicate",
            client));
        Assert.False(File.Exists(Path.Combine(_pluginsDir, "Duplicate.dll")));
    }

    [Fact]
    public async Task InstallThunderstoreMod_RejectsBackslashTraversalBeforeExtraction()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("plugins\\..\\..\\escape.dll");
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync("escape"u8.ToArray());
        }
        zipStream.Position = 0;

        var handler = new MockHttpMessageHandler(zipStream.ToArray());
        using var client = new HttpClient(handler);
        var service = new ValheimModService(_driver, _tempDir);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallThunderstoreModAsync(
            "https://valheim.thunderstore.io/package/download/Author/Escape/1.0.0/",
            "Author-Escape",
            client));
        Assert.False(File.Exists(Path.Combine(_tempDir, "escape.dll")));
    }

    [Fact]
    public async Task InstallThunderstoreMod_RejectsPackagesExceeding300MbLimit()
    {
        var handler = new MockHttpMessageHandler([], contentLengthOverride: 301L * 1024 * 1024);
        using var client = new HttpClient(handler);
        var service = new ValheimModService(_driver, _tempDir);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallThunderstoreModAsync(
            "https://valheim.thunderstore.io/package/download/Author/HugeMod/1.0.0/",
            "Author-HugeMod",
            client));
        Assert.Equal("Mod package exceeds 300MB size limit.", ex.Message);
    }

    [Theory]
    [InlineData("1.9.1", "1.7.5", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("v1.2.0", "1.2.0", false)]
    [InlineData("1.2.0", null, false)]
    public void IsNewerVersion_CorrectlyComparesVersions(string latest, string? current, bool expected)
    {
        var actual = ValheimModService.IsNewerVersion(latest, current);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task InstallThunderstoreMod_PreservesExistingConfigAndSavesMetadata()
    {
        var configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(configDir);
        var existingConfigPath = Path.Combine(configDir, "TestMod.cfg");
        await File.WriteAllTextAsync(existingConfigPath, "UserCustomSetting=True");

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            await using (var entryStream = manifestEntry.Open())
            {
                await entryStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(
                    """{"name":"TestMod","version_number":"1.2.0","description":"test"}"""
                ));
            }

            var dllEntry = archive.CreateEntry("TestMod.dll");
            await using (var entryStream = dllEntry.Open())
            {
                await entryStream.WriteAsync("dllcontent"u8.ToArray());
            }

            var cfgEntry = archive.CreateEntry("TestMod.cfg");
            await using (var entryStream = cfgEntry.Open())
            {
                await entryStream.WriteAsync("DefaultSetting=False"u8.ToArray());
            }
        }
        zipStream.Position = 0;

        var handler = new MockHttpMessageHandler(zipStream.ToArray());
        using var client = new HttpClient(handler);
        var service = new ValheimModService(_driver, _tempDir);

        var files = await service.InstallThunderstoreModAsync(
            "https://valheim.thunderstore.io/package/download/Author/TestMod/1.2.0/",
            "Author-TestMod",
            client);

        // Verify config is NOT overwritten
        var configContent = await File.ReadAllTextAsync(existingConfigPath);
        Assert.Equal("UserCustomSetting=True", configContent);

        // Verify .tsmeta.json is saved
        var metaPath = Path.Combine(_tempDir, "plugins", "Author-TestMod", ".tsmeta.json");
        Assert.True(File.Exists(metaPath));
        var metaText = await File.ReadAllTextAsync(metaPath);
        Assert.Contains("1.2.0", metaText);

        // Verify check updates detects current version and up to date
        var packages = new List<ThunderstorePackageSummary>
        {
            new("TestMod", "Author-TestMod", "Author", "https://ts.io", "1.2.0", null, "desc", "https://valheim.thunderstore.io/package/download/Author/TestMod/1.2.0/", 10, null, DateTime.UtcNow)
        };
        var checkedMods = service.CheckModUpdates(packages);
        var installedMod = checkedMods.Single(m => m.Name == "TestMod");
        Assert.Equal("1.2.0", installedMod.InstalledVersion);
        Assert.False(installedMod.HasUpdate);

        // Verify check updates flags when newer version exists
        var newerPackages = new List<ThunderstorePackageSummary>
        {
            new("TestMod", "Author-TestMod", "Author", "https://ts.io", "1.3.0", null, "desc", "https://valheim.thunderstore.io/package/download/Author/TestMod/1.3.0/", 15, null, DateTime.UtcNow)
        };
        var newerCheckedMods = service.CheckModUpdates(newerPackages);
        var updatedMod = newerCheckedMods.Single(m => m.Name == "TestMod");
        Assert.True(updatedMod.HasUpdate);
        Assert.Equal("1.3.0", updatedMod.LatestVersion);
    }

    private sealed class MockHttpMessageHandler(byte[] responseBytes, long? contentLengthOverride = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBytes)
            };
            if (contentLengthOverride.HasValue)
            {
                response.Content.Headers.ContentLength = contentLengthOverride.Value;
            }
            return Task.FromResult(response);
        }
    }
}
