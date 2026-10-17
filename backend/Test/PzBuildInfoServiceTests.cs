using GamePanel.Infrastructure.GameServers;

public sealed class PzBuildInfoServiceTests
{
    [Fact]
    public async Task ReadsInstalledBuildIdAndManifestTimestamp()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "steamapps", "appmanifest_380870.acf");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        await File.WriteAllTextAsync(manifest, "\"AppState\"\n{\n  \"appid\" \"380870\"\n  \"buildid\" \"12345678\"\n}\n");
        try
        {
            var result = new PzBuildInfoService(root, Path.Combine(root, "Logs")).Read();
            Assert.Equal("12345678", result.BuildId);
            Assert.NotNull(result.LastUpdatedUtc);
            Assert.Null(result.Error);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ReadsGameVersionFromLatestServerDebugLog()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var manifest = Path.Combine(root, "steamapps", "appmanifest_380870.acf");
        var logs = Path.Combine(root, "Logs");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        Directory.CreateDirectory(logs);
        await File.WriteAllTextAsync(manifest, "\"AppState\"\n{\n  \"buildid\" \"12345678\"\n}\n");
        await File.WriteAllTextAsync(Path.Combine(logs, "2026-10-17_DebugLog-server.txt"), "LOG : General > version=42.21.0 abc123 demo=false.\n");
        try
        {
            var result = new PzBuildInfoService(root, logs).Read();
            Assert.Equal("42.21.0", result.GameVersion);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingManifestReturnsUnavailableRatherThanInventingBuild()
    {
        var result = new PzBuildInfoService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Read();
        Assert.Null(result.BuildId);
        Assert.Null(result.LastUpdatedUtc);
        Assert.NotNull(result.Error);
    }
}
