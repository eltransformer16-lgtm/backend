namespace backend.Models;

public class JwtSettings
{
    public string Issuer { get; set; } = "backend";
    public string Audience { get; set; } = "backend-clients";
    public string Key { get; set; } = "";

    // Duración del token de acceso (corta).
    public int ExpiresMinutes { get; set; } = 15;

    // Duración del refresh token (permite renovar la sesión sin pedir la contraseña).
    public int RefreshTokenDays { get; set; } = 7;
}
