# Progress

## Mod archive path normalization hotfix — deployed

- Thunderstore ZIP entries now normalize both `/` and `\\` before route detection and extraction; Windows-style paths such as `plugins\\AutoRepair.dll` become canonical filesystem paths.
- Absolute paths, drive-prefixed paths, `.`/`..`, empty segments, and destinations that collide after normalization are rejected before any file is extracted.
- Regression tests cover Windows separators, backslash traversal, and post-normalization collisions; main passed 123/123 backend tests and the production-baseline hotfix passed 105/105.
- Production API runs hotfix revision `02c38548c360241bd3c49c24e30f82abfee4c1c0`; database counts remained 1 user and 3 servers. Valheim stayed inactive and PZ PID remained unchanged.
- Pre-deployment API/SQLite backup: `/home/nh4n/backups/game-server-panel/api-hotfix-mod-path-20260914T174124Z`.

## Client updater — dedicated sync listener

- Added `client/GamePanel.ClientUpdater`: self-contained Windows x64 console updater for authenticated, pinned client modpacks.
- It validates API origin, archive and per-file SHA-256 values, ZIP paths/symlinks, target allowlists, and performs backup/rollback transactions under `.gamepanel-updater/`.
- The dedicated Tailscale listener on port `5001` proxies login to the existing API and serves only authenticated, pinned manifest/package paths; the main API on port `5000` is unchanged.
- The root-owned revision `valheim-full-sync-20260914` is the current approved client manifest: 15 pinned packages, 239 files and 13,007,888 compressed download bytes.
- The updater now backs up and removes files managed by the previous manifest when they are absent from the current revision; unmanaged files remain untouched.
- Built artifact: `client/artifacts/win-x64/GamePanel.ClientUpdater.exe` (SHA-256 `22f690662530640251dd87e1f4800fcbb983470d8594e7916c0616535b21e48e`).
- Lab verification: `Advize/PlantEverything` `1.21.2` with BepInExPack `5.4.2350` loaded successfully on build `25253791`; a real client connected and confirmed the mod works. The isolated service is stopped, its temporary Tailscale UDP `2466:2467` rule was removed, and production remained untouched.
- Deployed the additive Nginx listener on `100.82.102.38:5001` with an exact `tailscale0` firewall rule. EXE/checksum return `200`; manifest/packages return `401` without a token; unrelated API paths return `404`.
- The current production Valheim invocation already loads BepInExPack `5.4.2350` and PlantEverything `1.21.2`. Client-sync deployment did not restart the API or either game service; their PIDs remained unchanged.
- A real Windows client applied `valheim-full-sync-20260914` and connected successfully. The server confirmed network protocol `40` plus matching PlantEverything `1.21.2`, AzuCraftyBoxes `1.8.18`, Seasonality `3.8.3`, and Quick Stack `1.4.15`; the candidate was then atomically promoted to `current` with the prior revision retained for rollback.

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
- Valheim tabs: Controls, Status & Checks, World Versions, Logs; online players are separate from Admin/Permitted/Banned access lists.
- PZ tabs: Controls, World Versions, Logs, Config, RCON, Mods, Sandbox.
- Shared JSON client sets `Content-Type: application/json`, preventing API 415 errors for body requests.

### Verified

- Backend build: 0 errors, 0 warnings.
- Backend tests: 77/77 passed.
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
