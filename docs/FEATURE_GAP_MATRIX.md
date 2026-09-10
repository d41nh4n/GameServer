# Feature Gap Matrix

Status: `DONE`, `PARTIAL`, `MISSING`, `DEFERRED`.

## 1. Dashboard — PARTIAL

- DONE: host CPU, RAM and root disk usage.
- DONE: per-server online/PID/CPU/RSS/threads/FDs.
- DONE: server list, Running/Stopped/Starting/Stopping, Start/Stop/Restart.
- MISSING: host temperature, service uptime and current player count on overview cards.

## 2. systemd control — PARTIAL

- DONE: adopted real Valheim and PZ units; status reconciled from systemd.
- DONE: Start/Stop/Restart via asynchronous in-memory job queue.
- DONE: HTTP returns `202 Accepted` immediately; UI polls job every five seconds.
- DONE: duplicate operation for the same server returns `409 Conflict`.
- DONE: graceful shutdown paths preserve world saves.
- DONE: failed jobs expose error text through `/api/operations/{id}`.
- MISSING: persisted queue across API restart.
- MISSING: UI control for systemd enable/disable at boot.
- PARTIAL: crash restart is currently a systemd unit policy, not a panel-configurable feature.

## 3. Console and logs — PARTIAL

- DONE: bounded journal reads for PZ and Valheim.
- DONE: severity and text filter.
- DONE: opt-in five-second auto-refresh with overlapping-request guard.
- DONE: PZ filesystem log list/read and RCON console.
- MISSING: log download endpoint, delete/rotation controls and explicit current-invocation filter.

## 4. Game configuration — PARTIAL

- DONE: PZ INI editor, raw view, validation and pre-write backup.
- DONE: PZ SandboxVars scalar editor and backup.
- MISSING: common Valheim form for name/world/port/public/max players.
- MISSING: generic `.env`/`.properties` editor and restart-after-save workflow.

## 5. Install and update — MISSING

- MISSING: SteamCMD install/update/validate/version jobs and progress UI.
- Required safety: stopped-only update, exclusive SteamCMD directory lock and pre-update backup.

## 6. Backup and restore — PARTIAL

- DONE: PZ live backup through RCON save then copy.
- DONE: Valheim stopped-only consistent backup.
- DONE: stopped-only rollback with protected HEAD and confirmation.
- MISSING: scheduled daily backup, retention (7–14 versions), delete-version endpoint and off-host copy.

## 7. Mods — PARTIAL

- DONE: PZ WorkshopItems/Mods list, add/remove and config backup.
- MISSING: enable/disable, update job, load order and log-linked broken-mod view.

## 8. Players — PARTIAL

- DONE: PZ online list and kick through RCON.
- DONE: Valheim permitted/admin/banned lists.
- MISSING: unified player count on dashboard and PZ ban/unban UI.

## 9. Security — MOSTLY DONE

- DONE: local login, password hashing, JWT expiry, rate limiting and admin-protected mutations.
- DONE: optional Tailscale middleware and non-root backend.
- DONE: dedicated Linux users and narrow systemd controls.
- PARTIAL: audit covers lifecycle actions; backup/restore/update audit coverage still incomplete.
- MISSING: explicit read-only Viewer role UX.

## 10. Discord notifications — MISSING

- MISSING: crash, lifecycle, backup/update failure, disk threshold and job-completion notifications.

## Recommended implementation order

1. Persisted operation queue and operation UI status.
2. Boot enable/disable and crash-policy visibility.
3. Automated backup schedule/retention.
4. SteamCMD update jobs.
5. Discord notifications.
6. Temperature/player-count/dashboard additions.
