import { useEffect, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr";

type Server = { id: string; name: string; gameType: string; status: number };
type ConnStatus = "connecting" | "connected" | "disconnected";

const API_BASE = "http://localhost:5000";

const CONN_LABEL: Record<ConnStatus, string> = {
  connecting: "Đang kết nối...",
  connected: "Đã kết nối",
  disconnected: "Mất kết nối",
};

export default function App() {
  const [servers, setServers] = useState<Server[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [connStatus, setConnStatus] = useState<ConnStatus>("connecting");
  const connRef = useRef<HubConnection | null>(null);

  const applyServerStatus = (id: string, status: number) => {
    setServers((prev) =>
      prev.map((s) => (s.id === id ? { ...s, status } : s))
    );
  };

  useEffect(() => {
    let disposed = false;

    const conn = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/server`)
      .configureLogging(LogLevel.Information)
      .build();
    connRef.current = conn;

    conn.on("ServerStateChanged", (id: string, status: number) => {
      console.log("ServerStateChanged", id, status);
      if (!disposed) applyServerStatus(id, status);
    });

    conn.onclose(() => {
      if (!disposed) setConnStatus("disconnected");
    });

    const connect = async () => {
      setConnStatus("connecting");
      try {
        await conn.start();
        if (!disposed) setConnStatus("connected");
      } catch (e) {
        console.error("SignalR connect failed:", e);
        if (!disposed) setConnStatus("disconnected");
      }
    };

    connect();
    return () => {
      disposed = true;
      conn.stop().catch(() => {});
    };
  }, []);

  const fetchServers = () =>
    fetch(`${API_BASE}/api/servers`)
      .then((r) => r.json())
      .then(setServers)
      .catch(() => setError("API Error"));

  useEffect(() => {
    fetchServers();
  }, []);

  const action = (id: string, type: "start" | "stop") => {
    setLoading(true);
    setError("");
    fetch(`${API_BASE}/api/servers/${id}/${type}`, { method: "POST" })
      .then((r) => {
        if (!r.ok) throw new Error("Invalid state transition");
        fetchServers();
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  };

  const connColor =
    connStatus === "connected"
      ? "green"
      : connStatus === "connecting"
      ? "orange"
      : "red";

  return (
    <div style={{ padding: 20, fontFamily: "sans-serif" }}>
      <h2>
        🎮 Game Server Control Panel{" "}
        <small style={{ color: "orange" }}>(CHẾ ĐỘ MÔ PHỎNG - FAKE RUNTIME)</small>
      </h2>
      <p style={{ fontSize: 14, color: connColor }}>
        Tín hiệu SignalR: <strong>{CONN_LABEL[connStatus]}</strong>
      </p>
      {error && <p style={{ color: "red" }}>{error}</p>}
      {servers.map((s) => (
        <div
          key={s.id}
          style={{ border: "1px solid #ccc", padding: 15, borderRadius: 8, maxWidth: 400 }}
        >
          <h3>{s.name} ({s.gameType})</h3>
          <p>
            Trạng thái: <strong>{s.status === 1 ? "RUNNING" : "STOPPED"}</strong>
          </p>
          <button disabled={loading || s.status === 1} onClick={() => action(s.id, "start")}>
            Start
          </button>{" "}
          <button disabled={loading || s.status === 0} onClick={() => action(s.id, "stop")}>
            Stop
          </button>
        </div>
      ))}
    </div>
  );
}