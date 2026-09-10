import { useCallback, useEffect, useState } from "react";
import type { AuthProvider } from "../../auth";
import { Btn } from "../common";

type Backup = { name: string; path: string; createdAt: string; sizeBytes: number };

export default function WorldBackups({ gameType, auth, serverStatus }: { gameType: "Valheim" | "ProjectZomboid"; auth: AuthProvider; serverStatus?: number }) {
  const [backups, setBackups] = useState<Backup[]>([]); const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  const load = useCallback(async () => { setLoading(true); try { const r = gameType === "Valheim" ? await auth.valheimBackups() : await auth.pzBackups(); setBackups(r.backups ?? []); } catch (e: any) { setError(e.message ?? "Failed to load backups"); } finally { setLoading(false); } }, [auth, gameType]);
  useEffect(() => { load(); }, [load]);
  const create = async () => { setBusy(true); setError(""); try { if (gameType === "Valheim") await auth.valheimCreateBackup(); else await auth.pzCreateBackup(); await load(); } catch (e: any) { setError(e.message ?? "Backup failed"); } finally { setBusy(false); } };
  const rollback = async (version: string) => {
    if (!window.confirm(`Rollback ${gameType} to ${version}? The current HEAD will be backed up first and the server must be stopped.`)) return;
    setBusy(true); setError(""); try { if (gameType === "Valheim") await auth.valheimRollback(version); else await auth.pzRollback(version); await load(); } catch (e: any) { setError(e.message ?? "Rollback failed"); } finally { setBusy(false); }
  };
  const liveValheim = gameType === "Valheim" && serverStatus === 1;
  return <div className="detail-section"><div className="status-panel-header"><div><h4>World version history</h4><p className="muted">{liveValheim ? "Stop Valheim before creating a consistent world backup" : "HEAD is protected before every rollback"}</p></div><Btn variant="ghost" busy={busy || loading} disabled={liveValheim} onClick={create}>＋ Create backup</Btn></div>{error && <p className="config-msg">❌ {error}</p>}
    <div className="backup-timeline">{backups.length === 0 ? <p className="empty-inline">No versions found</p> : backups.map((b, i) => <div className="backup-node" key={b.path}><div className="node-dot">{i === 0 ? "H" : i + 1}</div><div className="node-line" /><div className="backup-node-card"><div><strong>{i === 0 ? "HEAD · " : ""}{b.name}</strong><p className="muted">{new Date(b.createdAt).toLocaleString()} · {(b.sizeBytes / 1024 / 1024).toFixed(1)} MB</p></div>{i !== 0 && <Btn variant="danger" disabled={busy} onClick={() => rollback(b.name)}>Rollback</Btn>}</div></div>)}</div>
  </div>;
}
