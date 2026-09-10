# Game Server Panel

Control panel for adopted **Valheim** and **Project Zomboid** services, built with ASP.NET Core 10, EF Core/SQLite and React/Vite.

## Current status

- Valheim: `valheim-main.service`, systemd adoption, readiness checks, status, members, logs, backups and safe rollback.
- Project Zomboid: `pzserver-game.service`, systemd adoption, save-before-stop, RCON, players/kick, logs, config, SandboxVars, mods, operations metrics, backups and safe rollback.
- Flask PZ manager remains active on `127.0.0.1:8081` as fallback.
- Authenticated API with JWT, admin-only mutations, SignalR status updates and optional Tailscale filtering.
- Observability: AuditLogs, SystemEvents, five-minute LogAggregates and global metrics dashboard.
- Responsive dark UI with server overview charts and per-server feature tabs.

Detailed source/feature documentation: [`docs/SOURCE_KNOWLEDGE_BASE.md`](docs/SOURCE_KNOWLEDGE_BASE.md).

## Ports

- API: `http://localhost:5000` or `http://100.82.102.38:5000`
- React/Vite: `http://localhost:5173` or `http://100.82.102.38:5173`
- Legacy Flask PZ manager: `127.0.0.1:8081` behind existing nginx

## Build and test

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

cd backend
dotnet build
dotnet test

cd ../frontend/game-panel-web
npm run build
```

## Development listeners

```bash
cd backend/Api
dotnet run --urls=http://localhost:5000

cd frontend/game-panel-web
npm run dev -- --host 0.0.0.0
```

For remote access, bind the API to the host Tailscale address and run the frontend on `0.0.0.0:5173`. The frontend API base is configured in `src/auth.ts`.

## API areas

- `/api/auth/login`
- `/api/servers/{id}/start|stop`
- `/api/pz/rcon/*`
- `/api/pz/config*`
- `/api/pz/sandbox/*`
- `/api/pz/mods`
- `/api/pz/logs/*`
- `/api/pz/ops/health`
- `/api/pz/backups*`
- `/api/valheim/monitor`, `/api/valheim/logs`, `/api/valheim/members`, `/api/valheim/backups*`
- `/api/audit`, `/api/events`, `/api/aggregates`, `/api/metrics/global`

## Safety rules

- Do not stop Flask, mutate a live world, change firewall rules or perform final cutover without explicit approval.
- PZ live backup sends RCON `save` before copying the world and does not restart the service.
- Valheim backup requires an inactive service because no consistent live snapshot/RCON path is enabled.
- Rollback requires an inactive service and creates a protected backup of the current HEAD before replacement.
- Do not commit machine-specific `appsettings.json`, JWT secrets, RCON passwords or SQLite runtime files.
