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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Options models đọc từ appsettings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.Section));
builder.Services.Configure<TailscaleSettings>(builder.Configuration.GetSection(TailscaleSettings.Section));
builder.Services.Configure<ValheimSettings>(builder.Configuration.GetSection(ValheimSettings.Section));
builder.Services.Configure<ProjectZomboidSettings>(builder.Configuration.GetSection(ProjectZomboidSettings.Section));

// Auth service (JWT)
builder.Services.AddScoped<IAuthService, JwtAuthService>();

// Persistence & SignalR
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=gamepanel.db"));

// Game server process adapters
builder.Services.AddScoped<GameServerManager>();
builder.Services.AddScoped<IGameServerRuntime>(sp => sp.GetRequiredService<GameServerManager>());
builder.Services.AddScoped<ValheimAdapter>();
builder.Services.AddScoped<ICommandRunner>(_ => new ProcessCommandRunner());
builder.Services.AddScoped<ProjectZomboidAdapter>(sp =>
{
    var runner = sp.GetRequiredService<ICommandRunner>();
    return new ProjectZomboidAdapter(runner, builder.Configuration);
});
builder.Services.AddScoped<IGameServerAdapterFactory, GameServerAdapterFactory>();

builder.Services.AddSignalR();

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
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy("admin", p => p.RequireRole(UserRole.Admin.ToString())));

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
                Name = "Valheim Demo Server",
                GameType = "Valheim",
                Type = GameServerType.Valheim,
                Status = ServerStatus.Stopped,
                Port = 2456,
                WorldName = "Dedicated",
                Password = "viking",
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
                Name = "Project Zomboid Server",
                GameType = "ProjectZomboid",
                Type = GameServerType.ProjectZomboid,
                Status = ServerStatus.Stopped,
                Port = 16261,
                WorldName = "servertest_new",
                Password = "",
            });
        await db.SaveChangesAsync();
    }
}

// Middleware pipeline: Routing → CORS → Tailscale (tùy chọn) → Auth → Authorization
app.UseRouting();
app.UseCors("AllowReact");
app.UseMiddleware<TailscaleMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();

app.MapHub<ServerHub>("/hubs/server");

app.MapGet("/api/servers", async (IGameServerRuntime r) =>
{
    var all = await r.GetAllAsync();
    var response = all.Select(ServerResponse.FromDomain).ToArray();
    return Results.Ok(response);
});

// Fake login: trả JWT (không có user store thật ở M4)
app.MapPost("/api/auth/login", (IAuthService auth) =>
{
    var token = auth.GenerateToken(Guid.NewGuid(), UserRole.Admin);
    return Results.Ok(new { token = token.Value, expiresAt = token.ExpiresAt });
});

app.MapPost("/api/servers/{id}/start", async (Guid id, IGameServerRuntime r) =>
    (await r.StartAsync(id)) ? Results.Ok() : Results.BadRequest("Cannot start"))
    .RequireAuthorization("admin");

app.MapPost("/api/servers/{id}/stop", async (Guid id, IGameServerRuntime r) =>
    (await r.StopAsync(id)) ? Results.Ok() : Results.BadRequest("Cannot stop"))
    .RequireAuthorization("admin");

app.Run();