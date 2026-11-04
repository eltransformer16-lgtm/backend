using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public class RegisterRequest
{
    [Required]
    [StringLength(30, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$", ErrorMessage = "Solo letras, números, punto, guion y guion bajo.")]
    public string Username { get; set; } = "";

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "La contraseña debe tener entre 10 y 100 caracteres.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "La contraseña debe incluir al menos una letra y un número.")]
    public string Password { get; set; } = "";
}

public class LoginRequest
{
    [Required]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(100)]
    public string Password { get; set; } = "";
}

public class RefreshRequest
{
    [Required]
    [StringLength(200)]
    public string RefreshToken { get; set; } = "";
}

public record AuthResponse(string Token, DateTime ExpiresAtUtc, string RefreshToken, string Username);
