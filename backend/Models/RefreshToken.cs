using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [Required, MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    [MaxLength(128)]
    public string? ReplacedByTokenHash { get; set; }
}