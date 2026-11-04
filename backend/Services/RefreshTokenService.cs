using System.Security.Cryptography;
using System.Text;
using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.Services;

public record RotationResult(User User, string RefreshToken);

public class RefreshTokenService(AppDbContext db, IOptions<JwtSettings> options)
{
    private readonly JwtSettings _settings = options.Value;

    private static string NewRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private RefreshToken CreateEntity(Guid userId, string rawToken) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = Hash(rawToken),
        CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(_settings.RefreshTokenDays)
    };

    public async Task<string> IssueAsync(Guid userId)
    {
        var raw = NewRawToken();
        db.RefreshTokens.Add(CreateEntity(userId, raw));
        await db.SaveChangesAsync();
        return raw;
    }

    // Cada refresh token sirve una sola vez: al usarlo se revoca y se entrega uno nuevo.
    public async Task<RotationResult?> RotateAsync(string rawToken)
    {
        var hash = Hash(rawToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (stored is null)
            return null;

        if (stored.RevokedAtUtc is not null)
        {
            // Se intentó reutilizar un token ya usado: posible robo.
            // Se cierran todas las sesiones de ese usuario.
            await RevokeAllAsync(stored.UserId);
            return null;
        }

        if (stored.ExpiresAtUtc <= DateTime.UtcNow || !stored.User.IsActive)
            return null;

        stored.RevokedAtUtc = DateTime.UtcNow;
        var newRaw = NewRawToken();
        db.RefreshTokens.Add(CreateEntity(stored.UserId, newRaw));
        await db.SaveChangesAsync();

        return new RotationResult(stored.User, newRaw);
    }

    public async Task RevokeAsync(string rawToken)
    {
        var hash = Hash(rawToken);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAtUtc == null);

        if (stored is null)
            return;

        stored.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RevokeAllAsync(Guid userId)
    {
        var now = (DateTime?)DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now));
    }
}
