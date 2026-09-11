# CR-02 — Valheim live control foundation

## Scope

This phase adds the panel-side contract and a safe unavailable provider for future Valheim live control. It does **not** install a Valheim plugin, open a port, modify the live data directory, or change the existing systemd runtime.

## Control boundaries

- OS lifecycle: `IGameServerRuntime` → `ValheimProvider` → allowlisted systemd driver.
- Files/world/settings: existing Valheim configuration and world services.
- Live in-game control: `IGameControlProtocol` → future localhost-only plugin protocol.
- There is no shell-command fallback and no second Valheim process.

## Contract

Application contracts are in `backend/Application/Interfaces/IGameControlProtocol.cs`:

- `ValheimCapabilities`
- `ValheimOnlinePlayer`
- `ValheimActionResult`
- typed `ValheimControlRequest`
- `ValheimCapability` flags: OnlinePlayers, KickPlayer, BanPlayer, BroadcastMessage, SaveWorld, ChangeTime, ChangeWeather, TeleportPlayer, SpawnItem, InventoryManagement, GodMode.

The current `ValheimControlProtocol` is intentionally unavailable by default. It returns `503 PLUGIN_UNAVAILABLE`; it never executes shell text or RCON commands.

## API

Admin-only routes:

- `GET /api/servers/{id}/valheim/capabilities`
- `GET /api/servers/{id}/valheim/players`
- `POST /api/servers/{id}/valheim/actions/{kick|ban|broadcast|save|time|weather|teleport|spawn|inventory|god-mode}`

Action behavior:

- `404`: unknown/non-Valheim instance.
- `409`: server is not `Running`/`Started`.
- `400`: invalid player ID or missing/invalid `Idempotency-Key`.
- `501`: capability is not supported.
- `503`: plugin is unavailable or disconnected.
- `200`: structured action result.

Mutating requests require an `Idempotency-Key` header and are serialized per instance in memory. Audit metadata contains capability and target only; secrets/tokens are rejected by the existing audit guard.

## Future localhost plugin protocol

The future plugin must bind to `127.0.0.1` or use local IPC, authenticate panel requests, enforce a request timeout, and return structured errors. It must never bind to `0.0.0.0`, be reverse-proxied to WAN, or accept arbitrary commands.

The intended endpoints are `/health`, `/capabilities`, `/players`, and typed `/actions/*` routes matching the panel API. The real plugin is a separate deliverable and is not installed by this phase.

## UI

The Valheim `Admin Control` tab shows plugin health, capabilities, players, and P0 action controls (broadcast/save). Unsupported or disconnected capabilities remain disabled with a reason. The existing Controls tab remains responsible for systemd Start/Stop/Restart.

## Rollback

1. Stop the panel API process only.
2. Remove the CR-02 application registrations, API routes, UI tab, protocol service, and tests from source.
3. Rebuild backend/frontend.
4. Do not stop/restart Valheim and do not remove or edit Valheim data/config files.
5. No database rollback is required for CR-02 foundation; it uses existing audit persistence and in-memory locks.

## Verification

- Backend build: passed.
- Backend tests: `86/86` passed.
- Frontend UI tests: `9/9` passed.
- Frontend build: passed.
- No plugin installed and no external runtime mutation performed.
