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
            var result = new PzBuildInfoService(root).Read();
            Assert.Equal("12345678", result.BuildId);
            Assert.NotNull(result.LastUpdatedUtc);
            Assert.Null(result.Error);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingManifestReturnsUnavailableRatherThanInventingBuild()
    {
        var result = new PzBuildInfoService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Read();
        Assert.Null(result.BuildId);
        Assert.Null(result.LastUpdatedUtc);
        Assert.NotNull(result.Error);
    }
}
