using GamePanel.Infrastructure.GameServers;

public sealed class ValheimOnlinePlayerParserTests
{
    [Fact]
    public void ParseConnections_LatestZeroHeartbeatClearsHistoricalConnections()
    {
        var players = ValheimOnlinePlayerParser.ParseConnections(new[]
        {
            "Connections 3 ZDOS:100",
            "Got connection SteamID 76561199072534537",
            "Got character ZDOID from Dainhan : 10:1",
            "Connections 0 ZDOS:101",
        });

        Assert.Empty(players);
    }

    [Fact]
    public void ParseConnections_IncludesHandshakePlayerWithoutCharacterName()
    {
        var players = ValheimOnlinePlayerParser.ParseConnections(new[]
        {
            "Got connection SteamID 76561199072534537",
            "Got character ZDOID from Dainhan : 1929254412:1",
            "Got connection SteamID 76561199066578821",
        });

        Assert.Equal(2, players.Count);
        Assert.Equal("Dainhan", players[0].Name);
        Assert.True(players[0].Online);
        Assert.Null(players[1].Name);
        Assert.False(players[1].Online);
    }

    [Fact]
    public void Parse_UsesLatestCharacterStatePerPlayer()
    {
        var lines = new[]
        {
            "Got character ZDOID from Dainhan : 1929254412:1",
            "Got character ZDOID from Ambatukam : 1827688350:1",
            "Got character ZDOID from Dainhan : 0:0",
            "Got character ZDOID from Dainhan : 1929254412:31",
        };

        Assert.Equal(new[] { "Ambatukam", "Dainhan" }, ValheimOnlinePlayerParser.Parse(lines));
    }

    [Fact]
    public void Parse_RemovesPlayerWhoseLatestStateIsZero()
    {
        var lines = new[]
        {
            "Got character ZDOID from Dainhan : 10:1",
            "Got character ZDOID from Dainhan : 0:0",
        };

        Assert.Empty(ValheimOnlinePlayerParser.Parse(lines));
    }

    [Fact]
    public void Parse_IgnoresUnrelatedAndMalformedLines()
    {
        var lines = new[] { "Connections 4", "Got character ZDOID from : 1:2", "hello" };
        Assert.Empty(ValheimOnlinePlayerParser.Parse(lines));
    }
}
