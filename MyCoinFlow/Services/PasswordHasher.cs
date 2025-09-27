using System;
using System.Security.Cryptography;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// PBKDF2-Hasher für Passwörter.
    /// Format: {iterations}.{saltBase64}.{hashBase64}
    /// </summary>
    public static class PasswordHasher
    {
        public static string Hash(string password, int iterations = 100_000)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            byte[] salt = RandomNumberGenerator.GetBytes(16);
            using var rfc = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            byte[] hash = rfc.GetBytes(32);
            return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string hashString)
        {
            if (password == null || string.IsNullOrWhiteSpace(hashString)) return false;

            try
            {
                var parts = hashString.Split('.');
                if (parts.Length != 3) return false;

                int iterations = int.Parse(parts[0]);
                byte[] salt = Convert.FromBase64String(parts[1]);
                byte[] expected = Convert.FromBase64String(parts[2]);

                using var rfc = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
                byte[] actual = rfc.GetBytes(32);

                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch
            {
                return false;
            }
        }
    }
}
