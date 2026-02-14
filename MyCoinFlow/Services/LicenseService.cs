using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Offline-Lizenzsystem (Basic/Plus) mit signierten Keys (HMAC-SHA256).
    ///
    /// Key-Format:
    ///   MCF1-<payloadBase64Url>.<sigBase64Url>
    ///
    /// Payload JSON:
    ///   { "Edition":"PLUS", "Customer":"...", "IssuedUtc":"2026-02-13T...", "ExpiresUtc":null }
    ///
    /// Signatur:
    ///   HMACSHA256(secret, payloadBytes)
    ///
    /// Speicherung:
    ///   C:\ProgramData\MyCoinFlow\license.json
    /// </summary>
    public sealed class LicenseService
    {
        // ✅ Dein "Geheimnis" (einfach, offline). Bitte nach GoLive einmal fixieren und nicht ändern.
        // Achtung: wer dekompiliert, kann es finden. Für dein einfaches System ok.
        private const string Secret = "MCF-LICENSE-SECRET-CHANGE-ME-ONCE-AND-KEEP-STABLE";

        private static string LicensePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyCoinFlow", "license.json");

        public record LicensePayload(string Edition, string Customer, DateTime IssuedUtc, DateTime? ExpiresUtc);

        private record LicenseFile(string Key, DateTime SavedUtc);

        public bool TryLoadAndApply(out string message)
        {
            message = "";

            try
            {
                if (!File.Exists(LicensePath))
                {
                    AppEdition.SetPlus(false);
                    message = "Keine Lizenz gefunden (Basic aktiv).";
                    return false;
                }

                var json = File.ReadAllText(LicensePath);
                var file = JsonSerializer.Deserialize<LicenseFile>(json);

                if (file == null || string.IsNullOrWhiteSpace(file.Key))
                {
                    AppEdition.SetPlus(false);
                    message = "Lizenzdatei ungültig (Basic aktiv).";
                    return false;
                }

                if (!TryValidate(file.Key, out var payload, out var err))
                {
                    AppEdition.SetPlus(false);
                    message = "Lizenz ungültig: " + err;
                    return false;
                }

                var isPlus = payload.Edition.Equals("PLUS", StringComparison.OrdinalIgnoreCase);
                AppEdition.SetPlus(isPlus);

                message = isPlus
                    ? $"Lizenz OK (Plus). Kunde: {payload.Customer}"
                    : "Lizenz OK (Basic).";

                return isPlus;
            }
            catch (Exception ex)
            {
                AppEdition.SetPlus(false);
                message = "Lizenzprüfung fehlgeschlagen (Basic aktiv): " + ex.Message;
                return false;
            }
        }

        public void SaveKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key darf nicht leer sein.", nameof(key));

            var dir = Path.GetDirectoryName(LicensePath)!;
            Directory.CreateDirectory(dir);

            var file = new LicenseFile(key.Trim(), DateTime.UtcNow);
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LicensePath, json);
        }

        public bool TryValidate(string key, out LicensePayload payload, out string error)
        {
            payload = new LicensePayload("BASIC", "", DateTime.MinValue, null);
            error = "";

            try
            {
                key = (key ?? "").Trim();
                if (!key.StartsWith("MCF1-", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Falsches Format (Prefix fehlt).";
                    return false;
                }

                var rest = key.Substring(5);
                var parts = rest.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    error = "Falsches Format (payload.sig erwartet).";
                    return false;
                }

                var payloadBytes = Base64UrlDecode(parts[0]);
                var sigBytes = Base64UrlDecode(parts[1]);

                var expected = Hmac(payloadBytes);

                if (!FixedTimeEquals(sigBytes, expected))
                {
                    error = "Signatur stimmt nicht.";
                    return false;
                }

                var payloadJson = Encoding.UTF8.GetString(payloadBytes);
                var parsed = JsonSerializer.Deserialize<LicensePayload>(payloadJson);

                if (parsed == null || string.IsNullOrWhiteSpace(parsed.Edition))
                {
                    error = "Payload ungültig.";
                    return false;
                }

                if (parsed.ExpiresUtc.HasValue && parsed.ExpiresUtc.Value.ToUniversalTime() < DateTime.UtcNow)
                {
                    error = "Lizenz abgelaufen.";
                    return false;
                }

                payload = parsed;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ---------- Key-Generator (für dein Tool) ----------
        public string GenerateKey(string customer, bool plus, DateTime? expiresUtc)
        {
            var payload = new LicensePayload(
                Edition: plus ? "PLUS" : "BASIC",
                Customer: customer ?? "",
                IssuedUtc: DateTime.UtcNow,
                ExpiresUtc: expiresUtc?.ToUniversalTime()
            );

            var payloadJson = JsonSerializer.Serialize(payload);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            var sigBytes = Hmac(payloadBytes);

            return "MCF1-" + Base64UrlEncode(payloadBytes) + "." + Base64UrlEncode(sigBytes);
        }

        private static byte[] Hmac(byte[] payloadBytes)
        {
            var secretBytes = Encoding.UTF8.GetBytes(Secret);
            using var h = new HMACSHA256(secretBytes);
            return h.ComputeHash(payloadBytes);
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }
}
