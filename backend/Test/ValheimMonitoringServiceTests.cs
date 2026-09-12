using GamePanel.Infrastructure.GameServers;

namespace GamePanel.ContractTests;

public sealed class ValheimMonitoringServiceTests
{
    private sealed class FakeDriver : ISystemdRuntimeDriver
    {
        public SystemdUnitState State { get; set; } = new(true, "active", "running", 100, "inv-test");

        public Task<SystemdUnitState> GetStateAsync(string unitName, CancellationToken ct = default) =>
            Task.FromResult(State);

        public Task<bool> ControlAsync(string unitName, SystemdControlAction action, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    [Fact]
    public void ParseBuildIdFromManifest_ValidAcf_ReturnsBuildId()
    {
        const string acf = @"""AppState""
{
    ""appid""		""896660""
    ""Universe""		""1""
    ""name""		""Valheim Dedicated Server""
    ""StateFlags""		""4""
    ""installdir""		""Valheim dedicated server""
    ""LastUpdated""		""1715000000""
    ""UpdateResult""		""0""
    ""buildid""		""25253791""
}";
        var buildId = ValheimMonitoringService.ParseBuildIdFromManifest(acf);
        Assert.Equal("25253791", buildId);
    }

    [Fact]
    public void ParseBuildIdFromManifest_InvalidOrEmpty_ReturnsNull()
    {
        Assert.Null(ValheimMonitoringService.ParseBuildIdFromManifest(""));
        Assert.Null(ValheimMonitoringService.ParseBuildIdFromManifest("random text without buildid"));
    }

    [Fact]
    public async Task UpdateServerAsync_WhenActive_ThrowsInvalidOperationException()
    {
        var driver = new FakeDriver { State = new SystemdUnitState(true, "active", "running", 123, "inv-1") };
        var service = new ValheimMonitoringService(driver);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateServerAsync());
    }
}
