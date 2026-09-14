namespace GamePanel.ClientUpdater.Tests;

using GamePanel.ClientUpdater;

public sealed class UpdaterOptionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gamepanel_options_test_" + Guid.NewGuid().ToString("N"));

    public UpdaterOptionsTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Parse_AllowsTailscaleHttpAndExplicitGameDirectory()
    {
        File.WriteAllText(Path.Combine(_root, "valheim.exe"), "test");

        var options = UpdaterOptions.Parse([
            "--api-base", "http://100.82.102.38:5000",
            "--game-dir", _root,
            "--check-only",
        ]);

        Assert.Equal("http://100.82.102.38:5000/", options.ApiBase.ToString());
        Assert.Equal(Path.GetFullPath(_root), options.GameDirectory);
        Assert.True(options.CheckOnly);
    }

    [Fact]
    public void Parse_RejectsPlainHttpOutsideLoopbackOrTailscale()
    {
        Assert.Throws<ArgumentException>(() => UpdaterOptions.Parse([
            "--api-base", "http://example.com",
            "--game-dir", _root,
        ]));
    }

    [Fact]
    public void ResolveGameDirectory_RequiresValheimExecutable()
    {
        Assert.Throws<DirectoryNotFoundException>(() => SteamGameLocator.Resolve(_root));
    }
}
