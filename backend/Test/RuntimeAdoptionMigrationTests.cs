using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

public sealed class RuntimeAdoptionMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        "gamepanel-adoption-migration-" + Guid.NewGuid().ToString("N") + ".db");

    [Fact]
    public async Task MigrationAddsServerStatusSnapshotTable()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=" + _dbPath)
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        await using var connection = new SqliteConnection("Data Source=" + _dbPath);
        await connection.OpenAsync();
        var tables = await ReadNames(connection, "SELECT name FROM sqlite_master WHERE type = 'table'", 0);
        Assert.Contains("ServerStatusSnapshots", tables);
        var migrations = await ReadNames(connection, "SELECT MigrationId FROM __EFMigrationsHistory", 0);
        Assert.Contains("20260911002000_AddServerStatusSnapshots", migrations);
    }

    [Fact]
    public async Task MigrationAddsAdoptionColumnsAndUniqueIndexes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=" + _dbPath)
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        await using var connection = new SqliteConnection("Data Source=" + _dbPath);
        await connection.OpenAsync();
        var columns = await ReadNames(connection, "PRAGMA table_info('ServerInstances')", 1);
        Assert.Contains("InstanceKey", columns);
        Assert.Contains("ProvisioningMode", columns);
        Assert.Contains("RuntimeType", columns);
        Assert.Contains("RuntimeId", columns);
        Assert.Contains("Ready", columns);

        var indexes = await ReadNames(connection, "PRAGMA index_list('ServerInstances')", 1);
        Assert.Contains("UX_ServerInstances_InstanceKey", indexes);
        Assert.Contains("UX_ServerInstances_RuntimeType_RuntimeId", indexes);
    }

    [Fact]
    public async Task InstanceKeyIsUniqueWhenPresent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=" + _dbPath)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        db.ServerInstances.AddRange(
            Instance(Guid.NewGuid(), "same", "unit-a.service"),
            Instance(Guid.NewGuid(), "same", "unit-b.service"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RuntimeTypeAndRuntimeIdAreUniqueWhenRuntimeIdIsPresent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=" + _dbPath)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        db.ServerInstances.AddRange(
            Instance(Guid.NewGuid(), "one", "same.service"),
            Instance(Guid.NewGuid(), "two", "same.service"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static ServerInstance Instance(Guid id, string key, string runtimeId) => new()
    {
        Id = id,
        Name = key,
        GameType = "Valheim",
        Type = GameServerType.Valheim,
        InstanceKey = key,
        ProvisioningMode = ProvisioningMode.AdoptExisting,
        RuntimeType = ServerRuntimeType.Systemd,
        RuntimeId = runtimeId,
        Password = "",
    };

    private static async Task<HashSet<string>> ReadNames(
        SqliteConnection connection,
        string sql,
        int ordinal)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(ordinal));
        return names;
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
