using System.Text;
using GamePanel.Infrastructure.GameServers;

namespace GamePanel.Infrastructure.Services;

public sealed class ValheimSettingsApplyService
{
    private const string InputPath = "/home/nh4n/backups/game-server-panel/valheim-settings.apply";
    private readonly ICommandRunner _runner;
    public ValheimSettingsApplyService(ICommandRunner runner) => _runner = runner;

    public async Task ApplyAsync(ValheimWorldModifierSettingsModel settings, CancellationToken ct = default)
    {
        var args = ValheimSettingsAdapter.ToArguments(settings);
        var values = new Dictionary<string, string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "-modifier") { var category = args[++i]; var value = args[++i]; values[$"VALHEIM_MODIFIER_{category.ToUpperInvariant()}"] = value; }
            else if (args[i] == "-setkey") { var key = args[++i]; values[$"VALHEIM_SETKEY_{key.ToUpperInvariant()}"] = "1"; }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(InputPath)!);
        await File.WriteAllLinesAsync(InputPath, values.Select(x => $"{x.Key}={x.Value}"), new UTF8Encoding(false), ct);
        var result = await _runner.RunAsync(["sudo", "-n", "/usr/local/sbin/gamepanel-valheim-settings"], ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Valheim settings helper failed");
    }
}
