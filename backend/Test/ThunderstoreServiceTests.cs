namespace GamePanel.ContractTests;

using System.Net;
using System.Text;
using GamePanel.Infrastructure.GameServers;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

public sealed class ThunderstoreServiceTests
{
    [Fact]
    public async Task ResolveVersionAsync_ReturnsOnlyExactPinnedVersion()
    {
        const string json = """
        [{
          "name":"TestMod",
          "full_name":"Author-TestMod",
          "owner":"Author",
          "package_url":"https://thunderstore.io/c/valheim/p/Author/TestMod/",
          "versions":[
            {"name":"TestMod","full_name":"Author-TestMod-2.0.0","version_number":"2.0.0","download_url":"https://gcdn.thunderstore.io/live/repository/packages/Author-TestMod-2.0.0.zip","dependencies":[]},
            {"name":"TestMod","full_name":"Author-TestMod-1.2.3","version_number":"1.2.3","download_url":"https://gcdn.thunderstore.io/live/repository/packages/Author-TestMod-1.2.3.zip","dependencies":["denikson-BepInExPack_Valheim-5.4.2202"]}
          ]
        }]
        """;
        using var http = new HttpClient(new JsonHandler(json));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ThunderstoreService(http, cache);

        var version = await service.ResolveVersionAsync("Author", "TestMod", "1.2.3");

        Assert.Equal("Author-TestMod", version.PackageId);
        Assert.Equal("1.2.3", version.Version);
        Assert.EndsWith("Author-TestMod-1.2.3.zip", version.DownloadUrl, StringComparison.Ordinal);
        Assert.Equal(["denikson-BepInExPack_Valheim-5.4.2202"], version.Dependencies);
    }

    [Fact]
    public async Task ResolveVersionAsync_RejectsUnknownOrUnpinnedIdentity()
    {
        using var http = new HttpClient(new JsonHandler("[]"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ThunderstoreService(http, cache);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveVersionAsync("Author", "Missing", "latest"));
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }
}
