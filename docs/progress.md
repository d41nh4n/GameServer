# Progress

## Milestone 1 — CRUD/API + React
- Environment checked, solution initialized, In-Memory Fake Runtime, React Dashboard
- Verified end-to-end (GET + start/stop state machine + CORS + dashboard)

## Milestone 2 — SQLite & SignalR (HOÀN THÀNH)
- Cleanup rác M1, SQLite (AppDbContext), FakeGameServerRuntime đọc/ghi SQLite, seed 2 server
- SignalR (ServerHub + MapHub /hubs/server + ServerStateChanged push)
- Frontend kết nối hub, cập nhật state qua push, hiện trạng thái kết nối
- Verified: DB seed, CORS cho SignalR (WithOrigins+AllowCredentials), push hoạt động

## Milestone 3 — Valheim Adapter & Real Process Management (HOÀN THÀNH)
- Infrastructure/GameServers/IGameServerAdapter.cs: StartAsync→(bool,int pid), StopAsync(pid), GetStatusAsync(pid)→GameServerStatus
- ValheimAdapter: Process.Start (ArgumentList, không shell injection), path từ config GameServers:Valheim:ExecutablePath (không hardcode trong code); StopAsync gửi kill -15; GetStatusAsync check /proc/[pid]
- GameServerAdapterFactory: trả adapter theo ServerInstance.Type (Valheim/Minecraft)
- GameServerManager (Infrastructure/Services) thay FakeGameServerRuntime: inject IGameServerAdapterFactory + AppDbContext + IHubContext; Start/Stop lưu ProcessId vào DB + SignalR push (Starting/Running/Stopping/Stopped)
- ServerInstance thêm: Type (enum), ProcessId (int?), Port, WorldName, Password
- EF Core migration AddProcessManagementAndType (dotnet-ef + Design pkg); Program.cs dùng Database.MigrateAsync() thay EnsureCreatedAsync
- Dọn stale files template cũ (GameServerPanel.Api.*) + Class1.cs

### Verified (browser thật + curl + sqlite)
- Start Valheim: chạy process thật (test_valheim_server.sh) với đúng args -name/-port/-world/-password, PID lưu DB, status=1, /proc tồn tại
- Stop: kill -15, /proc biến mất, DB clear PID + status=0
- SignalR push: curl Start từ ngoài → UI tự đổi RUNNING (không client click)
- Persist qua restart: PID 23878 + status=1 còn trong DB sau khi restart backend; process thật vẫn chạy độc lập
- Build backend + frontend: 0 lỗi

### Deviation (kiến trúc)
- IGameServerAdapter/IGameServerAdapterFactory đặt ở Infrastructure/GameServers (theo milestone). GameServerManager đặt ở Infrastructure/Services (không phải Application) vì nó phụ thuộc AppDbContext + IHubContext (Infrastructure); nếu đặt Application thì Application phải reference Infrastructure → vòng. IGameServerRuntime (Application/Interfaces) vẫn là contract cho Api.
- Valheim binary CHƯA cài trên máy. Test bằng wrapper script backend/scripts/test_valheim_server.sh (cùng code path StartAsync/StopAsync/GetStatusAsync). Path test cấu hình trong appsettings.Development.json; path Valheim thật trong appsettings.json.

### Remaining
- Cài Valheim dedicated server thật (SteamCMD) rồi đổi ExecutablePath về valheim_server.x86_64
- Minecraft adapter chưa implement (factory throw NotSupportedException)
- Sync status thực tế với /proc khi backend khởi động (hiện chỉ đọc từ DB)
- Auth, deploy