import { useCallback, useEffect, useState } from "react";
import { AuthProvider } from "../../auth";
import type { Server } from "../../auth";
import { Btn, GAME_ICON, STATUS_COLOR, STATUS_LABEL, StatusDot } from "../common";
import ValheimStatus, { ValheimLogs } from "./ValheimStatus";
import WorldBackups from "./WorldBackups";

type DetailTab = "controls" | "status" | "backups" | "logs" | "config" | "rcon" | "mods" | "sandbox";

/* ═══════════ SERVER DETAIL VIEW ═══════════ */
export default function ServerDetailView({ server, auth, loading, onAction, onBack }: {
  server: Server; auth: AuthProvider; loading: boolean;
  onAction: (id: string, type: "start" | "stop") => void;
  onBack: () => void;
}) {
  const [tab, setTab] = useState<DetailTab>("controls");

  const isPZ = server.gameType === "ProjectZomboid";
  const isValheim = server.gameType === "Valheim";
  const tabs: { id: DetailTab; label: string }[] = [
    { id: "controls", label: "Controls" },
    ...(isValheim ? [{ id: "status" as DetailTab, label: "Status & Checks" }, { id: "backups" as DetailTab, label: "World Versions" }] : []),
    ...(isPZ ? [{ id: "backups" as DetailTab, label: "World Versions" }] : []),
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
        {tab === "status" && isValheim && <ValheimStatus auth={auth} />}
        {tab === "backups" && (isValheim || isPZ) && <WorldBackups gameType={isValheim ? "Valheim" : "ProjectZomboid"} auth={auth} serverStatus={server.status} />}
        {tab === "logs" && (isValheim ? <ValheimLogs auth={auth} /> : <DetailLogs server={server} auth={auth} />)}
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
