using GamePanel.Application.Interfaces;
using GamePanel.Infrastructure.Services;

public sealed class ValheimControlActionServiceTests
{
    [Fact]
    public async Task UnavailablePluginReturns503WithoutExecutingAction()
    {
        var fake = new FakeProtocol(new ValheimCapabilities(new HashSet<ValheimCapability>(), false, DateTime.UtcNow, "offline"));
        var result = await new ValheimControlActionService(fake).ExecuteAsync(Guid.NewGuid(), ValheimCapability.SaveWorld, new(), "key-1", CancellationToken.None);
        Assert.Equal(503, result.Status);
        Assert.Equal("PLUGIN_UNAVAILABLE", result.Result.Code);
        Assert.Equal(0, fake.ActionCalls);
    }

    [Fact]
    public async Task UnsupportedCapabilityReturns501()
    {
        var fake = new FakeProtocol(new ValheimCapabilities(new HashSet<ValheimCapability>(), true, DateTime.UtcNow, null));
        var result = await new ValheimControlActionService(fake).ExecuteAsync(Guid.NewGuid(), ValheimCapability.KickPlayer, new(), "key-1", CancellationToken.None);
        Assert.Equal(501, result.Status);
        Assert.Equal("CAPABILITY_UNSUPPORTED", result.Result.Code);
    }

    [Fact]
    public async Task DuplicateIdempotencyKeyIsRejected()
    {
        var id = Guid.NewGuid();
        var fake = new FakeProtocol(new ValheimCapabilities(new HashSet<ValheimCapability> { ValheimCapability.SaveWorld }, true, DateTime.UtcNow, null));
        var service = new ValheimControlActionService(fake);
        Assert.Equal(200, (await service.ExecuteAsync(id, ValheimCapability.SaveWorld, new(), "same", CancellationToken.None)).Status);
        var duplicate = await service.ExecuteAsync(id, ValheimCapability.SaveWorld, new(), "same", CancellationToken.None);
        Assert.Equal(409, duplicate.Status);
        Assert.Equal(1, fake.ActionCalls);
    }

    [Fact]
    public async Task SupportedActionIsExecutedWithTypedRequest()
    {
        var fake = new FakeProtocol(new ValheimCapabilities(new HashSet<ValheimCapability> { ValheimCapability.BroadcastMessage }, true, DateTime.UtcNow, null));
        var request = new ValheimControlRequest(Message: "hello");
        var result = await new ValheimControlActionService(fake).ExecuteAsync(Guid.NewGuid(), ValheimCapability.BroadcastMessage, request, "key-1", CancellationToken.None);
        Assert.Equal(200, result.Status);
        Assert.Equal("OK", result.Result.Code);
        Assert.Equal(request, fake.LastRequest);
    }

    [Fact]
    public void InvalidTypedParametersAreRejected()
    {
        Assert.NotNull(ValheimControlValidation.Validate(ValheimCapability.BroadcastMessage, new(Message: "")));
        Assert.NotNull(ValheimControlValidation.Validate(ValheimCapability.ChangeTime, new(Value: 24)));
        Assert.NotNull(ValheimControlValidation.Validate(ValheimCapability.SpawnItem, new(Item: "Stone", Amount: 0)));
        Assert.Null(ValheimControlValidation.Validate(ValheimCapability.ChangeWeather, new(Weather: "rain")));
    }
    private sealed class FakeProtocol(ValheimCapabilities capabilities) : IGameControlProtocol
    {
        public int ActionCalls { get; private set; }
        public ValheimControlRequest? LastRequest { get; private set; }
        public Task<ValheimCapabilities> GetCapabilitiesAsync(Guid serverId, CancellationToken ct = default) => Task.FromResult(capabilities);
        public Task<IReadOnlyList<ValheimOnlinePlayer>> GetPlayersAsync(Guid serverId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ValheimOnlinePlayer>>([]);
        public Task<ValheimActionResult> ExecuteAsync(Guid serverId, ValheimCapability capability, ValheimControlRequest request, CancellationToken ct = default)
        { ActionCalls++; LastRequest = request; return Task.FromResult(new ValheimActionResult(true, "OK")); }
    }
}
