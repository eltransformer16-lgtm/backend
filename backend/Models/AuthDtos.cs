using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public record RegisterRequest(
    [Required, MinLength(3), MaxLength(50)] string Username,
    [Required, EmailAddress, MaxLength(120)] string Email,
    [Required, MinLength(8), MaxLength(100)] string Password,
    [Required, MinLength(8), MaxLength(100)] string PasswordConfirm,
    [Required] DateOnly BirthDate
);

public record LoginRequest(
    [Required] string UsernameOrEmail,
    [Required] string Password
);

public record RefreshRequest(
    [Required] string RefreshToken
);

public record LogoutRequest(
    [Required] string RefreshToken
);

public record UserProfileResponse(
    Guid Id,
    string Username,
    string Email,
    DateOnly BirthDate,
    string Role,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);

public record AuthResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    UserProfileResponse User
);