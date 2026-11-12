using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<StoredFile> Files => Set<StoredFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Username).HasMaxLength(30).IsRequired();
            entity.Property(u => u.NormalizedUsername).HasMaxLength(30).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(254).IsRequired();
            entity.Property(u => u.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
            entity.Property(u => u.Role).HasMaxLength(20).IsRequired();

            // Evita usuarios y correos repetidos sin importar mayúsculas o minúsculas.
            entity.HasIndex(u => u.NormalizedUsername).IsUnique();
            entity.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(t => t.TokenHash).IsUnique();

            // Un usuario tiene como máximo un refresh token (relación uno a uno).
            entity.HasOne(t => t.User)
                .WithOne(u => u.RefreshToken)
                .HasForeignKey<RefreshToken>(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Folder>(entity =>
        {
            entity.ToTable("folders");
            entity.HasKey(f => f.Id);

            entity.Property(f => f.Name).HasMaxLength(100).IsRequired();

            // Al borrar un directorio se borran sus subdirectorios.
            entity.HasOne(f => f.Parent)
                .WithMany()
                .HasForeignKey(f => f.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(f => f.Owner)
                .WithMany()
                .HasForeignKey(f => f.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // No puede haber dos directorios con el mismo nombre en el mismo nivel
            // (incluida la raíz, donde ParentId es nulo).
            entity.HasIndex(f => new { f.ParentId, f.Name }).IsUnique().AreNullsDistinct(false);
        });

        modelBuilder.Entity<StoredFile>(entity =>
        {
            entity.ToTable("files");
            entity.HasKey(f => f.Id);

            entity.Property(f => f.Name).HasMaxLength(200).IsRequired();
            entity.Property(f => f.Extension).HasMaxLength(20).IsRequired();
            entity.Property(f => f.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(f => f.StoragePath).HasMaxLength(300).IsRequired();

            // Al borrar un directorio se borran sus archivos.
            entity.HasOne(f => f.Folder)
                .WithMany()
                .HasForeignKey(f => f.FolderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(f => f.Owner)
                .WithMany()
                .HasForeignKey(f => f.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(f => f.FolderId);
            entity.HasIndex(f => f.OwnerId);
        });
    }
}
