namespace backend.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}

public class User
{
    public Guid Id { get; set; }

    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";

    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public bool EmailConfirmed { get; set; }

    public DateOnly BirthDate { get; set; }

    public string PasswordHash { get; set; } = "";

    public string Role { get; set; } = Roles.User;

    // Las cuentas nuevas nacen desactivadas hasta que un administrador las active.
    public bool IsActive { get; set; }

    // Protección contra fuerza bruta.
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    // Cada usuario tiene como máximo un refresh token asignado.
    public RefreshToken? RefreshToken { get; set; }
}
