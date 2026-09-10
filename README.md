# Game Server Panel

Control panel for Valheim and Project Zomboid, built with ASP.NET Core 10 and React/Vite.

## Current status

- Valheim: adopted `valheim-main.service` with status probes and systemd controls.
- Project Zomboid: adopted `pzserver-game.service`; Flask manager remains active as fallback.
- PZ features: controls, save-before-stop, RCON, players/kick, journald/filesystem logs, INI config, SandboxVars, Workshop/Mods, and host/process metrics.
- Frontend: responsive dark gaming UI with server detail tabs.
- Latest verification: backend build 0 errors/0 warnings, 67/67 backend tests, frontend production build passed.

## Ports

- Backend API: `http://localhost:5000` or `http://100.82.102.38:5000`
- Frontend React: `http://localhost:5173` or `http://100.82.102.38:5173`
- Legacy Flask PZ manager: `127.0.0.1:8081` behind the existing nginx configuration

## Development

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

cd backend
 dotnet build
 dotnet test

cd ../frontend/game-panel-web
npm run build
```

Run the backend:

```bash
cd backend/Api
dotnet run --urls=http://localhost:5000
```

Run the frontend:

```bash
cd frontend/game-panel-web
npm run dev -- --host 0.0.0.0
```

For a laptop over SSH:

```bash
ssh -L 5173:localhost:5173 -L 5000:localhost:5000 nh4n@myserver
```

## PZ API areas

- `/api/servers/{id}/start` and `/stop`
- `/api/servers/{id}/logs`
- `/api/pz/rcon/*`
- `/api/pz/config*`
- `/api/pz/sandbox/*`
- `/api/pz/mods`
- `/api/pz/logs/*`
- `/api/pz/ops/health`

## Safety and migration rule

The legacy Flask manager and PZ systemd service must remain available during migration. Do not stop Flask, mutate the live world, change firewall rules, or perform a final cutover without explicit approval.

Machine-specific `appsettings.json`/`appsettings.Development.json`, JWT secrets, RCON passwords, and SQLite runtime files must not be committed.
