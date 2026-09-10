using System.Text;
using GamePanel.Api;
using GamePanel.Api.CoreContracts;
using GamePanel.Api.Middleware;
using GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Auth;
using GamePanel.Infrastructure.Data;
using GamePanel.Infrastructure.GameServers;
using GamePanel.Infrastructure.Hubs;
using GamePanel.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Options models đọc từ appsettings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.Section));
builder.Services.Configure<TailscaleSettings>(builder.Configuration.GetSection(TailscaleSettings.Section));
builder.Services.Configure<ValheimSettings>(builder.Configuration.GetSection(ValheimSettings.Section));
builder.Services.Configure<ProjectZomboidSettings>(builder.Configuration.GetSection(ProjectZomboidSettings.Section));

// Auth service (JWT) + local user password hasher
builder.Services.AddScoped<IPasswordHasher<User>>(_ => new PasswordHasher<User>());
builder.Services.AddScoped<IAuthService, JwtAuthService>();

// Persistence & SignalR
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=gamepanel.db"));

// Game server runtime providers
builder.Services.AddScoped<GameServerManager>();
builder.Services.AddScoped<IGameServerRuntime>(sp => sp.GetRequiredService<GameServerManager>());
builder.Services.AddScoped<ICommandRunner>(_ => new ProcessCommandRunner());
builder.Services.AddScoped<ISystemdRuntimeDriver>(sp =>
{
    var config = builder.Configuration;
    var runner = sp.GetRequiredService<ICommandRunner>();
    var valheimService = config["GameServers:Valheim:ServiceName"]
        ?? "valheim-main.service";
    var valheimControl = config["GameServers:Valheim:ControlExecutable"]
        ?? "/usr/bin/systemctl";
    var valheimSudoUser = config["GameServers:Valheim:SudoUser"] ?? "";
    var pzService = config["GameServers:ProjectZomboid:ServiceName"]
        ?? "pzserver-game.service";
    var pzControl = config["GameServers:ProjectZomboid:ControlExecutable"]
        ?? "/usr/local/sbin/pz-gamectl";
    var pzSudoUser = config["GameServers:ProjectZomboid:SudoUser"] ?? "";
    return new SystemdRuntimeDriver(runner, new[]
    {
        new SystemdUnitDefinition(
            valheimService,
            valheimControl,
            true,
            valheimSudoUser,
            true),
        new SystemdUnitDefinition(
            pzService,
            pzControl,
            true,
            pzSudoUser,
            true),
    });
});
builder.Services.AddScoped<IValheimRuntimeProbe, ValheimRuntimeProbe>();
builder.Services.AddScoped<ValheimRuntimeStrategy>();
builder.Services.AddScoped<ValheimProvider>(sp =>
    new ValheimProvider(
        sp.GetRequiredService<ValheimRuntimeStrategy>(),
        sp.GetRequiredService<ISystemdRuntimeDriver>()));
builder.Services.AddScoped<IRconClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new RconClient(
        config["GameServers:ProjectZomboid:RconHost"] ?? "127.0.0.1",
        int.TryParse(config["GameServers:ProjectZomboid:RconPort"], out var port) ? port : 27015,
        config["GameServers:ProjectZomboid:RconPassword"] ?? "");
});
builder.Services.AddScoped<ProjectZomboidAdapter>();
builder.Services.AddScoped<IGameServerAdapterFactory, GameServerAdapterFactory>();

// PZ extended services
builder.Services.AddSingleton<PzConfigService>();
builder.Services.AddSingleton<PzSandboxService>();
builder.Services.AddSingleton<PzLogService>();
builder.Services.AddSingleton<PzModService>();
builder.Services.AddScoped<PzOpsService>();
builder.Services.AddScoped<PzWorldBackupService>();
builder.Services.AddScoped<ValheimMonitoringService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<SystemEventService>();
builder.Services.AddScoped<LogAggregateService>();
builder.Services.AddScoped<GlobalMetricsService>();
if (builder.Configuration.GetValue("Metrics:EnableLogAggregateCollector", false))
    builder.Services.AddHostedService<LogAggregateCollector>();

builder.Services.AddSignalR();

// Login rate limiting partitionowane per client IP (5 prób / 1 minuta).
// AddPolicy + IRateLimiterPolicy (partycja po RemoteIpAddress), NIE globalny
// AddFixedWindowLimiter — każdy IP ma własny licznik. Odrzucenie → HTTP 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", new LoginRateLimitPolicy());
});

builder.Services.AddOpenApi();
builder.Services.AddCors(p => p
    .AddPolicy("AllowReact", policy => policy
        .WithOrigins(
            "http://localhost:5173",
            "http://127.0.0.1:5173",
            "http://100.82.102.38:5173")
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials()));

// JWT Bearer auth. Key từ appsettings "Jwt:Secret" (không hardcode).
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Missing config Jwt:Secret");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "GamePanelApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "GamePanelClient";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
        };

        // SignalR (WebSocket) nie może ustawić nagłówka Authorization, więc token
        // przekazujemy (wyłącznie dla /hubs/server) jako access_token w query string.
        // Path jest sprawdzany po SEGMENTACH (nie prefix substring), więc /hubs/server-evil
        // NIE dostaje query-token handlingu. Request.Query jest już zdekodowany przez
        // ASP.NET (bez ręcznego URL-decodu). Jeśli nagłówek Authorization jest obecny,
        // ma pierwszeństwo — nie nadpisujemy go tokenem z query.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = (ctx) =>
            {
                var req = ctx.HttpContext?.Request;
                var path = req?.Path;
                // Uwaga: OnMessageReceived wykonuje się ZANIM handler domyślnie
                // wyekstrahuje nagłówek Authorization, więc ctx.Token NIE jest
                // wiarygodnym źródłem obecności headera — sprawdzamy header wprost.
                var onHub = path != null && path.HasValue &&
                    path.Value.StartsWithSegments(PathString.FromUriComponent("/hubs/server"));
                var authHeader = req?.Headers?["Authorization"];
                var queryValues = req?.Query["access_token"];
                var token = HubAccessTokenDecision.Resolve(onHub, authHeader, queryValues);
                if (token != null)
                {
                    ctx.Token = token;
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("admin", p => p.RequireRole(UserRole.Admin.ToString()));
    options.AddPolicy("authenticated", p => p.RequireAuthenticatedUser());
});

var app = builder.Build();

// Ensure DB schema + seed 2 server nếu trống
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    if (!db.ServerInstances.Any())
    {
        db.ServerInstances.AddRange(
            new ServerInstance
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Valheim Main Server",
                GameType = "Valheim",
                Type = GameServerType.Valheim,
                Status = ServerStatus.Stopped,
                Port = 2456,
                WorldName = "Dedicated",
                Password = "viking",
                RuntimeType = ServerRuntimeType.Systemd,
                ProvisioningMode = ProvisioningMode.AdoptExisting,
                RuntimeId = "valheim-main.service",
                ReadinessMarker = "Game server connected",
            },
            new ServerInstance
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Minecraft Server",
                GameType = "Minecraft",
                Type = GameServerType.Minecraft,
                Status = ServerStatus.Stopped,
                Port = 25565,
                WorldName = "world",
                Password = "",
            },
            new ServerInstance
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Project Zomboid Main Server",
                GameType = "ProjectZomboid",
                Type = GameServerType.ProjectZomboid,
                Status = ServerStatus.Stopped,
                Port = 16261,
                WorldName = "servertest_new",
                Password = "",
                RuntimeType = ServerRuntimeType.Systemd,
                ProvisioningMode = ProvisioningMode.AdoptExisting,
                RuntimeId = "pzserver-game.service",
                ReadinessMarker = "Server started",
            });
        await db.SaveChangesAsync();
    }

    // Bootstrap pierwszego admina (tylko gdy Users pusta). Credentials przez
    // IConfiguration (klucze GAMEPANEL_BOOTSTRAP_ADMIN_USERNAME/PASSWORD) — dzięki
    // temu testy mogą je wstrzyknąć AddInMemoryCollection, a produkcja czyta środowisko
    // procesu (Aspire mapuje zmienne środowiskowe na konfigurację). NIE logujemy hasła.
    var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
    if (await auth.UsersNeedBootstrapAsync())
    {
        var cfgUser = builder.Configuration["GAMEPANEL_BOOTSTRAP_ADMIN_USERNAME"] ?? "";
        var cfgPass = builder.Configuration["GAMEPANEL_BOOTSTRAP_ADMIN_PASSWORD"] ?? "";
        // Fallback na zmienne środowiskowe (jeśli nie przeszły przez IConfiguration).
        var envUser = Environment.GetEnvironmentVariable("GAMEPANEL_BOOTSTRAP_ADMIN_USERNAME") ?? "";
        var envPass = Environment.GetEnvironmentVariable("GAMEPANEL_BOOTSTRAP_ADMIN_PASSWORD") ?? "";
        var bootUser = !string.IsNullOrEmpty(cfgUser) ? cfgUser : envUser;
        var bootPass = !string.IsNullOrEmpty(cfgPass) ? cfgPass : envPass;
        await auth.EnsureBootstrapAdminAsync(bootUser, bootPass);
    }
}

// Middleware pipeline: Routing → CORS → Tailscale (tùy chọn) → Auth → Authorization
app.UseRouting();
app.UseCors("AllowReact");
app.UseRateLimiter();
app.UseMiddleware<TailscaleMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();

app.MapHub<ServerHub>("/hubs/server", opts =>
{
    opts.CloseOnAuthenticationExpiration = true;
    opts.AllowStatefulReconnects = false;
}).RequireAuthorization("authenticated");

app.MapGet("/api/servers", async (IGameServerRuntime r) =>
{
    var all = await r.GetAllAsync();
    var response = all.Select(ServerResponse.FromDomain).ToArray();
    return Results.Ok(response);
}).RequireAuthorization("authenticated");

// Login: realna autoryzacja lokalnego użytkownika. Rola/sub z trwałej encji User.
// Ogólny 401 dla nieistniejącego użytkownika i złego hasła — bez ujawniania przyczyny.
var loginRoute = app.MapPost("/api/auth/login", async (LoginRequest body, IAuthService auth) =>
{
    if (body == null || string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
        return Results.BadRequest("Username and password are required.");

    var user = await auth.AuthenticateAsync(body.Username, body.Password);
    if (user == null)
        return Results.Unauthorized();

    var token = auth.GenerateToken(user.Id, user.Role);
    return Results.Ok(new LoginResponse(token.Value, token.ExpiresAt));
}).RequireRateLimiting("login");

app.MapPost("/api/servers/{id}/start", async (Guid id, IGameServerRuntime r, AuditLogService audit, HttpContext http) =>
{
    var success = await r.StartAsync(id);
    await audit.RecordAsync(http.User, AuditActions.StartServer, id, success, success ? "OK" : "START_FAILED", new { action = "start" });
    return success ? Results.Ok() : Results.BadRequest("Cannot start");
}).RequireAuthorization("admin");

app.MapPost("/api/servers/{id}/stop", async (Guid id, IGameServerRuntime r, AuditLogService audit, HttpContext http) =>
{
    var success = await r.StopAsync(id);
    await audit.RecordAsync(http.User, AuditActions.StopServer, id, success, success ? "OK" : "STOP_FAILED", new { action = "stop" });
    return success ? Results.Ok() : Results.BadRequest("Cannot stop");
}).RequireAuthorization("admin");

app.MapGet("/api/servers/{id}/logs", async (Guid id, int? lines, AppDbContext db, IServiceProvider sp) =>
{
    var server = await db.ServerInstances.FirstOrDefaultAsync(x => x.Id == id);
    if (server == null) return Results.NotFound();
    if (server.GameType != "ProjectZomboid")
        return Results.BadRequest("Logs only available for Project Zomboid");

    var adapter = sp.GetRequiredService<ProjectZomboidAdapter>();
    var content = await adapter.GetLogsAsync(lines ?? 200);
    return Results.Ok(new { content, lines = lines ?? 200 });
}).RequireAuthorization("authenticated");

// ─── RCON ───

app.MapPost("/api/pz/rcon/command", async (RconRequest body, IRconClient rcon) =>
{
    try
    {
        var output = await rcon.SendCommandAsync(body.Command);
        return Results.Ok(new { success = true, output });
    }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapPost("/api/pz/rcon/save", async (IRconClient rcon) =>
{
    try { return Results.Ok(new { success = true, output = await rcon.SendCommandAsync("save"), message = "World saved" }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapPost("/api/pz/rcon/broadcast", async (RconBroadcast body, IRconClient rcon) =>
{
    try { return Results.Ok(new { success = true, output = await rcon.SendCommandAsync($"servermsg \"{body.Message}\"") }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapPost("/api/pz/rcon/kick", async (RconKick body, IRconClient rcon) =>
{
    try { return Results.Ok(new { success = true, output = await rcon.SendCommandAsync($"kickuser \"{body.Username}\" -r \"{body.Reason}\"") }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapGet("/api/pz/rcon/players", async (IRconClient rcon) =>
{
    try { return Results.Ok(new { success = true, output = await rcon.SendCommandAsync("players") }); }
    catch (Exception e) { return Results.Problem(e.Message, statusCode: StatusCodes.Status502BadGateway); }
}).RequireAuthorization("authenticated");

app.MapGet("/api/pz/mods", (PzModService mods) =>
{
    try { return Results.Ok(new { success = true, mods = mods.Read() }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapPut("/api/pz/mods", (PzModUpdateBody body, PzModService mods) =>
{
    try
    {
        var backup = mods.Update(new PzModUpdate(body.WorkshopIds, body.ModIds));
        return Results.Ok(new { success = true, backup, mods = mods.Read() });
    }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

// ─── Config INI ───

app.MapGet("/api/pz/config", (PzConfigService cfg) =>
{
    try { return Results.Ok(new { success = true, file = cfg.IniPath, config = cfg.Parse() }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapGet("/api/pz/config/raw", (PzConfigService cfg) =>
{
    try { return Results.Ok(new { success = true, file = cfg.IniPath, content = cfg.ReadRaw() }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapPut("/api/pz/config", (ConfigUpdateBody body, PzConfigService cfg) =>
{
    try
    {
        var backup = cfg.WriteUpdates(body.Config);
        return Results.Ok(new { success = true, message = "Config updated", backup, config = cfg.Parse() });
    }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapPost("/api/pz/config/backup", (PzConfigService cfg) =>
{
    try { return Results.Ok(new { success = true, backup = cfg.WriteUpdates(new()) }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

// ─── SandboxVars Lua ───

app.MapGet("/api/pz/sandbox/config", (PzSandboxService sandbox) =>
{
    try
    {
        var config = sandbox.GetConfig();
        var editable = config.Groups.Sum(g => g.Fields.Count(f => f.Editable));
        var total = config.Groups.Sum(g => g.Fields.Count);
        return Results.Ok(new { success = true, file = "/home/pzserver/Zomboid/Server/servertest_new_SandboxVars.lua", groups = config.Groups, editable_count = editable, detected_count = total });
    }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapPost("/api/pz/sandbox/save", async (SandboxSaveBody body, PzSandboxService sandbox) =>
{
    try
    {
        List<string> backups = new();
        foreach (var change in body.Values)
        {
            var backup = sandbox.SaveValue(change.Section, change.Key, change.Value);
            backups.Add(backup);
        }
        var config = sandbox.GetConfig();
        return Results.Ok(new { success = true, message = "Sandbox settings saved", backups, groups = config.Groups });
    }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

// ─── Logs (filesystem) ───

app.MapGet("/api/pz/logs/list", (PzLogService log) =>
{
    try { return Results.Ok(new { success = true, files = log.ListFiles() }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapGet("/api/pz/logs/read", (string? filename, PzLogService log) =>
{
    if (string.IsNullOrEmpty(filename)) return Results.BadRequest(new { success = false, error = "Missing filename" });
    try { return Results.Ok(new { success = true, filename, content = log.ReadFile(filename) }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

// ─── Ops dashboard ───

app.MapGet("/api/pz/ops/health", async (AppDbContext db, PzOpsService ops) =>
{
    try
    {
        var pzServer = await db.ServerInstances.FirstOrDefaultAsync(x => x.GameType == "ProjectZomboid");
        var pid = pzServer?.ProcessId ?? 0;
        var metrics = await ops.GetMetricsAsync(pid);
        return Results.Ok(new { success = true, metrics });
    }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapGet("/api/valheim/monitor", async (ValheimMonitoringService monitoring) =>
{
    try { return Results.Ok(new { success = true, monitor = await monitoring.GetSnapshotAsync() }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapGet("/api/valheim/logs", async (int? lines, ValheimMonitoringService monitoring) =>
{
    try { return Results.Ok(new { success = true, content = await monitoring.GetLogsAsync(lines ?? 200), lines = Math.Clamp(lines ?? 200, 1, 1000) }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapGet("/api/valheim/members", (ValheimMonitoringService monitoring) =>
    Results.Ok(new { success = true, members = monitoring.ReadMembers() }))
    .RequireAuthorization("authenticated");

app.MapPost("/api/valheim/members", ([FromBody] ValheimMemberChange body, ValheimMonitoringService monitoring) =>
{
    try { monitoring.AddMember(body.Id, body.Role); return Results.Ok(new { success = true, members = monitoring.ReadMembers() }); }
    catch (ArgumentException e) { return Results.BadRequest(new { success = false, error = e.Message }); }
}).RequireAuthorization("admin");

app.MapDelete("/api/valheim/members", ([FromBody] ValheimMemberChange body, ValheimMonitoringService monitoring) =>
{
    try { monitoring.RemoveMember(body.Id, body.Role); return Results.Ok(new { success = true, members = monitoring.ReadMembers() }); }
    catch (ArgumentException e) { return Results.BadRequest(new { success = false, error = e.Message }); }
}).RequireAuthorization("admin");

app.MapGet("/api/valheim/backups", (ValheimMonitoringService monitoring) =>
    Results.Ok(new { success = true, backups = monitoring.ListBackups() }))
    .RequireAuthorization("authenticated");

app.MapPost("/api/valheim/backups", async (ValheimMonitoringService monitoring) =>
{
    try { return Results.Ok(new { success = true, backup = await monitoring.CreateBackupAsync() }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { success = false, error = e.Message }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapPost("/api/valheim/backups/{version}/rollback", async (string version, ValheimMonitoringService monitoring, CancellationToken ct) =>
{
    try { return Results.Ok(new { success = true, headBackup = await monitoring.RollbackAsync(version, ct) }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { success = false, error = e.Message }); }
    catch (Exception e) { return Results.BadRequest(new { success = false, error = e.Message }); }
}).RequireAuthorization("admin");

app.MapGet("/api/pz/backups", (PzWorldBackupService backups) =>
    Results.Ok(new { success = true, backups = backups.List() }))
    .RequireAuthorization("authenticated");

app.MapPost("/api/pz/backups", async (PzWorldBackupService backups, CancellationToken ct) =>
{
    try { return Results.Ok(new { success = true, backup = await backups.CreateAsync(ct) }); }
    catch (Exception e) { return Results.Conflict(new { success = false, error = e.Message }); }
}).RequireAuthorization("admin");

app.MapPost("/api/pz/backups/{version}/rollback", async (string version, PzWorldBackupService backups, CancellationToken ct) =>
{
    try { return Results.Ok(new { success = true, headBackup = await backups.RollbackAsync(version, ct) }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { success = false, error = e.Message }); }
    catch (Exception e) { return Results.BadRequest(new { success = false, error = e.Message }); }
}).RequireAuthorization("admin");

app.MapGet("/api/audit", async (Guid? serverId, int? limit, AppDbContext db) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 500);
    var query = db.AuditLogs.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).AsQueryable();
    if (serverId.HasValue) query = query.Where(x => x.ServerInstanceId == serverId.Value).OrderByDescending(x => x.CreatedAtUtc);
    return Results.Ok(await query.Take(take).ToListAsync());
}).RequireAuthorization("admin");

app.MapGet("/api/events", async (Guid? serverId, int? limit, SystemEventService events) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 500);
    return Results.Ok(await events.Query(serverId).Take(take).ToListAsync());
}).RequireAuthorization("authenticated");

app.MapGet("/api/aggregates", async (Guid? serverId, DateTime? fromUtc, DateTime? toUtc, int? limit, LogAggregateService aggregates) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 1000);
    return Results.Ok(await aggregates.Query(serverId, fromUtc, toUtc).Take(take).ToListAsync());
}).RequireAuthorization("authenticated");

app.MapGet("/api/metrics/global", async (GlobalMetricsService metrics, CancellationToken ct) =>
{
    try { return Results.Ok(await metrics.GetAsync(ct)); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.Run();

// Eksponuje kompilator-dependentny punkt wejścia Minimal API jako klasę, dzięki
// czemu WebApplicationFactory<Program> może uruchomić pipeline w pamięci (TestServer)
// bez otwierania portu. Produkcyjne top-level statements pozostają nietknięte.
public partial class Program { }