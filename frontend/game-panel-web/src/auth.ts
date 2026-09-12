// Central authentication state and shared API client for GamePanel.
// The JWT lives only in sessionStorage, is restored after refresh, and is never
// logged or displayed.

const API_BASE = "http://100.82.102.38:5000";
const TOKEN_KEY = "gamepanel_access_token";

export type Server = {
  id: string;
  name: string;
  gameType: string;
  type: number;
  status: number;
  port: number;
  worldName: string;
  instanceKey: string | null;
  provisioningMode: string;
  runtimeType: string;
  ready: boolean;
};

export type ServerOperationJob = { id: string; serverId: string; kind: number; status: number; error?: string | null };

export class ApiError extends Error {
  readonly status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

// Owns the token and is the only access point for protected API requests.
export class AuthProvider {
  private token: string | null = null;
  private listeners: (() => void)[] = [];

  constructor() {
    // Restore the token after a page refresh.
    try {
      this.token = sessionStorage.getItem(TOKEN_KEY) ?? null;
    } catch {
      this.token = null;
    }
  }

  get isAuthenticated(): boolean {
    return this.token !== null;
  }

  get currentToken(): string | null {
    return this.token;
  }

  subscribe(fn: () => void) {
    this.listeners.push(fn);
    return () => {
      this.listeners = this.listeners.filter((x) => x !== fn);
    };
  }

  private notify() {
    for (const fn of this.listeners) fn();
  }

  // Exchange credentials for a JWT without logging either credentials or token.
  async login(username: string, password: string) {
    const res = await fetch(`${API_BASE}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password }),
    });

    if (res.status === 429) {
      throw new ApiError(
        429,
        "Quá nhiều lần đăng nhập. Vui lòng đợi rồi thử lại."
      );
    }
    if (!res.ok) {
      throw new ApiError(res.status, "Tên đăng nhập hoặc mật khẩu không đúng.");
    }

    const body = await res.json();
    const t = body.token as string;
    if (!t) throw new ApiError(500, "Phản hồi không có token.");

    this.token = t;
    try {
      sessionStorage.setItem(TOKEN_KEY, t);
    } catch {
      /* sessionStorage niedostępny — token tylko w pamięci */
    }
    this.notify();
  }

  logout() {
    this.token = null;
    try {
      sessionStorage.removeItem(TOKEN_KEY);
    } catch {
      /* ignore */
    }
    this.notify();
  }

  // Shared client: attaches Bearer and clears authentication on HTTP 401.
  private async request<T>(path: string, init?: RequestInit): Promise<T> {
    const headers = new Headers(init?.headers ?? {});
    if (init?.body && !headers.has("Content-Type")) {
      headers.set("Content-Type", "application/json");
    }
    if (this.token) {
      headers.set("Authorization", `Bearer ${this.token}`);
    }
    const res = await fetch(`${API_BASE}${path}`, {
      ...init,
      headers,
    });

    if (res.status === 401) {
      // Expired or invalid token: clear state without exposing the token.
      this.logout();
      throw new ApiError(401, "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
    }
    if (!res.ok) {
      let msg = `Lỗi (${res.status})`;
      try {
        const txt = await res.text();
        if (txt) msg = txt;
      } catch {
        /* ignore */
      }
      throw new ApiError(res.status, msg);
    }
    const text = await res.text();
    if (!text) return undefined as T;
    return JSON.parse(text) as T;
  }

  listServers(): Promise<Server[]> {
    return this.request<Server[]>("/api/servers");
  }

  trigger(id: string, action: "start" | "stop" | "restart"): Promise<ServerOperationJob> {
    return this.request<ServerOperationJob>(`/api/servers/${id}/${action}`, { method: "POST" });
  }

  operation(id: string): Promise<ServerOperationJob> {
    return this.request<ServerOperationJob>(`/api/operations/${id}`);
  }

  valheimCapabilities(id: string): Promise<any> {
    return this.request<any>(`/api/servers/${id}/valheim/capabilities`);
  }
  valheimPlayers(id: string): Promise<any[]> {
    return this.request<any[]>(`/api/servers/${id}/valheim/players`);
  }
  valheimAction(id: string, action: string, body: Record<string, unknown> = {}, idempotencyKey = crypto.randomUUID()): Promise<any> {
    return this.request<any>(`/api/servers/${id}/valheim/actions/${action}`, { method: "POST", headers: { "Idempotency-Key": idempotencyKey }, body: JSON.stringify(body) });
  }


  async pzLogs(id: string, lines = 200): Promise<{ content: string; lines: number }> {
    return this.request<{ content: string; lines: number }>(`/api/servers/${id}/logs?lines=${lines}`);
  }
  async pzHealth(): Promise<any> {
    return this.request<any>("/api/pz/ops/health");
  }
  async pzConfig(): Promise<any> {
    return this.request<any>("/api/pz/config");
  }
  async pzConfigRaw(): Promise<any> {
    return this.request<any>("/api/pz/config/raw");
  }
  async pzConfigUpdate(config: Record<string, Record<string, string>>): Promise<any> {
    return this.request<any>("/api/pz/config", { method: "PUT", body: JSON.stringify({ config }) });
  }
  async pzSandboxConfig(): Promise<any> {
    return this.request<any>("/api/pz/sandbox/config");
  }
  async pzSandboxSave(values: { section: string; key: string; value: any }[]): Promise<any> {
    return this.request<any>("/api/pz/sandbox/save", { method: "POST", body: JSON.stringify({ values }) });
  }
  async pzLogsList(): Promise<any> {
    return this.request<any>("/api/pz/logs/list");
  }
  async pzLogRead(filename: string): Promise<any> {
    return this.request<any>(`/api/pz/logs/read?filename=${encodeURIComponent(filename)}`);
  }
  async pzRconCommand(command: string): Promise<any> {
    return this.request<any>("/api/pz/rcon/command", { method: "POST", body: JSON.stringify({ command }) });
  }
  async pzSetAccessLevel(username: string, level: "user" | "admin"): Promise<any> { return this.request<any>("/api/pz/rcon/access-level", { method: "POST", body: JSON.stringify({ username, level }) }); }
  async pzRconPlayers(): Promise<any> {
    return this.request<any>("/api/pz/rcon/players");
  }
  async pzMods(): Promise<any> {
    return this.request<any>("/api/pz/mods");
  }
  async pzModsUpdate(workshopIds: string[], modIds: string[]): Promise<any> {
    return this.request<any>("/api/pz/mods", { method: "PUT", body: JSON.stringify({ workshopIds, modIds }) });
  }
  async pzRconSave(): Promise<any> {
    return this.request<any>("/api/pz/rcon/save", { method: "POST" });
  }
  async pzRconBroadcast(message: string): Promise<any> {
    return this.request<any>("/api/pz/rcon/broadcast", { method: "POST", body: JSON.stringify({ message }) });
  }
  async pzRconKick(username: string, reason = "Kicked"): Promise<any> {
    return this.request<any>("/api/pz/rcon/kick", { method: "POST", body: JSON.stringify({ username, reason }) });
  }
  async valheimMonitor(): Promise<any> { return this.request<any>("/api/valheim/monitor"); }
  async valheimSettings(): Promise<any> { return this.request<any>("/api/valheim/settings"); }
  async valheimSettingsUpdate(settings: any): Promise<any> { return this.request<any>("/api/valheim/settings", { method: "PUT", body: JSON.stringify(settings) }); }
  async valheimLogs(lines = 200): Promise<any> { return this.request<any>(`/api/valheim/logs?lines=${lines}`); }
  async valheimMembers(): Promise<any> { return this.request<any>("/api/valheim/members"); }
  async valheimMemberAdd(id: string, role: string): Promise<any> { return this.request<any>("/api/valheim/members", { method: "POST", body: JSON.stringify({ id, role }) }); }
  async valheimMemberRemove(id: string, role: string): Promise<any> { return this.request<any>("/api/valheim/members", { method: "DELETE", body: JSON.stringify({ id, role }) }); }
  async valheimBackups(): Promise<any> { return this.request<any>("/api/valheim/backups"); }
  async valheimCreateBackup(): Promise<any> { return this.request<any>("/api/valheim/backups", { method: "POST" }); }
  async valheimRollback(version: string): Promise<any> { return this.request<any>(`/api/valheim/backups/${encodeURIComponent(version)}/rollback`, { method: "POST" }); }
  async valheimVersion(): Promise<{ success: boolean; installedBuildId?: string; latestBuildId?: string; updateAvailable?: boolean }> {
    return this.request<{ success: boolean; installedBuildId?: string; latestBuildId?: string; updateAvailable?: boolean }>("/api/valheim/version");
  }
  async valheimUpdate(): Promise<{ success: boolean; result?: { success: boolean; exitCode: number; output: string; installedBuildId?: string }; error?: string }> {
    return this.request<{ success: boolean; result?: { success: boolean; exitCode: number; output: string; installedBuildId?: string }; error?: string }>("/api/valheim/update", { method: "POST" });
  }
  async pzBackups(): Promise<any> { return this.request<any>("/api/pz/backups"); }
  async pzCreateBackup(): Promise<any> { return this.request<any>("/api/pz/backups", { method: "POST" }); }
  async pzRollback(version: string): Promise<any> { return this.request<any>(`/api/pz/backups/${encodeURIComponent(version)}/rollback`, { method: "POST" }); }
  async resourcesOverview(): Promise<any> { return this.request<any>("/api/resources/overview"); }
}