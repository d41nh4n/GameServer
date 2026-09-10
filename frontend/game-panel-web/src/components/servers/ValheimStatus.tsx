import { useCallback, useEffect, useState } from "react";
import type { AuthProvider } from "../../auth";
import { Btn } from "../common";

type Monitor = {
  activeState: string; subState: string; mainPid: number | null;
  invocationId: string; ready: boolean; worldPath: string;
  members: { id: string; role: string; source: string }[];
  backups: { name: string; path: string; createdAt: string; sizeBytes: number }[];
};

function Check({ label, ok, value }: { label: string; ok: boolean; value: string }) {
  return <div className="health-check"><span className={`health-icon ${ok ? "ok" : "bad"}`}>{ok ? "✓" : "!"}</span><span className="health-label">{label}</span><strong>{value}</strong></div>;
}

export default function ValheimStatus({ auth }: { auth: AuthProvider }) {
  const [monitor, setMonitor] = useState<Monitor | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [memberId, setMemberId] = useState("");
  const [memberRole, setMemberRole] = useState("Permitted");
  const [memberBusy, setMemberBusy] = useState(false);
  const [backupBusy, setBackupBusy] = useState(false);

  const refresh = useCallback(async () => {
    setLoading(true); setError("");
    try { const response = await auth.valheimMonitor(); setMonitor(response.monitor); }
    catch (e: any) { setError(e.message ?? "Failed to load Valheim monitoring"); }
    finally { setLoading(false); }
  }, [auth]);

  useEffect(() => { refresh(); }, [refresh]);

  const addMember = async () => {
    if (!memberId.trim()) return;
    setMemberBusy(true);
    try { await auth.valheimMemberAdd(memberId.trim(), memberRole); setMemberId(""); await refresh(); }
    catch (e: any) { setError(e.message ?? "Failed to add member"); }
    finally { setMemberBusy(false); }
  };

  const removeMember = async (id: string, role: string) => {
    setMemberBusy(true);
    try { await auth.valheimMemberRemove(id, role); await refresh(); }
    catch (e: any) { setError(e.message ?? "Failed to remove member"); }
    finally { setMemberBusy(false); }
  };

  const createBackup = async () => {
    setBackupBusy(true); setError("");
    try { await auth.valheimCreateBackup(); await refresh(); }
    catch (e: any) { setError(e.message ?? "Backup failed"); }
    finally { setBackupBusy(false); }
  };

  if (loading && !monitor) return <div className="empty-state">Loading Valheim checks...</div>;
  if (!monitor) return <div className="empty-state">{error || "No monitoring data"}<br /><Btn variant="ghost" onClick={refresh}>Retry</Btn></div>;
  const active = monitor.activeState === "active";
  const running = active && monitor.subState === "running";

  return <div className="detail-section valheim-status">
    <div className="status-panel-header"><div><h4>Runtime health</h4><p className="muted">Live systemd and world checks</p></div><Btn variant="ghost" busy={loading} onClick={refresh}>↻ Refresh</Btn></div>
    {error && <p className="config-msg">❌ {error}</p>}
    <div className="health-grid">
      <Check label="Service" ok={active} value={monitor.activeState} />
      <Check label="Runtime" ok={running} value={monitor.subState} />
      <Check label="Readiness" ok={monitor.ready} value={monitor.ready ? "Ready" : "Not ready"} />
      <Check label="Main PID" ok={monitor.mainPid !== null} value={monitor.mainPid?.toString() ?? "—"} />
    </div>
    <div className="info-grid status-info"><div><span className="info-label">Invocation ID</span><span>{monitor.invocationId || "—"}</span></div><div><span className="info-label">World path</span><span title={monitor.worldPath}>{monitor.worldPath || "—"}</span></div></div>

    <div className="status-subsection"><div className="status-panel-header"><h4>Members ({monitor.members.length})</h4></div>
      <div className="rcon-input-row"><input className="rcon-input" value={memberId} onChange={e => setMemberId(e.target.value)} placeholder="Steam ID / member ID" /><select className="sandbox-select" value={memberRole} onChange={e => setMemberRole(e.target.value)}><option>Permitted</option><option>Admin</option><option>Banned</option></select><Btn busy={memberBusy} onClick={addMember}>Add</Btn></div>
      <div className="member-list">{monitor.members.length === 0 ? <p className="empty-inline">No members configured</p> : monitor.members.map(m => <div className="player-row" key={`${m.role}-${m.id}`}><span><strong>{m.role}</strong> · {m.id}</span><Btn variant="danger" busy={memberBusy} onClick={() => removeMember(m.id, m.role)}>Remove</Btn></div>)}</div>
    </div>

    <div className="status-subsection"><div className="status-panel-header"><div><h4>World backups ({monitor.backups.length})</h4><p className="muted">Create only while the service is stopped</p></div><Btn variant="ghost" busy={backupBusy} disabled={active} onClick={createBackup}>Create backup</Btn></div>
      <div className="backup-list">{monitor.backups.length === 0 ? <p className="empty-inline">No backups found</p> : monitor.backups.map(b => <div className="backup-row" key={b.path}><span>{b.name}</span><span className="muted">{(b.sizeBytes / 1024 / 1024).toFixed(1)} MB · {new Date(b.createdAt).toLocaleString()}</span></div>)}</div>
    </div>
  </div>;
}

export function ValheimLogs({ auth }: { auth: AuthProvider }) {
  const [lines, setLines] = useState(200); const [content, setContent] = useState(""); const [loading, setLoading] = useState(false);
  const load = useCallback(async () => { setLoading(true); try { const r = await auth.valheimLogs(lines); setContent(r.content ?? ""); } catch { setContent("Failed to load Valheim logs"); } finally { setLoading(false); } }, [auth, lines]);
  useEffect(() => { load(); }, [load]);
  return <div className="detail-section"><div className="log-controls"><label>Journal lines</label><input type="number" min={10} max={1000} value={lines} onChange={e => setLines(Number(e.target.value))} className="sm-input" /><Btn variant="ghost" busy={loading} onClick={load}>Load</Btn></div><pre className="log-viewer">{content || (loading ? "Loading..." : "No logs")}</pre></div>;
}
