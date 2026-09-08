# GAMESERVER PROJECT - Context Started 2026-09-08

===HEADER===
Milestone 4 (Auth & Security) | Status DONE | Date 2026-09-08 | Duration 3 | Lang: MVVM

===CURRENT_STATE===
ASP.NET Core 10 (SDK ~/.dotnet) GamePanel.slnx (Domain/Application/Infrastructure/Api) + SQLite (gamepanel.db, EF Core Migrations) + SignalR Hub /hubs/server. Frontend React TS Vite (:5173) + @microsoft/signalr. Backend :5000. CORS: WithOrigins + AllowCredentials + thêm origin Tailscale. Bind 0.0.0.0:
- Backend: http://100.82.102.38:5000, http://localhost:5000
- Frontend: http://100.82.102.38:5173, http://localhost:5173
JWT Bearer auth bảo vệ Start/Stop. GitHub d41nh4n/GameServer (main).

===MILESTONE_4_DELIVERABLES===
Auth JWT + Tailscale whitelist (Option A) — HOÀN THÀNH
- [x] appsettings.json/Development chuyển {HOME} placeholder (không hardcode /home/nh4n/...); appsettings.example.json template để contributor setup
- [x] appsettings.json + appsettings.Development.json thêm vào .gitignore (machine-specific: đường dẫn + JWT secret)
- [x] AppSettings.cs (backend/Api): JwtSettings, TailscaleSettings, ValheimSettings options model, đọc từ appsettings
- [x] Domain/ValueObjects/JwtToken.cs: claims sub=userId, role=admin, iat, exp
- [x] Domain/Entities/User.cs: User {Id, Username, Role}
- [x] Application/Interfaces/IAuthService.cs: GenerateToken(userId), ValidateToken(token)→User
- [x] Infrastructure/Auth/JwtAuthService.cs: implement IAuthService, JwtSecurityTokenHandler, HS256, key từ "Jwt:Secret", chặn CHANGE_ME
- [x] Program.cs: AddAuthentication("Bearer") + AddJwtBearer (validate token + lifetime), AddAuthorizationPolicy "admin" (RequireRole Admin)
- [x] Protect endpoints: /api/servers/{id}/start, /stop → .RequireAuthorization("admin") (minimal API không dùng [Authorize] attribute)
- [x] TailscaleMiddleware: chặn RemoteIpAddress ngoài subnet 100.64.0.0/10 khi Tailscale:Require=true (mặc định false); IPNetwork tự parse subnet
- [x] POST /api/auth/login (fake): trả JWT {token, expiresAt}
- [x] jwt bearer + identitymodel package (Api.csproj: Auth.JwtBearer; Infrastructure.csproj: System.IdentityModel.Tokens.Jwt) — dotnet restore OK
- [x] Verify: Start ko token → 401; Start có Bearer token → 200; GET /api/servers public → 200; login → 200
- [x] Bind 0.0.0.0 + CORS thêm http://100.82.102.38:5173 (Tailscale VPN remote access)

===ARCHITECTURE_DECISIONS===
- IGameServerAdapter abstraction → nhiều game (Valheim/Minecraft/Zomboid) không đụng Manager/API.
- GameServerAdapterFactory map enum Type → adapter, tiêm vào GameServerManager (DI, không circular).
- GameServerManager phụ thuộc AppDbContext + IHubContext<ServerHub> → application push qua SignalR, UI realtime không cần polling GET.
- ValheimAdapter: Process.Start + ArgumentList thay vì chuỗi shell → tránh injection.
- Stop kill -15 (graceful); status đọc /proc (PID thật, không cache RAM).
- EF MigrateAsync (không EnsureCreatedAsync) → schema versioned.
- Đường dẫn binary đặt appsettings.json, placeholder {HOME} được ExpandHome() trong ValheimAdapter; không hardcode.
- Minimal-style endpoints giữ nguyên (không refactor sang Controllers), policy dùng .RequireAuthorization("admin").
- JWT config đọc từ "Jwt:Secret" (appsettings, không hardcode); nếu "CHANGE_ME" → throw (ép cấu hình trước khi chạy).
- Middleware Tailscale là tuỳ chọn (Require=false mặc định) để dev local không bị chặn; không đổi binding code, bind 0.0.0.0 khi deploy home-server.

===VERIFIED_END_TO_END===
- M3 (giữ nguyên): Start→process thật→PID DB→/proc OK→Running; Stop→kill -15→/proc gone→DB clear; SignalR push; persist restart; clean build.
- M4: POST /api/auth/login → 200 kèm token (len 365 HS256)
- POST /api/servers/{id}/start KHÔNG token → 401 Unauthorized
- POST /api/servers/{id}/start CÓ "Authorization: Bearer <token>" → 200
- GET /api/servers (public) → 200
- Frontend http://100.82.102.38:5173 → 200; API http://100.82.102.38:5000/api/auth/login → 200
- CORS preflight từ origin Tailscale (OPTIONS /start) → 204
- dotnet build: Build succeeded, 0 Error, 0 Warning

===REMAINING_TODO===
- cài Valheim binary thật (SteamCMD ~1GB) thay test wrapper → prod
- Minecraft/Zomboid adapter (chưa có binary)
- /proc sync khi backend restart (PID còn sót trong DB)
- token expired → hiện chưa test vì resolve tuỳ middleware (JWt hiện trả 401 khi expired); user yêu cầu 403 → cần tùy chỉnh nếu muốn
- Bảo mật: GET /api/servers vẫn public; cân nhắc [Authorize] nếu không muốn expose
- Muốn endpoint chỉ từ Tailscale → bật Tailscale:Require=true (middleware sẵn)

===NEXT_MILESTONE_OPTIONS===
- Option M5-A: Config UI (POST cấu hình qua UI, ~2h MEDIUM)
- Option M5-B: SteamCMD auto-install Valheim binary (~2-3h HIGH)
- Option M5-C: Logs/Monitor backend (~3-4h MED-HIGH)
- Option B (M4 cũ): Auth JWT đã xong → chuyển sang xử lý role-based nếu cần
- Luôn ALWAYS/prioritized — xem todo trên

===CRITICAL_PATHS===
- Config binary: appsettings.json "GameServers:Valheim:ExecutablePath" (placeholder {HOME})
- Test wrapper: backend/scripts/test_valheim_server.sh
- Auth service: backend/Infrastructure/Auth/JwtAuthService.cs
- IAuthService: backend/Application/Interfaces/IAuthService.cs
- Options model: backend/Api/AppSettings.cs
- Tailscale middleware: backend/Api/Middleware/TailscaleMiddleware.cs
- Auth config: backend/Api/Program.cs (AddAuthentication/AddJwtBearer/AddAuthorization)
- GameServerManager: backend/Infrastructure/Services/GameServerManager.cs
- Adapters: backend/Infrastructure/GameServers/
- SignalR Hub: backend/Infrastructure/Hubs/ServerHub.cs
- DB context: backend/Infrastructure/Data/AppDbContext.cs
- Sln: backend/GamePanel.slnx