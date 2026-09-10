import { useCallback, useEffect, useState } from "react";
import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { ApiError, AuthProvider } from "./auth";
import type { Server } from "./auth";
import { ConnIndicator, Btn } from "./components/common";
import LoginPage from "./components/auth/LoginPage";
import ServerListView from "./components/servers/ServerListView";
import ServerDetailView from "./components/servers/ServerDetailView";

const API_BASE = "http://100.82.102.38:5000";
const HUB_URL = `${API_BASE}/hubs/server`;
type Page = "servers" | "detail";

export default function App() {
  const [auth] = useState(() => new AuthProvider());
  const [loggedIn, setLoggedIn] = useState(auth.isAuthenticated);
  const [username, setUsername] = useState("");
  const [servers, setServers] = useState<Server[]>([]);
  const [resources, setResources] = useState<any>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [connStatus, setConnStatus] = useState<"connecting" | "connected" | "disconnected">("disconnected");
  const [conn, setConn] = useState<HubConnection | null>(null);
  const [page, setPage] = useState<Page>("servers");
  const [selectedServer, setSelectedServer] = useState<Server | null>(null);

  useEffect(() => auth.subscribe(() => setLoggedIn(auth.isAuthenticated)), [auth]);

  const fetchServers = useCallback(async () => {
    try { setServers(await auth.listServers()); }
    catch (e) { setError(e instanceof ApiError ? e.message : "API Error"); }
  }, [auth]);

  const fetchResources = useCallback(async () => {
    try { setResources(await auth.resourcesOverview()); }
    catch { setResources(null); }
  }, [auth]);

  useEffect(() => {
    if (!loggedIn) return;
    let disposed = false;
    const connection = new HubConnectionBuilder().withUrl(HUB_URL, { accessTokenFactory: () => auth.currentToken ?? "" }).configureLogging(LogLevel.Error).build();
    const start = async () => {
      setConnStatus("connecting");
      try { await connection.start(); if (!disposed) { setConn(connection); setConnStatus("connected"); } }
      catch (e) { if (!disposed) { const err = e as { status?: number }; if (err.status === 401 || err.status === 403) auth.logout(); else setConnStatus("disconnected"); } }
    };
    connection.on("ServerStateChanged", (id: string, status: number) => {
      setServers(prev => prev.map(s => s.id === id ? { ...s, status } : s));
      setSelectedServer(prev => prev?.id === id ? { ...prev, status } : prev);
    });
    connection.onclose(() => { if (!disposed) setConnStatus("disconnected"); });
    start(); fetchServers(); fetchResources();
    return () => { disposed = true; connection.stop().catch(() => {}); };
  }, [loggedIn, auth, fetchServers, fetchResources]);

  const doLogin = async (user: string, pass: string) => { setUsername(user); await auth.login(user, pass); };
  const doLogout = () => { conn?.stop().catch(() => {}); setConn(null); setServers([]); setResources(null); setConnStatus("disconnected"); auth.logout(); };
  const trackOperation = async (jobId: string) => {
    for (let i = 0; i < 36; i++) {
      await new Promise(resolve => window.setTimeout(resolve, 5000));
      try {
        const job = await auth.operation(jobId);
        if (job.status === 2) { await fetchServers(); await fetchResources(); return; }
        if (job.status === 3) { setError(job.error || "Server operation failed"); await fetchServers(); return; }
      } catch { return; }
    }
    setError("Server operation is still running. Check status and journal logs.");
  };
  const action = async (id: string, type: "start" | "stop" | "restart") => {
    setLoading(true); setError("");
    try { const job = await auth.trigger(id, type); void trackOperation(job.id); }
    catch (e) { setError(e instanceof ApiError ? e.message : "Action failed"); }
    finally { setLoading(false); }
  };

  if (!loggedIn) return <LoginPage onLogin={doLogin} />;
  return <div className="app-layout">
    <nav className="navbar"><div className="nav-left"><span className="nav-brand" onClick={() => { setPage("servers"); setSelectedServer(null); }} style={{ cursor: "pointer" }}>🎮 <span className="nav-brand-text">Game Panel</span></span><div className="nav-links"><button className={`nav-link${page === "servers" ? " active" : ""}`} onClick={() => { setPage("servers"); setSelectedServer(null); }}>📊 Servers</button></div></div><div className="nav-right"><ConnIndicator status={connStatus} /><span className="header-user">{username}</span><Btn variant="ghost" onClick={doLogout}>Logout</Btn></div></nav>
    <main className="app-main">{error && <div className="error-bar">{error}</div>}{page === "servers" ? <ServerListView servers={servers} error={error} loading={loading} resources={resources} onRefresh={fetchServers} onSelect={s => { setSelectedServer(s); setPage("detail"); }} /> : selectedServer ? <ServerDetailView server={selectedServer} auth={auth} loading={loading} resources={resources} onAction={action} onBack={() => { setPage("servers"); setSelectedServer(null); }} /> : null}</main>
  </div>;
}