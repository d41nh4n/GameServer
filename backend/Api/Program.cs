using GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using GamePanel.Infrastructure.GameServers;
using GamePanel.Infrastructure.Hubs;
using GamePanel.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Persistence & SignalR
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=gamepanel.db"));

// Game server process adapters
builder.Services.AddScoped<GameServerManager>();
builder.Services.AddScoped<IGameServerRuntime>(sp => sp.GetRequiredService<GameServerManager>());
builder.Services.AddScoped<ValheimAdapter>();
builder.Services.AddScoped<IGameServerAdapterFactory, GameServerAdapterFactory>();

builder.Services.AddSignalR();

builder.Services.AddOpenApi();
builder.Services.AddCors(p => p
    .AddPolicy("AllowReact", policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials()));

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
            });
        await db.SaveChangesAsync();
    }
}

// CORS must be installed before route matching so preflight OPTIONS short-circuits.
app.UseRouting();
app.UseCors("AllowReact");
app.MapOpenApi();

app.MapHub<ServerHub>("/hubs/server");

app.MapGet("/api/servers", async (IGameServerRuntime r) => Results.Ok(await r.GetAllAsync()));
app.MapPost("/api/servers/{id}/start", async (Guid id, IGameServerRuntime r) =>
    (await r.StartAsync(id)) ? Results.Ok() : Results.BadRequest("Cannot start"));
app.MapPost("/api/servers/{id}/stop", async (Guid id, IGameServerRuntime r) =>
    (await r.StopAsync(id)) ? Results.Ok() : Results.BadRequest("Cannot stop"));

app.Run();