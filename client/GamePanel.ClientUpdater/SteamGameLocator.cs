namespace GamePanel.ClientUpdater;

using System.Text.RegularExpressions;

public static class SteamGameLocator
{
    public static string Resolve(string? explicitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory)) return RequireGame(explicitDirectory);

        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                var steamRoot = Path.Combine(programFilesX86, "Steam");
                candidates.Add(Path.Combine(steamRoot, "steamapps", "common", "Valheim"));
                AddSteamLibraries(candidates, Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"));
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var steamRoot = Path.Combine(home, ".steam", "steam");
            candidates.Add(Path.Combine(steamRoot, "steamapps", "common", "Valheim"));
            AddSteamLibraries(candidates, Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"));
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (HasExecutable(candidate)) return Path.GetFullPath(candidate);
        throw new DirectoryNotFoundException("Valheim was not found. Pass --game-dir with the folder containing valheim.exe.");
    }

    private static void AddSteamLibraries(ICollection<string> candidates, string vdfPath)
    {
        if (!File.Exists(vdfPath)) return;
        var text = File.ReadAllText(vdfPath);
        foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant))
        {
            var library = match.Groups[1].Value.Replace("\\\\", "\\");
            candidates.Add(Path.Combine(library, "steamapps", "common", "Valheim"));
        }
    }

    private static string RequireGame(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!HasExecutable(full)) throw new DirectoryNotFoundException("The selected folder does not contain valheim.exe.");
        return full;
    }

    private static bool HasExecutable(string directory) =>
        Directory.Exists(directory) &&
        (File.Exists(Path.Combine(directory, "valheim.exe")) || File.Exists(Path.Combine(directory, "valheim.x86_64")));
}
