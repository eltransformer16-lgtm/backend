using System.Collections.Concurrent;
using backend.Models;

namespace backend.Services;

public interface IUserStore
{
    User? FindByUsername(string username);
    bool TryAdd(User user);
}

// Almacén en memoria: los usuarios se pierden al reiniciar la app.
// Para persistir datos se puede reemplazar por una implementación con EF Core.
public class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<string, User> _users =
        new(StringComparer.OrdinalIgnoreCase);

    public User? FindByUsername(string username) =>
        _users.TryGetValue(username, out var user) ? user : null;

    public bool TryAdd(User user) => _users.TryAdd(user.Username, user);
}
