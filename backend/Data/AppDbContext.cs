using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<StorageDirectory> Directories => Set<StorageDirectory>();
    public DbSet<StoredFile> Files => Set<StoredFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.NormalizedUsername).IsUnique();
            e.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(rt => rt.TokenHash).IsUnique();
            e.HasOne(rt => rt.User)
             .WithMany(u => u.RefreshTokens)
             .HasForeignKey(rt => rt.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StorageDirectory>(e =>
        {
            e.HasOne(d => d.Parent)
             .WithMany(p => p.Children)
             .HasForeignKey(d => d.ParentId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(d => d.Owner)
             .WithMany()
             .HasForeignKey(d => d.OwnerId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StoredFile>(e =>
        {
            e.HasOne(f => f.Directory)
             .WithMany(d => d.Files)
             .HasForeignKey(f => f.DirectoryId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(f => f.Owner)
             .WithMany()
             .HasForeignKey(f => f.OwnerId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}