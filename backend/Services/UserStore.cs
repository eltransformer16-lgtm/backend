using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend.Services;

public interface IUserStore
{
    Task<User?> FindByEmailAsync(string email);
    Task<bool> TryAddAsync(User user);
    Task SaveChangesAsync();
}

public class EfUserStore(AppDbContext db) : IUserStore
{
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public Task<User?> FindByEmailAsync(string email)
    {
        var normalized = Normalize(email);
        return db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);
    }

    public async Task<bool> TryAddAsync(User user)
    {
        user.NormalizedUsername = Normalize(user.Username);
        user.NormalizedEmail = Normalize(user.Email);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync() => db.SaveChangesAsync();
}
