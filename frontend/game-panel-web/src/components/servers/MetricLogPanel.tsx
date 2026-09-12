import { useEffect, useState } from "react";
import type { AuthProvider, Server } from "../../auth";
import { Btn } from "../common";

export default function MetricLogPanel({ auth, servers: _servers }: { auth: AuthProvider; servers: Server[] }) {
  const [store, setStore] = useState<any>(null);
  const [loading, setLoading] = useState(false);
  const refreshStore = async () => {
    setLoading(true);
    try { setStore(await auth.metricStoreStatus()); }
    catch { setStore({ reachable: false }); }
    finally { setLoading(false); }
  };
  useEffect(() => { void refreshStore(); }, []);
  return <section className="resource-panel metric-test-panel"><div className="resource-header"><div><h3>Metric logging</h3><p>Structured metrics → Elasticsearch · retention 7 days</p></div><span className={`resource-dot ${store?.reachable ? "online" : "offline"}`} /></div><div className="metric-test-controls"><Btn variant="ghost" onClick={() => void refreshStore()} disabled={loading}>{loading ? "Refreshing…" : "Refresh metrics"}</Btn></div><p className="muted">Store: {store?.reachable ? `${store.clusterStatus ?? "reachable"} · ${store.documents ?? 0} documents` : "unreachable or disabled"}</p></section>;
}
