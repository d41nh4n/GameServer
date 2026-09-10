namespace GamePanel.Infrastructure.GameServers;

public sealed class PzModService
{
    private readonly PzConfigService _config;
    public PzModService(PzConfigService config) => _config = config;

    public PzModSnapshot Read()
    {
        var parsed = _config.Parse();
        var global = parsed.GetValueOrDefault("global") ?? new();
        static List<string> Split(string? value) => (value ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new PzModSnapshot
        {
            WorkshopIds = Split(global.GetValueOrDefault("WorkshopItems")),
            ModIds = Split(global.GetValueOrDefault("Mods")),
        };
    }

    public string Update(PzModUpdate update)
    {
        var current = Read();
        var workshop = update.WorkshopIds ?? current.WorkshopIds;
        var mods = update.ModIds ?? current.ModIds;
        return _config.WriteUpdates(new Dictionary<string, Dictionary<string, string>>
        {
            ["global"] = new()
            {
                ["WorkshopItems"] = string.Join(';', workshop.Distinct()),
                ["Mods"] = string.Join(';', mods.Distinct()),
            }
        });
    }
}

public sealed record PzModSnapshot
{
    public List<string> WorkshopIds { get; init; } = new();
    public List<string> ModIds { get; init; } = new();
    public int WorkshopCount => WorkshopIds.Count;
    public int ModCount => ModIds.Count;
}
public sealed record PzModUpdate(List<string>? WorkshopIds, List<string>? ModIds);