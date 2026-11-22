using System.Security.Cryptography;
using System.Text;
using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public class RefreshTokenService
{
    private readonly AppDbContext _db;
    private readonly int _refreshDays;

    public RefreshTokenService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _refreshDays = int.TryParse(config["Jwt:RefreshTokenDays"], out var days) ? days : 7;
    }

    public async Task<string> IssueAsync(Guid userId)
    {
        // Revocar cualquier token activo previo de este usuario (máximo 1 activo a la vez)
        await RevokeAllForUserAsync(userId);

        var rawToken = GenerateSecureToken();
        var tokenHash = HashToken(rawToken);

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_refreshDays)
        };

        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync();

        return rawToken;
    }

    public async Task<(bool Ok, User? User, string? NewRefreshToken, string? Error)> RotateAsync(string rawRefreshToken)
    {
        var hash = HashToken(rawRefreshToken);
        var existing = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash);

        if (existing is null || existing.User is null)
            return (false, null, null, "Refresh token inválido.");

        if (existing.RevokedAtUtc is not null)
        {
            // Detección de reuso: revocar todo por seguridad
            await RevokeAllForUserAsync(existing.UserId);
            return (false, null, null, "Refresh token revocado.");
        }

        if (existing.ExpiresAtUtc <= DateTime.UtcNow)
            return (false, null, null, "Refresh token expirado.");

        if (!existing.User.IsActive)
            return (false, null, null, "El usuario está desactivado.");

        var newRaw = GenerateSecureToken();
        var newHash = HashToken(newRaw);

        existing.RevokedAtUtc = DateTime.UtcNow;
        existing.ReplacedByTokenHash = newHash;

        var replacement = new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = newHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_refreshDays)
        };

        _db.RefreshTokens.Add(replacement);
        await _db.SaveChangesAsync();

        return (true, existing.User, newRaw, null);
    }

    public async Task<bool> RevokeAsync(string rawRefreshToken)
    {
        var hash = HashToken(rawRefreshToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash);
        if (existing is null || existing.RevokedAtUtc is not null)
            return false;

        existing.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task RevokeAllForUserAsync(Guid userId)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null)
            .ToListAsync();

        if (activeTokens.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var token in activeTokens)
        {
            token.RevokedAtUtc = now;
        }

        await _db.SaveChangesAsync();
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}