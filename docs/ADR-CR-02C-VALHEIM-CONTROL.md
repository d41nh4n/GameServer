# ADR — CR-02C Valheim read-only control architecture and compatibility gate

- Status: Accepted for isolated read-only snapshot lab
- Scope: static API discovery plus empty-player runtime validation; no production plugin deployment
- Date: 2026-09-11

## Decision

Use the following target architecture:

```text
Control Panel
  -> HTTP 127.0.0.1:27666
  -> ValheimControlBridge
  -> local typed IPC
  -> in-process BepInEx plugin
  -> immutable game-state snapshot
```

All Unity and Valheim API calls are initially treated as Unity-main-thread-only. The IPC thread may validate, queue, and publish typed data, but must never invoke game APIs directly.

The existing sidecar bridge plus local IPC is preferred over embedding an HTTP listener in the BepInEx plugin.

## Options

### A — HTTP listener inside the BepInEx plugin

Pros:

- Fewer processes.
- Direct access to game state.
- Simple request path.

Cons:

- Opens networking code inside the game process.
- HTTP callback/threading can accidentally call Unity APIs off-main-thread.
- Plugin crash or blocking I/O can affect the game.
- Harder to isolate authentication, timeouts, and backpressure.
- Increases the blast radius of a protocol bug.

### B — Existing sidecar bridge plus local IPC (selected)

Pros:

- Bridge owns authentication, bounded HTTP, structured errors, and client timeouts.
- Plugin only sends typed snapshots over local IPC.
- Game-thread access can be isolated behind a main-thread queue.
- Bridge restart and plugin reconnect are independently testable.
- No WAN listener is needed.

Cons:

- One additional local process and IPC protocol.
- Snapshot freshness and reconnect state must be tracked explicitly.

## Compatibility evidence

Verified facts:

- Steam Build ID: `25185644`.
- Handoff runtime version: `l-1.0.7`.
- Network version: `39`.
- `valheim_server.x86_64` is ELF64 x86-64.
- `UnityPlayer.so` is ELF64 x86-64.
- `Assembly-CSharp.dll`, `assembly_valheim.dll`, `mscorlib.dll`, `UnityEngine.dll`, and `UnityEngine.CoreModule.dll` are PE32 Mono/.NET assemblies.
- Managed assemblies exist under `valheim_server_Data/Managed`.
- The exact Unity engine version was established by the isolated BepInEx loader smoke test as `6000.0.75f1`.
- The isolated loader smoke test passed for BepInEx 5.4.23.5 on Steam Build `25185644`.
Inferences, now supported by the loader smoke test:

- The server uses Unity Mono rather than IL2CPP.
- BepInEx Unity Mono Linux x64 is compatible with this isolated build/runtime pair.
- Plugin compilation requires the selected `net40` target with local game/BepInEx references.

Static hashes (do not redistribute the files):

```text
Assembly-CSharp.dll:              a31831564938f3513a0022016aa0d98d994741f7e1f183ff28a837bfc5b851f8
assembly_valheim.dll:             6ebcb5ce3742b3f65b1510eafaf96468e87ae4273c8c1cba90e8d0782be47854
mscorlib.dll:                     ccee7cda65c541774220d0d741f3d8794efb9981db7f1e22b5f9ee71f01655c5
UnityEngine.dll:                  ee449f6c6d0a76826f4ef5b26cde9494b4500e9b0c9217508f2e3f3c50d3351c
UnityEngine.CoreModule.dll:       4cb2a683351f6644de26086d01e7b5879fcf228ee0dbd0d7cc0536570bd15681
UnityPlayer.so:                   66c4643dd702e726ed85abcb17e50923cd1de259d58e5536e5eed4647249e268
valheim_server.x86_64:            afdc1feb7b381da99ba43c25cf75b211068ee8f778a187951f5ca644faf819cc
```

## Candidate loader

Upstream BepInEx documentation/repository indicates stable BepInEx 5 is the candidate family for Unity Mono, with a Linux x64 distribution for a 64-bit game. The upstream release page currently lists `BepInEx 5.4.23.5` as the latest 5.x release observed during discovery.

This candidate was validated by the isolated loader and empty-snapshot runtime test for Steam Build `25185644`. No production plugin deployment is implied.

Jötunn is not selected. It is an additional compatibility dependency and is not required to establish the first empty read-only snapshot.

## Compatibility gate result

```text
EMPTY_PLAYER_SNAPSHOT_COMPATIBLE
```

The selected public API mapping, Unity-main-thread collector, authenticated snapshot protocol, fresh empty snapshot, `OnlinePlayers` capability, and lab restore gates all passed. Mutation capabilities remain false. A real player was not connected.