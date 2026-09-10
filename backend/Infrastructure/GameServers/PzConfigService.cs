namespace GamePanel.Infrastructure.GameServers;
using System.Text.RegularExpressions;

/// <summary>
/// INI reader/writer for Project Zomboid servertest.ini.
/// Preserves comments, sections, line endings. No dependency on ConfigParser.
/// </summary>
public sealed class PzConfigService
{
    private readonly string _iniPath;
    private readonly string _backupDir;

    private static readonly Regex SectionRe = new(@"^\s*\[([^\]]+)\]\s*$", RegexOptions.Multiline);
    private static readonly Regex KeyRe = new(@"^(?<indent>\s*)(?<key>[^=]+?)(?<eq>\s*=\s*)(?<value>.*?)(?:\r?\n)?$", RegexOptions.Multiline);

    public PzConfigService(string? iniPath = null, string? backupDir = null)
    {
        _iniPath = iniPath ?? "/home/pzserver/Zomboid/Server/servertest_new.ini";
        _backupDir = backupDir ?? Path.Combine(Path.GetDirectoryName(_iniPath)!, "panel_backups");
    }

    public string IniPath => _iniPath;

    public Dictionary<string, Dictionary<string, string>> Parse()
    {
        EnsureFile();
        var config = new Dictionary<string, Dictionary<string, string>> { ["global"] = new() };
        string section = "global";

        foreach (var rawLine in File.ReadLines(_iniPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';')) continue;

            var m = SectionRe.Match(line);
            if (m.Success) { section = m.Groups[1].Value; if (!config.ContainsKey(section)) config[section] = new(); continue; }

            if (line.Contains('='))
            {
                var parts = line.Split('=', 2);
                var key = parts[0].Trim();
                if (!string.IsNullOrEmpty(key)) config[section][key] = parts[1].Trim();
            }
        }
        return config;
    }

    public string ReadRaw()
    {
        EnsureFile();
        return File.ReadAllText(_iniPath);
    }

    public string WriteUpdates(Dictionary<string, Dictionary<string, string>> updates)
    {
        EnsureFile();
        var original = File.ReadAllLines(_iniPath).ToList();
        var backup = Backup();

        // Collect existing keys
        var existing = new HashSet<(string section, string key)>();
        string currentSection = "global";
        foreach (var line in original)
        {
            var m = SectionRe.Match(line.Trim());
            if (m.Success) { currentSection = m.Groups[1].Value; continue; }
            var km = KeyRe.Match(line);
            if (km.Success) existing.Add((currentSection, km.Groups["key"].Value.Trim()));
        }
        existing.Add(("global", ""));

        // Replace and add
        var output = new List<string>();
        currentSection = "global";
        var replaced = new HashSet<(string section, string key)>();
        var seenSections = new HashSet<string>();

        int i = 0;
        while (i < original.Count)
        {
            var rawLine = original[i];
            var sm = SectionRe.Match(rawLine.Trim());
            if (sm.Success)
            {
                currentSection = sm.Groups[1].Value;
                seenSections.Add(currentSection);
                output.Add(rawLine);
                i++;
                while (i < original.Count && !SectionRe.IsMatch(original[i].Trim()))
                {
                    var line = original[i];
                    var km = KeyRe.Match(line);
                    if (km.Success && updates.TryGetValue(currentSection, out var secUpdates) && secUpdates.TryGetValue(km.Groups["key"].Value.Trim(), out var newVal))
                    {
                        output.Add($"{km.Groups["key"].Value.Trim()}={newVal}\n");
                        replaced.Add((currentSection, km.Groups["key"].Value.Trim()));
                    }
                    else output.Add(line);
                    i++;
                }
                // Add missing keys
                if (updates.TryGetValue(currentSection, out var secUp))
                {
                    foreach (var kv in secUp)
                        if (!replaced.Contains((currentSection, kv.Key)))
                        { output.Add($"{kv.Key}={kv.Value}\n"); replaced.Add((currentSection, kv.Key)); }
                }
                continue;
            }
            output.Add(rawLine);
            i++;
        }

        // Add global missing
        if (updates.TryGetValue("global", out var globalUp))
        {
            var missingGlobals = new List<string>();
            foreach (var kv in globalUp)
                if (!replaced.Contains(("global", kv.Key)))
                    missingGlobals.Add($"{kv.Key}={kv.Value}\n");
            if (missingGlobals.Count > 0) output.InsertRange(0, missingGlobals);
        }

        // Add new sections
        foreach (var sec in updates.Keys)
        {
            if (sec == "global" || seenSections.Contains(sec)) continue;
            if (!updates[sec].Any()) continue;
            if (!output.Last().EndsWith('\n')) output.Add("\n");
            output.Add($"[{sec}]\n");
            foreach (var kv in updates[sec]) output.Add($"{kv.Key}={kv.Value}\n");
        }

        File.WriteAllText(_iniPath + ".tmp", string.Concat(output));
        File.Replace(_iniPath + ".tmp", _iniPath, null);
        return backup;
    }

    private void EnsureFile()
    {
        if (!File.Exists(_iniPath))
            throw new FileNotFoundException($"INI not found: {_iniPath}");
    }

    private string Backup()
    {
        Directory.CreateDirectory(_backupDir);
        var backup = Path.Combine(_backupDir, $"servertest_new.ini.{DateTime.Now:yyyyMMdd_HHmmss}.bak");
        File.Copy(_iniPath, backup, overwrite: true);
        return backup;
    }
}