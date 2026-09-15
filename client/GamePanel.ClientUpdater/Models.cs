namespace GamePanel.ClientUpdater;

public sealed record ClientUpdateManifest(
    int SchemaVersion,
    string ProfileId,
    string Revision,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ClientModPackage> Packages);

public sealed record ClientModPackage(
    string PackageId,
    string Version,
    string Target,
    string DownloadPath,
    string ArchiveSha256,
    IReadOnlyList<ClientPackageFile> Files);

public sealed record ClientPackageFile(string Path, string Sha256);

public sealed record UpdateResult(
    string Revision,
    bool CheckOnly,
    IReadOnlyList<string> ChangedFiles,
    string? BackupDirectory);
