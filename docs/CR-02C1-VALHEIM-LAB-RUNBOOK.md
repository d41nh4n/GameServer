# CR-02C.1 — Isolated compatibility lab runbook

This document is a plan only. The lab directory is intentionally not created in this phase.

## Planned root

```text
/srv/gamepanel/labs/valheim-plugin-compat/
```

Planned isolated directories:

```text
server/
data/
logs/
runtime/
plugin/
bridge/
```

## Preconditions

- `valheim-main.service` is stopped and verified inactive.
- Production data is not copied; create a new disposable world.
- Use a different UDP base port, e.g. 2556/2557, after checking allocation.
- Use `-public 0`.
- Generate a lab-only password interactively; never reuse or record production credentials.
- No UFW/Tailscale/WAN allow rule for the lab port.
- Lab ownership is isolated and does not change `valheim:valheim` production ownership.

## Production state capture and restore

Capture the exact production state before the lab (`active/running` or `inactive/dead`, plus readiness and ports). The production service must be returned to that exact pre-test state:

- Pre-test `active/running` → restore `active/running` and verify the current invocation readiness marker.
- Pre-test `inactive/dead` → leave it `inactive/dead`.
- Any request to change that state requires separate explicit authorization.

The lab may run only when production is inactive. This gate does not authorize starting production after a pre-test inactive state.

## Planned deployment sequence

1. Stop before any copy/update operation and verify production unit inactive.
2. Install/copy the dedicated server into the lab `server/` directory only.
3. Install the candidate BepInEx Unity Mono Linux x64 package into the lab copy only.
4. Place the minimal plugin DLL in the lab plugin directory only.
5. Start the sidecar bridge manually on loopback with a lab-only token supplied through an interactive environment, never source-controlled.
6. Start the lab server with a separate launcher/process and disposable world.
7. Execute the smoke-test gates from the ADR.
8. Capture only bounded, non-secret logs and structured test results.
9. Stop and remove the entire lab tree after the test, or retain it only with explicit approval.

The production launcher, unit, env file, data directory, and world files must not be used as lab inputs or outputs.

## IPC design

Preferred transport: Unix domain socket owned by the bridge account, with restrictive permissions. The socket carries newline-delimited typed JSON messages with:

```text
PluginHello
PluginHeartbeat
ServerStateSnapshot
PlayerListSnapshot
CapabilitySnapshot
PluginDisconnect
```

Rules:

- Maximum frame size is bounded before parsing.
- Unknown message types are rejected.
- Malformed JSON/schema is rejected without calling game APIs.
- Read/write operations have bounded timeouts.
- IPC thread only validates and queues messages.
- Unity/Valheim APIs run through a main-thread dispatcher.
- Snapshot publication is immutable and replaces the previous snapshot atomically.
- Snapshot TTL expiry makes the bridge report disconnected/stale; it never presents expired players as live.

## Future smoke-test acceptance

- Loader starts without changing the production process.
- Plugin loads without game API access.
- Bridge receives authenticated `PluginHello`.
- Heartbeat freshness remains below the configured TTL.
- Bridge restart is followed by plugin reconnect.
- Plugin restart is followed by bridge recovery.
- Empty player snapshot is accepted.
- Real player API is identified only after static and runtime evidence.
- `OnlinePlayers=true` only for a fresh valid snapshot.
- All mutation capabilities remain false.

## Rollback

1. Stop the lab process only.
2. Verify `valheim-main.service` remains stopped/unchanged.
3. Remove the lab plugin, BepInEx, bridge, server, runtime, logs, and disposable data directories.
4. Remove lab-only credentials from the shell/environment.
5. Verify production paths, unit file, launcher, env file, and world/data hashes are unchanged.
6. Do not restore any lab world into production.

## Go/No-Go

Go requires all static evidence, loader logs, reconnect tests, fresh snapshot tests, and no-secret scan to pass in the isolated lab. Any loader error, Unity crash, main-thread violation, stale-player exposure, WAN binding, or mutation-capability activation is an immediate No-Go.
