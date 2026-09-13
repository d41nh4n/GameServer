using System.Diagnostics;
using System.Text;
using System.Security.Claims;
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
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((_, _, logger) =>
{
    logger.MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(new RenderedCompactJsonFormatter());
    var enabled = builder.Configuration.GetValue<bool>("Observability:Elasticsearch:Enabled");
    var endpoint = builder.Configuration["Observability:Elasticsearch:Uri"];
    if (enabled && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
    {
        logger.WriteTo.Logger(metrics => metrics
            .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("MetricEvent"))
            .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(uri)
            {
                IndexFormat = "gamepanel-metrics-{0:yyyy.MM.dd}",
                AutoRegisterTemplate = true,
                NumberOfShards = 1,
                NumberOfReplicas = 0,
                FailureCallback = (e, ex) => Console.Error.WriteLine("Elasticsearch metric sink failed: " + e.MessageTemplate.Text),
            }));
    }
});

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
builder.Services.AddSingleton<PrivilegedBrokerClient>();
builder.Services.AddScoped<ISystemdRuntimeDriver>(sp =>
{
    var runner = sp.GetRequiredService<ICommandRunner>();
    const string valheimService = "valheim-main.service";
    const string valheimControl = "/usr/bin/systemctl";
    const string valheimSudoUser = "";
    const string pzService = "pzserver-game.service";
    const string pzControl = "/usr/local/sbin/pz-gamectl";
    const string pzSudoUser = "";
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
    },
    broker: sp.GetRequiredService<PrivilegedBrokerClient>());
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
builder.Services.AddScoped<ValheimModService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ThunderstoreService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<SystemEventService>();
builder.Services.AddScoped<ResourceMetricsService>();
builder.Services.AddSingleton<MetricObservabilityService>();
builder.Services.AddScoped<ValheimSettingsApplyService>();
builder.Services.AddHostedService<AuditLogRetentionService>();
builder.Services.AddSingleton<IGameControlProtocol>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var client = new HttpClient
    {
        BaseAddress = new Uri(config["GameServers:Valheim:ControlProtocol:BaseUrl"] ?? "http://127.0.0.1:27666"),
        Timeout = TimeSpan.FromSeconds(3),
    };
    return new ValheimControlProtocol(client, config);
});
builder.Services.AddSingleton<ValheimControlActionService>();
builder.Services.AddSingleton<ServerOperationQueue>();
builder.Services.AddHostedService<ServerOperationWorker>();
builder.Services.AddHostedService<ServerStatusHeartbeat>();
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
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnostics, http) =>
    {
        diagnostics.Set("HttpMethod", http.Request.Method);
        diagnostics.Set("RequestPath", http.Request.Path.Value ?? "");
        diagnostics.Set("StatusCode", http.Response.StatusCode);
    };
});
app.UseMiddleware<GlobalErrorMiddleware>();
app.UseRouting();
app.UseCors("AllowReact");
app.UseRateLimiter();
app.UseMiddleware<TailscaleMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var shouldAudit = HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/api");
    var stopwatch = shouldAudit ? Stopwatch.StartNew() : null;
    try { await next(); }
    finally
    {
        if (shouldAudit && stopwatch is not null)
        {
            try
            {
            Guid? serverId = null;
            var segments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (segments.Length >= 3 && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) && segments[1].Equals("servers", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(segments[2], out var parsed))
                serverId = parsed;
            using var scope = context.RequestServices.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<AuditLogService>();
            var isMetricCall = context.Request.Path.StartsWithSegments("/api/resources") || context.Request.Path.StartsWithSegments("/api/observability/metrics");
            var action = isMetricCall ? AuditActions.MetricCall : AuditActions.StatusCall;
            await audit.RecordAsync(context.User, action, serverId, context.Response.StatusCode < 400, $"HTTP_{context.Response.StatusCode}", new { method = context.Request.Method, path = context.Request.Path.Value, statusCode = context.Response.StatusCode, durationMs = stopwatch.ElapsedMilliseconds });
        }
        catch { /* Observability must not change the API response. */ }
    }
    }
    });
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

app.MapPost("/api/servers/{id}/start", async (Guid id, AppDbContext db, ServerOperationQueue queue, AuditLogService audit, HttpContext http) =>
{
    try
    {
        var server = await db.ServerInstances.FindAsync([id]);
        if (server is null) return Results.NotFound();
        if (server.Status != ServerStatus.Stopped) return Results.Conflict(new { error = $"Cannot start while server status is {server.Status}" });
        var subject = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? http.User.FindFirst("sub")?.Value;
        var userId = Guid.TryParse(subject, out var parsed) ? parsed : (Guid?)null;
        var job = queue.Enqueue(id, ServerOperationKind.Start, userId, http.User.Identity?.Name ?? "admin");
        server.Status = ServerStatus.Starting;
        server.Ready = false;
        await db.SaveChangesAsync();
        await audit.RecordAsync(http.User, AuditActions.StartServer, id, true, "QUEUED", new { action = "start", jobId = job.Id });
        return Results.Accepted($"/api/operations/{job.Id}", job);
    }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
}).RequireAuthorization("admin");

app.MapPost("/api/servers/{id}/stop", async (Guid id, AppDbContext db, ServerOperationQueue queue, AuditLogService audit, HttpContext http) =>
{
    try
    {
        var server = await db.ServerInstances.FindAsync([id]);
        if (server is null) return Results.NotFound();
        if (server.Status != ServerStatus.Running) return Results.Conflict(new { error = $"Cannot stop while server status is {server.Status}" });
        var subject = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? http.User.FindFirst("sub")?.Value;
        var userId = Guid.TryParse(subject, out var parsed) ? parsed : (Guid?)null;
        var job = queue.Enqueue(id, ServerOperationKind.Stop, userId, http.User.Identity?.Name ?? "admin");
        server.Status = ServerStatus.Stopping;
        server.Ready = false;
        await db.SaveChangesAsync();
        await audit.RecordAsync(http.User, AuditActions.StopServer, id, true, "QUEUED", new { action = "stop", jobId = job.Id });
        return Results.Accepted($"/api/operations/{job.Id}", job);
    }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
}).RequireAuthorization("admin");

app.MapPost("/api/servers/{id}/restart", async (Guid id, AppDbContext db, ServerOperationQueue queue, HttpContext http) =>
{
    try
    {
        var server = await db.ServerInstances.FindAsync([id]);
        if (server is null) return Results.NotFound();
        if (server.Status != ServerStatus.Running) return Results.Conflict(new { error = $"Cannot restart while server status is {server.Status}" });
        var subject = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? http.User.FindFirst("sub")?.Value;
        var userId = Guid.TryParse(subject, out var parsed) ? parsed : (Guid?)null;
        var job = queue.Enqueue(id, ServerOperationKind.Restart, userId, http.User.Identity?.Name ?? "admin");
        server.Status = ServerStatus.Stopping;
        server.Ready = false;
        await db.SaveChangesAsync();
        return Results.Accepted($"/api/operations/{job.Id}", job);
    }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
}).RequireAuthorization("admin");

app.MapGet("/api/operations/{id}", (Guid id, ServerOperationQueue queue) =>
    queue.Get(id) is { } job ? Results.Ok(job) : Results.NotFound())
    .RequireAuthorization("authenticated");

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

app.MapPost("/api/pz/rcon/access-level", async ([FromBody] PzAccessLevelChange body, IRconClient rcon) =>
{
    if (string.IsNullOrWhiteSpace(body.Username) || body.Username.Length > 64 || body.Username.Any(char.IsWhiteSpace) || body.Username.Any(c => c is '"' or '\\'))
        return Results.BadRequest(new { error = "Invalid PZ username" });
    var level = body.Level.Trim().ToLowerInvariant();
    if (level is not ("user" or "admin")) return Results.BadRequest(new { error = "Level must be user or admin" });
    try { return Results.Ok(new { success = true, username = body.Username, level, output = await rcon.SendCommandAsync($"setaccesslevel \"{body.Username}\" {level}") }); }
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

app.MapGet("/api/valheim/settings", async (AppDbContext db) =>
{
    var server = await db.ServerInstances.AsNoTracking().FirstOrDefaultAsync(x => x.GameType == "Valheim");
    if (server == null) return Results.NotFound();
    var settings = await db.ValheimWorldModifierSettings.AsNoTracking().FirstOrDefaultAsync(x => x.ServerInstanceId == server.Id)
        ?? new ValheimWorldModifierSettings { ServerInstanceId = server.Id };
    return Results.Ok(new { serverId = server.Id, settings });
}).RequireAuthorization("authenticated");

app.MapPut("/api/valheim/settings", async ([FromBody] ValheimWorldModifierSettingsRequest body, AppDbContext db, ISystemdRuntimeDriver driver, ValheimSettingsApplyService apply, AuditLogService audit, HttpContext http) =>
{
    var server = await db.ServerInstances.FirstOrDefaultAsync(x => x.GameType == "Valheim");
    if (server == null) return Results.NotFound();
    var state = await driver.GetStateAsync(server.RuntimeId ?? "valheim-main.service");
    if (state.ActiveState == "active") return Results.Conflict(new { error = "Stop Valheim before changing world modifiers" });
    try
    {
        var model = ValheimSettingsAdapter.Validate(new ValheimWorldModifierSettingsModel(body.Combat, body.ResourceRate, body.RaidRate, body.DeathPenalty, body.PortalMode, body.PassiveEnemies, body.PlayerBasedRaids, body.HammerMode, body.NoBuildCost));
        await apply.ApplyAsync(model);
        var settings = await db.ValheimWorldModifierSettings.FirstOrDefaultAsync(x => x.ServerInstanceId == server.Id);
        if (settings == null) { settings = new ValheimWorldModifierSettings { ServerInstanceId = server.Id }; db.ValheimWorldModifierSettings.Add(settings); }
        settings.Combat = model.Combat; settings.ResourceRate = model.ResourceRate; settings.RaidRate = model.RaidRate; settings.DeathPenalty = model.DeathPenalty; settings.PortalMode = model.PortalMode; settings.PassiveEnemies = model.PassiveEnemies; settings.PlayerBasedRaids = model.PlayerBasedRaids; settings.HammerMode = model.HammerMode; settings.NoBuildCost = model.NoBuildCost;
        await db.SaveChangesAsync();
        await audit.RecordAsync(http.User, AuditActions.UpdateConfig, server.Id, true, "APPLIED", new { settings = "valheim_world_modifiers" });
        return Results.Ok(new { success = true, settings, requiresRestart = true, message = "Settings applied to launcher; restart Valheim to activate them" });
    }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
}).RequireAuthorization("admin");

var valheimActions = new Dictionary<string, ValheimCapability>
{
    ["kick"] = ValheimCapability.KickPlayer, ["ban"] = ValheimCapability.BanPlayer,
    ["broadcast"] = ValheimCapability.BroadcastMessage, ["save"] = ValheimCapability.SaveWorld,
    ["time"] = ValheimCapability.ChangeTime, ["weather"] = ValheimCapability.ChangeWeather,
    ["teleport"] = ValheimCapability.TeleportPlayer, ["spawn"] = ValheimCapability.SpawnItem,
    ["inventory"] = ValheimCapability.InventoryManagement, ["god-mode"] = ValheimCapability.GodMode,
};
app.MapGet("/api/servers/{id}/valheim/capabilities", async (Guid id, AppDbContext db, IGameControlProtocol protocol) =>
{
    if (!await db.ServerInstances.AnyAsync(x => x.Id == id && x.GameType == "Valheim")) return Results.NotFound();
    return Results.Ok(await protocol.GetCapabilitiesAsync(id));
}).RequireAuthorization("admin");
app.MapGet("/api/servers/{id}/valheim/players", async (Guid id, AppDbContext db, IGameControlProtocol protocol) =>
{
    if (!await db.ServerInstances.AnyAsync(x => x.Id == id && x.GameType == "Valheim")) return Results.NotFound();
    var caps = await protocol.GetCapabilitiesAsync(id);
    if (!caps.Connected) return Results.Problem(caps.Reason, statusCode: StatusCodes.Status503ServiceUnavailable);
    if (!caps.Supported.Contains(ValheimCapability.OnlinePlayers)) return Results.Problem("Online players capability is not supported", statusCode: StatusCodes.Status501NotImplemented);
    return Results.Ok(await protocol.GetPlayersAsync(id));
}).RequireAuthorization("admin");
foreach (var action in valheimActions)
{
    app.MapPost($"/api/servers/{{id}}/valheim/actions/{action.Key}", async (Guid id, [FromBody] ValheimControlActionBody body, AppDbContext db, ValheimControlActionService controls, AuditLogService audit, HttpRequest request, CancellationToken ct) =>
    {
        var server = await db.ServerInstances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.GameType == "Valheim", ct);
        if (server is null) return Results.NotFound();
        if (server.Status != ServerStatus.Running) return Results.Conflict(new { error = "Valheim must be Started for live control" });
        if (body is null) return Results.BadRequest(new { error = "Request body is required" });
        var requestBody = new ValheimControlRequest(body.TargetPlayerId, body.Message, body.Value, body.Weather, body.Item, body.Amount);
        var validationError = ValheimControlValidation.Validate(action.Value, requestBody);
        if (validationError is not null) return Results.BadRequest(new { error = validationError });
        var key = request.Headers["Idempotency-Key"].ToString();
        var result = await controls.ExecuteAsync(id, action.Value, requestBody, key, ct);
        await audit.RecordAsync(request.HttpContext.User, $"VALHEIM_{action.Key.ToUpperInvariant()}", id, result.Status == 200, result.Result.Code, new { capability = action.Value.ToString(), targetPlayerId = body.TargetPlayerId });
        return Results.Json(result.Result, statusCode: result.Status);
    }).RequireAuthorization("admin");
}
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

app.MapGet("/api/valheim/mods", (ValheimModService modService) =>
    Results.Ok(new { success = true, mods = modService.ListMods() }))
    .RequireAuthorization("authenticated");

app.MapPost("/api/valheim/mods/toggle", async ([FromBody] ValheimModToggleRequest body, ValheimModService modService, CancellationToken ct) =>
{
    try
    {
        var mod = await modService.ToggleModAsync(body.RelativePath, ct);
        return Results.Ok(new { success = true, mod });
    }
    catch (InvalidOperationException e) { return Results.Conflict(new { success = false, error = e.Message }); }
    catch (FileNotFoundException e) { return Results.NotFound(new { success = false, error = e.Message }); }
    catch (ArgumentException e) { return Results.BadRequest(new { success = false, error = e.Message }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapDelete("/api/valheim/mods", async (string? path, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ValheimModDeleteRequest? body, ValheimModService modService, CancellationToken ct) =>
{
    var targetPath = !string.IsNullOrWhiteSpace(path) ? path : body?.RelativePath;
    if (string.IsNullOrWhiteSpace(targetPath)) return Results.BadRequest(new { success = false, error = "Path is required." });
    try
    {
        await modService.DeleteModAsync(targetPath, ct);
        return Results.Ok(new { success = true });
    }
    catch (InvalidOperationException e) { return Results.Conflict(new { success = false, error = e.Message }); }
    catch (FileNotFoundException e) { return Results.NotFound(new { success = false, error = e.Message }); }
    catch (ArgumentException e) { return Results.BadRequest(new { success = false, error = e.Message }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapGet("/api/valheim/mods/config", async (string name, ValheimModService modService, CancellationToken ct) =>
{
    try
    {
        var content = await modService.GetConfigAsync(name, ct);
        return Results.Ok(new { success = true, content });
    }
    catch (FileNotFoundException e) { return Results.NotFound(new { success = false, error = e.Message }); }
    catch (ArgumentException e) { return Results.BadRequest(new { success = false, error = e.Message }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapPut("/api/valheim/mods/config", async ([FromBody] ValheimModConfigSaveRequest body, ValheimModService modService, CancellationToken ct) =>
{
    try
    {
        await modService.SaveConfigAsync(body.Name, body.Content, ct);
        return Results.Ok(new { success = true });
    }
    catch (InvalidOperationException e) { return Results.Conflict(new { success = false, error = e.Message }); }
    catch (ArgumentException e) { return Results.BadRequest(new { success = false, error = e.Message }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapPost("/api/valheim/mods/upload", async (IFormFile file, ValheimModService modService, CancellationToken ct) =>
{
    if (file == null || file.Length == 0) return Results.BadRequest(new { success = false, error = "File is required." });
    if (file.Length > 50 * 1024 * 1024) return Results.BadRequest(new { success = false, error = "File size exceeds 50MB limit." });
    try
    {
        using var stream = file.OpenReadStream();
        var files = await modService.UploadModAsync(file.FileName, stream, ct);
        return Results.Ok(new { success = true, files });
    }
    catch (InvalidOperationException e) { return Results.Conflict(new { success = false, error = e.Message }); }
    catch (ArgumentException e) { return Results.BadRequest(new { success = false, error = e.Message }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapGet("/api/valheim/mods/thunderstore/search", async (string? q, int? page, int? pageSize, ThunderstoreService thunderstore, CancellationToken ct) =>
{
    try
    {
        var result = await thunderstore.SearchAsync(q, page ?? 1, pageSize ?? 20, ct);
        return Results.Ok(new { success = true, result });
    }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapPost("/api/valheim/mods/thunderstore/install", async ([FromBody] ValheimThunderstoreInstallRequest body, ValheimModService modService, IHttpClientFactory httpFactory, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.DownloadUrl)) return Results.BadRequest(new { success = false, error = "DownloadUrl is required." });
    try
    {
        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);
        var files = await modService.InstallThunderstoreModAsync(body.DownloadUrl, body.PackageFullName, client, ct);
        return Results.Ok(new { success = true, files });
    }
    catch (InvalidOperationException e) { return Results.Conflict(new { success = false, error = e.Message }); }
    catch (ArgumentException e) { return Results.BadRequest(new { success = false, error = e.Message }); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("admin");

app.MapGet("/api/valheim/mods/export-modpack", (ValheimModService modService) =>
{
    var memoryStream = new MemoryStream();
    modService.ExportClientModpack(memoryStream);
    memoryStream.Position = 0;
    return Results.File(memoryStream, "application/zip", "Valheim_Client_Mods.zip");
}).RequireAuthorization("authenticated");

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

app.MapGet("/api/resources/overview", async (ResourceMetricsService resources, CancellationToken ct) =>
{
    try { return Results.Ok(await resources.GetOverviewAsync(ct)); }
    catch (Exception e) { return Results.Problem(e.Message); }
}).RequireAuthorization("authenticated");

app.MapGet("/api/observability/metrics", async (MetricObservabilityService metrics, CancellationToken ct) => Results.Ok(await metrics.GetStatusAsync(ct)))
    .RequireAuthorization("authenticated");

app.MapGet("/api/observability/metrics/logs", async (Guid? serverId, DateTime? fromUtc, DateTime? toUtc, string? metricType, int? page, int? pageSize, MetricObservabilityService metrics, CancellationToken ct) =>
{
    if (fromUtc.HasValue && toUtc.HasValue && fromUtc > toUtc) return Results.BadRequest(new { error = "fromUtc must be earlier than or equal to toUtc" });
    var allowed = new[] { "resource_server", "resource_host", "status_heartbeat", "pz_process" };
    if (!string.IsNullOrWhiteSpace(metricType) && !allowed.Contains(metricType, StringComparer.Ordinal)) return Results.BadRequest(new { error = "Unsupported metricType" });
    try { return Results.Ok(await metrics.QueryAsync(serverId, fromUtc, toUtc, metricType, Math.Clamp(page ?? 1, 1, 100000), Math.Clamp(pageSize ?? 50, 1, 200), ct)); }
    catch (HttpRequestException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
}).RequireAuthorization("authenticated");

app.MapGet("/api/audit", async (Guid? serverId, DateTime? fromUtc, DateTime? toUtc, string? type, string? resultCode, int? page, int? pageSize, int? limit, AppDbContext db) =>
{
    if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        return Results.BadRequest(new { error = "fromUtc must be earlier than or equal to toUtc" });
    var query = db.AuditLogs.AsNoTracking().AsQueryable();
    if (serverId.HasValue) query = query.Where(x => x.ServerInstanceId == serverId.Value);
    if (fromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= fromUtc.Value.ToUniversalTime());
    if (toUtc.HasValue) query = query.Where(x => x.CreatedAtUtc < toUtc.Value.ToUniversalTime());
    if (!string.IsNullOrWhiteSpace(type)) query = query.Where(x => x.Action == type);
    if (!string.IsNullOrWhiteSpace(resultCode)) query = query.Where(x => x.ResultCode == resultCode);
    var paged = page.HasValue || pageSize.HasValue || serverId.HasValue || fromUtc.HasValue || toUtc.HasValue || !string.IsNullOrWhiteSpace(type) || !string.IsNullOrWhiteSpace(resultCode);
    if (!paged)
    {
        var legacyItems = await query.OrderByDescending(x => x.CreatedAtUtc).Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync();
        return Results.Ok(legacyItems);
    }
    var currentPage = Math.Clamp(page ?? 1, 1, 100000);
    var size = Math.Clamp(pageSize ?? limit ?? 50, 1, 200);
    var total = await query.CountAsync();
    var items = await query.OrderByDescending(x => x.CreatedAtUtc).Skip((currentPage - 1) * size).Take(size).ToListAsync();
    return Results.Ok(new { items, total, page = currentPage, pageSize = size, hasMore = currentPage * size < total });
}).RequireAuthorization("admin");

app.MapGet("/api/events", async (Guid? serverId, int? limit, SystemEventService events) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 500);
    return Results.Ok(await events.Query(serverId).Take(take).ToListAsync());
}).RequireAuthorization("authenticated");

app.Run();

// Eksponuje kompilator-dependentny punkt wejścia Minimal API jako klasę, dzięki
// czemu WebApplicationFactory<Program> może uruchomić pipeline w pamięci (TestServer)
// bez otwierania portu. Produkcyjne top-level statements pozostają nietknięte.
public partial class Program { }