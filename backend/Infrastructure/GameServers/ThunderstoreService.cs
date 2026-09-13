namespace GamePanel.Infrastructure.GameServers;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

public sealed record ThunderstorePackageSummary(
    string Name,
    string FullName,
    string Owner,
    string PackageUrl,
    string VersionNumber,
    string? IconUrl,
    string? Description,
    string DownloadUrl,
    int Downloads,
    string? WebsiteUrl,
    DateTime DateCreated
);

public sealed record ThunderstoreSearchResult(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<ThunderstorePackageSummary> Items
);

public sealed class ThunderstoreService
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "Thunderstore_Valheim_Packages";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public ThunderstoreService(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri("https://valheim.thunderstore.io/");
        }
    }

    public async Task<IReadOnlyList<ThunderstorePackageSummary>> GetPackagesAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<ThunderstorePackageSummary>? cached) && cached is not null)
        {
            return cached;
        }

        var rawPackages = await _http.GetFromJsonAsync<List<ThunderstoreRawPackage>>("api/v1/package/", ct)
            ?? [];

        var summaries = new List<ThunderstorePackageSummary>(rawPackages.Count);
        foreach (var p in rawPackages)
        {
            var latest = p.Versions?.FirstOrDefault();
            if (latest is null) continue;

            summaries.Add(new ThunderstorePackageSummary(
                Name: p.Name,
                FullName: p.FullName,
                Owner: p.Owner,
                PackageUrl: p.PackageUrl,
                VersionNumber: latest.VersionNumber,
                IconUrl: latest.Icon,
                Description: latest.Description,
                DownloadUrl: latest.DownloadUrl,
                Downloads: latest.Downloads,
                WebsiteUrl: latest.WebsiteUrl,
                DateCreated: latest.DateCreated
            ));
        }

        _cache.Set(CacheKey, (IReadOnlyList<ThunderstorePackageSummary>)summaries, CacheDuration);
        return summaries;
    }

    public async Task<ThunderstoreSearchResult> SearchAsync(
        string? query,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var packages = await GetPackagesAsync(ct);
        IEnumerable<ThunderstorePackageSummary> queryable = packages;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            queryable = queryable.Where(p =>
                terms.All(t =>
                    p.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.FullName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.Owner.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    (p.Description?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
                )
            );
        }

        var ordered = queryable.OrderByDescending(p => p.Downloads).ToList();
        var total = ordered.Count;
        var clampedPage = Math.Max(1, page);
        var clampedSize = Math.Clamp(pageSize, 1, 100);
        var items = ordered.Skip((clampedPage - 1) * clampedSize).Take(clampedSize).ToList();

        return new ThunderstoreSearchResult(total, clampedPage, clampedSize, items);
    }

    private sealed class ThunderstoreRawPackage
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
        [JsonPropertyName("owner")] public string Owner { get; set; } = "";
        [JsonPropertyName("package_url")] public string PackageUrl { get; set; } = "";
        [JsonPropertyName("versions")] public List<ThunderstoreRawVersion>? Versions { get; set; }
    }

    private sealed class ThunderstoreRawVersion
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("icon")] public string Icon { get; set; } = "";
        [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
        [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
        [JsonPropertyName("downloads")] public int Downloads { get; set; }
        [JsonPropertyName("date_created")] public DateTime DateCreated { get; set; }
        [JsonPropertyName("website_url")] public string WebsiteUrl { get; set; } = "";
    }
}
