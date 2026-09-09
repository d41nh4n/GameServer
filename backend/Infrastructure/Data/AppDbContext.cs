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