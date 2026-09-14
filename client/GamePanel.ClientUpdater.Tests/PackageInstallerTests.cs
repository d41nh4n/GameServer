namespace GamePanel.ClientUpdater.Tests;

using System.IO.Compression;
using System.Security.Cryptography;
using GamePanel.ClientUpdater;

public sealed class PackageInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gamepanel_updater_test_" + Guid.NewGuid().ToString("N"));

    public PackageInstallerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task ApplyAsync_VerifiesHashesAndBacksUpExistingFiles()
    {
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx", "plugins"));
        var target = Path.Combine(game, "BepInEx", "plugins", "PlantEverything.dll");
        await File.WriteAllTextAsync(target, "old");
        var archive = CreateZip(("BepInEx/plugins/PlantEverything.dll", "new"));
        var package = Package("Advize-PlantEverything", "1.21.2", archive,
            new ClientPackageFile("BepInEx/plugins/PlantEverything.dll", Sha("new")));
        var manifest = Manifest(package);
        var installer = new PackageInstaller();

        var result = await installer.ApplyAsync(game, manifest, (_, _) => Task.FromResult<Stream>(new MemoryStream(archive)), checkOnly: false);

        Assert.Equal("new", await File.ReadAllTextAsync(target));
        Assert.Single(result.ChangedFiles);
        Assert.NotNull(result.BackupDirectory);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory!, "BepInEx", "plugins", "PlantEverything.dll")));
        Assert.True(File.Exists(Path.Combine(game, ".gamepanel-updater", "current-manifest.json")));
    }

    [Fact]
    public async Task ApplyAsync_HashMismatchLeavesExistingFilesUnchanged()
    {
        var game = Path.Combine(_root, "game-hash");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx", "plugins"));
        var target = Path.Combine(game, "BepInEx", "plugins", "PlantEverything.dll");
        await File.WriteAllTextAsync(target, "old");
        var archive = CreateZip(("BepInEx/plugins/PlantEverything.dll", "tampered"));
        var package = Package("Advize-PlantEverything", "1.21.2", archive,
            new ClientPackageFile("BepInEx/plugins/PlantEverything.dll", Sha("expected")));
        var installer = new PackageInstaller();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.ApplyAsync(game, Manifest(package), (_, _) => Task.FromResult<Stream>(new MemoryStream(archive)), checkOnly: false));

        Assert.Equal("old", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ApplyAsync_RejectsPathTraversalBeforeWriting()
    {
        var game = Path.Combine(_root, "game-traversal");
        Directory.CreateDirectory(game);
        var archive = CreateZip(("../escape.dll", "bad"));
        var package = Package("Bad-Package", "1.0.0", archive,
            new ClientPackageFile("../escape.dll", Sha("bad")));
        var installer = new PackageInstaller();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.ApplyAsync(game, Manifest(package), (_, _) => Task.FromResult<Stream>(new MemoryStream(archive)), checkOnly: false));

        Assert.False(File.Exists(Path.Combine(_root, "escape.dll")));
    }

    [Fact]
    public async Task ApplyAsync_CheckOnlyDoesNotModifyGameDirectory()
    {
        var game = Path.Combine(_root, "game-check");
        Directory.CreateDirectory(game);
        var archive = CreateZip(("BepInEx/plugins/PlantEverything.dll", "new"));
        var package = Package("Advize-PlantEverything", "1.21.2", archive,
            new ClientPackageFile("BepInEx/plugins/PlantEverything.dll", Sha("new")));
        var installer = new PackageInstaller();

        var result = await installer.ApplyAsync(game, Manifest(package), (_, _) => Task.FromResult<Stream>(new MemoryStream(archive)), checkOnly: true);

        Assert.Single(result.ChangedFiles);
        Assert.False(Directory.Exists(Path.Combine(game, "BepInEx")));
        Assert.False(Directory.Exists(Path.Combine(game, ".gamepanel-updater")));
    }

    [Fact]
    public async Task ApplyAsync_RemovesFilesManagedByThePreviousManifestButNotTheCurrentRevision()
    {
        var game = Path.Combine(_root, "game-remove-stale");
        var plugins = Path.Combine(game, "BepInEx", "plugins");
        Directory.CreateDirectory(plugins);
        var stale = Path.Combine(plugins, "OldMod.dll");
        var unmanaged = Path.Combine(plugins, "MyLocalMod.dll");
        await File.WriteAllTextAsync(stale, "old-managed-mod");
        await File.WriteAllTextAsync(unmanaged, "keep-local-mod");

        var updaterRoot = Path.Combine(game, ".gamepanel-updater");
        Directory.CreateDirectory(updaterRoot);
        var previousArchive = CreateZip(("BepInEx/plugins/OldMod.dll", "old-managed-mod"));
        var previous = Manifest(Package("Old-Mod", "1.0.0", previousArchive,
            new ClientPackageFile("BepInEx/plugins/OldMod.dll", Sha("old-managed-mod"))));
        await File.WriteAllTextAsync(
            Path.Combine(updaterRoot, "current-manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(previous, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            }));

        var currentArchive = CreateZip(("BepInEx/plugins/NewMod.dll", "new-mod"));
        var current = Manifest(Package("New-Mod", "2.0.0", currentArchive,
            new ClientPackageFile("BepInEx/plugins/NewMod.dll", Sha("new-mod"))));
        var installer = new PackageInstaller();

        var result = await installer.ApplyAsync(
            game,
            current,
            (_, _) => Task.FromResult<Stream>(new MemoryStream(currentArchive)),
            checkOnly: false);

        Assert.False(File.Exists(stale));
        Assert.Equal("keep-local-mod", await File.ReadAllTextAsync(unmanaged));
        Assert.Equal("new-mod", await File.ReadAllTextAsync(Path.Combine(plugins, "NewMod.dll")));
        Assert.Contains("BepInEx/plugins/OldMod.dll", result.ChangedFiles);
        Assert.Equal(
            "old-managed-mod",
            await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory!, "BepInEx", "plugins", "OldMod.dll")));
    }

    [Fact]
    public async Task ApplyAsync_RecordsCurrentRevisionWhenManagedFilesAreAlreadyCurrent()
    {
        var game = Path.Combine(_root, "game-revision-only");
        var plugins = Path.Combine(game, "BepInEx", "plugins");
        Directory.CreateDirectory(plugins);
        await File.WriteAllTextAsync(Path.Combine(plugins, "Same.dll"), "same-content");

        var archive = CreateZip(("BepInEx/plugins/Same.dll", "same-content"));
        var oldPackage = Package("Same-Mod", "1.0.0", archive,
            new ClientPackageFile("BepInEx/plugins/Same.dll", Sha("same-content")));
        var oldManifest = Manifest(oldPackage) with { Revision = "revision-old" };
        var updaterRoot = Path.Combine(game, ".gamepanel-updater");
        Directory.CreateDirectory(updaterRoot);
        await File.WriteAllTextAsync(
            Path.Combine(updaterRoot, "current-manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(oldManifest, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            }));

        var currentManifest = Manifest(oldPackage) with { Revision = "revision-new" };
        var installer = new PackageInstaller();

        var result = await installer.ApplyAsync(
            game,
            currentManifest,
            (_, _) => Task.FromResult<Stream>(new MemoryStream(archive)),
            checkOnly: false);

        var state = await File.ReadAllTextAsync(Path.Combine(updaterRoot, "current-manifest.json"));
        Assert.Empty(result.ChangedFiles);
        Assert.Contains("revision-new", state);
        Assert.DoesNotContain("revision-old", state);
    }

    [Fact]
    public void ValidateManifest_RejectsServerOnlyAndDuplicatePaths()
    {
        var file = new ClientPackageFile("BepInEx/plugins/X.dll", Sha("x"));
        var package = new ClientModPackage("X", "1.0.0", "server-only", "/pkg/x", new string('a', 64), [file, file]);

        Assert.Throws<InvalidDataException>(() => ManifestValidator.Validate(Manifest(package)));
    }

    private static ClientUpdateManifest Manifest(params ClientModPackage[] packages) =>
        new(1, "valheim-main", "revision-1", DateTimeOffset.UtcNow, packages);

    private static ClientModPackage Package(string id, string version, byte[] archive, params ClientPackageFile[] files) =>
        new(id, version, "client-server", $"/api/client-updater/packages/{id}/{version}", Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant(), files);

    private static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries)
            {
                var item = zip.CreateEntry(entry.Name);
                using var writer = new StreamWriter(item.Open());
                writer.Write(entry.Content);
            }
        return stream.ToArray();
    }

    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
