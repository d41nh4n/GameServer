# Game Panel Web

React + TypeScript + Vite frontend for the Game Server Panel.

## Structure

- `src/App.tsx`: authentication lifecycle, SignalR lifecycle, page/server selection and lifecycle actions.
- `src/auth.ts`: sessionStorage JWT, Bearer client, JSON request headers and all API methods.
- `src/components/common.tsx`: shared buttons/status primitives.
- `src/components/auth/LoginPage.tsx`: login form.
- `src/components/servers/ServerListView.tsx`: overview cards, server grid and log/player charts.
- `src/components/servers/ServerDetailView.tsx`: per-server tab router and controls.
- `src/components/servers/ValheimStatus.tsx`: Valheim health checks, members and journal logs.
- `src/components/servers/WorldBackups.tsx`: PZ/Valheim version timeline, backup and rollback confirmation.
- `src/index.css`: responsive dark gaming theme and chart/timeline styles.

## Server detail tabs

Valheim:

```text
Controls · Status & Checks · World Versions · Logs
```

Project Zomboid:

```text
Controls · World Versions · Logs · Config · RCON · Mods · Sandbox
```

## Resource usage

The overview loads `GET /api/resources/overview` and displays host CPU, RAM and disk plus per-server online state, PID, CPU, RSS memory, threads and file descriptors. The same server process snapshot appears at the top of each server detail view.

## Important client behavior

`AuthProvider.request()` automatically sets `Content-Type: application/json` whenever a request has a body. This is required by ASP.NET Minimal API body binding and prevents HTTP 415 errors for RCON/config/backup mutations.

## Commands

```bash
npm run dev -- --host 0.0.0.0
npm run build
```

The production build must pass before deployment. Do not place credentials or tokens in source, logs or documentation.
