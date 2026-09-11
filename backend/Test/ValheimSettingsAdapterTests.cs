using GamePanel.Infrastructure.GameServers;

public sealed class ValheimSettingsAdapterTests
{
    [Fact]
    public void ToArguments_MapsTypedSettingsToAllowlistedFlags()
    {
        var args = ValheimSettingsAdapter.ToArguments(new(
            Combat: "hard", ResourceRate: 2, RaidRate: "less", DeathPenalty: "casual", PortalMode: "casual",
            PassiveEnemies: true, PlayerBasedRaids: true, HammerMode: false, NoBuildCost: false));

        Assert.Equal(new[] { "-modifier", "combat", "hard", "-modifier", "deathpenalty", "casual", "-modifier", "raids", "less", "-modifier", "portals", "casual", "-modifier", "resources", "muchmore", "-setkey", "passivemobs", "-setkey", "playerevents" }, args);
    }

    [Fact]
    public void ResourceRateThreeMapsToMostForValheimThreeTimes()
    {
        var args = ValheimSettingsAdapter.ToArguments(new(ResourceRate: 3)).ToList();
        Assert.Equal("most", args[args.IndexOf("resources") + 1]);
    }

    [Fact]
    public void Validate_RejectsUnknownEnumAndUnsupportedResourceRate()
    {
        Assert.Throws<ArgumentException>(() => ValheimSettingsAdapter.Validate(new(Combat: "nightmare")));
        Assert.Throws<ArgumentException>(() => ValheimSettingsAdapter.Validate(new(ResourceRate: 1.5)));
    }

    [Fact]
    public void ToArguments_EmitsAllWorldModifiersIncludingDefaults()
    {
        var args = ValheimSettingsAdapter.ToArguments(new());
        Assert.Equal(new[] { "-modifier", "combat", "normal", "-modifier", "deathpenalty", "normal", "-modifier", "raids", "normal", "-modifier", "portals", "hard", "-modifier", "resources", "normal" }, args);
    }
}
