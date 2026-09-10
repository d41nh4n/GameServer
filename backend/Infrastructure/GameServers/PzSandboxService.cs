namespace GamePanel.Infrastructure.GameServers;
using System.Text.RegularExpressions;

/// <summary>
/// Minimal Lua table parser for SandboxVars.lua.
/// Supports: `SandboxVars = { ... }`, `return { ... }`, nested sections,
/// comments, strings, bool/int/float/string values. Preserves comments on write.
/// </summary>
public sealed class PzSandboxService
{
    private readonly string _filePath;
    private readonly string _backupDir;

    private static readonly Regex SectionHeaderRe = new(
        @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\{",
        RegexOptions.Multiline);

    private static readonly Regex AssignRe = new(
        @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*,?\s*$",
        RegexOptions.Multiline);

    public PzSandboxService(string? filePath = null, string? backupDir = null)
    {
        _filePath = filePath ?? "/home/pzserver/Zomboid/Server/servertest_new_SandboxVars.lua";
        _backupDir = backupDir ?? "/home/pzserver/pzservermanager/backup/sandbox";
    }

    public string ReadText()
    {
        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"Sandbox file not found: {_filePath}");
        return File.ReadAllText(_filePath);
    }

    /// <summary>Returns grouped config with read-only info per field.</summary>
    public SandboxConfig GetConfig()
    {
        var text = ReadText();
        var groups = new List<SandboxSectionGroup>();

        // Find root table (SandboxVars or return)
        var root = FindRootSection(text);
        if (root == null)
            throw new InvalidOperationException("Sandbox root table not found");

        // Parse root variables
        var rootVars = ParseDirectVariables(root.Body);
        if (rootVars.Count > 0)
        {
            groups.Add(new SandboxSectionGroup
            {
                Name = "SandboxVars",
                Label = "General Sandbox Variables",
                Fields = rootVars.Select(v => BuildField(v)).ToList()
            });
        }

        // Parse child sections
        var childNames = GetChildSectionNames(text);
        foreach (var name in childNames)
        {
            var section = FindSection(text, name);
            if (section == null) continue;
            var vars = ParseDirectVariables(section.Body);
            groups.Add(new SandboxSectionGroup
            {
                Name = name,
                Label = name,
                Fields = vars.Select(v => BuildField(v)).ToList()
            });
        }

        return new SandboxConfig { Groups = groups };
    }

    public string SaveValue(string section, string key, object value)
    {
        var text = ReadText();
        var formatted = FormatLuaValue(value);

        // Backup
        Directory.CreateDirectory(_backupDir);
        var backupPath = Path.Combine(_backupDir, $"SandboxVars.{DateTime.Now:yyyyMMdd_HHmmss}.bak");
        File.Copy(_filePath, backupPath, overwrite: true);

        // Find and replace
        var sectionObj = FindSectionOrRoot(text, section);
        if (sectionObj == null)
            throw new InvalidOperationException($"Section '{section}' not found");

        var pattern = new Regex(
            $@"(?m)^(?<prefix>[ \t]*{Regex.Escape(key)}[ \t]*=[ \t]*)(?<value>[^,\r\n]+)(?<suffix>[ \t]*,?(?:[ \t]*--[^\r\n]*)?)$");

        var match = pattern.Match(sectionObj.Body);
        if (!match.Success)
            throw new InvalidOperationException($"Variable '{section}.{key}' not found");

        var replacement = match.Groups["prefix"].Value + formatted + match.Groups["suffix"].Value;
        var newBody = sectionObj.Body[..match.Index] + replacement + sectionObj.Body[(match.Index + match.Length)..];

        // Rebuild full text
        var newText = text[..(sectionObj.Open + 1)] + newBody + text[sectionObj.Close..];

        // Verify parseable
        try { GetConfig(); } catch { throw new InvalidOperationException("Write verification failed"); }

        // Atomic write
        File.WriteAllText(_filePath + ".tmp", newText);
        File.Replace(_filePath + ".tmp", _filePath, null);
        return backupPath;
    }

    // --- Parse helpers ---

    private sealed record SectionInfo(int Open, int Close, string Body);

    private static SectionInfo? FindRootSection(string text)
    {
        // Try SandboxVars = { ... } first
        var s = FindSection(text, "SandboxVars");
        if (s != null) return s;

        // Try return { ... } (Build 42 wrapper)
        s = FindReturnRoot(text);
        if (s != null) return s;

        // Fallback: find first real { outside strings/comments
        s = FindOuterRoot(text);
        if (s != null) return s;

        // Last resort: raw brace matching (handles any Lua format)
        return FindOuterRootRaw(text);
    }

    private static int? FindStringSafeIndex(string text, char ch, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == ch) return i;
        }
        return null;
    }

    private static SectionInfo? FindOuterRootRaw(string text)
    {
        var openPos = text.IndexOf('{');
        if (openPos < 0) return null;

        int depth = 0;
        for (int i = openPos; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return new SectionInfo(openPos, i, text[(openPos + 1)..i]);
            }
        }
        return null;
    }

    private static SectionInfo? FindReturnRoot(string text)
    {
        var m = Regex.Match(text, @"(?m)^[ \t]*return[ \t]*\{");
        return m.Success ? ExtractBrace(text, m.Index) : null;
    }

    private static SectionInfo? FindOuterRoot(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
                return ExtractBrace(text, i);
        }
        return null;
    }

    private static SectionInfo? FindSection(string text, string name)
    {
        var pattern = new Regex($@"(?m)^[ \t]*{Regex.Escape(name)}[ \t]*=[ \t]*\{{");
        var m = pattern.Match(text);
        if (!m.Success) return null;
        var openPos = text.IndexOf('{', m.Index);
        return openPos >= 0 ? ExtractBrace(text, openPos) : null;
    }

    private static SectionInfo? ExtractBrace(string text, int openPos)
    {
        int depth = 0;
        bool inString = false;
        char stringChar = '\0';
        bool escaped = false;

        for (int pos = openPos; pos < text.Length; pos++)
        {
            var ch = text[pos];

            if (inString)
            {
                if (escaped) { escaped = false; continue; }
                if (ch == '\\') { escaped = true; continue; }
                if (ch == stringChar) { inString = false; continue; }
                continue;
            }

            if (ch == '\'' || ch == '"')
            {
                inString = true;
                stringChar = ch;
                continue;
            }

            if (ch == '{') depth++;
            else if (ch == '}') { depth--; if (depth == 0) return new SectionInfo(openPos, pos, text[(openPos + 1)..pos]); }
        }
        return null;
    }

    private static List<SandboxVariable> ParseDirectVariables(string body)
    {
        var vars = new List<SandboxVariable>();
        foreach (var rawLine in body.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var m = AssignRe.Match(line);
            if (!m.Success || m.Groups[2].Value.TrimStart().StartsWith('{')) continue;

            var key = m.Groups[1].Value;
            var raw = m.Groups[2].Value.Trim();
            vars.Add(new SandboxVariable { Key = key, RawValue = raw, DetectedType = DetectType(raw) });
        }
        return vars;
    }

    private static string StripComment(string line)
    {
        var idx = line.IndexOf("--");
        return idx >= 0 ? line[..idx] : line;
    }

    private static string DetectType(string raw)
    {
        if (raw == "true" || raw == "false") return "bool";
        if (int.TryParse(raw, out _)) return "int";
        if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) return "float";
        return "string";
    }

    private static string FormatLuaValue(object value)
    {
        if (value is bool b) return b ? "true" : "false";
        if (value is int i) return i.ToString();
        if (value is double d) return d.ToString("0.0########", System.Globalization.CultureInfo.InvariantCulture);
        if (value is float f) return f.ToString("0.0########", System.Globalization.CultureInfo.InvariantCulture);
        return value?.ToString() ?? "nil";
    }

    private static List<string> GetChildSectionNames(string text) =>
        SectionHeaderRe.Matches(text).Select(m => m.Groups[1].Value).Where(n => n != "SandboxVars").Distinct().ToList();

    private static SectionInfo? FindSectionOrRoot(string text, string name) =>
        name == "SandboxVars" ? FindRootSection(text) : FindSection(text, name);

    private static SandboxField BuildField(SandboxVariable v)
    {
        var editable = v.DetectedType is "bool" or "int" or "float";
        return new SandboxField
        {
            Key = v.Key,
            Value = v.RawValue,
            DetectedType = v.DetectedType,
            Editable = editable,
        };
    }
}

// --- DTOs ---

public sealed record SandboxVariable { public string Key { get; set; } = ""; public string RawValue { get; set; } = ""; public string DetectedType { get; set; } = ""; }
public sealed record SandboxField { public string Key { get; set; } = ""; public string Value { get; set; } = ""; public string DetectedType { get; set; } = ""; public bool Editable { get; set; } }
public sealed record SandboxSectionGroup { public string Name { get; set; } = ""; public string Label { get; set; } = ""; public List<SandboxField> Fields { get; set; } = new(); }
public sealed record SandboxConfig { public List<SandboxSectionGroup> Groups { get; set; } = new(); }