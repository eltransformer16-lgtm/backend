using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string NormalizedUsername { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string NormalizedEmail { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    public DateOnly BirthDate { get; set; }

    [Required, MaxLength(20)]
    public string Role { get; set; } = "User";

    public bool IsActive { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<RefreshToken> RefreshTokens { get; set; } = new();
}