# Progress

## Current implementation status — 2026-09-10

### Backend

- ASP.NET Core 10 API with JWT auth, admin/authenticated policies, SignalR and SQLite.
- Valheim adopted through `valheim-main.service`; systemd/readiness/UDP/cgroup checks, logs, members and versioned backups.
- Project Zomboid adopted through `pzserver-game.service`; Flask fallback remains active.
- PZ features: save-before-stop, RCON, players/kick/broadcast/raw commands, journal/filesystem logs, INI, SandboxVars, mods and host/process metrics.
- World versions: PZ live backup performs RCON `save` before copying; Valheim backup requires stopped service; rollback requires stopped service and protects current HEAD first.
- Persistence: `AuditLogs` and `SystemEvents` remain active. The former `LogAggregates` table was removed by migration `20260910130230_DropLogAggregates` after SQLite backup.
- Lifecycle operations use an in-memory queue: Start/Stop/Restart return `202 Accepted`, duplicate commands per server return `409`, and UI polls job status every five seconds while SignalR pushes state changes.
- Journal views support severity/text filters and opt-in five-second refresh with an overlapping-request guard.
- Resource usage: `/api/resources/overview` reads realtime host/process usage; no tick data is persisted.

### Frontend

- `App.tsx` contains orchestration only; feature components are under `src/components/`.
- Overview shows host CPU/RAM/disk and per-server process usage; detail view repeats selected server usage.
- Valheim tabs: Controls, Status & Checks, World Versions, Logs.
- PZ tabs: Controls, World Versions, Logs, Config, RCON, Mods, Sandbox.
- Shared JSON client sets `Content-Type: application/json`, preventing API 415 errors for body requests.

### Verified

- Backend build: 0 errors, 0 warnings.
- Backend tests: 74/74 passed.
- Frontend production build: passed.
- Live RCON: `players`, `servermsg`, `save` returned HTTP 200.
- Live backup routes loaded after backend reload; no live backup/rollback was executed during implementation.

### Documentation

- Architecture/source map: `docs/SOURCE_KNOWLEDGE_BASE.md`.
- Milestone history and acceptance status: `MILESTONE_CONTEXT.md`.
- Product/development entry point: `README.md`.
- Frontend component guide: `frontend/game-panel-web/README.md`.

### Remaining work

- Add dedicated automated tests for RconClient, PZ config/Sandbox/Mods, backup/rollback and journal filtering.
- Review and stage the complete worktree before commit/push.
- Decide production process supervision for API/frontend; current listeners are manually started.
- Do not disable or migrate Flask until explicit cutover approval.
