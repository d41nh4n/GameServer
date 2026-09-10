import type { ButtonHTMLAttributes, ReactNode } from "react";

export const STATUS_LABEL: Record<number, string> = { 0: "Stopped", 1: "Running", 2: "Starting", 3: "Stopping" };
export const STATUS_COLOR: Record<number, string> = { 0: "var(--red)", 1: "var(--green)", 2: "var(--orange)", 3: "var(--orange)" };
export const GAME_ICON: Record<string, string> = { Valheim: "⚔", Minecraft: "⛏", ProjectZomboid: "🧟", default: "🎮" };

export function Btn({ children, variant, busy, ...rest }: { children: ReactNode; variant?: "primary" | "danger" | "ghost"; busy?: boolean } & ButtonHTMLAttributes<HTMLButtonElement>) {
  return <button className={`btn btn-${variant ?? "primary"}`} disabled={busy || rest.disabled} {...rest}>{busy ? <span className="spinner" /> : children}</button>;
}

export function StatusDot({ status, sm }: { status: number; sm?: boolean }) {
  const color = STATUS_COLOR[status] ?? "var(--text-muted)";
  return <span className={`status-dot${sm ? " sm" : ""}`} style={{ background: color, boxShadow: `0 0 6px ${color}` }} />;
}

export function ConnIndicator({ status }: { status: "connecting" | "connected" | "disconnected" }) {
  const colors = { connecting: "var(--orange)", connected: "var(--green)", disconnected: "var(--red)" };
  const labels = { connecting: "Connecting...", connected: "Live", disconnected: "Disconnected" };
  return <div className="conn-indicator"><span className="status-dot sm" style={{ background: colors[status] }} /><span>{labels[status]}</span></div>;
}