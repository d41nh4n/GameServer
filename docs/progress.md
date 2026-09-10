# Progress

## Current implementation status — 2026-09-10

### Backend

- ASP.NET Core 10 API with JWT auth, admin/authenticated policies, SignalR and SQLite.
- Valheim adopted through `valheim-main.service`; systemd/readiness/UDP/cgroup checks, logs, members and versioned backups.
- Project Zomboid adopted through `pzserver-game.service`; Flask fallback remains active.
- PZ features: save-before-stop, RCON, players/kick/broadcast/raw commands, journal/filesystem logs, INI, SandboxVars, mods and host/process metrics.
- World versions: PZ live backup performs RCON `save` before copying; Valheim backup requires stopped service; rollback requires stopped service and protects current HEAD first.
- Persistence: `AuditLogs`, `SystemEvents`, `LogAggregates` migrations applied after SQLite backup. Collector runs on five-minute UTC windows in current production config.
- Metrics: `/api/metrics/global` and `/api/aggregates`; raw logs are never persisted in SQLite.

### Frontend

- `App.tsx` contains orchestration only; feature components are under `src/components/`.
- Overview cards show server state and 24-hour totals.
- SVG/CSS charts show log health and player activity from aggregate windows.
- Valheim tabs: Controls, Status & Checks, World Versions, Logs.
- PZ tabs: Controls, World Versions, Logs, Config, RCON, Mods, Sandbox.
- Shared JSON client sets `Content-Type: application/json`, preventing API 415 errors for body requests.

### Verified

- Backend build: 0 errors, 0 warnings.
- Backend tests: 67/67 passed.
- Frontend production build: passed.
- Live RCON: `players`, `servermsg`, `save` returned HTTP 200.
- Live backup routes loaded after backend reload; no live backup/rollback was executed during implementation.

### Documentation

- Architecture/source map: `docs/SOURCE_KNOWLEDGE_BASE.md`.
- Milestone history and acceptance status: `MILESTONE_CONTEXT.md`.
- Product/development entry point: `README.md`.
- Frontend component guide: `frontend/game-panel-web/README.md`.

### Remaining work

- Add dedicated automated tests for RconClient, PZ config/Sandbox/Mods, backup/rollback and metrics chart data mapping.
- Review and stage the complete worktree before commit/push.
- Decide production process supervision for API/frontend; current listeners are manually started.
- Do not disable or migrate Flask until explicit cutover approval.
