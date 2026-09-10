# GameServer Panel — CR-00x → CR-01B Context Handoff

Session ID: 20260910_010847_55dfc5
Repo: /home/nh4n/projects/game-server-panel (branch: main, origin: d41nh4n/GameServer)
Người dùng: nh4n — trao đổi bằng tiếng Việt; code/comment giữ tiếng Anh.

---

## 1. Môi trường & hạ tầng (đã xác minh)

| Hạng mục | Giá trị |
|---|---|
| Host | `myserver`, Ubuntu 24.04.4 LTS, kernel 6.8.0-139-generic |
| Tailscale IP | 100.82.102.38 |
| LAN | 192.168.1.21 |
| .NET SDK | 10.0.x tại `~/.dotnet` (export PATH="$HOME/.dotnet:$PATH") |
| Node/Vite | frontend :5173 (đang chạy), backend :5000/:5073 (không luôn chạy) |
| dotnet-ef | cài user-local: `/home/nh4n/.local/share/gamepanel-tools/dotnet-ef-10.0.11/dotnet-ef` (cần `DOTNET_ROOT=/home/nh4n/.dotnet`) |
| SQLite | `backend/Api/gamepanel.db` (thật; backup tại `/home/nh4n/backups/game-server-panel/gamepanel-20260909T192555Z.db`) |
| sudo | nh4n có `NOPASSWD: ALL` (KHÔNG dùng cho service GamePanel; cần dedicated user) |

Submit hội thoại tiết kiệm quota: trả lời ngắn tiếng Việt, không in lại file đã sửa, chỉ sửa đúng phạm vi, không chạy subagent lặp, build/test phần đã sửa, khi sửa fail 2 lần cùng cách thì dừng báo nguyên nhân. Không restart game/world/firewall/editable hệ thống khi chưa được yêu cầu.

---

## 2. Lịch sử CR

| CR | Nội dung | Commit |
|---|---|---|
| (M4 base) | Auth JWT + Tailscale whitelist | — |
| PZ adapter | systemd-backed Project Zomboid (pz-gamectl wrapper, MainPID) | c1db2ef |
| CR-00A | DTO ServerResponse chống lộ password/processId; *.db-wal/*.db-shm vào .gitignore | d595b2c |
| CR-00B | Local auth: User entity, PasswordHasher (PBKDF2/IdentityV3, KHÔNG bcrypt), JWT sub/role, bootstrap admin từ env, rate-limit per-IP login (5/min) | aa59fb7 |
| CR-00C | JWT bảo vệ /api/servers + SignalR; query-token cho /hubs/server; frontend AuthProvider/sessionStorage; [Authorize] trên hub; WebApplicationFactory tests | 98c7cf7 |
| CR-01B | Valheim AdoptExisting code-only (xem §5) | IMPLEMENTED, awaiting review, base 98c7cf7, CHƯA commit |

HEAD hiện tại: `98c7cf7` (đồng bộ origin/main).

---

## 3. CR-00C — JWT / SignalR (ĐÃ xong & đóng)

- GET /api/servers → `RequireAuthorization("authenticated")`; start/stop → `RequireAuthorization("admin")`.
- Explicit policies: "authenticated" (RequireAuthenticatedUser), "admin" (RequireRole Admin).
- SignalR: `[Authorize(Policy="authenticated")]` trên `ServerHub` + `.RequireAuthorization("authenticated")` trên `MapHub`.
- Query-token cho WebSocket: `JwtBearerEvents.OnMessageReceived` nhận `access_token` từ query CHỈ khi `path.StartsWithSegments("/hubs/server")`; `Request.Headers["Authorization"]` có ưu tiên (không dựa `ctx.Token` vì chưa fill); dùng `Request.Query["access_token"]` Count==1; fail-closed khi rỗng/multi.
- `MapHub` options: `CloseOnAuthenticationExpiration=true`, `AllowStatefulReconnects=false`.
- BỘ ĐỆM QUAN TRỌNG: `OnMessageReceived` phải `return Task.CompletedTask;` — `return null` gây NRE 500 trên mọi request.
- Frontend: AuthProvider (auth.ts) — JWT chỉ trong sessionStorage, restore sau refresh, logout, shared client gắn Bearer, 401→logout, 429 thông báo, SignalR chỉ sau auth qua accessTokenFactory, stop khi logout, không log token.
- Bé chia khóa: `Microsoft.AspNetCore.Mvc.Testing` phiên bản 10.0.11; usermization `: X<Y>` chứ không `extends`.

---

## 4. CR-00B — Local Auth (đã xong)

- `User { Id, Username, NormalizedUsername, PasswordHash(255), Role }`; unique index `NormalizedUsername` COLLATE NOCASE.
- Migration: `20260909120000_AddLocalUsers`.
- JWT: sub=userId, role từ DB; HS256; key từ `Jwt:Secret` (không hardcode); nếu `CHANGE_ME` → throw.
- Bootstrap admin: chỉ khi Users rỗng; đọc `GAMEPANEL_BOOTSTRAP_ADMIN_USERNAME/PASSWORD` qua IConfiguration + fallback env; fail-closed nếu thiếu; không log password.
- Rate limit: `LoginRateLimitPolicy` (IRateLimiterPolicy) partition theo `RemoteIpAddress`, fallback unknown-ip, 5/min, HTTP 429.
- 20 test LocalAuth.

---

## 5. CR-01B — Valheim AdoptExisting (ĐANG làm, code-only, CHƯA commit)

### 5.1 Mục tiêu / ràng buộc
- Nối Valheim Dedicated hiện có (systemd `valheim-main.service`) vào panel. KHÔNG cài lại/không di chuyển/không tạo world/không đổi password/không chạy executable trực tiếp.
- systemd là chủ sở hữu duy nhất runtime.
- PZ hiện có (`pzserver-game.service`) KHÔNG được thay đổi hành vi.

### 5.2 Runtime facts đã xác minh (read-only, § discovery)
- `valheim-main.service`: active/running, MainPID=72028 (đã nhận trước đó), InvocationID `5284c2b1c2e74e9887391d5fce4a905d`, User/Group `valheim:valheim`, Restart=on-failure, KillSignal=SIGINT, TimeoutStopSec=120, NoNewPrivileges=true, PrivateTmp=true, UMask=0027, UnitFileState=disabled.
- WorkingDirectory: `/srv/gamepanel/instances/valheim-main/server`
- Launcher `/usr/local/libexec/valheim-main-start`: root:root 0755.
- Env file `/etc/gamepanel/valheim-main.env`: root:valheim 0640 — chỉ xác nhận các KEY VALHEIM_SERVER_NAME/WORLD_NAME/PASSWORD/PORT/PUBLIC tồn tại, KHÔNG in giá trị.
- Paths: server/data/backups/logs riêng biệt, valheim:valheim 0750; runtime 0700; binary valheim_server.x86_64 valheim:valheim 0775; steamcmd root:root 0755.
- UDP 2456 + 2457 bind bởi PID 72028; 2458 không dùng. UFW: rule `2456:2457/udp on tailscale0` (IPv4+v6); default deny; không có WAN rule cho Valheim.
- Readiness marker: `Game server connected` có trong journal đúng InvocationID hiện tại.
- Version: l-1.0.7, network 39. Steam App ID 896660 (server), 892970 (game).
- Client đã connect thành công qua 100.82.102.38:2456 (2026-09-09).

### 5.3 Kiến trúc mới đã viết (Infrastructure/GameServers)
- `ISystemdRuntimeDriver` + `SystemdRuntimeDriver` + `SystemdUnitDefinition` + `SystemdUnitState`
  - allowlist unit từ cấu hình nội bộ; RequireUnit ném nếu unit lạ → fail-closed, KHÔNG chạy lệnh.
  - Chỉ đọc `ActiveState`, `SubState`, `MainPID`, `InvocationID` qua `systemctl show ... --no-pager`.
  - Control qua wrapper allowlist (không shell); Valheim control disabled; PZ dùng `sudo -n -u ... /usr/local/sbin/pz-gamectl start|stop|restart`.
  - Gần đây bổ sung: command timeout nội bộ (default 5s, cấu hình qua ctor optional `TimeSpan?`) + link cancellation caller; runner ném exception / timeout → fail-closed (`QuerySucceeded=false`, control=false). Caller cancel → propagate.
- `IValheimRuntimeProbe` + `ValheimRuntimeProbe`
  - kiểm tra cgroup PID thuộc unit (đọc `/proc/{pid}/cgroup`), UDP ownership qua `ss -H -lunp` (cả 2 port cùng PID), readiness marker qua `journalctl -u <unit> _SYSTEMD_INVOCATION_ID=<id> --grep=<marker> -n 1 -o cat`.
  - KHÔNG đọc env secret, KHÔNG đọc cmdline, KHÔNG dùng shell.
  - Bổ sung command timeout nội bộ (default 5s) + cancellation; exception/timeout → false.
- `ValheimRuntimeStrategy`
  - InspectAsync: chỉ chạy khi `ProvisioningMode==AdoptExisting && RuntimeType==Systemd && RuntimeId != null`.
  - Systemd state fail → Unknown; inactive → Stopped; active nhưng chưa running → Starting; chạy full → check cgroup + 2 port + marker đúng invocation. Cả 3 đúng → Running/ready; thiếu bất kỳ → Starting/not-ready.
  - Bổ sung try/catch quanh driver call và probe-task; caller cancel → propagate; exception → fail-closed (Unknown hoặc not-ready). Kiểm tra port hợp lệ 1..65534.
- `ValheimProvider` (adapter AdoptExisting)
  - InspectAsync → strategy. Start/Stop LUÔN trả `(false, snapshot)` — fail-closed cho tới CR privilege-wrapper, không gọi systemd control.

### 5.4 Adapter interface refactor (breaking, internal)
- `IGameServerAdapter` đổi từ PID-oriented sang:
  - `Task<GameServerRuntimeSnapshot> InspectAsync(ServerInstance, ct)`
  - `Task<GameServerActionResult> StartAsync(ServerInstance, ct)`
  - `Task<GameServerActionResult> StopAsync(ServerInstance, ct)`
- `GameServerStatus` mở rộng: Unknown, Stopped, Starting, Running, Stopping.
- `GameServerRuntimeSnapshot { Status, int? MainPid, bool Ready }`.
- `ProjectZomboidAdapter` đã refactor sang dùng `ISystemdRuntimeDriver` (shared driver, giữ wrapper+sudo+bounded wait+/proc/không shell). Có regression tests.
- `IGameServerRuntime` (Application) thêm `CancellationToken ct` cho GetAllAsync (và Start/Stop — đang nửa chừng: giao diện đã thêm ct, manager ANMEN gọi adapter có ct; cần kiểm tra build).

### 5.5 GameServerManager thay đổi
- `GetAllAsync`: inspect toàn bộ server (kể cả DB Stopped có runtime AdoptExisting); áp snapshot → reconcile; Unknown→giữ nguyên; rồi 1 lần SaveChanges + 1 SignalR nếu có thay đổi. Ready/Lỗi all đồng bộ từ snapshot (không tin stale DB).
- `StartAsync`: inspect trước; nếu đang Running→false; đặt Starting; gọi adapter; nếu snapshot Unknown→fallback Stopped; áp snapshot; trả result.Success. Không kẹt `Starting` khi thất bại.
- `StopAsync`: inspect trước; nếu Stopped→false; đặt Stopping; gọi; snapshot Unknown→giữ current; áp snapshot; trả result.Success. Không ghi bậy `Stopped` khi thất bại.
- `ApplySnapshot` map Status/ProcessId/Ready từ snapshot.

### 5.6 Domain/Database
- `ServerInstance` thêm: InstanceKey, ProvisioningMode (Managed/AdoptExisting), RuntimeType (Process/Systemd), RuntimeId, InstallationPath, DataPath, BackupPath, SteamAppId, Protocol (Udp/Tcp), ReadinessMarker, Ready.
- Mặc định record cũ: Managed + Process + Ready=false → KHÔNG tự bind vào valheim-main.service, KHÔNG gọi systemd, KHÔNG chạy executable.
- Migration mới: `20260909235900_AddRuntimeAdoption` — additive nullable/default; NO InsertData/UpdateData; không tạo/chuyển đổi record Valheim demo.
- Indexes: unique filtered `InstanceKey` (IS NOT NULL); unique filtered `(RuntimeType, RuntimeId)` (RuntimeId IS NOT NULL). Designer + ModelSnapshot đã cập nhật.
- CHƯA áp dụng migration vào gamepanel.db thật.

### 5.7 API / Frontend
- `ServerResponse` chỉ thêm: `instanceKey`, `provisioningMode`, `runtimeType`, `ready`. KHÔNG lộ RuntimeId, paths, MainPID/ProcessId, InvocationID, password, env, arguments.
- `frontend/game-panel-web/src/auth.ts` type `Server` đã thêm 4 field.
- `appsettings.example.json`: `GameServers:Valheim` → `ServiceName`, `InstanceKey`, `ReadinessMarker`, `BasePort` (thay ExecutablePath cũ). KHÔNG sửa machine appsettings.

### 5.8 Tests (backend Test/)
- `SystemdRuntimeDriverTests`: fixed-property query; unknown unit no-command; Valheim control disabled; PZ wrapper parity; runner-exception fail-closed; internal timeout fail-closed; caller cancellation propagate.
- `ValheimRuntimeStrategyTests`: ready; thiếu marker→Starting; sai cgroup→fail-closed; legacy Managed+Process record → KHÔNG gọi systemd/control (0 StateCalls/ControlCalls); RuntimeId ngoài allowlist → fail-closed KHÔNG chạy lệnh; Valheim start/stop không gọi control.
- `ValheimRuntimeProbeTests`: cgroup/port/journal không cmdline; timeout fail-closed; caller cancel propagate; port phải cùng PID.
- `GameServerManagerAdoptionTests`: reconcile DB Stopped→Runtime Running; start failure không kẹt Starting; stop failure không ghi Stopped.
- `RuntimeAdoptionMigrationTests`: cột/index; InstanceKey unique; (RuntimeType,RuntimeId) unique.
- `ProjectZomboidAdapterRegressionTests`: PZ start/stop dùng shared driver.
- `ServerResponseContractTests` + `IntegrationAuthTests`: không lộ RuntimeId/paths/PID/password.

### 5.9 Trạng thái hiện tại (tại thời điểm handoff)
- Backend build: PASS (trước khi bổ sung ct cho GetAll — cần chạy lại `dotnet build` + `dotnet test`).
- Test trước đó: 59/59 PASS (trước khi thêm các test mới timeout/cancel/legacy/allowlist).
- Frontend `npm run build`: PASS.
- Chưa stage/commit/push. `VALHEIM_EXISTING_RUNTIME_HANDOFF.md` + `promt_migration_pz.MD` vẫn untracked, KHÔNG sửa.

### 5.10 Còn phải làm CR-01B
- Chạy lại: `dotnet build`, `dotnet test` (đếm chính xác), `npm run build`, `git diff --check`, secret scan.
- Verify `IGameServerRuntime` + `GameServerManager` compile với ct tham số mới; endpoint Program.cs có gọi `r.GetAllAsync()` / `StartAsync(id)` / `StopAsync(id)` cần cập nhật nếu signature đổi.
- Những điểm đang sửa dở khi handoff: built-in command timeout (driver + probe), cancellation, exception fail-closed, `IGameServerRuntime` ct . — cần chạy build để biết trạng thái chính xác.

---

## 6. Việc triển khai ANM chia (sau CR-01B, chưa làm)

- Áp migration `AddRuntimeAdoption` cho gamepanel.db thật (backup trước; read-only kiểm tra pending `20260909120000_AddLocalUsers` đã áp; migration này thêm mới để áp).
- Tạo record AdoptExisting `valheim-main` (cần duyệt: record mới hay chuyển đổi Valheim demo — khuyến nghị record mới).
- CR privilege-wrapper: dedicated user `gamepanel` (KHÔNG cho vào group valheim — đọc được secret/data), wrapper root-owned allowlist: `systemctl start/stop/restart valheim-main.service`, `systemctl show` fixed properties, `journalctl` đúng unit.
- Cho phép panel start/stop Valheim chỉ sau privilege wrapper được duyệt; hiện fail-closed.
- Chỉ đọc cổng 2456-2457 theo runtime bằng chứng; không tự mở cổng mới.
- Update/backup Valheim: dừng graceful → confirm process+port dừng → backup data + timestamp/checksum → `steamcmd +app_update 896660 validate` (dashboard: `/usr/games/steamcmd`, env HOME=runtime) → start → chờ readiness invocation mới → test.

---

## 7. Lệnh hữu ích (tái sử dụng)

```bash
export PATH="$HOME/.dotnet:$PATH"; export DOTNET_ROOT="$HOME/.dotnet"
# build/test
cd backend && dotnet build && dotnet test
# frontend
cd frontend/game-panel-web && npm run build
# migration phiên (tool user-local)
$HOME/.local/share/gamepanel-tools/dotnet-ef-10.0.11/dotnet-ef --version
# read-only kiểm tra migration đã áp (python3 sqlite3, mode=ro, query_only)
python3 - <<'EOF'
from pathlib import Path
import sqlite3, re, hashlib
p=Path("backend/Api/gamepanel.db").resolve(strict=True)
c=sqlite3.connect(p.as_uri()+"?mode=ro",uri=True); c.execute("PRAGMA query_only=ON")
print([r[0] for r in c.execute("SELECT MigrationId FROM __EFMigrationsHistory")])
EOF
```

Note: chạy backend bằng `dotnet run --no-build --urls=http://0.0.0.0:5000` (bind 0.0.0.0 để truy cập qua Tailscale). Bootstrap admin đọc env GAMEPANEL_BOOTSTRAP_ADMIN_USERNAME/PASSWORD nhập bằng `read -s` trong SSH shell (không gửi credential qua chat).

---

## 8. Quyết định đang chờ duyệt (CR-01A/01B + triển khai)
1. Cho phép trích shared systemd driver và dùng chung PZ + Valheim? (đã làm trong CR-01B code — cần xác nhận lại).
2. Schema: thêm adoption fields vào ServerInstance (đang làm) hay entity riêng.
3. Metadata API: chỉ instanceKey/provisioningMode/runtimeType/ready (đang làm).
4. Record Valheim: record mới hay chuyển đổi demo.
5. Service `valheim-main.service` hiện disabled khi boot — có enable khi triển khai không.
6. CR-01B chỉ read/status; sau review có cho start/stop qua wrapper không.
7. Dedicated backend user: `gamepanel` hay khác.
8. Credential/JWT secret: luôn nhập tương tác `read -s`, không qua chat/history/source.

---

## 9. Các file quan trọng
- Program.cs, AppSettings.cs, ServerResponse.cs, LoginContracts.cs
- Domain/Entities/ServerInstance.cs (+ User.cs)
- Infrastructure/Data/AppDbContext.cs, Migrations/
- Infrastructure/GameServers/: IGameServerAdapter, GameServerAdapterFactory, ICommandRunner, ProcessCommandRunner, SystemdRuntimeDriver, ValheimProvider, ValheimRuntimeStrategy, ValheimRuntimeProbe, ProjectZomboidAdapter
- Infrastructure/Services/GameServerManager.cs
- Application/Interfaces/IGameServerRuntime.cs
- Infrastructure/Hubs/ServerHub.cs
- Test/GamePanel.ContractTests.csproj + các *Tests.cs
- frontend/game-panel-web/src/auth.ts, App.tsx
