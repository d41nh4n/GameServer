# CR-02C.3A — Read-only Valheim Player API Discovery and Empty Snapshot

## Static API discovery

Inspected exact lab managed assembly for Steam Build 25253791:

- Assembly: `assembly_valheim.dll`
- Runtime: Unity Mono, Unity `6000.0.75f1`
- BepInEx: `5.4.23.5` (latest stable BepInEx 5.x; official archive hash verified and lab installation refreshed)

Verified members used by the collector:

- `ZNet.instance : public static ZNet` — `assembly_valheim.dll`
- `ZNet.IsServer() : public bool` — `assembly_valheim.dll`
- `ZNet.IsDedicated() : public bool` — `assembly_valheim.dll`
- `ZNet.GetConnectedPeers() : public List<ZNetPeer>` — `assembly_valheim.dll`
- `ZNetPeer.IsReady() : public bool` — `assembly_valheim.dll`
- `ZNetPeer.m_uid : public long` — `assembly_valheim.dll`
- `ZNetPeer.m_playerName : public string` — `assembly_valheim.dll`

Classification:

- `ZNet.instance`, `IsServer`, `IsDedicated`, `GetConnectedPeers`: `VERIFIED_PUBLIC_API`
- `ZNetPeer.IsReady`: `VERIFIED_PUBLIC_API`
- `ZNetPeer.m_uid`, `m_playerName`: `VERIFIED_PUBLIC_API` fields
- No unrestricted reflection is required.
- `m_playerID` and `m_playfabId` are not exposed as `PlatformId`; exact identity semantics were not established.

## Identity policy

- `ConnectionId` uses the opaque peer UID string.
- `DisplayName` is bounded to 128 characters and CR/LF sanitized.
- `PlatformId` remains nullable.
- IP address, endpoint, authentication ticket, RPC internals, and ownership tokens are not exposed.

## Collector

`PlayerSnapshotCollector` runs from Unity `Update` every 3 seconds. It reads Valheim objects only on the Unity main thread, creates a completed immutable snapshot, and atomically publishes it. The IPC worker reads only the published immutable snapshot and performs no Unity/Valheim access.

Snapshot fields:

- sequence
- capturedAtUtc
- gameReady
- bounded player list
- collector status

## IPC contract

`PlayerListSnapshot` is authenticated and validated by the bridge. The bridge rejects unsupported protocol, instance mismatch, duplicate/decreasing sequence, future timestamps, oversized player lists, and oversized strings. It retains only the latest snapshot in memory, marks it stale after 15 seconds, and clears it on plugin disconnect. No player state is persisted to SQLite.

`OnlinePlayers` is enabled only when plugin authentication, heartbeat freshness, build/runtime identity, collector compatibility, game readiness, and snapshot freshness all pass. An empty player list is valid.

## Test result

```text
EMPTY_PLAYER_SNAPSHOT_COMPATIBLE
```

Verified in the isolated no-player lab:

- BepInEx loaded exactly one expected plugin.
- Plugin Hello and heartbeat authenticated.
- `ZNet` collector reported compatible and `gameReady=true`.
- Fresh empty snapshots were accepted with sequences `15`, `16`, and `20`.
- `OnlinePlayers=true` with zero players.
- Authenticated `GET /players` returned HTTP 200 and `[]`.
- Authenticated capabilities returned only `OnlinePlayers`.
- No mutation capability was enabled.
- Lab shutdown and production restore passed.
- Production returned `active/running` with `Game server connected` and unchanged runtime boundary.
- Project Zomboid remained active.

- Plugin DLL SHA-256: `458eb253cd21f73186ad418d2dd7a8f717ca738828d393bfd56aea0424583df7`.
- Sanitized runtime evidence: `/srv/gamepanel/labs/valheim-plugin-compat/evidence/cr03a-lab-selected.log` and `cr03a-plugin-selected.log`.
- Snapshot sequence samples: `15`, `16`, `20`.
- `/players`: HTTP 200, player count `0`.
- Capabilities: `OnlinePlayers` only.
- No real player connected. No player endpoint/IP, token, password, process arguments, or proprietary source was recorded.
