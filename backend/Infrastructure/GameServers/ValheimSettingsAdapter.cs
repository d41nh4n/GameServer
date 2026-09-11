namespace GamePanel.Infrastructure.GameServers;

public sealed record ValheimWorldModifierSettingsModel(
    string Combat = "normal",
    double ResourceRate = 1,
    string RaidRate = "normal",
    string DeathPenalty = "normal",
    string PortalMode = "hard",
    bool PassiveEnemies = false,
    bool PlayerBasedRaids = false,
    bool HammerMode = false,
    bool NoBuildCost = false);

public static class ValheimSettingsAdapter
{
    private static readonly HashSet<string> CombatValues = new(StringComparer.OrdinalIgnoreCase) { "veryeasy", "easy", "normal", "hard", "veryhard" };
    private static readonly HashSet<string> RaidValues = new(StringComparer.OrdinalIgnoreCase) { "none", "less", "normal", "more", "muchmore" };
    private static readonly HashSet<string> DeathValues = new(StringComparer.OrdinalIgnoreCase) { "casual", "veryeasy", "easy", "normal", "hard", "hardcore" };
    private static readonly HashSet<string> PortalValues = new(StringComparer.OrdinalIgnoreCase) { "casual", "hard", "veryhard" };

    public static ValheimWorldModifierSettingsModel Validate(ValheimWorldModifierSettingsModel input)
    {
        var combat = Normalize(input.Combat); var raids = Normalize(input.RaidRate); var death = Normalize(input.DeathPenalty); var portals = Normalize(input.PortalMode);
        if (!CombatValues.Contains(combat)) throw new ArgumentException("Invalid combat modifier");
        if (!RaidValues.Contains(raids)) throw new ArgumentException("Invalid raid modifier");
        if (!DeathValues.Contains(death)) throw new ArgumentException("Invalid death penalty modifier");
        if (!PortalValues.Contains(portals)) throw new ArgumentException("Invalid portal modifier");
        if (input.ResourceRate is not (0.5 or 1 or 2 or 3)) throw new ArgumentException("ResourceRate must be 0.5, 1, 2 or 3");
        return input with { Combat = combat, RaidRate = raids, DeathPenalty = death, PortalMode = portals };
    }

    public static IReadOnlyList<string> ToArguments(ValheimWorldModifierSettingsModel raw)
    {
        var s = Validate(raw); var args = new List<string>();
        AddModifier(args, "combat", s.Combat, "normal");
        AddModifier(args, "deathpenalty", s.DeathPenalty, "normal");
        AddModifier(args, "raids", s.RaidRate, "normal");
        AddModifier(args, "portals", s.PortalMode, "hard");
        args.AddRange(["-modifier", "resources", ResourceValue(s.ResourceRate)]);
        if (s.PassiveEnemies) args.AddRange(["-setkey", "passivemobs"]);
        if (s.PlayerBasedRaids) args.AddRange(["-setkey", "playerevents"]);
        if (s.HammerMode) args.AddRange(["-setkey", "nobuildcost"]);
        if (s.NoBuildCost && !s.HammerMode) args.AddRange(["-setkey", "nobuildcost"]);
        return args;
    }

    private static void AddModifier(List<string> args, string category, string value, string defaultValue)
    { args.AddRange(["-modifier", category, value]); }
    private static string ResourceValue(double value) => value switch { 0.5 => "muchless", 2 => "muchmore", 3 => "most", _ => "normal" };
    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
