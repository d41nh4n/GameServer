namespace GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using GamePanel.Domain.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();

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
    }
}