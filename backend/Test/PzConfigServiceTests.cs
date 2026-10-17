using GamePanel.Infrastructure.GameServers;

public sealed class PzConfigServiceTests
{
    [Fact]
    public void WriteUpdatesPreservesOtherKeysAndOriginalLineEndings()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var iniPath = Path.Combine(root, "servertest_new.ini");
        var backupDir = Path.Combine(root, "panel_backups");
        const string original = "WorkshopItems=111\r\nMods=OldMod\r\nPVP=true\r\nPublic=false\r\n";
        File.WriteAllText(iniPath, original);

        try
        {
            var service = new PzConfigService(iniPath, backupDir);
            service.WriteUpdates(new Dictionary<string, Dictionary<string, string>>
            {
                ["global"] = new() { ["Mods"] = "OldMod;NewMod" }
            });

            Assert.Equal("WorkshopItems=111\r\nMods=OldMod;NewMod\r\nPVP=true\r\nPublic=false\r\n", File.ReadAllText(iniPath));
            Assert.Single(Directory.GetFiles(backupDir, "*.bak"));
            Assert.Equal(original, File.ReadAllText(Directory.GetFiles(backupDir, "*.bak")[0]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
