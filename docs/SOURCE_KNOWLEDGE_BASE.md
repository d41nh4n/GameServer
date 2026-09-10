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
    ├── LogAggregateCollector → LogAggregateService
    └── SQLite gamepanel.db
        ├── ServerInstances
        ├── Users
        ├── AuditLogs
        ├── SystemEvents
        └── LogAggregates

Raw logs remain in systemd journal/filesystem; SQLite stores bounded facts only.
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
| Persistence services | `AuditLogService.cs`, `SystemEventService.cs`, `LogAggregateService.cs` | Writes and bounded queries |
| Metrics | `LogAggregateCollector.cs`, `GlobalMetricsService.cs` | Five-minute windows and 24-hour summary |

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
        ├── ServerListView.tsx      # overview cards, charts and server grid
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

The overview dashboard displays live server counts and aggregate charts for log health and player activity. The Metrics Explorer below the charts can query every stored window by server, severity and UTC date range, then shows the persisted counters and last error message.

## 6. Persistence and metrics flow

```text
systemd journal / game files
        │ bounded five-minute read
        ▼
LogAggregateParser
        ▼
LogAggregates(window, counters, last error)
        ▼
GlobalMetricsService
        ├── GET /api/metrics/global
        └── GET /api/aggregates
```

Stored properties:

- `WarningCount`, `ErrorCount`, `FatalCount`
- `PlayerJoinCount`, `PlayerLeaveCount`
- `WindowStartUtc`, `WindowEndUtc`
- bounded `LastErrorMessage`

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
- Lifecycle: `GET /api/servers`, `POST /api/servers/{id}/start|stop`
- PZ: `/api/pz/rcon/*`, `/api/pz/config*`, `/api/pz/sandbox/*`, `/api/pz/mods`, `/api/pz/logs/*`, `/api/pz/ops/health`, `/api/pz/backups*`
- Valheim: `/api/valheim/monitor`, `/api/valheim/logs`, `/api/valheim/members`, `/api/valheim/backups*`
- Observability: `/api/audit`, `/api/events`, `/api/aggregates`, `/api/metrics/global`

Admin authorization is required for mutations. Authenticated users can read status/log/metrics data.

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
