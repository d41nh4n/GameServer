import { useState } from "react";
import { ApiError } from "../../auth";
import { Btn } from "../common";

export default function LoginPage({ onLogin }: { onLogin: (u: string, p: string) => Promise<void> }) {
  const [user, setUser] = useState(""); const [pass, setPass] = useState("");
  const [error, setError] = useState(""); const [busy, setBusy] = useState(false); const [show, setShow] = useState(false);
  const submit = async () => {
    if (!user || !pass) { setError("Enter username and password."); return; }
    setBusy(true); setError("");
    try { await onLogin(user, pass); } catch (e) { setError(e instanceof ApiError ? e.message : "Cannot connect."); }
    finally { setBusy(false); }
  };
  return <div className="login-wrapper"><div className="login-card"><div className="login-brand"><span className="login-icon">🎮</span><h1>Game Panel</h1><p className="brand-sub">Server Control</p></div><div className="login-fields"><div className="field"><label>Username</label><input value={user} onChange={e => setUser(e.target.value)} autoComplete="username" placeholder="admin" onKeyDown={e => e.key === "Enter" && !busy && pass && submit()} /></div><div className="field"><label>Password</label><div className="pass-wrap"><input type={show ? "text" : "password"} value={pass} onChange={e => setPass(e.target.value)} autoComplete="current-password" placeholder="••••••••" onKeyDown={e => e.key === "Enter" && !busy && submit()} /><button className="pass-toggle" type="button" onClick={() => setShow(v => !v)} tabIndex={-1}>{show ? "🙈" : "👁"}</button></div></div>{error && <p className="field-error">{error}</p>}<Btn busy={busy} onClick={submit} style={{ width: "100%", marginTop: 8 }}>Sign In</Btn></div></div></div>;
}