namespace GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using GamePanel.Domain.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();
    public DbSet<User> Users => Set<User>();

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
    }
}