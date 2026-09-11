namespace GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using GamePanel.Domain.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();
    public DbSet<ServerStatusSnapshot> ServerStatusSnapshots => Set<ServerStatusSnapshot>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();
    public DbSet<ValheimWorldModifierSettings> ValheimWorldModifierSettings => Set<ValheimWorldModifierSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerInstance>(e =>
        {
            e.ToTable("ServerInstances");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.GameType).HasMaxLength(100);
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.WorldName).HasMaxLength(120);
            e.Property(x => x.Password).HasMaxLength(100);
            e.Property(x => x.InstanceKey).HasMaxLength(100);
            e.Property(x => x.ProvisioningMode).HasConversion<int>();
            e.Property(x => x.RuntimeType).HasConversion<int>();
            e.Property(x => x.RuntimeId).HasMaxLength(200);
            e.Property(x => x.InstallationPath).HasMaxLength(500);
            e.Property(x => x.DataPath).HasMaxLength(500);
            e.Property(x => x.BackupPath).HasMaxLength(500);
            e.Property(x => x.Protocol).HasConversion<int>();
            e.Property(x => x.ReadinessMarker).HasMaxLength(200);
            e.HasIndex(x => x.InstanceKey)
                .IsUnique()
                .HasFilter("\"InstanceKey\" IS NOT NULL")
                .HasDatabaseName("UX_ServerInstances_InstanceKey");
            e.HasIndex(x => new { x.RuntimeType, x.RuntimeId })
                .IsUnique()
                .HasFilter("\"RuntimeId\" IS NOT NULL")
                .HasDatabaseName("UX_ServerInstances_RuntimeType_RuntimeId");
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(200);
            e.Property(x => x.NormalizedUsername).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(255);
            e.Property(x => x.Role).HasConversion<int>();
            e.HasIndex(x => x.NormalizedUsername)
                .IsUnique()
                .HasDatabaseName("UX_Users_NormalizedUsername");
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.UsernameSnapshot).HasMaxLength(200);
            e.Property(x => x.Action).HasMaxLength(80);
            e.Property(x => x.ResultCode).HasMaxLength(80);
            e.Property(x => x.MetadataJson).HasMaxLength(4000);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => new { x.ServerInstanceId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<SystemEvent>(e =>
        {
            e.ToTable("SystemEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(80);
            e.Property(x => x.Message).HasMaxLength(255);
            e.Property(x => x.Severity).HasMaxLength(20);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => new { x.ServerInstanceId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<ServerStatusSnapshot>(e =>
        {
            e.ToTable("ServerStatusSnapshots");
            e.HasKey(x => x.ServerInstanceId);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Source).HasMaxLength(40);
            e.HasOne(x => x.ServerInstance).WithOne().HasForeignKey<ServerStatusSnapshot>(x => x.ServerInstanceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ObservedAtUtc);
        });
        modelBuilder.Entity<ValheimWorldModifierSettings>(e =>
        {
            e.ToTable("ValheimWorldModifierSettings");
            e.HasKey(x => x.ServerInstanceId);
            e.Property(x => x.Combat).HasMaxLength(20);
            e.Property(x => x.RaidRate).HasMaxLength(20);
            e.Property(x => x.DeathPenalty).HasMaxLength(20);
            e.Property(x => x.PortalMode).HasMaxLength(20);
        });

    }
}