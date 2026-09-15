namespace GamePanel.ClientUpdater;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class UpdaterApiClient(HttpClient http)
{
    private readonly HttpClient _http = http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task LoginAsync(string username, string password, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("api/auth/login", new { username, password }, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Login failed (HTTP {(int)response.StatusCode}).");
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct)
            ?? throw new InvalidDataException("Login response is invalid.");
        if (string.IsNullOrWhiteSpace(login.Token)) throw new InvalidDataException("Login response has no token.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
    }

    public async Task<ClientUpdateManifest> GetManifestAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("api/client-updater/manifest", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClientUpdateManifest>(JsonOptions, ct)
            ?? throw new InvalidDataException("Client update manifest is empty.");
    }

    public async Task<Stream> DownloadAsync(ClientModPackage package, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(package.DownloadPath.TrimStart('/'), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 268435456)
            throw new InvalidDataException("Package exceeds the client download limit.");
        var memory = new MemoryStream();
        await response.Content.CopyToAsync(memory, ct);
        memory.Position = 0;
        return memory;
    }

    private sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);
}
