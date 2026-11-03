using System.Security.Cryptography;

namespace backend.Services;

public class PasswordService
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600_000;

    // Hash falso para igualar el tiempo de respuesta cuando el usuario no existe.
    public string DummyHash { get; }

    public PasswordService()
    {
        DummyHash = Hash(Guid.NewGuid().ToString());
    }

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
