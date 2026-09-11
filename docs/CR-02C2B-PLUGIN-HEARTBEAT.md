# CR-02C.2B — Minimal Valheim Plugin Hello and Heartbeat

## Result

```text
PLUGIN_HEARTBEAT_COMPATIBLE
```

## Verified target

- BepInEx: 5.4.23.5
- Valheim build: 25185644
- Valheim runtime: l-1.0.7
- Unity detected by BepInEx: 6000.0.75f1
- Plugin target framework: `net40`
- Reason: BepInEx core metadata is built from net35-era sources, but the local Unity/BepInEx references carry .NET Framework 4.0-era indirect references. `net35` compilation produced unresolved mscorlib/System conflicts; `net40` with local references compiled successfully.
- Local references were passed through MSBuild properties and were not copied into the repository.

## Plugin

Project:

```text
plugin/ValheimControlPlugin/
```

The plugin contains:

- `ValheimControlPlugin`
- `PluginConnectionWorker`
- `PluginMessageSerializer`
- `PluginProtocolOptions`
- `PluginLifecycleState`

Behavior:

- `Awake` starts one background worker and returns without blocking.
- `Update` performs no network I/O and no game API calls.
- `OnDestroy` cancels and joins the worker with a bounded timeout.
- No Harmony, Jötunn, player API, world API, shell, RCON, systemd, or mutation logic.
- Plugin DLL hash:

```text
d9911dd9aa0e48d75cd8699521c5cdfe2de0ff970481a13c5e9bef9032961586
```

## IPC

- TCP loopback only: `127.0.0.1:27667`.
- Four-byte network-order length prefix.
- Maximum frame: 64 KiB.
- Separate plugin token from panel HTTP token.
- Fixed-time token comparison in bridge.
- Typed allowlisted message names only.
- Plugin sends `PluginAuthenticate`, `PluginHello`, and `PluginHeartbeat`.
- Bridge sends `BridgeAcknowledgement`.
- Unknown/malformed/oversized frames are rejected.
- No token is logged or persisted in evidence.

## Runtime result

BepInEx log confirmed:

```text
BepInEx 5.4.23.5
Preloader finished
Chainloader startup complete
1 plugin to load
ValheimControlPlugin lifecycle started
```

Bridge health confirmed:

```text
pluginConnected=true
pluginAuthenticated=true
pluginVersion=0.1.0
protocolVersion=1
heartbeatAge < 5 seconds
```

At least three consecutive heartbeat samples were accepted. Capabilities remained:

```json
[]
```

No online player state or gameplay API was accessed.

## Reconnect test

Only the bridge was restarted. The plugin reconnected and authenticated again; fresh heartbeats resumed within the bounded reconnect window.

## Valheim health

The lab reached:

```text
Game server connected
```

Lab ports were `2466/2467`. Production ports were never shared with the lab.

## Restore

- Lab stopped gracefully.
- Lab process and ports removed.
- Temporary bridge/plugin tokens removed.
- Production restored to active/running.
- Current production invocation reached `Game server connected`.
- Production UDP 2456/2457 listening.
- Production before/after integrity manifest: `UNCHANGED`.
- Project Zomboid unaffected.

## Not implemented

- Player state.
- OnlinePlayers capability.
- Bridge player snapshots.
- Gameplay mutations.
- Jötunn.
- Production installation/deployment.
- Commit/push.
