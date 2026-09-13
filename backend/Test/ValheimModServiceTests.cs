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
                Exists: true,
                ActiveState: IsRunning ? "active" : "inactive",
                SubState: IsRunning ? "running" : "dead",
                MainPid: IsRunning ? 1234 : null,
                InvocationId: "inv-1"
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

    private sealed class MockHttpMessageHandler(byte[] responseBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBytes)
            };
            return Task.FromResult(response);
        }
    }
}
