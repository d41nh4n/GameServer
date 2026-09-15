namespace GamePanel.ClientUpdater;

using System.Diagnostics;

public static class UpdaterProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = UpdaterOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }
            if (IsValheimRunning())
                throw new InvalidOperationException("Close Valheim before checking or applying mod updates.");

            Console.Write("GamePanel username: ");
            var username = Console.ReadLine()?.Trim() ?? "";
            Console.Write("GamePanel password: ");
            var password = ReadPassword();
            Console.WriteLine();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Username and password are required.");

            using var http = new HttpClient { BaseAddress = options.ApiBase, Timeout = TimeSpan.FromMinutes(5) };
            var api = new UpdaterApiClient(http);
            await api.LoginAsync(username, password);
            var manifest = await api.GetManifestAsync();
            var installer = new PackageInstaller();
            var result = await installer.ApplyAsync(
                options.GameDirectory,
                manifest,
                api.DownloadAsync,
                options.CheckOnly);

            if (result.ChangedFiles.Count == 0)
                Console.WriteLine($"Client is current at revision {result.Revision}.");
            else if (options.CheckOnly)
            {
                Console.WriteLine($"Update available: {result.Revision}");
                foreach (var path in result.ChangedFiles) Console.WriteLine("  " + path);
            }
            else
            {
                Console.WriteLine($"Applied revision {result.Revision}; changed {result.ChangedFiles.Count} file(s).");
                Console.WriteLine("Backup: " + result.BackupDirectory);
            }

            if (options.Launch && !options.CheckOnly)
                Process.Start(new ProcessStartInfo("steam://rungameid/892970") { UseShellExecute = true });
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Update failed: " + error.Message);
            return 1;
        }
    }

    private static bool IsValheimRunning()
    {
        try { return Process.GetProcessesByName("valheim").Length > 0; }
        catch { return false; }
    }

    private static string ReadPassword()
    {
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0) value.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
        return value.ToString();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("GamePanel Client Updater");
        Console.WriteLine("  --game-dir <path>   Valheim folder containing valheim.exe");
        Console.WriteLine("  --api-base <url>    GamePanel API (default: Tailscale panel URL)");
        Console.WriteLine("  --check-only        Verify and report without changing game files");
        Console.WriteLine("  --launch            Launch Valheim through Steam after a successful update");
    }
}
