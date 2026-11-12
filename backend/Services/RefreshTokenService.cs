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

    private void Assign(RefreshToken token, string rawToken)
    {
        token.TokenHash = Hash(rawToken);
        token.CreatedAtUtc = DateTime.UtcNow;
        token.ExpiresAtUtc = DateTime.UtcNow.AddDays(_settings.RefreshTokenDays);
    }

    // Cada usuario tiene como máximo un refresh token: emitir uno nuevo reemplaza al anterior.
    public async Task<string> IssueAsync(Guid userId)
    {
        var raw = NewRawToken();

        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.UserId == userId);
        if (stored is null)
        {
            stored = new RefreshToken { Id = Guid.NewGuid(), UserId = userId };
            db.RefreshTokens.Add(stored);
        }

        Assign(stored, raw);
        await db.SaveChangesAsync();
        return raw;
    }

    // Al renovar, el token anterior queda invalidado y se entrega uno nuevo.
    public async Task<RotationResult?> RotateAsync(string rawToken)
    {
        var hash = Hash(rawToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (stored is null)
            return null;

        if (stored.ExpiresAtUtc <= DateTime.UtcNow || !stored.User.IsActive)
        {
            db.RefreshTokens.Remove(stored);
            await db.SaveChangesAsync();
            return null;
        }

        var newRaw = NewRawToken();
        Assign(stored, newRaw);
        await db.SaveChangesAsync();

        return new RotationResult(stored.User, newRaw);
    }

    // Revocar = borrar la asignación: el token deja de existir y no se puede usar.
    public async Task RevokeAsync(string rawToken)
    {
        var hash = Hash(rawToken);
        await db.RefreshTokens.Where(t => t.TokenHash == hash).ExecuteDeleteAsync();
    }
}
