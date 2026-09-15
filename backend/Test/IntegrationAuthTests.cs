using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

/// <summary>
/// CR-00C HTTP integration tests through WebApplicationFactory/TestServer without
/// opening a port. Runtime dependencies are fakes and SQLite uses an isolated
/// temporary file, never gamepanel.db, systemd, or real processes.
/// </summary>
public class IntegrationAuthTests : IDisposable
{
    private static readonly string TEST_SECRET = "0123456789abcdef0123456789abcdef012345";
    private static readonly string TEST_ISSUER = "GamePanelTest";
    private static readonly string TEST_AUDIENCE = "GamePanelClient";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly FakeGameRuntime _runtime;

    public IntegrationAuthTests()
    {
        _factory = new CustomWebApplicationFactory();
        _runtime = _factory.fakeRuntime;
        try
        {
            _factory.StartServer();
        }
        catch (Exception e)
        {
            Assert.Fail("StartServer threw: " + e.GetType().FullName + ": " + e.Message);
        }
        _client = _factory.CreateClient();
    }

    private async Task<(HttpStatusCode Status, byte[]? Body)> Send(string method, string path, string? body, string? bearer, string? queryToken)
    {
        var url = "http://localhost" + path + (queryToken != null ? (path.Contains("?") ? "&" : "?") + "access_token=" + queryToken : "");
        var msg = new HttpRequestMessage(Parse(method), url);
        if (bearer != null) msg.Headers.Add("Authorization", "Bearer " + bearer);
        if (body != null) msg.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var res = await _client.SendAsync(msg);
        var status = res.StatusCode;
        byte[]? payload = null;
        if (res.Content is { } c)
        {
            payload = await c.ReadAsByteArrayAsync();
        }
        msg.Dispose();
        res.Dispose();
        return (status, payload);
    }

    private static HttpMethod Parse(string m) => m == "POST" ? HttpMethod.Post : HttpMethod.Get;

    private static string Str(byte[]? b) => b == null ? "" : Encoding.UTF8.GetString(b);

    // Login może być anonimowy; bootstrap admin = testadmin/TestPass123.
    private async Task<string> LoginAdmin()
    {
        var (status, body) = await Send("POST", "/api/auth/login",
            "{\"username\":\"testadmin\",\"password\":\"TestPass123\"}", null, null);
        if (status != HttpStatusCode.OK)
        {
            Assert.Fail("login failed: " + status + " body=" + Str(body));
        }
        var text = Str(body);
        // token w JSON: {"token":"...","expiresAt":"..."}
        var start = text.IndexOf("\"token\":\"") + 9;
        var end = text.IndexOf("\"", start);
        return text.Substring(start, end - start);
    }

    // Signs a JWT with the test issuer, audience, secret, and requested role.
    private static string SignToken(UserRole role)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TEST_SECRET,
                ["Jwt:Issuer"] = TEST_ISSUER,
                ["Jwt:Audience"] = TEST_AUDIENCE,
                ["Jwt:ExpiryMinutes"] = "30",
            })
            .Build();
        var svc = new JwtAuthService(cfg, null!, new PasswordHasher<User>());
        return svc.GenerateToken(Guid.NewGuid(), role).Value;
    }

    [Fact]
    public async Task NonAdmin_Start_Returns403_NoRuntimeInvocation()
    {
        var userToken = SignToken(UserRole.User);
        var calls = _runtime.StartCallCount;
        var (status, _) = await Send("POST", "/api/servers/11111111-1111-1111-1111-111111111111/start", null, userToken, null);
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(calls, _runtime.StartCallCount);
    }

    [Fact]
    public async Task NonAdmin_Stop_Returns403_NoRuntimeInvocation()
    {
        var userToken = SignToken(UserRole.User);
        var calls = _runtime.StopCallCount;
        var (status, _) = await Send("POST", "/api/servers/11111111-1111-1111-1111-111111111111/stop", null, userToken, null);
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(calls, _runtime.StopCallCount);
    }

    [Fact]
    public async Task Admin_Stop_WhenStopped_Returns409_NoRuntimeInvocation()
    {
        var admin = await LoginAdmin();
        var before = _runtime.StopCallCount;
        var (status, _) = await Send("POST", "/api/servers/11111111-1111-1111-1111-111111111111/stop", null, admin, null);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(before, _runtime.StopCallCount);
    }

    [Fact]
    public async Task Login_IsReachableAnonymously_ReturnsToken()
    {
        var token = await LoginAdmin();
        Assert.True(token.Length > 20, "token z bootstrap admina");
    }

    [Fact]
    public async Task Anon_Get_Servers_Returns401()
    {
        var (status, _) = await Send("GET", "/api/servers", null, null, null);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task Admin_Get_Servers_Returns200_NoSensitiveFields()
    {
        _runtime.Servers = new[]
        {
            new ServerInstance
            {
                Id = Guid.NewGuid(),
                Name = "Valheim Main",
                GameType = "Valheim",
                Type = GameServerType.Valheim,
                Password = "must-not-leak",
                ProcessId = 4242,
                InstanceKey = "valheim-main",
                ProvisioningMode = ProvisioningMode.AdoptExisting,
                RuntimeType = ServerRuntimeType.Systemd,
                RuntimeId = "valheim-main.service",
                InstallationPath = "/sensitive/server",
                DataPath = "/sensitive/data",
                BackupPath = "/sensitive/backups",
                Ready = true,
            },
        };
        var admin = await LoginAdmin();
        var (status, body) = await Send("GET", "/api/servers", null, admin, null);
        Assert.Equal(HttpStatusCode.OK, status);
        var text = Str(body).ToLowerInvariant();
        Assert.Contains("instancekey", text);
        Assert.Contains("provisioningmode", text);
        Assert.Contains("runtimetype", text);
        Assert.Contains("ready", text);
        Assert.DoesNotContain("password", text);
        Assert.DoesNotContain("processid", text);
        Assert.DoesNotContain("runtimeid", text);
        Assert.DoesNotContain("installationpath", text);
        Assert.DoesNotContain("datapath", text);
        Assert.DoesNotContain("backuppath", text);
        Assert.DoesNotContain("invocationid", text);
        Assert.DoesNotContain("must-not-leak", text);
        Assert.DoesNotContain("/sensitive/", text);
    }

    [Fact]
    public async Task Anon_Start_Returns401_NoRuntimeInvocation()
    {
        var calls = _runtime.StartCallCount;
        var (status, _) = await Send("POST", "/api/servers/11111111-1111-1111-1111-111111111111/start", null, null, null);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(calls, _runtime.StartCallCount);
    }

    [Fact]
    public async Task Anon_Stop_Returns401_NoRuntimeInvocation()
    {
        var calls = _runtime.StopCallCount;
        var (status, _) = await Send("POST", "/api/servers/11111111-1111-1111-1111-111111111111/stop", null, null, null);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(calls, _runtime.StopCallCount);
    }

    [Fact]
    public async Task Admin_Start_Returns202_ThenInvokesRuntime()
    {
        var admin = await LoginAdmin();
        var before = _runtime.StartCallCount;
        var (status, body) = await Send("POST", "/api/servers/11111111-1111-1111-1111-111111111111/start", null, admin, null);
        Assert.Equal(HttpStatusCode.Accepted, status);
        using var json = JsonDocument.Parse(Str(body));
        var jobId = json.RootElement.GetProperty("id").GetGuid();
        await WaitUntilAsync(() => _runtime.StartCallCount == before + 1);
        var (jobStatus, jobBody) = await Send("GET", $"/api/operations/{jobId}", null, admin, null);
        Assert.Equal(HttpStatusCode.OK, jobStatus);
        Assert.Equal(2, JsonDocument.Parse(Str(jobBody)).RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Anon_Restart_Returns401_NoRuntimeInvocation()
    {
        var starts = _runtime.StartCallCount; var stops = _runtime.StopCallCount;
        var (status, _) = await Send("POST", "/api/servers/11111111-1111-1111-1111-111111111111/restart", null, null, null);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(starts, _runtime.StartCallCount); Assert.Equal(stops, _runtime.StopCallCount);
    }

    [Fact]
    public async Task Resources_RequiresAuthentication_ThenReturnsHostAndServerContract()
    {
        var (anonStatus, _) = await Send("GET", "/api/resources/overview", null, null, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonStatus);

        var admin = await LoginAdmin();
        var (status, body) = await Send("GET", "/api/resources/overview", null, admin, null);
        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(Str(body));
        Assert.True(json.RootElement.GetProperty("host").TryGetProperty("cpuPercent", out _));
        Assert.True(json.RootElement.GetProperty("host").TryGetProperty("memoryTotalKb", out _));
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("servers").ValueKind);
    }

    [Fact]
    public async Task ClientUpdaterEndpoints_RequireAuthentication()
    {
        var (manifestStatus, _) = await Send("GET", "/api/client-updater/manifest", null, null, null);
        var (packageStatus, _) = await Send("GET", "/api/client-updater/packages/Advize-PlantEverything/1.21.2", null, null, null);

        Assert.Equal(HttpStatusCode.Unauthorized, manifestStatus);
        Assert.Equal(HttpStatusCode.Unauthorized, packageStatus);
    }

    [Fact]
    public async Task Anon_HubNegotiate_Returns401()
    {
        var (status, _) = await Send("POST", "/hubs/server/negotiate?negotiateVersion=1", null, null, null);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task HubNegotiate_WithAuthHeader_Succeeds()
    {
        var admin = await LoginAdmin();
        var (status, _) = await Send("POST", "/hubs/server/negotiate?negotiateVersion=1", null, admin, null);
        Assert.True(status != HttpStatusCode.Unauthorized, "z ważnym headerem negotiate nie powinno być 401, było " + status);
    }

    [Fact]
    public async Task HubNegotiate_WithSingleQueryToken_Succeeds()
    {
        var admin = await LoginAdmin();
        var (status, _) = await Send("POST", "/hubs/server/negotiate?negotiateVersion=1", null, null, admin);
        Assert.True(status != HttpStatusCode.Unauthorized, "query-token na hub powinien nie być 401, było " + status);
    }

    [Fact]
    public async Task HubNegotiate_ValidAuthHeader_InvalidQueryToken_StillSucceeds_HeaderPrecedence()
    {
        var admin = await LoginAdmin();
        var (status, _) = await Send("POST", "/hubs/server/negotiate?negotiateVersion=1", null, admin, "bogus-query-token");
        Assert.True(status != HttpStatusCode.Unauthorized, "ważny header wygrywa nad złym query, było " + status);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(predicate(), "Queued operation was not executed before timeout");
    }

    public void Dispose()
    {
        try { _client.Dispose(); } catch { }
        try { _factory.Dispose(); } catch { }
    }
}