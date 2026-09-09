# GAMESERVER PROJECT - Context Started 2026-09-08

===HEADER===
Milestone 4 (Auth & Security) | Status DONE | Date 2026-09-08 | Duration 3 | Lang: MVVM
CR-00A (API Response Data Exposure) | Status DONE | Commit base c1db2ef
CR-00B (Real Local Authentication) | Status IMPLEMENTED (nie commited/nie uruchamiany) | Commit base d595b2c

===CR_00B_DELIVERABLES===
- [x] User entity: + NormalizedUsername + PasswordHash (Domain)
- [x] AppDbContext: DbSet Users + unique index NormalizedUsername (COLLATE NOCASE w migracji)
- [x] Migracja 20260909120000_AddLocalUsers (+ Designer + ModelSnapshot zaktualizowane)
- [x] IAuthService: + AuthenticateAsync / UsersNeedBootstrapAsync / EnsureBootstrapAdminAsync
- [x] JwtAuthService: auth przez DB + IPasswordHasher<User> (Microsoft.AspNetCore.Identity), gen.sub=user.Id, role z DB
- [x] LoginContracts: LoginRequest / LoginResponse (Api/CoreContracts)
- [x] Program.cs: realny login (401 ogólny), bootstrap admin z env (GAMEPANEL_BOOTSTRAP_ADMIN_USERNAME/PASSWORD, tylko gdy Users pusta), rate limit per-IP na login (AddPolicy + IRateLimiterPolicy partitionowana po RemoteIpAddress, 5/min, HTTP 429)
- [x] HARDENING: partitioning per-IP (LoginRateLimitPolicy, fallback unknown-ip), Normalize→Trim().ToLowerInvariant(), RehashNeeded→persist upgrade hash, dokumentacja PBKDF2 (PasswordHasher default IdentityV3), 429 na odrzucenie
- [x] Testy (izolowana plikowa SQLite, 20 testów): poprawny login, złe hasło, nieznany user, brak inputów, hash!=plaintext, case-insens username, invariant normalization (ı vs i), JWT sub/role (payload), bootstrap idempotentny, no uncond admin token, per-IP separation (niezależne liczniki), throttling (6. próba odrzucona)
- [ ] NIE uruchomiono backendu, NIE dotnet-ef update (zgodnie z CR), NIE commitowano/pushowano

===CR_00A_DELIVERABLES===
- [x] GET /api/servers → ServerResponse DTO (Api/CoreContracts) zamiast bezpośrednio encji; NIE zwraca password/processId
- [x] Mapper ServerResponse.FromDomain (enum jako string; zachowane id/name/gameType/type/status/port/worldName)
- [x] Test projekt Test/GamePanel.ContractTests.csproj (xunit) — 3 testy kontraktu (pola wymagane, brak password/processId, reprezentacja enum) — pass
- [x] .gitignore + *.db-wal + *.db-shm (hygiene — SQLite runtime files)
- [x] Frontend build: bez zmian, kompiluje się (kontrakt zachowany)

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
- Follow-up CR-PZ: RCON graceful save/broadcast/players (CR-PZ-02), logs+journal streaming (CR-PZ-03), servertest.ini editor (CR-PZ-04), SandboxVars.lua (CR-PZ-05), Workshop/Mods manager (CR-PZ-06), World/Profile manager (CR-PZ-07), health/readiness metrics (CR-PZ-08)
- Option M5-A: Config UI (POST cấu hình qua UI, ~2h MEDIUM)
- Option M5-B: SteamCMD auto-install Valheim binary (~2-3h HIGH)
- Option M5-C: Logs/Monitor backend (~3-4h MED-HIGH)

===PZ_CR_DONE (systemd-backed Project Zomboid adapter)===
- [x] GameServerType.ProjectZomboid enum (ServerInstance.cs)
- [x] ProjectZomboidAdapter (Infrastructure/GameServers/) — systemd lifecycle via /usr/local/sbin/pz-gamectl + sudo -n; status z systemctl show (ActiveState/MainPID), nie z DB
- [x] ICommandRunner + ProcessCommandRunner (ProcessStartInfo + ArgumentList, bez shell) — testowalny, wstrzykiwany
- [x] GameServerAdapterFactory: ProjectZomboid → ProjectZomboidAdapter
- [x] Program.cs: rejestracja ICommandRunner + ProjectZomboidAdapter (czyta config przez IConfiguration), seed PZ (Type=2, WorldName=servertest_new)
- [x] appsettings.example.json + .Development.json: GameServers:ProjectZomboid (ServiceName, ControlExecutable, RconHost/Port, SudoUser)
- [x] Reconcile stale-PID w GameServerManager.GetAllAsync (adapter źródłem prawdy: /proc dla Valheim, systemd dla PZ)
- [x] Config adapter przez IConfiguration (nie Configure<T> — brak sekcji nie rejestruje usługi!)
- [x] bounded MainPID wait (max 150s) — Type=simple zgłasza active przed wstaniem PZ

===VERIFIED_END_TO_END (PZ)_===
- [x] Backend restart + GET /servers → PZ status=1 pid=44566 (real MainPID, nie stale DB)
- [x] POST /api/servers/{pz}/start (DB Stopped, systemd active) → 200, MainPID reconciled 40418
- [x] POST /start na już-running → 400 (idempotentny manager)
- [x] POST /api/servers/{pz}/stop → 200, systemd inactive, DB Stopped/pid=None
- [x] START cold boot (after stop) → server doładował, MainPID 44566 po ~2min (bounded retry)
- [x] Valheim stale pid=999999 → GET reconcile do Stopped/pid=null; restart Valheim → 200 (lifecycle OK)
- [x] dotnet build: 0 Error, 0 Warning

===DEPLOY_STEPS (sudoers/sudo)===
- nh4n ma NOPASSWD:ALL (sudo -l potwierdza) — adapter używa `sudo -n /usr/local/sbin/pz-gamectl <subcmd>`
- Wrapper /usr/local/sbin/pz-gamectl to whitelist: start/stop/restart/is-active/status/show/logs/stream
- pzserver-game.service: KillSignal=SIGINT, TimeoutStopSec=90, ExecStartPre=preflight, SuccessExitStatus=130
- RCONPort=27015 (non-secret; password w servertest_new.ini — NIE commitować)

===CRITICAL_PATHS===
- Config binary: appsettings.json "GameServers:Valheim:ExecutablePath" (placeholder {HOME})
- Test wrapper: backend/scripts/test_valheim_server.sh
- Auth service: backend/Infrastructure/Auth/JwtAuthService.cs
- IAuthService: backend/Application/Interfaces/IAuthService.cs
- Options model: backend/Api/AppSettings.cs
- Tailscale middleware: backend/Api/Middleware/TailscaleMiddleware.cs
- Auth config: backend/Api/Program.cs (AddAuthentication/AddJwtBearer/AddAuthorization)
- GameServerManager: backend/Infrastructure/Services/GameServerManager.cs
- PZ adapter: backend/Infrastructure/GameServers/ProjectZomboidAdapter.cs
- Command runner: backend/Infrastructure/GameServers/ProcessCommandRunner.cs + ICommandRunner.cs
- PZ config: appsettings "GameServers:ProjectZomboid" (ServiceName/ControlExecutable/SudoUser)
- Adapters: backend/Infrastructure/GameServers/
- SignalR Hub: backend/Infrastructure/Hubs/ServerHub.cs
- DB context: backend/Infrastructure/Data/AppDbContext.cs
- Sln: backend/GamePanel.slnx