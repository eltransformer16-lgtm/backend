namespace backend.Models;

public class User
{
    public Guid Id { get; set; }

    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";

    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public bool EmailConfirmed { get; set; }

    public string PasswordHash { get; set; } = "";

    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;

    // Protección contra fuerza bruta.
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
