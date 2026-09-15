namespace GamePanel.ClientUpdater.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GamePanel.ClientUpdater;

public sealed class UpdaterApiClientTests
{
    [Fact]
    public async Task LoginThenFetchManifestAndPackage_UsesBearerToken()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://100.82.102.38:5000/") };
        var client = new UpdaterApiClient(http);

        await client.LoginAsync("player", "not-logged");
        var manifest = await client.GetManifestAsync();
        await using var package = await client.DownloadAsync(manifest.Packages[0]);

        Assert.Equal("revision-1", manifest.Revision);
        Assert.Equal("zip", await new StreamReader(package).ReadToEndAsync());
        Assert.Equal(3, handler.Requests.Count);
        Assert.Null(handler.Requests[0].Authorization);
        Assert.Equal("Bearer", handler.Requests[1].Authorization?.Scheme);
        Assert.Equal("token-value", handler.Requests[1].Authorization?.Parameter);
        Assert.Equal("/api/client-updater/packages/Test-Mod/1.0.0", handler.Requests[2].PathAndQuery);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        public List<(string PathAndQuery, AuthenticationHeaderValue? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.PathAndQuery, request.Headers.Authorization));
            var content = request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/login" => "{\"token\":\"token-value\",\"expiresAt\":\"2030-01-01T00:00:00Z\"}",
                "/api/client-updater/manifest" => "{\"schemaVersion\":1,\"profileId\":\"valheim-main\",\"revision\":\"revision-1\",\"generatedAtUtc\":\"2030-01-01T00:00:00Z\",\"packages\":[{\"packageId\":\"Test-Mod\",\"version\":\"1.0.0\",\"target\":\"client-server\",\"downloadPath\":\"/api/client-updater/packages/Test-Mod/1.0.0\",\"archiveSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"files\":[{\"path\":\"BepInEx/plugins/Test.dll\",\"sha256\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"}]}]}",
                "/api/client-updater/packages/Test-Mod/1.0.0" => "zip",
                _ => "",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8) });
        }
    }
}
