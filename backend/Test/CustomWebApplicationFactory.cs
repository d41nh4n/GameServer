using GamePanel.Api;
using GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using GamePanel.Infrastructure.GameServers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

/// <summary>Fake runtime gry — liczniki wywołań, bez Process/systemd/FS.</summary>
public class FakeGameRuntime : IGameServerRuntime
{
    public int GetCallCount = 0;
    public int StartCallCount = 0;
    public int StopCallCount = 0;
    public bool StartResult = true;
    public bool StopResult = true;
    public IEnumerable<ServerInstance> Servers = new List<ServerInstance>();

    public Task<IEnumerable<ServerInstance>> GetAllAsync() { GetCallCount++; return Task.FromResult(Servers); }
    public Task<bool> StartAsync(Guid id) { StartCallCount++; return Task.FromResult(StartResult); }
    public Task<bool> StopAsync(Guid id) { StopCallCount++; return Task.FromResult(StopResult); }
}

/// <summary>No-op command runner — nigdy nie wywołuje Process/systemctl/sudo.</summary>
public class NoopCommandRunner : ICommandRunner
{
    public Task<CommandResult> RunAsync(List<string> program, CancellationToken ct = default)
    {
        return Task.FromResult(new CommandResult { ExitCode = 0 });
    }
}

/// <summary>
/// Custom WebApplicationFactory: pipeline w pamięci (TestServer, bez portu).
/// Wstrzykuje testową konfigurację, izolowaną bazę SQLite :memory: oraz fake
/// runtime (liczniki wywołań). Nigdy nie dotyka gamepanel.db / systemd / procesów.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<global::Program>
{
    public readonly FakeGameRuntime fakeRuntime = new FakeGameRuntime();
    private string? _dbPath;

    /// <summary>Gotowy, otwarty SQLite in-memory NIE jest trwały między połączeniami EF;
    /// używamy izolowanego pliku tymczasowego (posprzątanego przez Dispose).</summary>
    private string TestDbPath() => _dbPath ??= Path.Join(Path.GetTempPath(), "gamepanel-it-" + Guid.NewGuid().ToString("N") + ".db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Secret", "0123456789abcdef0123456789abcdef012345");
        builder.UseSetting("Jwt:Issuer", "GamePanelTest");
        builder.UseSetting("Jwt:Audience", "GamePanelClient");
        builder.UseSetting("GAMEPANEL_BOOTSTRAP_ADMIN_USERNAME", "testadmin");
        builder.UseSetting("GAMEPANEL_BOOTSTRAP_ADMIN_PASSWORD", "TestPass123");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddSingleton<AppDbContext>(
                new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>()
                        .UseSqlite("Data Source=" + TestDbPath())
                        .Options));
            services.AddScoped<IGameServerRuntime>(_ => fakeRuntime);
            services.AddScoped<ICommandRunner>(_ => new NoopCommandRunner());
            services.AddScoped<IGameServerAdapterFactory>(_ =>
                throw new InvalidOperationException("fake runtime — bez adapterów"));
        });
    }

    /// <summary>Posprzątać izolowany plik bazy po teście (best-effort).</summary>
    private void CleanupDb()
    {
        if (_dbPath == null) return;
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) CleanupDb();
    }
}