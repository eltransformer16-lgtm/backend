using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public class RegisterRequest
{
    [Required]
    [StringLength(30, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$", ErrorMessage = "Solo letras, números, punto, guion y guion bajo.")]
    public string Username { get; set; } = "";

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = "";
}

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public record AuthResponse(string Token, DateTime ExpiresAtUtc, string Username);
