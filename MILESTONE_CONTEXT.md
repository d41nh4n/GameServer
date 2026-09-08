# GAMESERVER PROJECT - Context Started 2026-09-08

===HEADER===
Milestone 3 | Status DONE | Date 2026-09-08 | Duration 3 | 

===CURRENT_STATE===
ASP.NET Core 10 (SDK ~/.dotnet) GamePanel.slnx (Domain/Application/Infrastructure/Api) + SQLite (gamepanel.db, EF Core Migrations) + SignalR Hub /hubs/server. Frontend React TS Vite (:5173) + @microsoft/signalr. Backend :5000. CORS: WithOrigins + AllowCredentials. SSH: ssh -L 5173:localhost:5173 -L 5000:localhost:5000 nh4n@myserver. Code pushed to GitHub d41nh4n/GameServer (main).

===MILESTONE_3_DELIVERABLES===
- [x] .NET 10 + React TS setup, CORS (WithOrigins + AllowCredentials), API, Dashboard, 0 lỗi
- [x] SQLite seed 2 server (Valheim/Minecraft); DbSet<ServerInstance>; EnsureCreatedAsync
- [x] SignalR ServerHub (Infrastructure/Hubs) + FakeGameServerRuntime push ServerStateChanged; Frontend listener, UI auto xanh/đỏ no GET
- [x] AppDbContext (Infrastructure/Data)
- [x] IGameServerAdapter (Infrastructure/GameServers): StartAsync(ServerInstance)→(bool,pid), StopAsync(pid), GetStatusAsync(pid)→GameServerStatus
- [x] ValheimAdapter: Process.Start ArgumentList (safe), valheim_server.x86_64 -name -port -world -password -logFile -, kill -15, /proc check
- [x] GameServerAdapterFactory: Valheim/Minecraft/ProjectZomboid enum
- [x] GameServerManager (Infrastructure/Services): AppDbContext + IHubContext, inject Factory
- [x] ServerInstance: Type enum, ProcessId int?, Port, WorldName, Password
- [x] EF Migration MigrateAsync (thay EnsureCreatedAsync), auto columns
- [x] Config: appsettings.Development.json (test wrapper), appsettings.json (real binary), không hardcode
- [x] Dead code removed
- [x] Verified E2E: Start→real process, PID DB, /proc OK, status Running; Stop→kill -15, /proc gone, DB clear; SignalR push; persist restart; clean build
- [x] Git pushed to https://github.com/d41nh4n/GameServer (branch main)

===ARCHITECTURE_DECISIONS===
- IGameServerAdapter abstraction để hỗ trợ nhiều game (Valheim/Minecraft/Zomboid) mà không đụng vào Manager/API.
- GameServerAdapterFactory map enum Type → adapter concrete, tiêm vào GameServerManager (DI, không circular).
- GameServerManager phụ thuộc AppDbContext + IHubContext<ServerHub> → application push qua SignalR, kích hoạt UI realtime không cần polling GET.
- ValheimAdapter dùng Process.Start + ArgumentList thay vì chuỗi shell → tránh injection, argument an toàn.
- Stop dùng kill -15 (graceful); status check đọc /proc thay vì cache trong RAM → PID thật.
- EF MigrateAsync (không EnsureCreatedAsync) → schema versioned, auto columns qua Migrations.
- Đường dẫn binary đặt trong appsettings.json (không hardcode trong code). appsettings.Development.json trỏ tới test wrapper.

===VERIFIED_END_TO_END===
- Start server → process thật chạy → PID ghi vào DB → /proc có process → GetStatus = Running
- Stop → kill -15 → /proc hết process → DB xoá/cập nhật PID
- SignalR: curl Start → frontend UI tự chuyển Running (không có GET polling)
- Persist sau restart backend/frontend
- Clean build: 0 lỗi

===REMAINING_TODO===
- Install Valheim binary thật (SteamCMD ~1GB) để chạy production không phải test wrapper
- Minecraft adapter (Database chưa có game/binary)
- /proc sync khi backend restart (PID còn sót trong DB)
- Xử lý route one appsettings.json có đường dẫn tuyệt đối /home/nh4n/... (chuyển placeholder nếu deploy máy khác)

===NEXT_MILESTONE_OPTIONS===
- Option A: Auth JWT/API key (~2-3h, HIGH priority)
- Option B: Config flow POST Cấu hình qua UI (~2h, MEDIUM)
- Option C: Minecraft/Zombie adapter (~3h, LOW)
- Option D: Logs/Monitor (~3-4h, MED-HIGH)
- Option E: SteamCMD auto-install (~2-3h, HIGH)

===CRITICAL_PATHS===
- Config binary: appsettings.json "GameServers:Valheim:ExecutablePath"
- Test wrapper: backend/scripts/test_valheim_server.sh
- GameServerManager: backend/Infrastructure/Services/GameServerManager.cs
- Adapters: backend/Infrastructure/GameServers/
- SignalR Hub: backend/Infrastructure/Hubs/ServerHub.cs
- DB context: backend/Infrastructure/Data/AppDbContext.cs
- Migration: backend/Infrastructure/Migrations/
- Sln: backend/GamePanel.slnx