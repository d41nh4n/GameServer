import { useEffect, useState } from "react";
import type { AuthProvider } from "../../auth";
import { Btn } from "../common";

const actions = [
  ["broadcast", "Broadcast message", "BroadcastMessage"],
  ["save", "Save world now", "SaveWorld"],
] as const;
const futureActions = [["time", "Change time", "ChangeTime"], ["weather", "Change weather", "ChangeWeather"], ["teleport", "Teleport", "TeleportPlayer"], ["spawn", "Spawn item", "SpawnItem"], ["inventory", "Inventory", "InventoryManagement"], ["god-mode", "God mode", "GodMode"]] as const;

export default function ValheimAdmin({ serverId, auth, serverStatus }: { serverId: string; auth: AuthProvider; serverStatus: number }) {
  const [caps, setCaps] = useState<any>(null); const [players, setPlayers] = useState<any[]>([]); const [message, setMessage] = useState(""); const [notice, setNotice] = useState("");
  const load = async () => { try { setCaps(await auth.valheimCapabilities(serverId)); } catch (e: any) { setNotice(e.message ?? "Control plugin unavailable"); } };
  useEffect(() => { void load(); }, [serverId]);
  const supported = (name: string) => Boolean(caps?.connected && (caps.supported ?? []).some((x: any) => String(x) === name));
  const run = async (action: string, capability: string, body: Record<string, unknown> = {}) => { if (serverStatus !== 1) return setNotice("Valheim must be Started"); if (!supported(capability)) return setNotice(caps?.reason ?? "Capability unavailable"); try { await auth.valheimAction(serverId, action, body); setNotice("Action completed"); } catch (e: any) { setNotice(e.message ?? "Action failed"); } };
  const refreshPlayers = async () => { try { setPlayers(await auth.valheimPlayers(serverId)); } catch (e: any) { setNotice(e.message ?? "Players unavailable"); } };
  return <div className="detail-section"><div className="status-panel-header"><div><h4>Valheim Admin Control</h4><p className="muted">Live plugin protocol only; no shell fallback.</p></div><Btn variant="ghost" onClick={load}>Refresh health</Btn></div><p className="muted">Plugin: {caps?.connected ? "Connected" : "Disconnected"} · {caps?.reason ?? "No capability data"}</p><div className="action-btns">{actions.map(([action, label, capability]) => <Btn key={action} disabled={!supported(capability) || serverStatus !== 1} onClick={() => action === "broadcast" ? run(action, capability, { message }) : run(action, capability)}>{label}</Btn>)}{futureActions.map(([action, label, capability]) => <Btn key={action} disabled={!supported(capability) || serverStatus !== 1} onClick={() => setNotice(`${label} requires typed parameters`)}>{label}</Btn>)}</div><div className="log-controls"><input value={message} onChange={e => setMessage(e.target.value)} placeholder="Broadcast message" disabled={!supported("BroadcastMessage")} /><Btn variant="ghost" onClick={refreshPlayers}>Refresh players</Btn></div>{players.map(p => <div className="player-row" key={p.platformId}><span>{p.name}</span><span className="muted">{p.platformId}</span><span className="player-actions"><Btn variant="danger" disabled={!supported("KickPlayer") || serverStatus !== 1} onClick={() => window.confirm(`Kick ${p.name}?`) && run("kick", "KickPlayer", { targetPlayerId: p.platformId })}>Kick</Btn><Btn variant="danger" disabled={!supported("BanPlayer") || serverStatus !== 1} onClick={() => window.confirm(`Ban ${p.name}?`) && run("ban", "BanPlayer", { targetPlayerId: p.platformId })}>Ban</Btn></span></div>)}{notice && <p className="config-msg">{notice}</p>}</div>;
}
