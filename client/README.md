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

1. Download `GamePanel.ClientUpdater.exe` and `SHA256SUMS` from `http://100.82.102.38:5001/`, then verify the checksum.
2. Run it by double-clicking or PowerShell:

```powershell
.\GamePanel.ClientUpdater.exe --check-only
.\GamePanel.ClientUpdater.exe --launch
```

It detects the normal Steam Valheim path. If Steam is installed elsewhere:

```powershell
.\GamePanel.ClientUpdater.exe --game-dir "D:\SteamLibrary\steamapps\common\Valheim" --launch
```

Use `--check-only` first: it downloads and verifies the approved manifest/packages but does not alter Valheim files.
On apply, files managed by the previous manifest but removed from the current revision are backed up and removed. Unmanaged client files are left untouched.

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
