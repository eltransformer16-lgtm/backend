using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public class StorageDirectory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
    public StorageDirectory? Parent { get; set; }

    [Required]
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<StorageDirectory> Children { get; set; } = new();
    public List<StoredFile> Files { get; set; } = new();
}

public class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(200)]
    public string OriginalName { get; set; } = string.Empty;

    [Required, MaxLength(260)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    [Required]
    public Guid DirectoryId { get; set; }
    public StorageDirectory? Directory { get; set; }

    [Required]
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}