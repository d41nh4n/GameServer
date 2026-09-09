using System.Text;
using GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Auth;
using GamePanel.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

/// <summary>
/// CR-00B: testy nowej lokalnej autoryzacji. Używają IZOLOWANEJ plikowej bazy
/// SQLite w katalogu tymczasowym (nigdy gamepanel.db ani realnego runtime).
/// Nie dotykają systemd / procesów gry / żadnego endpoint.
/// </summary>
public class LocalAuthTests : IDisposable
{
    private readonly string _dbPath;
    private IConfiguration _config;
    private IPasswordHasher<User> _hasher;

    public LocalAuthTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "gamepanel-auth-" + Guid.NewGuid().ToString("N") + ".db");

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-0123456789abcdef0123456789abcdef",
                ["Jwt:Issuer"] = "GamePanelTest",
                ["Jwt:Audience"] = "GamePanelClient",
                ["Jwt:ExpiryMinutes"] = "30",
            })
            .Build();

        _hasher = new PasswordHasher<User>();
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=" + _dbPath)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreatedAsync().GetAwaiter().GetResult();
        return db;
    }

    private JwtAuthService CreateService(AppDbContext db)
        => new JwtAuthService(_config, db, _hasher);

    private async Task SeedUserAsync(AppDbContext db, string username, string password, UserRole role = UserRole.Admin)
    {
        var dummy = new User { Id = Guid.NewGuid(), Username = "seed" };
        var hash = _hasher.HashPassword(dummy, password);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = username.Trim().ToLowerInvariant(),
            PasswordHash = hash,
            Role = role,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsUser()
    {
        using var db = CreateDb();
        await SeedUserAsync(db, "admin", "CorrectHorseBattery");
        var service = CreateService(db);

        var user = await service.AuthenticateAsync("admin", "CorrectHorseBattery");

        Assert.NotNull(user);
        Assert.Equal("admin", user!.Username);
        Assert.Equal(UserRole.Admin, user.Role);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsNull()
    {
        using var db = CreateDb();
        await SeedUserAsync(db, "admin", "CorrectHorseBattery");
        var service = CreateService(db);

        var user = await service.AuthenticateAsync("admin", "WrongPassword");

        Assert.Null(user);
    }

    [Fact]
    public async Task Login_WithUnknownUsername_ReturnsNull()
    {
        using var db = CreateDb();
        await SeedUserAsync(db, "admin", "CorrectHorseBattery");
        var service = CreateService(db);

        var user = await service.AuthenticateAsync("nobody", "CorrectHorseBattery");

        Assert.Null(user);
    }

    [Fact]
    public async Task Login_WithMissingInput_ReturnsNull()
    {
        using var db = CreateDb();
        await SeedUserAsync(db, "admin", "CorrectHorseBattery");
        var service = CreateService(db);

        Assert.Null(await service.AuthenticateAsync("", "CorrectHorseBattery"));
        Assert.Null(await service.AuthenticateAsync("admin", ""));
        Assert.Null(await service.AuthenticateAsync("", ""));
    }

    [Fact]
    public void PasswordHash_IsNotEqualToPlaintext()
    {
        var dummy = new User { Id = Guid.NewGuid(), Username = "x" };
        var plain = "s3cret-password";
        var hash = _hasher.HashPassword(dummy, plain);

        Assert.NotEqual(plain, hash);
        Assert.DoesNotContain(plain, hash);
        Assert.True(hash.Length >= 60, "PBKDF2 hash powinien być długi");
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveOnUsername()
    {
        using var db = CreateDb();
        await SeedUserAsync(db, "Admin", "CorrectHorseBattery");
        var service = CreateService(db);

        var mixed = await service.AuthenticateAsync("admin", "CorrectHorseBattery");
        var upper = await service.AuthenticateAsync("ADMIN", "CorrectHorseBattery");

        Assert.NotNull(mixed);
        Assert.NotNull(upper);
        Assert.Equal(mixed!.Id, upper!.Id);
    }

    [Fact]
    public async Task UsernameNormalization_IsLocaleIndependent()
    {
        using var db = CreateDb();
        // tureckie 'ı' (U+0131, dotless i) — w tureckim locale ToLower() mapowałoby
        // 'I'→'ı', ale ToLowerInvariant() robi to deterministycznie (wariant invariant).
        var dotlessI = "\u0131";          // ı
        await SeedUserAsync(db, dotlessI, "pw1234", UserRole.Admin);
        var service = CreateService(db);

        // login z dokładnie tym samym znakiem działa
        var same = await service.AuthenticateAsync(dotlessI, "pw1234");
        Assert.NotNull(same);

        // zwykłe łacińskie 'ı' z 'i' NIE jest fuzją (invariant traktuje je oddzielnie)
        var asciiI = await service.AuthenticateAsync("i", "pw1234");
        Assert.Null(asciiI);
    }

    [Fact]
    public async Task Jwt_SubjectAndRoleComeFromPersistedUser()
    {
        using var db = CreateDb();
        await SeedUserAsync(db, "operator", "pw1234", UserRole.Admin);
        var service = CreateService(db);
        var user = await service.AuthenticateAsync("operator", "pw1234");

        var token = service.GenerateToken(user!.Id, user.Role);

        // Dekodujemy payload JWT (bez zależności od ValidateToken) by potwierdzić,
        // że subject i role pochodzą z trwałego użytkownika DB.
        var payload = DecodeJwtPayload(token.Value);
        Assert.True(payload.Contains(user.Id.ToString()),
            "payload powinien zawierać sub=userId, payload=" + payload);
        Assert.True(payload.Contains("Admin"),
            "payload powinien zawierać role=Admin, payload=" + payload);
    }

    /// <summary>Dekoduje (bez weryfikacji) payload JWT do surowego stringa (base64url).</summary>
    private static string DecodeJwtPayload(string token)
    {
        var parts = token.Split('.');
        var payloadB64 = parts.Length >= 2 ? parts[1] : "";
        return DecodeBase64Url(payloadB64);
    }

    private static readonly string _b64chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    private static string DecodeBase64Url(string input)
    {
        var bufferOut = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var c in input.ToCharArray())
        {
            var idx = _b64chars.IndexOf(c.ToString());
            if (idx < 0) continue;
            buffer = (buffer << 6) | idx;
            bits += 6;
            if (bits >= 8)
            {
                bits -= 8;
                bufferOut.Append((char)((buffer >> bits) & 0xFF));
            }
        }
        return bufferOut.ToString();
    }

    [Fact]
    public async Task Bootstrap_IsIdempotent_AndDoesNotOverwrite()
    {
        using var db = CreateDb();
        var service = CreateService(db);

        await service.EnsureBootstrapAdminAsync("bootadmin", "BootPass1");
        await service.EnsureBootstrapAdminAsync("bootadmin", "OtherPassZzz"); // drugi raz — ignorowane

        var count = await db.Users.CountAsync();
        Assert.Equal(1, count);

        var user = await service.AuthenticateAsync("bootadmin", "BootPass1");
        Assert.NotNull(user);

        // hasło z drugiego wywołania NIE zostaje nadpisane — stare hasło działa
        var userOld = await service.AuthenticateAsync("bootadmin", "OtherPassZzz");
        Assert.Null(userOld);
    }

    [Fact]
    public async Task Bootstrap_ThrowsWhenEnvMissing()
    {
        using var db = CreateDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureBootstrapAdminAsync("", ""));
    }

    [Fact]
    public async Task NoUnconditionalAdminToken_WithoutUser()
    {
        using var db = CreateDb();
        var service = CreateService(db); // pusta baza — żadnych userów

        Assert.Null(await service.AuthenticateAsync("anything", "password"));
        Assert.True(await service.UsersNeedBootstrapAsync());
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch { /* best-effort cleanup */ }
    }
}