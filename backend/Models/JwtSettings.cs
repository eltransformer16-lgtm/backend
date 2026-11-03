namespace backend.Models;

public class JwtSettings
{
    public string Issuer { get; set; } = "backend";
    public string Audience { get; set; } = "backend-clients";
    public string Key { get; set; } = "";
    public int ExpiresMinutes { get; set; } = 60;
}
