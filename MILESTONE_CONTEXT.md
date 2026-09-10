# GAMESERVER PROJECT - Context Started 2026-09-08

===HEADER===
Milestone 4 (Auth & Security) | Status DONE | Date 2026-09-08 | Duration 3 | Lang: MVVM
CR-00A (API Response Data Exposure) | Status DONE | Commit c1db2ef
CR-00B (Real Local Authentication) | Status DONE | Commit aa59fb7
CR-00C (End-to-end JWT protection) | Status DONE | Commit 98c7cf7
CR-01B (Valheim AdoptExisting + systemd control) | Status IMPLEMENTED | Code + runtime verified
CR-PZ (Project Zomboid migration + control UI) | Status IMPLEMENTED, awaiting review | Code-only migration; Flask remains active

===CR_01B_DELIVERABLES===
- [x] Shared allowlisted ISystemdRuntimeDriver/SystemdRuntimeDriver over ICommandRunner; fixed systemctl properties and wrapper control abstraction
- [x] ValheimProvider -> ValheimRuntimeStrategy -> SystemdRuntimeDriver; status probes plus allowlisted start/stop through systemd
- [x] Valheim runtime port probe works without root (`ss -lun`); cgroup and journal marker remain readiness checks
- [x] Runtime snapshot-oriented IGameServerAdapter; manager reconciles AdoptExisting state and preserves truthful state on action failures
- [x] PZ moved to shared systemd driver with existing wrapper/control behavior retained
- [x] Additive nullable/default adoption schema + unique InstanceKey and filtered unique RuntimeType/RuntimeId; no data insert/update
- [x] ServerResponse adds only instanceKey/provisioningMode/runtimeType/ready; sensitive runtime fields remain excluded
- [x] Fake-only driver/Valheim/PZ/manager/migration/API tests; 67/67 backend suite passed

===CR_PZ_DELIVERABLES===
- [x] Existing `pzserver-game.service` adopted by the new panel; Flask/gunicorn on :8081 remains running
- [x] RCON client with Project Zomboid auth-handshake handling; save, players, broadcast, kick, and raw command endpoints
- [x] PZ stop performs RCON save attempt before systemd stop; systemd SIGINT remains fallback save path
- [x] INI config read/raw/write/backup service with preserved unrelated lines
- [x] SandboxVars.lua read/edit/backup service with scalar detection and atomic write
- [x] Journald logs plus filesystem log listing/read endpoints
- [x] Ops metrics endpoint for CPU, host memory, disks, and process metrics
- [x] WorkshopItems/Mods read/update endpoints with backup
- [x] Responsive UI navbar, server detail pages, Controls, Logs, Config, RCON, Mods, and Sandbox tabs
- [x] PZ detail page displays runtime user (`pzserver`); player list and Kick action use RCON
- [x] Migration is code-first; no Flask shutdown, systemd migration, firewall change, or world mutation performed

===CR_01B_LEGACY_DELIVERABLES===
- [x] Shared allowlisted ISystemdRuntimeDriver/SystemdRuntimeDriver over ICommandRunner; fixed systemctl properties and wrapper control abstraction
- [x] ValheimProvider -> ValheimRuntimeStrategy -> SystemdRuntimeDriver; read/status only, start/stop fail closed, no direct game Process.Start or env-secret reads
- [x] Runtime snapshot-oriented IGameServerAdapter; manager reconciles AdoptExisting state and preserves truthful state on action failures
- [x] PZ moved to shared systemd driver with existing wrapper/control behavior retained
- [x] Additive nullable/default adoption schema + unique InstanceKey and filtered unique RuntimeType/RuntimeId; no data insert/update
- [x] ServerResponse adds only instanceKey/provisioningMode/runtimeType/ready; sensitive runtime fields remain excluded
- [x] Fake-only driver/Valheim/PZ/manager/migration/API tests; 59/59 full backend suite passed; no real systemd, database, or game runtime calls

===CR_00C_DELIVERABLES===
- [x] Backend GET /api/servers → RequireAuthorization("authenticated"); start/stop → "admin" (już)
- [x] Explicit auth policies: "admin" (RequireRole Admin), "authenticated" (RequireAuthenticatedUser)
- [x] Hub /hubs/server: JWT auth przez access_token w query string (SignalR nie może ustawić nagłówka)
- [x] HARDENING-00C: PathString.StartsWithSegments(/hubs/server) (path-boundary: /hubs/server-evil odrzucone); Request.Query["access_token"] (decoded przez ASP.NET, bez ręcznego URL-decodu, Count==1 jednoznaczny, pusty/multi odrzucone); MapHub CloseOnAuthenticationExpiration=true; usunięto QueryTokenExtractor
- [x] CORRECTNESS-00C: poprawka błędnego założenia (ctx.Token NIE jest wcześniej wypełniony w OnMessageReceived) — nagłówek Authorization sprawdzany bezpośrednio przez Request.Headers["Authorization"]; logika wyciągnięta do czystej HubAccessTokenDecision.Resolve(onHub, authHeader, query) (fail-closed, header ma pierwszeństwo, jednoznaczny token); testy jednostkowe precedencji (header>query) i path-boundary
- [x] Frontend: login screen, AuthProvider, JWT w sessionStorage, restore, logout, wspólny klient Bearer, 401→logout, 429 komunikat, SignalR po auth accessTokenFactory, stop na logout, SignalR connect/reconnect fail z powodu 401/403→logout (bez logowania tokenu)
- [x] Login anonimowy + rate-limited (per-IP 429); ServerResponse kontrakt bez zmian
- [x] Testy: HubAccessTokenDecisionTests (path-boundary, precedencja header>query, fail-closed) + LocalAuth + LoginRateLimit + ServerResponse — 28/28 łącznie
- [x] TESTY INTEGRACYJNE (WebApplicationFactory/TestServer, fake runtime, temp SQLite): 13/13 przechodzi; cały zestaw 41/41 — anon GET/hub/start/stop 401, non-Admin start/stop 403, admin GET/start/stop 200, bez password/processId, odrzucone mutacje bez wywołań fake, admin start/stop po jednym wywołaniu, hub Authorization/query-token/header-precedence OK
- [x] HUB AUTH: `[Authorize(Policy = "authenticated")]` na ServerHub i `.RequireAuthorization("authenticated")` na MapHub; anonimowy negotiate zwraca 401
- [x] CORRECTNESS-fix: OnMessageReceived zwraca Task.CompletedTask (NIE null); null powodował NRE w JwtBearerHandler (500 na każdy request)
- [x] NIE uruchomiono dotnet run/realnego listenera/systemd/PZ/Valheim/firewall/Tailscale; promt_migration_pz.MD niezmieniony

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
- ValheimProvider/ValheimRuntimeStrategy dùng shared SystemdRuntimeDriver; không chạy executable trực tiếp.
- Valheim AdoptExisting hiện read/status-only; start/stop fail-closed cho tới CR privilege-wrapper.
- Systemd state dùng fixed properties + allowlisted unit; Valheim readiness thêm cgroup, UDP 2456-2457 và marker theo InvocationID.
- EF MigrateAsync (không EnsureCreatedAsync) → schema versioned.
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

===CR_VALHEIM_MONITORING_DELIVERABLES===
- [x] Valheim monitor snapshot: systemd state, MainPID, InvocationID, readiness, members and backup versions
- [x] Valheim journal logs endpoint with bounded line count
- [x] Valheim member management: read/add/remove Admin, Permitted and Banned lists; file backup before writes; role/ID validation
- [x] Safe world backup endpoint: refuses while service is active to avoid copying an inconsistent live world; stores versions outside the game data directory
- [x] Responsive Valheim UI tabs: Logs, Members and World Backups
- [x] Live verification: `active=true`, `ready=true`, 3 member entries, 0 existing panel backups

===PHASE_1_AUDIT_LOGS===
- [x] AuditLogs entity + EF model: CreatedAtUtc, UserId, UsernameSnapshot, ServerInstanceId, Action, IsSuccess, ResultCode, MetadataJson
- [x] Metadata secret guard blocks password/token/secret/JWT fields
- [x] Start/Stop endpoints record admin actions and result codes
- [x] Admin read endpoint: `GET /api/audit` with server filter and bounded limit
- [x] Migration applied after external SQLite backup: `AddAuditLogs`, `AddSystemEvents`, `AddLogAggregates`

===PHASE_2_SYSTEM_EVENTS===
- [x] SystemEvents entity + EF model: CreatedAtUtc, ServerInstanceId, EventType, status transition, MainPid, Message, Severity
- [x] Runtime state transitions recorded by GameServerManager during discovery/start/stop
- [x] Authenticated timeline endpoint: `GET /api/events` with server filter and bounded limit
- [x] Migration applied after external SQLite backup: `AddSystemEvents`

===PHASE_3_LOG_AGGREGATES_RETIRED===
- [x] LogAggregates entity + EF model: window, warning/error/fatal counts, player join/leave counts, last error
- [x] Parser separates aggregate counters from raw log text; raw logs remain in journal/filesystem
- [x] Upsert service with unique `(ServerInstanceId, WindowStartUtc)` window key
- [x] Authenticated query endpoint: `GET /api/aggregates` with server/time filters and bounded limit
- [x] Migration applied after external SQLite backup: `AddLogAggregates`

===PHASE_4_LOG_AGGREGATE_COLLECTOR_RETIRED===
- [x] BackgroundService aligns to configurable UTC windows (default 5 minutes)
- [x] Reads bounded systemd journal windows for adopted systemd servers
- [x] Parses counters and upserts LogAggregates; never stores raw journal text
- [x] Collector enabled in current production `appsettings.json`; feature flag remains false in example template
- [x] Interval configurable via `Metrics:LogAggregateIntervalMinutes`

===PHASE_5_GLOBAL_METRICS_RETIRED===
- [x] GlobalMetricsService aggregates server status and 24h LogAggregate counters
- [x] Authenticated endpoint: `GET /api/metrics/global`
- [x] Responsive server overview metrics strip: Running, Ready, Stopped, Errors 24h, Fatal 24h
- [x] Metrics gracefully hide when production migrations are not yet applied

===PHASE_5_GLOBAL_METRICS_RETIRED===
- [x] GlobalMetricsService aggregates server status and 24h LogAggregate counters
- [x] Authenticated endpoint: `GET /api/metrics/global`
- [x] Authenticated endpoint: `GET /api/aggregates?limit=...`
- [x] Responsive overview cards and SVG/CSS charts for log health and player activity
- [x] Metrics Explorer table shows every stored aggregate window with server, UTC window, warning/error/fatal, joins/leaves and last error
- [x] Metrics Explorer filters by server, severity and UTC date range through `/api/aggregates`
- [x] Chart properties documented in `docs/SOURCE_KNOWLEDGE_BASE.md`

===PHASE_6_WORLD_VERSIONS===
- [x] PZ live backup: RCON `save`, short settle delay, then world copy without restart/stop
- [x] Valheim backup refuses active service because no consistent live snapshot/RCON path exists
- [x] Versioned backup roots remain outside live game data
- [x] Rollback requires stopped service and creates protected `HEAD-before-rollback` first
- [x] Rollback validates version names and uses staging replacement
- [x] PZ/Valheim World Versions UI uses node timeline and confirmation before rollback
- [x] API routes documented in source knowledge base

===PHASE_7_UI_REFACTOR_AND_KNOWLEDGE_BASE===
- [x] Host resource component shows CPU, RAM and disk usage from `/api/resources/overview`
- [x] Per-server resource rows show online state, PID, CPU, RSS memory, threads and file descriptors
- [x] `App.tsx` reduced to orchestration: auth, SignalR, navigation and lifecycle actions
- [x] Feature components separated under `frontend/game-panel-web/src/components/`
- [x] Valheim Status & Checks tab exposes runtime, readiness, PID, members and world state
- [x] Shared JSON client sets `Content-Type: application/json` to prevent HTTP 415 body-binding failures
- [x] `docs/SOURCE_KNOWLEDGE_BASE.md` documents architecture, source map, API groups, UI matrix, metrics and safety rules
- [x] Root README and frontend README updated for current implementation

===PHASE_8_RESOURCE_USAGE_AND_REMOVE_LOG_METRICS===
- [x] Removed LogAggregate collector, service, entity and active aggregate/global-metrics API routes
- [x] Removed `LogAggregates` from EF model and applied `20260910130230_DropLogAggregates`
- [x] SQLite backup created before drop: `/home/nh4n/backups/game-server-panel/gamepanel-before-drop-logaggregates-20260910T130248Z.db`
- [x] Added authenticated `GET /api/resources/overview`
- [x] Host component shows CPU, RAM and disk usage
- [x] Per-server component shows online state, PID, CPU, RSS RAM, threads and file descriptors
- [x] Server detail view shows selected server live resource usage
- [x] Resource snapshots are realtime and not persisted in SQLite

===PHASE_9_ASYNC_OPERATION_QUEUE===
- [x] In-memory single-reader operation queue for Start, Stop and Restart
- [x] API returns `202 Accepted` immediately with operation ID
- [x] `GET /api/operations/{id}` reports Queued, Running, Succeeded or Failed
- [x] Duplicate queued/running operation for the same server returns `409 Conflict`
- [x] Background worker resolves scoped runtime and performs lifecycle action outside request timeout
- [x] UI polls operation status every five seconds and relies on SignalR for server state changes
- [x] Start/Stop/Restart buttons no longer wait for slow game boot response
- [x] Queue unit/worker/integration tests added; backend suite 74/74 passed
- [x] Manual API: resource overview HTTP 200; idempotent Start returned 202 then Succeeded for Valheim/PZ
- [x] Manual safety check: Valheim PID `167796` and PZ PID `184252` unchanged before/after queue test
- [x] Feature gap matrix added: `docs/FEATURE_GAP_MATRIX.md`

===CURRENT_STATE_2026-09-10===
- Backend .NET 10 listens on `http://100.82.102.38:5000` under group context including `pzserver`; frontend Vite listens on `0.0.0.0:5173`.
- Valheim `valheim-main.service` and PZ `pzserver-game.service` are adopted by the panel; both were tested through API lifecycle controls.
- Flask PZ web manager remains available at `127.0.0.1:8081` and was verified HTTP 200 after new-panel integration.
- PZ runtime data: config `servertest_new.ini`, SandboxVars `servertest_new_SandboxVars.lua`, RCON `127.0.0.1:27015`, logs under `/home/pzserver/Zomboid/Logs`.
- PZ new-panel UI has responsive navbar and per-server detail tabs: Controls, World Versions, Logs, Config, RCON, Mods, Sandbox.
- Valheim UI has Controls, Status & Checks, World Versions and Logs; status checks include systemd state, readiness, PID, members and backup versions.
- Overview UI renders host CPU/RAM/disk and per-server process usage; detail view shows selected server usage.
- World version API/UI verified for route loading; PZ live backup uses RCON save before copying, Valheim live backup is refused while active; rollback is stopped-only with HEAD protection.
- Latest verification: backend build 0 errors/0 warnings, backend tests 67/67, frontend production build passed; RCON `players`, `servermsg` and `save` returned HTTP 200; frontend JSON body requests include Content-Type to avoid HTTP 415.
- Source knowledge base: `docs/SOURCE_KNOWLEDGE_BASE.md`; current worktree contains the resource replacement and LogAggregates removal pending commit.

===REMAINING_TODO_2026-09-10===
- Review security and behavior of the new PZ config/Sandbox/Mods write paths before production use.
- Add dedicated unit/integration tests for RconClient packet handshake, PzConfigService, PzSandboxService, PzModService, and new API endpoints.
- Decide production process supervision for the new backend/frontend; current dev listeners are manually started.
- Do not disable or migrate Flask until explicit cutover approval.

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
- Config Valheim AdoptExisting: appsettings "GameServers:Valheim" (ServiceName/InstanceKey/ReadinessMarker/BasePort)
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