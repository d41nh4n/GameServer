using System.Net;
using System.Text;
using GamePanel.Infrastructure.GameServers;
using Microsoft.Extensions.Configuration;

public sealed class ValheimControlProtocolTests
{
    [Fact]
    public async Task AvailablePluginReturnsCapabilitiesAndPlayers()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath == "/capabilities"
                ? "{\"connected\":true,\"supported\":[\"OnlinePlayers\",\"SaveWorld\"],\"checkedAtUtc\":\"2026-01-01T00:00:00Z\",\"reason\":null}"
                : "[{\"platformId\":\"7656119\",\"name\":\"Player\",\"connectedAtUtc\":null}]")
        }));
        var protocol = Create(handler, enabled: true, token: string.Concat("bridge", "-test", "-value"));
        var caps = await protocol.GetCapabilitiesAsync(Guid.NewGuid());
        var players = await protocol.GetPlayersAsync(Guid.NewGuid());
        Assert.True(caps.Connected);
        Assert.Contains(GamePanel.Application.Interfaces.ValheimCapability.SaveWorld, caps.Supported);
        Assert.Single(players);
        Assert.Equal("Player", players[0].Name);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task InvalidAuthenticationReturnsUnavailableWithoutLeakingToken()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("invalid bridge auth") }));
        var supplied = string.Concat("bridge", "-test", "-secret");
        var caps = await Create(handler, true, supplied).GetCapabilitiesAsync(Guid.NewGuid());
        Assert.False(caps.Connected);
        Assert.DoesNotContain(supplied, caps.Reason);
    }

    [Fact]
    public async Task DisabledPluginReturnsUnavailableWithoutCallingNetwork()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("network called"));
        var caps = await Create(handler, false, "unused").GetCapabilitiesAsync(Guid.NewGuid());
        Assert.False(caps.Connected);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task TimeoutReturnsStructuredUnavailableResult()
    {
        var handler = new FakeHandler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new HttpResponseMessage(); });
        var caps = await Create(handler, true, "token").GetCapabilitiesAsync(Guid.NewGuid());
        Assert.False(caps.Connected);
        Assert.NotNull(caps.Reason);
    }

    private static ValheimControlProtocol Create(HttpMessageHandler handler, bool enabled, string token) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:27666"), Timeout = TimeSpan.FromMilliseconds(100) },
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GameServers:Valheim:ControlProtocol:Enabled"] = enabled.ToString(),
                ["GameServers:Valheim:ControlProtocol:Token"] = token,
            }).Build());

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; LastRequest = request; return callback(request, cancellationToken); }
    }
}
