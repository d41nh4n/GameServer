import { useEffect, useState, useCallback } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr";
import { ApiError, AuthProvider } from "./auth";
import type { Server } from "./auth";

const API_BASE = "http://100.82.102.38:5000";
const HUB_URL = `${API_BASE}/hubs/server`;

/* ───────── helpers ───────── */
const STATUS_LABEL: Record<number, string> = { 0: "Stopped", 1: "Running", 2: "Starting", 3: "Stopping" };
const STATUS_COLOR: Record<number, string> = { 0: "var(--red)", 1: "var(--green)", 2: "var(--orange)", 3: "var(--orange)" };
const GAME_ICON: Record<string, string> = { Valheim: "⚔", Minecraft: "⛏", ProjectZomboid: "🧟", default: "🎮" };

type Page = "servers" | "detail";
type DetailTab = "controls" | "logs" | "config" | "rcon" | "mods" | "sandbox";

/* ───────── Button ───────── */
function Btn({ children, variant, busy, ...rest }: { children: React.ReactNode; variant?: "primary" | "danger" | "ghost"; busy?: boolean } & React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button className={`btn btn-${variant ?? "primary"}`} disabled={busy || rest.disabled} {...rest}>
      {busy ? <span className="spinner" /> : children}
    </button>
  );
}

/* ───────── StatusDot ───────── */
function StatusDot({ status, sm }: { status: number; sm?: boolean }) {
  const color = STATUS_COLOR[status] ?? "var(--text-muted)";
  return <span className={`status-dot${sm ? " sm" : ""}`} style={{ background: color, boxShadow: `0 0 6px ${color}` }} />;
}

/* ───────── ConnIndicator ───────── */
function ConnIndicator({ status }: { status: "connecting" | "connected" | "disconnected" }) {
  const c = { connecting: "var(--orange)", connected: "var(--green)", disconnected: "var(--red)" };
  const l = { connecting: "Connecting...", connected: "Live", disconnected: "Disconnected" };
  return (
    <div className="conn-indicator">
      <span className="status-dot sm" style={{ background: c[status] }} />
      <span>{l[status]}</span>
    </div>
  );
}

/* ═══════════ LOGIN ═══════════ */
function LoginPage({ onLogin }: { onLogin: (u: string, p: string) => Promise<void> }) {
  const [user, setUser] = useState(""); const [pass, setPass] = useState("");
  const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const [show, setShow] = useState(false);
  const submit = async () => {
    if (!user || !pass) { setError("Enter username and password."); return; }
    setBusy(true); setError("");
    try { await onLogin(user, pass); } catch (e) { setError(e instanceof ApiError ? e.message : "Cannot connect."); }
    finally { setBusy(false); }
  };
  return (
    <div className="login-wrapper">
      <div className="login-card">
        <div className="login-brand">
          <span className="login-icon">🎮</span>
          <h1>Game Panel</h1>
          <p className="brand-sub">Server Control</p>
        </div>
        <div className="login-fields">
          <div className="field">
            <label>Username</label>
            <input value={user} onChange={e => setUser(e.target.value)} autoComplete="username" placeholder="admin" onKeyDown={e => e.key === "Enter" && !busy && pass && submit()} />
          </div>
          <div className="field">
            <label>Password</label>
            <div className="pass-wrap">
              <input type={show ? "text" : "password"} value={pass} onChange={e => setPass(e.target.value)} autoComplete="current-password" placeholder="••••••••" onKeyDown={e => e.key === "Enter" && !busy && submit()} />
              <button className="pass-toggle" type="button" onClick={() => setShow(v => !v)} tabIndex={-1}>{show ? "🙈" : "👁"}</button>
            </div>
          </div>
          {error && <p className="field-error">{error}</p>}
          <Btn busy={busy} onClick={submit} style={{ width: "100%", marginTop: 8 }}>Sign In</Btn>
        </div>
      </div>
    </div>
  );
}

/* ═══════════ LOGIN PAGE END ═══════════ */

/* ═══════════ SERVER CARD ═══════════ */
function ServerCard({ server, onClick }: { server: Server; onClick: () => void }) {
  const icon = GAME_ICON[server.gameType] ?? GAME_ICON.default;
  return (
    <div className="server-card" onClick={onClick} style={{ cursor: "pointer" }}>
      <div className="card-header">
        <span className="game-icon">{icon}</span>
        <div className="card-title">
          <h3>{server.name}</h3>
          <span className="game-type">{server.gameType}</span>
        </div>
        <div className="card-status">
          <StatusDot status={server.status} />
          <span className="status-text">{STATUS_LABEL[server.status] ?? "Unknown"}</span>
        </div>
      </div>
      <div className="card-meta">
        <div className="meta-item"><span className="meta-label">World</span><span className="meta-value">{server.worldName || "—"}</span></div>
        <div className="meta-item"><span className="meta-label">Port</span><span className="meta-value">{server.port}</span></div>
        {server.provisioningMode === "AdoptExisting" && <div className="meta-item"><span className="meta-label">Type</span><span className="meta-value">{server.runtimeType === "Systemd" ? "Systemd" : "Process"}</span></div>}
        <div className="meta-item"><span className="meta-label">Ready</span><span className={`meta-value ${server.ready ? "ready" : "not-ready"}`}>{server.ready ? "Yes" : "No"}</span></div>
      </div>
      <div className="card-click-hint">Click to manage →</div>
    </div>
  );
}

/* ═══════════ SERVER LIST VIEW ═══════════ */
function ServerListView({ servers, error, loading, onRefresh, onSelect }: {
  servers: Server[]; error: string; loading: boolean;
  onRefresh: () => void; onSelect: (s: Server) => void;
}) {
  return (
    <>
      <div className="toolbar">
        <h2 className="section-title">
          Servers
          <span className="server-count">{servers.length}</span>
        </h2>
        <div className="toolbar-actions">
          {error && <p className="error-msg">{error}</p>}
          <Btn variant="ghost" onClick={onRefresh} disabled={loading}>&#8635; Refresh</Btn>
        </div>
      </div>
      {servers.length === 0 && !loading && (
        <div className="empty-state"><span style={{ fontSize: 40 }}>📡</span><p>No servers found</p></div>
      )}
      <div className="server-grid">
        {servers.map(s => <ServerCard key={s.id} server={s} onClick={() => onSelect(s)} />)}
      </div>
    </>
  );
}

/* ═══════════ SERVER DETAIL VIEW ═══════════ */
function ServerDetailView({ server, auth, loading, onAction, onBack }: {
  server: Server; auth: AuthProvider; loading: boolean;
  onAction: (id: string, type: "start" | "stop") => void;
  onBack: () => void;
}) {
  const [tab, setTab] = useState<DetailTab>("controls");

  const isPZ = server.gameType === "ProjectZomboid";
  const tabs: { id: DetailTab; label: string }[] = [
    { id: "controls", label: "Controls" },
    { id: "logs", label: "Logs" },
    ...(isPZ ? [
      { id: "config" as DetailTab, label: "Config" },
      { id: "rcon" as DetailTab, label: "RCON" },
      { id: "mods" as DetailTab, label: "Mods" },
      { id: "sandbox" as DetailTab, label: "Sandbox" },
    ] : []),
  ];

  return (
    <>
      <div className="detail-toolbar">
        <Btn variant="ghost" onClick={onBack}>← Back</Btn>
        <h2 className="section-title" style={{ margin: 0 }}>
          <span className="game-icon" style={{ fontSize: "1.2rem" }}>{GAME_ICON[server.gameType] ?? "🎮"}</span>
          {server.name}
        </h2>
        <div className="card-status">
          <StatusDot status={server.status} />
          <span className="status-text">{STATUS_LABEL[server.status] ?? "Unknown"}</span>
        </div>
      </div>

      <div className="detail-tabs">
        {tabs.map(t => (
          <button key={t.id} className={`detail-tab${tab === t.id ? " active" : ""}`} onClick={() => setTab(t.id)}>
            {t.label}
          </button>
        ))}
      </div>

      <div className="detail-content">
        {tab === "controls" && <DetailControls server={server} loading={loading} onAction={onAction} />}
        {tab === "logs" && <DetailLogs server={server} auth={auth} />}
        {tab === "config" && isPZ && <DetailConfig auth={auth} />}
        {tab === "rcon" && isPZ && <DetailRcon auth={auth} />}
        {tab === "mods" && isPZ && <DetailMods auth={auth} />}
        {tab === "sandbox" && isPZ && <DetailSandbox auth={auth} />}
      </div>
    </>
  );
}

/* ───── Controls tab ───── */
function DetailControls({ server, loading, onAction }: { server: Server; loading: boolean; onAction: (id: string, type: "start" | "stop") => void }) {
  const isStopped = server.status === 0;
  const isRunning = server.status === 1;
  const isBusy = server.status === 2 || server.status === 3;
  return (
    <div className="detail-controls">
      <div className="detail-section">
        <h4>Server Info</h4>
        <div className="info-grid">
          <div><span className="info-label">Game</span><span>{server.gameType}</span></div>
          <div><span className="info-label">World</span><span>{server.worldName || "—"}</span></div>
          <div><span className="info-label">Port</span><span>{server.port}</span></div>
          <div><span className="info-label">Mode</span><span>{server.provisioningMode === "AdoptExisting" ? "Adopt (systemd)" : "Managed"}</span></div>
          <div><span className="info-label">Runtime user</span><span>{server.gameType === "ProjectZomboid" ? "pzserver" : server.gameType === "Valheim" ? "valheim" : "—"}</span></div>
          <div><span className="info-label">Status</span><span style={{ color: STATUS_COLOR[server.status] }}>{STATUS_LABEL[server.status]}</span></div>
          <div><span className="info-label">Ready</span><span>{server.ready ? "✅ Yes" : "❌ No"}</span></div>
        </div>
      </div>
      <div className="detail-section">
        <h4>Actions</h4>
        <div className="action-btns">
          <Btn variant="primary" disabled={loading || isRunning || isBusy} busy={loading && !isRunning} onClick={() => onAction(server.id, "start")}>▶ Start</Btn>
          <Btn variant="danger" disabled={loading || isStopped || isBusy} busy={loading && isRunning} onClick={() => onAction(server.id, "stop")}>■ Stop</Btn>
        </div>
      </div>
    </div>
  );
}

/* ───── Logs tab ───── */
function DetailLogs({ server, auth }: { server: Server; auth: AuthProvider }) {
  const [lines, setLines] = useState(200);
  const [content, setContent] = useState("");
  const [loading, setLoading] = useState(false);
  const [files, setFiles] = useState<{ name: string; size: number; mtime: string }[]>([]);
  const [selectedFile, setSelectedFile] = useState("");
  const [fileContent, setFileContent] = useState("");
  const [mode, setMode] = useState<"journal" | "files">("journal");

  const fetchJournal = useCallback(async () => {
    setLoading(true);
    try {
      const res = await auth.pzLogs(server.id, lines);
      setContent(res.content);
    } catch { setContent("Failed to load logs"); }
    finally { setLoading(false); }
  }, [server.id, lines, auth]);

  const fetchFileList = useCallback(async () => {
    try { const res = await auth.pzLogsList(); setFiles(res.files ?? []); } catch {}
  }, [auth]);

  const fetchFile = useCallback(async (name: string) => {
    setLoading(true); setSelectedFile(name);
    try { const res = await auth.pzLogRead(name); setFileContent(res.content); }
    catch { setFileContent("Failed to load file"); }
    finally { setLoading(false); }
  }, [auth]);

  useEffect(() => { if (mode === "journal") fetchJournal(); else fetchFileList(); }, [mode, fetchJournal, fetchFileList]);

  return (
    <div className="detail-section">
      <div className="log-mode-tabs">
        <button className={`log-mode-tab${mode === "journal" ? " active" : ""}`} onClick={() => setMode("journal")}>Journald</button>
        <button className={`log-mode-tab${mode === "files" ? " active" : ""}`} onClick={() => { setMode("files"); fetchFileList(); }}>Log Files</button>
      </div>
      {mode === "journal" ? (
        <>
          <div className="log-controls">
            <label>Lines</label>
            <input type="number" value={lines} onChange={e => setLines(Number(e.target.value))} min={10} max={1000} className="sm-input" />
            <Btn variant="ghost" busy={loading} onClick={fetchJournal}>Load</Btn>
          </div>
          <pre className="log-viewer">{content || (loading ? "Loading..." : "No logs")}</pre>
        </>
      ) : (
        <div className="log-file-browser">
          <div className="file-list">
            {files.map(f => (
              <div key={f.name} className={`file-item${selectedFile === f.name ? " active" : ""}`} onClick={() => fetchFile(f.name)}>
                <span className="file-name">{f.name}</span>
                <span className="file-size">{(f.size / 1024).toFixed(1)} KB</span>
              </div>
            ))}
            {files.length === 0 && <p className="empty-state" style={{ padding: 20 }}>No log files</p>}
          </div>
          {selectedFile && <pre className="log-viewer">{fileContent || "Loading..."}</pre>}
        </div>
      )}
    </div>
  );
}

/* ───── Config tab ───── */
function DetailConfig({ auth }: { auth: AuthProvider }) {
  const [config, setConfig] = useState<any>(null);
  const [raw, setRaw] = useState("");
  const [view, setView] = useState<"parsed" | "raw">("parsed");
  const [msg, setMsg] = useState("");

  useEffect(() => {
    (async () => {
      try {
        const [c, r] = await Promise.all([auth.pzConfig(), auth.pzConfigRaw()]);
        setConfig(c.config); setRaw(r.content);
      } catch { setMsg("Failed to load config"); }
    })();
  }, [auth]);

  const saveConfig = async () => {
    setMsg("");
    try {
      // Parse raw back to structured updates
      await auth.pzConfigUpdate(config);
      setMsg("✅ Config saved (backup created)");
    } catch { setMsg("❌ Save failed"); }
  };

  return (
    <div className="detail-section">
      <div className="log-mode-tabs">
        <button className={`log-mode-tab${view === "parsed" ? " active" : ""}`} onClick={() => setView("parsed")}>Parsed</button>
        <button className={`log-mode-tab${view === "raw" ? " active" : ""}`} onClick={() => setView("raw")}>Raw</button>
      </div>
      {msg && <p className="config-msg">{msg}</p>}
      {view === "parsed" && config && (
        <div className="config-viewer">
          {Object.entries(config).map(([section, keys]) => (
            <div key={section} className="config-section">
              <h5>[{section}]</h5>
              {Object.entries(keys as Record<string, string>).map(([k, v]) => (
                <div key={k} className="config-row">
                  <span className="config-key">{k}</span>
                  <input value={v} onChange={e => {
                    const copy = { ...config };
                    copy[section] = { ...copy[section], [k]: e.target.value };
                    setConfig(copy);
                  }} className="config-input" />
                </div>
              ))}
            </div>
          ))}
          <Btn onClick={saveConfig} style={{ marginTop: 12 }}>Save Changes</Btn>
        </div>
      )}
      {view === "raw" && <pre className="log-viewer">{raw || "Loading..."}</pre>}
    </div>
  );
}

/* ───── RCON tab ───── */
function DetailRcon({ auth }: { auth: AuthProvider }) {
  const [cmd, setCmd] = useState(""); const [output, setOutput] = useState(""); const [busy, setBusy] = useState(false);
  const [players, setPlayers] = useState<string[]>([]); const [playerBusy, setPlayerBusy] = useState(false);

  const loadPlayers = async () => {
    setPlayerBusy(true);
    try {
      const res = await auth.pzRconPlayers();
      const text = String(res.output ?? "");
      setOutput(text || "Players connected (0)");
      setPlayers(text.split("\n").slice(1).map(x => x.trim()).filter(Boolean));
    } catch (e: any) { setOutput(`RCON error: ${e.message ?? e}`); setPlayers([]); }
    finally { setPlayerBusy(false); }
  };

  const run = async (command: string) => {
    setBusy(true); setOutput("");
    try { const res = await auth.pzRconCommand(command); setOutput(res.output || "(empty response)"); }
    catch (e: any) { setOutput(`RCON error: ${e.message ?? e}`); }
    finally { setBusy(false); }
  };
  const presets = [{ label: "Players", cmd: "players" }, { label: "Save", cmd: "save" }, { label: "Say Hello", cmd: 'servermsg "Hello from Panel"' }];

  return (
    <div className="detail-section">
      <div className="rcon-presets">
        <Btn variant="ghost" onClick={loadPlayers} busy={playerBusy}>↻ Refresh Players</Btn>
        {presets.slice(1).map(p => <Btn key={p.label} variant="ghost" onClick={() => run(p.cmd)} busy={busy}>{p.label}</Btn>)}
      </div>
      <div className="player-list">
        <div className="player-list-title">Online players <span className="server-count">{players.length}</span></div>
        {players.length === 0 ? <p className="empty-inline">No players online or click Refresh Players</p> : players.map((p, i) => <div className="player-row" key={`${p}-${i}`}><span>👤 {p}</span><Btn variant="danger" onClick={() => run(`kickuser "${p}" -r "Kicked by admin"`)}>Kick</Btn></div>)}
      </div>
      <div className="rcon-input-row">
        <input value={cmd} onChange={e => setCmd(e.target.value)} placeholder="Enter RCON command..." className="rcon-input" onKeyDown={e => e.key === "Enter" && !busy && cmd && run(cmd)} />
        <Btn busy={busy} onClick={() => cmd && run(cmd)}>Send</Btn>
      </div>
      <pre className="log-viewer">{output || "Run a command to see output"}</pre>
    </div>
  );
}

/* ───── Mods tab ───── */
function DetailMods({ auth }: { auth: AuthProvider }) {
  const [workshop, setWorkshop] = useState<string[]>([]); const [mods, setMods] = useState<string[]>([]);
  const [newWorkshop, setNewWorkshop] = useState(""); const [newMod, setNewMod] = useState(""); const [msg, setMsg] = useState(""); const [loading, setLoading] = useState(true);
  useEffect(() => { (async () => { try { const r = await auth.pzMods(); setWorkshop(r.mods?.workshopIds ?? []); setMods(r.mods?.modIds ?? []); } catch { setMsg("Failed to load mods"); } finally { setLoading(false); } })(); }, [auth]);
  const save = async () => { try { const r = await auth.pzModsUpdate(workshop, mods); setWorkshop(r.mods.workshopIds); setMods(r.mods.modIds); setMsg("✅ Mods saved; restart required"); } catch (e: any) { setMsg(`❌ ${e.message ?? e}`); } };
  if (loading) return <div className="empty-state">Loading mods...</div>;
  return <div className="detail-section"><h4>Workshop dependencies ({workshop.length})</h4><div className="tag-list">{workshop.map(x => <span className="id-tag" key={x}>{x}<button onClick={() => setWorkshop(workshop.filter(v => v !== x))}>×</button></span>)}</div><div className="rcon-input-row"><input className="rcon-input" value={newWorkshop} onChange={e => setNewWorkshop(e.target.value)} placeholder="Workshop ID" /><Btn onClick={() => { if (newWorkshop && !workshop.includes(newWorkshop)) setWorkshop([...workshop, newWorkshop]); setNewWorkshop(""); }}>Add</Btn></div><h4>Mod IDs ({mods.length})</h4><div className="tag-list">{mods.map(x => <span className="id-tag" key={x}>{x}<button onClick={() => setMods(mods.filter(v => v !== x))}>×</button></span>)}</div><div className="rcon-input-row"><input className="rcon-input" value={newMod} onChange={e => setNewMod(e.target.value)} placeholder="Mod ID" /><Btn onClick={() => { if (newMod && !mods.includes(newMod)) setMods([...mods, newMod]); setNewMod(""); }}>Add</Btn></div>{msg && <p className="config-msg">{msg}</p>}<Btn onClick={save}>Save Mod Configuration</Btn></div>;
}

/* ───── Sandbox tab ───── */
function DetailSandbox({ auth }: { auth: AuthProvider }) {
  const [groups, setGroups] = useState<any[]>([]);
  const [msg, setMsg] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    (async () => {
      try { const res = await auth.pzSandboxConfig(); setGroups(res.groups ?? []); }
      catch { setMsg("Failed to load SandboxVars"); }
      finally { setLoading(false); }
    })();
  }, [auth]);

  if (loading) return <div className="empty-state">Loading SandboxVars...</div>;
  if (!groups.length) return <div className="empty-state">No SandboxVars found</div>;

  return (
    <div className="detail-section">
      <div className="sandbox-content">
        {groups.map(g => (
          <details key={g.name} className="sandbox-group">
            <summary className="sandbox-summary">{g.label} ({g.fields?.length ?? 0})</summary>
            <div className="sandbox-fields">
              {(g.fields ?? []).map((f: any) => (
                <div key={f.key} className="sandbox-field">
                  <span className="config-key">{f.key}</span>
                  {f.editable ? (
                    <SandboxFieldInput field={f} auth={auth} onMsg={setMsg} />
                  ) : (
                    <span className="meta-value">{String(f.value)}</span>
                  )}
                </div>
              ))}
            </div>
          </details>
        ))}
      </div>
      {msg && <p className="config-msg">{msg}</p>}
    </div>
  );
}

function SandboxFieldInput({ field, auth, onMsg }: { field: any; auth: AuthProvider; onMsg: (m: string) => void }) {
  const [value, setValue] = useState(String(field.value));
  const [saving, setSaving] = useState(false);
  const save = async () => {
    setSaving(true); onMsg("");
    try {
      const parsed = field.detectedType === "bool" ? value === "true" :
        field.detectedType === "int" ? parseInt(value) :
        field.detectedType === "float" ? parseFloat(value) : value;
      await auth.pzSandboxSave([{ section: "SandboxVars", key: field.key, value: parsed }]);
      onMsg(`✅ ${field.key} saved`);
    } catch (e: any) { onMsg(`❌ ${e.message ?? e}`); }
    finally { setSaving(false); }
  };
  return (
    <div className="sandbox-input-row">
      {field.detectedType === "bool" ? (
        <select value={value} onChange={e => setValue(e.target.value)} className="sandbox-select">
          <option value="true">true</option><option value="false">false</option>
        </select>
      ) : <input value={value} onChange={e => setValue(e.target.value)} className="config-input sm" />}
      <Btn variant="ghost" busy={saving} onClick={save}>Save</Btn>
    </div>
  );
}

/* ═══════════ MAIN APP ═══════════ */
export default function App() {
  const [auth] = useState(() => new AuthProvider());

  const [loggedIn, setLoggedIn] = useState(auth.isAuthenticated);
  const [username, setUsername] = useState("");
  const [servers, setServers] = useState<Server[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [connStatus, setConnStatus] = useState<"connecting" | "connected" | "disconnected">("disconnected");
  const [conn, setConn] = useState<HubConnection | null>(null);
  const [page, setPage] = useState<Page>("servers");
  const [selectedServer, setSelectedServer] = useState<Server | null>(null);

  useEffect(() => {
    const unsub = auth.subscribe(() => setLoggedIn(auth.isAuthenticated));
    return unsub;
  }, []);

  const doLogin = async (user: string, pass: string) => {
    setUsername(user);
    await auth.login(user, pass);
  };

  const doLogout = () => {
    conn?.stop().catch(() => {});
    setConn(null); setConnStatus("disconnected"); setServers([]); setError("");
    auth.logout();
  };

  const fetchServers = useCallback(async () => {
    try { setServers(await auth.listServers()); }
    catch (e) { if (e instanceof ApiError) setError(e.message); else setError("API Error"); }
  }, [auth]);

  useEffect(() => {
    if (!loggedIn) return;
    let disposed = false;
    const build = new HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: () => auth.currentToken ?? "" })
      .configureLogging(LogLevel.Error).build();
    const start = async () => {
      setConnStatus("connecting");
      try {
        await build.start();
        if (disposed) return;
        setConn(build); setConnStatus("connected");
      } catch (e) {
        if (disposed) return;
        const err = e as { status?: number };
        if (err?.status === 401 || err?.status === 403) { auth.logout(); return; }
        setConnStatus("disconnected");
      }
    };
    build.on("ServerStateChanged", (id: string, status: number) => {
      setServers(prev => prev.map(s => s.id === id ? { ...s, status } : s));
      setSelectedServer(prev => prev?.id === id ? { ...prev, status } : prev);
    });
    build.onclose(() => { if (!disposed) setConnStatus("disconnected"); });
    start();
    fetchServers();
    return () => { disposed = true; build.stop().catch(() => {}); };
  }, [loggedIn, auth, fetchServers]);

  const action = async (id: string, type: "start" | "stop") => {
    setLoading(true); setError("");
    try { await auth.trigger(id, type); await fetchServers(); }
    catch (e) { if (e instanceof ApiError) setError(e.message); else setError("Action failed"); }
    finally { setLoading(false); }
  };

  const selectServer = (s: Server) => { setSelectedServer(s); setPage("detail"); };

  if (!loggedIn) return <LoginPage onLogin={doLogin} />;

  return (
    <div className="app-layout">
      {/* Navbar */}
      <nav className="navbar">
        <div className="nav-left">
          <span className="nav-brand" onClick={() => { setPage("servers"); setSelectedServer(null); }} style={{ cursor: "pointer" }}>
            🎮 <span className="nav-brand-text">Game Panel</span>
          </span>
          <div className="nav-links">
            <button className={`nav-link${page === "servers" ? " active" : ""}`}
              onClick={() => { setPage("servers"); setSelectedServer(null); }}>
              📊 Servers
            </button>
          </div>
        </div>
        <div className="nav-right">
          <ConnIndicator status={connStatus} />
          <span className="header-user">{username}</span>
          <Btn variant="ghost" onClick={doLogout}>Logout</Btn>
        </div>
      </nav>

      <main className="app-main">
        {error && <div className="error-bar">{error}</div>}
        {page === "servers" ? (
          <ServerListView servers={servers} error={error} loading={loading}
            onRefresh={fetchServers} onSelect={selectServer} />
        ) : selectedServer ? (
          <ServerDetailView server={selectedServer} auth={auth} loading={loading}
            onAction={action} onBack={() => { setPage("servers"); setSelectedServer(null); }} />
        ) : null}
      </main>
    </div>
  );
}