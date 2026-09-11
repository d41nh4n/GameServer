# CR-02B — Valheim Control Plugin Bridge

## Discovery report

Read-only discovery confirmed:

- Dedicated server Steam App ID: `896660`.
- Game App ID: `892970`.
- Steam build ID: `25185644`.
- Existing handoff identifies runtime version `l-1.0.7` and network version `39`.
- Runtime: `valheim-main.service`, user/group `valheim:valheim`.
- Launcher: `/usr/local/libexec/valheim-main-start`.
- Existing control remains systemd-only; no plugin/BepInEx installation was found or performed.
- Existing readiness checks remain systemd state, cgroup, UDP ports, and current journal invocation marker.

Compatibility decision: the bridge is a separate .NET protocol host with no BepInEx or Unity dependency in this phase. It is a contract/test harness for a future server-side plugin. The real plugin compatibility implementation must be validated against the exact Valheim build before deployment.

## Bridge project

`plugin/ValheimControlBridge/` is an independent ASP.NET Core project. It is not referenced by the game runtime and was not deployed.

The host:

- binds explicitly to `http://127.0.0.1:27666`;
- requires a bearer token from `VALHEIM_CONTROL_TOKEN`;
- uses fixed-time token comparison;
- exposes only `GET /health`, `GET /capabilities`, and `GET /players`;
- returns structured JSON;
- has no shell, RCON, systemd, world-file, or arbitrary-command path.

The current host reports an empty capability set because no game plugin provider is attached. It is a protocol foundation, not a live game controller.

## Panel integration

`ValheimControlProtocol` now uses a bounded `HttpClient` when explicitly enabled and configured. It defaults to disabled and preserves `PLUGIN_UNAVAILABLE`/503 behavior when absent. Unauthorized, timeout, malformed, and connection failures are converted into unavailable structured results without exposing the configured token.

Live action execution remains unavailable in CR-02B; no mutation endpoint is enabled in the bridge. Existing CR-02 API routes therefore continue to fail closed when the plugin is absent.

## Tests

Added tests cover:

- available plugin capability and player JSON;
- disabled plugin without network calls;
- invalid bridge authentication;
- HTTP timeout;
- capability parsing;
- online player response;
- token non-disclosure;
- existing action provider unavailable/unsupported/idempotency/typed validation behavior.

Verification:

- Backend build: passed.
- Backend tests: `91/91` passed.
- Bridge project build: passed.
- Frontend UI tests: `9/9` passed.
- Frontend build: passed.
- `git diff --check`: passed.

## Safety and rollback

No plugin was installed, no listener was started, no BepInEx/mod was installed, no systemd unit/launcher/config/data was modified, no Valheim restart was performed, and Project Zomboid was not touched.

Rollback is source-only: remove the bridge project, protocol client registration, CR-02B tests, and this document, then rebuild. No database rollback or game-world restore is required.
