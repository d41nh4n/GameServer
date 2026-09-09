import { useEffect, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr";
import { ApiError, AuthProvider } from "./auth";
import type { Server } from "./auth";

const API_BASE = "http://localhost:5000";
const HUB_URL = `${API_BASE}/hubs/server`;

type ConnStatus = "connecting" | "connected" | "disconnected";

const CONN_LABEL: Record<ConnStatus, string> = {
  connecting: "Đang kết nối...",
  connected: "Đã kết nối",
  disconnected: "Mất kết nối",
};

export default function App() {
  const authRef = useRef<AuthProvider | null>(null);
  if (authRef.current === null) {
    authRef.current = new AuthProvider();
  }
  const auth = authRef.current;
  // Authentication state only; the JWT remains outside React state.
  const [loggedIn, setLoggedIn] = useState(auth.isAuthenticated);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [loginError, setLoginError] = useState("");
  const [loginBusy, setLoginBusy] = useState(false);

  const [servers, setServers] = useState<Server[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [connStatus, setConnStatus] = useState<ConnStatus>("disconnected");
  const [conn, setConn] = useState<HubConnection | null>(null);

  // Subscribe to authentication changes such as logout and HTTP 401.
  useEffect(() => {
    const unsub = auth.subscribe(() => setLoggedIn(auth.isAuthenticated));
    return unsub;
  }, []);

  const doLogin = async () => {
    setLoginBusy(true);
    setLoginError("");
    try {
      await auth.login(username, password);
      setPassword("");
      setLoggedIn(true);
    } catch (e) {
      if (e instanceof ApiError) {
        setLoginError(e.message);
      } else {
        setLoginError("Không thể kết nối tới API.");
      }
    } finally {
      setLoginBusy(false);
    }
  };

  const doLogout = () => {
    if (conn) {
      conn.stop().catch(() => {});
      setConn(null);
    }
    setConnStatus("disconnected");
    setServers([]);
    setError("");
    auth.logout();
  };

  const fetchServers = async () => {
    try {
      setServers(await auth.listServers());
    } catch (e) {
      if (e instanceof ApiError) setError(e.message);
      else setError("API Error");
    }
  };

  // Connect SignalR and load servers only after authentication.
  useEffect(() => {
    if (!loggedIn) return;
    let disposed = false;

    const build = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        // SignalR uses the current token through accessTokenFactory.
        // Never log or use the token as a client-side authorization decision.
        accessTokenFactory: () => auth.currentToken ?? "",
      })
      .configureLogging(LogLevel.Error) // Avoid token-bearing debug logs.
      .build();

    const startConn = async () => {
      setConnStatus("connecting");
      try {
        await build.start();
        if (disposed) return;
        setConn(build);
        setConnStatus("connected");
      } catch (e) {
        if (disposed) return;
        // Do not log tokens or error details. Return to login when authorization
        // is rejected or has expired.
        const err = e as { status?: number };
        if (err?.status === 401 || err?.status === 403) {
          auth.logout();
          return;
        }
        console.error("SignalR connect failed"); // Never include the token.
        setConnStatus("disconnected");
      }
    };

    build.on("ServerStateChanged", (id: string, status: number) => {
      setServers((prev) =>
        prev.map((s) => (s.id === id ? { ...s, status } : s))
      );
    });

    build.onclose(() => {
      if (!disposed) setConnStatus("disconnected");
    });

    startConn();
    fetchServers();

    return () => {
      disposed = true;
      build.stop().catch(() => {});
    };
    // AuthProvider is stable and deliberately omitted from dependencies.
  }, [loggedIn]);

  const action = async (id: string, type: "start" | "stop") => {
    setLoading(true);
    setError("");
    try {
      await auth.trigger(id, type);
      await fetchServers();
    } catch (e) {
      if (e instanceof ApiError) setError(e.message);
      else setError("Invalid state transition");
    } finally {
      setLoading(false);
    }
  };

  if (!loggedIn) {
    return (
      <div style={{ padding: 20, fontFamily: "sans-serif" }}>
        <h2>🎮 Game Server Control Panel</h2>
        <div style={{ maxWidth: 360, marginTop: 16 }}>
          <div>Tên đăng nhập</div>
          <input
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoComplete="username"
          />
          <div style={{ marginTop: 8 }}>Mật khẩu</div>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            onKeyDown={(e) => {
              if (e.key === "Enter") doLogin();
            }}
          />
          {loginError && (
            <p style={{ color: "red", fontSize: 13 }}>{loginError}</p>
          )}
          <div style={{ marginTop: 12 }}>
            <button disabled={loginBusy} onClick={doLogin}>
              {loginBusy ? "Đang đăng nhập..." : "Đăng nhập"}
            </button>
          </div>
        </div>
      </div>
    );
  }

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
        <button
          style={{ marginLeft: 8, fontSize: 12 }}
          onClick={doLogout}
        >
          Đăng xuất
        </button>
      </h2>
      <p style={{ fontSize: 14, color: connColor }}>
        SignalR: <strong>{CONN_LABEL[connStatus]}</strong>
      </p>
      {error && <p style={{ color: "red" }}>{error}</p>}
      <button
        style={{ marginTop: 4, fontSize: 12 }}
        onClick={fetchServers}
        disabled={loading}
      >
        Làm mới
      </button>
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