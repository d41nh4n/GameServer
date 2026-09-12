import { useCallback, useEffect, useState } from "react";
import type { AuthProvider, Server } from "../../auth";
import { Btn } from "../common";

const types = ["", "resource_server", "resource_host", "status_heartbeat", "pz_process"];
const label = (v: string) => v.replaceAll("_", " ");

export default function MetricLogsView({ auth, servers }: { auth: AuthProvider; servers: Server[] }) {
  const [serverId, setServerId] = useState("");
  const [metricType, setMetricType] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [result, setResult] = useState<any>({ items: [], total: 0, hasMore: false });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const load = useCallback(async () => {
    setLoading(true); setError("");
    try { setResult(await auth.metricLogs({ serverId, metricType, fromUtc: from ? new Date(from).toISOString() : undefined, toUtc: to ? new Date(to).toISOString() : undefined, page, pageSize })); }
    catch (e) { setError(e instanceof Error ? e.message : "Metric query failed"); }
    finally { setLoading(false); }
  }, [auth, serverId, metricType, from, to, page, pageSize]);
  useEffect(() => { void load(); }, [load]);
  const apply = () => setPage(1);
  return <section className="metric-log-screen"><div className="page-heading"><div><h2>Metric logs</h2><p className="muted">Structured operational metrics from Elasticsearch. Raw console/journal text is not shown.</p></div><Btn variant="ghost" onClick={() => void load()} disabled={loading}>{loading ? "Loading…" : "Refresh"}</Btn></div><div className="metric-query-bar"><label>Server<select value={serverId} onChange={e => { setServerId(e.target.value); apply(); }}><option value="">All servers</option>{servers.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label><label>Metric type<select value={metricType} onChange={e => { setMetricType(e.target.value); apply(); }}>{types.map(t => <option key={t} value={t}>{t ? label(t) : "All types"}</option>)}</select></label><label>From<input type="datetime-local" value={from} onChange={e => setFrom(e.target.value)} /></label><label>To<input type="datetime-local" value={to} onChange={e => setTo(e.target.value)} /></label><Btn variant="primary" onClick={apply}>Apply filters</Btn></div>{error && <p className="error-msg">{error}</p>}<div className="metric-log-summary"><span>{result.total ?? 0} records</span><label>Rows <select value={pageSize} onChange={e => { setPageSize(Number(e.target.value)); setPage(1); }}><option value="25">25</option><option value="50">50</option><option value="100">100</option><option value="200">200</option></select></label></div><div className="metric-log-table"><div className="metric-log-head"><span>Time</span><span>Type</span><span>Server</span><span>Status</span><span>Process</span><span>CPU</span><span>RAM RSS</span><span>Ready/Online</span></div>{(result.items ?? []).map((item: any, i: number) => <div className="metric-log-row" key={`${item.timestamp ?? "row"}-${i}`}><span>{item.timestamp ? new Date(item.timestamp).toLocaleString() : "—"}</span><span>{item.metricType ? label(item.metricType) : "—"}</span><span>{servers.find(s => s.id === item.serverId)?.name ?? item.serverId ?? "Host"}</span><span>{item.status ?? "—"}</span><span>{item.processId ?? "—"}</span><span>{item.cpuPercent == null ? "—" : `${Number(item.cpuPercent).toFixed(1)}%`}</span><span>{item.memoryRssKb == null ? "—" : `${(Number(item.memoryRssKb) / 1024).toFixed(1)} MB`}</span><span>{item.ready ?? item.online ?? "—"}</span></div>)}{!loading && (result.items ?? []).length === 0 && <div className="empty-state">No metric records match the filters.</div>}</div><div className="metric-pagination"><Btn variant="ghost" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={loading || page <= 1}>Previous</Btn><span>Page {page} · {result.total ?? 0} total</span><Btn variant="ghost" onClick={() => setPage(p => p + 1)} disabled={loading || !result.hasMore}>Next</Btn></div></section>;
}
