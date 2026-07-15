using System.Security.Cryptography;

namespace Erp.Shared.Infrastructure;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}

// Salted PBKDF2 (Phase 4 §3). The iteration count is stored inside each hash, so raising it later
// doesn't break existing passwords.
public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16, KeySize = 32, Iterations = 600_000;

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('.', 3);
        var iterations = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var key = Convert.FromBase64String(parts[2]);
        var attempt = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, key.Length);
        return CryptographicOperations.FixedTimeEquals(attempt, key);
    }
}
