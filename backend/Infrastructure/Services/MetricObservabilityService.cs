using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace GamePanel.Infrastructure.Services;

public sealed record MetricStoreStatus(bool Enabled, bool Reachable, string? ClusterStatus, long Documents, string IndexPattern);
public sealed record MetricLogItem(DateTime? Timestamp, string? MetricType, string? ServerId, string? Status, bool? Ready, int? ProcessId, bool? Online, double? CpuPercent, long? MemoryRssKb, long? MemoryTotalKb, long? MemoryUsedKb, long? Threads, int? FileDescriptors, int? DiskCount, DateTime? ObservedAtUtc, DateTime? GeneratedAtUtc);
public sealed record MetricLogPage(IReadOnlyList<MetricLogItem> Items, long Total, int Page, int PageSize, bool HasMore);

public sealed class MetricObservabilityService
{
    private static readonly HashSet<string> AllowedTypes = ["resource_server", "resource_host", "status_heartbeat", "pz_process"];
    private readonly IConfiguration _configuration;
    private readonly HttpClient _http;

    public MetricObservabilityService(IConfiguration configuration)
    {
        _configuration = configuration;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    private (bool Enabled, string Uri) Settings() => (_configuration.GetValue<bool>("Observability:Elasticsearch:Enabled"), _configuration["Observability:Elasticsearch:Uri"] ?? "http://127.0.0.1:9200");

    public async Task<MetricStoreStatus> GetStatusAsync(CancellationToken ct = default)
    {
        const string pattern = "gamepanel-metrics-*";
        var (enabled, uri) = Settings();
        if (!enabled) return new(false, false, null, 0, pattern);
        try
        {
            var health = await _http.GetFromJsonAsync<JsonElement>($"{uri.TrimEnd('/')}/_cluster/health", ct);
            var status = health.TryGetProperty("status", out var s) ? s.GetString() : null;
            var indices = await _http.GetFromJsonAsync<JsonElement[]>($"{uri.TrimEnd('/')}/_cat/indices/{pattern}?format=json", ct) ?? [];
            var docs = indices.Sum(x => x.TryGetProperty("docs.count", out var d) && long.TryParse(d.GetString(), out var n) ? n : 0);
            return new(true, true, status, docs, pattern);
        }
        catch (HttpRequestException) { return new(true, false, null, 0, pattern); }
        catch (TaskCanceledException) { return new(true, false, null, 0, pattern); }
    }

    public async Task<MetricLogPage> QueryAsync(Guid? serverId, DateTime? fromUtc, DateTime? toUtc, string? metricType, int page, int pageSize, CancellationToken ct = default)
    {
        var (enabled, uri) = Settings();
        if (!enabled) return new([], 0, page, pageSize, false);
        var filters = new List<string>();
        if (serverId.HasValue) filters.Add($"{{\"term\":{{\"fields.ServerId.keyword\":\"{serverId.Value:D}\"}}}}");
        if (!string.IsNullOrWhiteSpace(metricType) && AllowedTypes.Contains(metricType)) filters.Add($"{{\"term\":{{\"fields.MetricType.keyword\":\"{metricType}\"}}}}");
        if (fromUtc.HasValue || toUtc.HasValue)
        {
            var range = new List<string>();
            if (fromUtc.HasValue) range.Add($"\"gte\":\"{fromUtc.Value.ToUniversalTime():O}\"");
            if (toUtc.HasValue) range.Add($"\"lt\":\"{toUtc.Value.ToUniversalTime():O}\"");
            filters.Add($"{{\"range\":{{\"@timestamp\":{{{string.Join(',', range)}}}}}}}");
        }
        var query = filters.Count == 0 ? "{\"match_all\":{}}" : $"{{\"bool\":{{\"filter\":[{string.Join(',', filters)}]}}}}";
        var body = $"{{\"from\":{(page - 1) * pageSize},\"size\":{pageSize},\"track_total_hits\":true,\"sort\":[{{\"@timestamp\":\"desc\"}}],\"query\":{query}}}";
        try
        {
            using var response = await _http.PostAsync($"{uri.TrimEnd('/')}/gamepanel-metrics-*/_search", new StringContent(body, Encoding.UTF8, "application/json"), ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var hits = document.RootElement.GetProperty("hits");
            var total = hits.GetProperty("total").ValueKind == JsonValueKind.Object ? hits.GetProperty("total").GetProperty("value").GetInt64() : hits.GetProperty("total").GetInt64();
            var items = hits.GetProperty("hits").EnumerateArray().Select(ParseItem).ToList();
            return new(items, total, page, pageSize, page * pageSize < total);
        }
        catch (HttpRequestException) { throw; }
        catch (JsonException ex) { throw new HttpRequestException("Metric store returned an invalid response", ex); }
        catch (InvalidOperationException ex) { throw new HttpRequestException("Metric store returned an invalid response", ex); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new HttpRequestException("Metric store request timed out"); }
    }

    private static MetricLogItem ParseItem(JsonElement hit)
    {
        DateTime? Date(JsonElement x) => x.ValueKind == JsonValueKind.String && DateTime.TryParse(x.GetString(), out var value) ? value : null;
        var source = hit.GetProperty("_source");
        var timestamp = source.TryGetProperty("@timestamp", out var stamp) ? Date(stamp) : null;
        var fields = source.TryGetProperty("fields", out var f) ? f : default;
        string? S(string name) => fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(name, out var x) ? x.ToString() : null;
        bool? B(string name) => bool.TryParse(S(name), out var value) ? value : null;
        int? I(string name) => int.TryParse(S(name), out var value) ? value : null;
        long? L(string name) => long.TryParse(S(name), out var value) ? value : null;
        double? D(string name) => double.TryParse(S(name), out var value) ? value : null;
        DateTime? FDate(string name) => fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(name, out var x) ? Date(x) : null;
        return new(timestamp, S("MetricType"), S("ServerId"), S("Status"), B("Ready"), I("ProcessId"), B("Online"), D("CpuPercent"), L("MemoryRssKb"), L("MemoryTotalKb"), L("MemoryUsedKb"), L("Threads"), I("FileDescriptors"), I("DiskCount"), FDate("ObservedAtUtc"), FDate("GeneratedAtUtc"));
    }
}
