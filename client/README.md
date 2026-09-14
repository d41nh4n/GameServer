# GamePanel Client Updater

Portable Windows x64 updater for the approved GamePanel Valheim client modpack.

## Safety model

- Prompts for the existing GamePanel username/password; it keeps the JWT in memory only.
- Fetches only authenticated endpoints under `/api/client-updater/`.
- Accepts HTTP only for loopback or Tailscale CGNAT addresses; use HTTPS for any other host.
- Verifies each archive SHA-256 and every declared file SHA-256.
- Rejects traversal, symlinks, server-only packages, duplicate targets, undeclared archive files, and unapproved download paths.
- Writes only allowlisted BepInEx/Doorstop paths, backs up overwritten files under `.gamepanel-updater/backups/`, and rolls back a failed transaction.
- Refuses to update while Valheim is running.

The updater never follows a moving `latest` version. The server must publish an approved client manifest generated from lab-tested packages.

## Windows usage

1. Download `GamePanel.ClientUpdater.exe` from the release supplied by the panel operator.
2. Run it by double-clicking or PowerShell:

```powershell
.\GamePanel.ClientUpdater.exe --api-base http://100.82.102.38:5000 --check-only
.\GamePanel.ClientUpdater.exe --api-base http://100.82.102.38:5000 --launch
```

It detects the normal Steam Valheim path. If Steam is installed elsewhere:

```powershell
.\GamePanel.ClientUpdater.exe --api-base http://100.82.102.38:5000 --game-dir "D:\SteamLibrary\steamapps\common\Valheim" --launch
```

Use `--check-only` first: it downloads and verifies the approved manifest/packages but does not alter Valheim files.

## Build a portable EXE

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
cd client

dotnet test GamePanel.ClientUpdater.Tests/GamePanel.ClientUpdater.Tests.csproj

dotnet publish GamePanel.ClientUpdater/GamePanel.ClientUpdater.csproj \
  --configuration Release --runtime win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  --output artifacts/win-x64
```

Do not distribute an executable without publishing a matching approved client modpack from the server side.
