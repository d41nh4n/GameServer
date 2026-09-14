# Game Server Panel — Source Knowledge Base

## 1. Product scope

The panel manages two adopted production game services:

- **Valheim**: `valheim-main.service`, runtime user `valheim`.
- **Project Zomboid**: `pzserver-game.service`, runtime user `pzserver`.
- **Legacy fallback**: Flask PZ manager remains available on `127.0.0.1:8081`.

The panel is an additive migration layer. It must not stop Flask, mutate a live world, change firewall rules, or perform a final cutover without explicit approval.

## 2. Runtime architecture

```text
React/Vite :5173
    │ JWT Bearer / SignalR
    ▼
ASP.NET Core API :5000
    ├── Auth + rate limiting + optional Tailscale filter
    ├── GameServerManager
    │     ├── ValheimProvider → ValheimRuntimeStrategy → SystemdRuntimeDriver
    │     └── ProjectZomboidAdapter → SystemdRuntimeDriver / pz-gamectl
    ├── PZ feature services
    ├── ValheimMonitoringService
    ├── AuditLogService / SystemEventService
    ├── ResourceMetricsService
    └── SQLite gamepanel.db
        ├── ServerInstances
        ├── Users
        ├── AuditLogs
        └── SystemEvents

Raw logs remain in systemd journal/filesystem; SQLite stores audit and lifecycle facts only. The optional Windows Client Updater authenticates through the dedicated Tailscale listener on port `5001`, reads an approved client-modpack manifest, verifies pinned archive/file hashes, then installs only allowlisted BepInEx/Doorstop paths with rollback backups. Files from the previous managed revision that are absent from the current manifest are backed up and removed; unmanaged files remain untouched. It never follows a moving `latest` package.
```

## 3. Backend source map

| Area | Source | Responsibility |
|---|---|---|
| API composition | `backend/Api/Program.cs` | DI, auth policies, CORS, routes, migrations, collector registration |
| Contracts | `backend/Api/CoreContracts/` | API request/response DTOs and settings |
| Domain | `backend/Domain/Entities/` | Server, user, audit, event and aggregate entities |
| Runtime abstraction | `backend/Application/Interfaces/` | Stable contracts consumed by API/application |
| Systemd control | `backend/Infrastructure/GameServers/SystemdRuntimeDriver.cs` | Allowlisted state/control commands |
| Valheim lifecycle | `ValheimProvider.cs`, `ValheimRuntimeStrategy.cs`, `ValheimRuntimeProbe.cs` | Adopt-existing detection, readiness and safe control |
| PZ lifecycle | `ProjectZomboidAdapter.cs` | PZ systemd/wrapper status and control |
| RCON | `RconClient.cs` | Source RCON framing, two-packet PZ auth handshake, command response |
| PZ config | `PzConfigService.cs` | Preserve-format INI read/write and backups |
| PZ sandbox | `PzSandboxService.cs` | Safe scalar SandboxVars read/write |
| PZ logs | `PzLogService.cs` | Bounded journal/file log reads with path protection |
| PZ mods | `PzModService.cs` | WorkshopItems/Mods read/write with backup |
| PZ operations | `PzOpsService.cs` | Host CPU/memory/disk/process metrics |
| World versions | `PzWorldBackupService.cs`, `ValheimMonitoringService.cs` | Backup, protected HEAD and stopped-only rollback |
| Persistence services | `AuditLogService.cs`, `SystemEventService.cs` | Writes and bounded queries |
| Operations | `ServerOperationQueue.cs` | Async Start/Stop/Restart queue, duplicate suppression and background worker |
| Resources | `ResourceMetricsService.cs` | Host and per-process CPU/RAM/disk/process usage |

## 4. Frontend source map

```text
frontend/game-panel-web/src/
├── App.tsx                         # auth, SignalR, page/server selection, actions
├── auth.ts                         # JWT session + shared JSON/Bearer API client
├── index.css                       # dark responsive design system
└── components/
    ├── common.tsx                  # buttons, status dots, labels and icons
    ├── auth/LoginPage.tsx          # login form and error state
    └── servers/
        ├── ServerCard.tsx          # server summary card
        ├── ServerListView.tsx      # host/per-server resource overview and server grid
        ├── ServerDetailView.tsx    # tab router and shared controls
        ├── ValheimStatus.tsx       # health checks, members and Valheim logs
        ├── WorldBackups.tsx        # PZ/Valheim node timeline and rollback UI
        └── ...                     # PZ Logs, Config, RCON, Mods, Sandbox tabs
```

`App.tsx` owns orchestration only. Feature state belongs in the relevant component. `auth.ts` automatically adds `Content-Type: application/json` for body requests; omitting this header causes ASP.NET HTTP 415.

## 5. UI feature matrix

| Server | Controls | Status/checks | Logs | Config | RCON | Mods | Sandbox | World versions |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Valheim | ✓ | ✓ | ✓ | — | — | — | — | ✓ |
| Project Zomboid | ✓ | shared status | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

The overview displays live host resources and per-server process usage. The selected server detail view repeats its CPU/RAM/thread/file-descriptor snapshot.

## 6. Resource usage flow

```text
/proc/stat + /proc/meminfo + /proc/<pid>/status
        ▼
ResourceMetricsService
        ▼
GET /api/resources/overview
        ├── host CPU/RAM/disk
        └── per-server online/PID/CPU/RSS/threads/FDs
```

Resource snapshots are realtime and are not persisted in SQLite.

Never store raw journal lines, passwords, JWTs, RCON passwords or full config text in SQLite.

## 7. World backup safety

- **PZ live backup**: send RCON `save`, wait briefly, then copy `servertest_new` without stopping/restarting the service.
- **Valheim backup**: allowed only while inactive because the current setup has no RCON snapshot mechanism.
- **Rollback**: always requires inactive service, creates a `HEAD-before-rollback` backup first, stages the selected version, then replaces the world directory. It never auto-starts the service.
- Backup roots are outside live game data:
  - PZ: `/home/nh4n/backups/game-server-panel/pz`
  - Valheim: `/home/nh4n/backups/game-server-panel/valheim`

## 8. API groups

- Auth: `POST /api/auth/login`
- Lifecycle: `GET /api/servers`, `POST /api/servers/{id}/start|stop|restart`, `GET /api/operations/{id}`
- PZ: `/api/pz/rcon/*`, `/api/pz/config*`, `/api/pz/sandbox/*`, `/api/pz/mods`, `/api/pz/logs/*`, `/api/pz/ops/health`, `/api/pz/backups*`
- Valheim: `/api/valheim/monitor`, `/api/valheim/logs`, `/api/valheim/members`, `/api/valheim/backups*`
- Client updater: `GET /api/client-updater/manifest`, `GET /api/client-updater/packages/{packageId}/{version}`
- Observability: `/api/audit`, `/api/events`, `/api/resources/overview`

Admin authorization is required for mutations. Authenticated users can read status, logs and resource data.

## 9. Verification

```bash
cd backend
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet build
dotnet test

cd ../frontend/game-panel-web
npm run build
```

Current verified baseline: backend build 0 errors/0 warnings, 67/67 tests passed, frontend production build passed.
