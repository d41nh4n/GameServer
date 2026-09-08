# Game Server Panel - Development Guide

## Ports
- Backend API: http://localhost:5000
- Frontend React: http://localhost:5173

## SSH Port Forwarding (Xem từ Laptop)

```bash
ssh -L 5173:localhost:5173 -L 5000:localhost:5000 nh4n@myserver
```
Sau đó mở browser laptop truy cập: http://localhost:5173

## Run Development
1. Backend: `cd backend/Api && dotnet run --urls=http://localhost:5000`
2. Frontend: `cd frontend/game-panel-web && npm run dev`

## Notes
- .NET SDK lives in ~/.dotnet (not on PATH): export PATH="$HOME/.dotnet:$PATH"
- Backend uses .NET 10 Aspire minimal-API style (Results.*, no AddSwaggerGen/AddCors plugins removed in .NET 10).
- Fake in-memory runtime: backend/Infrastructure/Services/FakeGameServerRuntime.cs