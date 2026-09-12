import { useCallback, useEffect, useRef, useState } from "react";
import type { AuthProvider } from "../../auth";
import { Btn } from "../common";

type Monitor = {
  activeState: string; subState: string; mainPid: number | null;
  invocationId: string; ready: boolean; worldPath: string;
  members: { id: string; role: string; source: string }[];
  onlinePlayers: string[];
  connectedPlayers: { steamId: string; name: string | null; online: boolean }[];
  backups: { name: string; path: string; createdAt: string; sizeBytes: number }[];
  installedBuildId?: string;
  latestBuildId?: string;
  updateAvailable?: boolean;
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
  const [updateBusy, setUpdateBusy] = useState(false);
  const [versionChecking, setVersionChecking] = useState(false);
  const [updateLog, setUpdateLog] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true); setError("");
    try { const response = await auth.valheimMonitor(); setMonitor(response.monitor); }
    catch (e: any) { setError(e.message ?? "Failed to load Valheim monitoring"); }
    finally { setLoading(false); }
  }, [auth]);

  useEffect(() => { refresh(); }, [refresh]);

  const checkVersion = async () => {
    setVersionChecking(true); setError("");
    try {
      const res = await auth.valheimVersion();
      setMonitor(prev => prev ? {
        ...prev,
        installedBuildId: res.installedBuildId ?? prev.installedBuildId,
        latestBuildId: res.latestBuildId ?? prev.latestBuildId,
        updateAvailable: res.updateAvailable ?? prev.updateAvailable,
      } : prev);
    } catch (e: any) {
      setError(e.message ?? "Failed to check latest game version");
    } finally {
      setVersionChecking(false);
    }
  };

  const runUpdate = async () => {
    if (active) {
      alert("Please stop the Valheim server before updating via SteamCMD.");
      return;
    }
    if (!window.confirm("Are you sure you want to update the Valheim Dedicated Server using SteamCMD? This will validate and update the server installation files.")) {
      return;
    }
    setUpdateBusy(true); setError(""); setUpdateLog(null);
    try {
      const res = await auth.valheimUpdate();
      if (res.result) {
        setUpdateLog(res.result.output);
        if (res.result.success) {
          await refresh();
        } else {
          setError(`SteamCMD update failed (Exit code: ${res.result.exitCode})`);
        }
      }
    } catch (e: any) {
      setError(e.message ?? "Update request failed");
    } finally {
      setUpdateBusy(false);
    }
  };

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

    <div className="status-subsection">
      <div className="status-panel-header">
        <div>
          <h4>Game Version & SteamCMD Update</h4>
          <p className="muted">App ID 896660 (Valheim Dedicated Server)</p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <Btn variant="ghost" busy={versionChecking} onClick={checkVersion}>
            Check for updates
          </Btn>
          <Btn
            variant={monitor.updateAvailable ? "primary" : "ghost"}
            busy={updateBusy}
            disabled={active || updateBusy}
            title={active ? "Stop the server first to update" : "Run SteamCMD update"}
            onClick={runUpdate}
          >
            {updateBusy ? "Updating..." : "Update Game"}
          </Btn>
        </div>
      </div>
      <div className="health-grid">
        <Check label="Installed Build" ok={Boolean(monitor.installedBuildId)} value={monitor.installedBuildId || "Unknown"} />
        <Check label="Latest Steam Build" ok={Boolean(monitor.latestBuildId)} value={monitor.latestBuildId || "Not checked"} />
        <Check
          label="Version Status"
          ok={!monitor.updateAvailable}
          value={
            monitor.updateAvailable
              ? "Update Available"
              : monitor.installedBuildId && monitor.latestBuildId
              ? "Up to date"
              : "Checking..."
          }
        />
        <Check
          label="Update Policy"
          ok={!active}
          value={active ? "Blocked (Server Active)" : "Ready (Stopped)"}
        />
      </div>
      {active && (
        <p className="config-msg" style={{ marginTop: "8px" }}>
          ℹ️ Server is currently active. To update files safely without corruption, Stop the Valheim server first.
        </p>
      )}
      {updateLog && (
        <div style={{ marginTop: "12px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "4px" }}>
            <strong>SteamCMD Output:</strong>
            <Btn variant="ghost" onClick={() => setUpdateLog(null)}>Dismiss</Btn>
          </div>
          <pre className="log-viewer" style={{ maxHeight: "200px" }}>{updateLog}</pre>
        </div>
      )}
    </div>

    <div className="status-subsection"><div className="status-panel-header"><h4>Connected players ({monitor.connectedPlayers?.length ?? monitor.onlinePlayers?.length ?? 0})</h4></div>
      <div className="player-list">{(monitor.connectedPlayers?.length ?? 0) === 0 ? <p className="empty-inline">No players detected online</p> : monitor.connectedPlayers.map(player => <div className="player-row" key={player.steamId}><span>{player.online ? "🟢" : "🟡"} {player.name || "Loading character..."}</span><span className="muted">{player.steamId} · {player.online ? "Online" : "Connecting"}</span></div>)}</div>
    </div>

    <div className="status-subsection"><div className="status-panel-header"><h4>Access lists ({monitor.members.length})</h4></div>
      <div className="rcon-input-row"><input className="rcon-input" value={memberId} onChange={e => setMemberId(e.target.value)} placeholder="Steam ID / member ID" /><select className="sandbox-select" value={memberRole} onChange={e => setMemberRole(e.target.value)}><option>Permitted</option><option>Admin</option><option>Banned</option></select><Btn busy={memberBusy} onClick={addMember}>Add</Btn></div>
      <div className="member-list">{monitor.members.length === 0 ? <p className="empty-inline">No members configured</p> : monitor.members.map(m => <div className="player-row" key={`${m.role}-${m.id}`}><span><strong>{m.role}</strong> · {m.id}</span><Btn variant="danger" busy={memberBusy} onClick={() => removeMember(m.id, m.role)}>Remove</Btn></div>)}</div>
    </div>

    <div className="status-subsection"><div className="status-panel-header"><div><h4>World backups ({monitor.backups.length})</h4><p className="muted">Create only while the service is stopped</p></div><Btn variant="ghost" busy={backupBusy} disabled={active} onClick={createBackup}>Create backup</Btn></div>
      <div className="backup-list">{monitor.backups.length === 0 ? <p className="empty-inline">No backups found</p> : monitor.backups.map(b => <div className="backup-row" key={b.path}><span>{b.name}</span><span className="muted">{(b.sizeBytes / 1024 / 1024).toFixed(1)} MB · {new Date(b.createdAt).toLocaleString()}</span></div>)}</div>
    </div>
  </div>;
}

export function ValheimLogs({ auth }: { auth: AuthProvider }) {
  const [lines, setLines] = useState(200); const [content, setContent] = useState(""); const [loading, setLoading] = useState(false); const [severity, setSeverity] = useState("all"); const [search, setSearch] = useState(""); const [live, setLive] = useState(false); const inFlight = useRef(false);
  const load = useCallback(async () => { if (inFlight.current) return; inFlight.current = true; setLoading(true); try { const r = await auth.valheimLogs(Math.min(lines, 300)); setContent(r.content ?? ""); } catch { setContent("Failed to load Valheim logs"); } finally { inFlight.current = false; setLoading(false); } }, [auth, lines]);
  useEffect(() => { load(); }, [load]); useEffect(() => { if (!live) return; const timer = window.setInterval(() => { void load(); }, 5000); return () => window.clearInterval(timer); }, [live, load]);
  const visible = content.split("\n").filter(line => { const l = line.toLowerCase(); return (severity === "all" || (severity === "error" && /\berror\b|\bexception\b|\bfatal\b/i.test(line)) || (severity === "warning" && /\bwarn(?:ing)?\b/i.test(line))) && (!search || l.includes(search.toLowerCase())); }).join("\n");
  return <div className="detail-section"><div className="log-controls"><label>Journal lines</label><input type="number" min={20} max={300} value={lines} onChange={e => setLines(Math.max(20, Math.min(300, Number(e.target.value))))} className="sm-input" /><select value={severity} onChange={e => setSeverity(e.target.value)} className="sm-input"><option value="all">All</option><option value="warning">Warning</option><option value="error">Error/Fatal</option></select><input value={search} onChange={e => setSearch(e.target.value)} placeholder="Filter text..." className="log-filter-input" /><label className="live-toggle"><input type="checkbox" checked={live} onChange={e => setLive(e.target.checked)} /> Auto-refresh 5s</label><Btn variant="ghost" busy={loading} onClick={load}>Load</Btn></div><pre className="log-viewer">{visible || (loading ? "Loading..." : "No matching logs")}</pre></div>;
}
