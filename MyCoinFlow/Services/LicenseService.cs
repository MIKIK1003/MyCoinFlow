using Microsoft.Win32;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Offline-Lizenzsystem für MyCoinFlow.
    ///
    /// MCF1:
    /// - alter langer Key
    /// - Basic / Plus
    ///
    /// MCF2:
    /// - kurzer Key
    /// - signierte Lizenzdatei
    /// - Modulrechte: Finance, Property, Wealth, Home
    /// </summary>
    public sealed class LicenseService
    {
        private const string Secret = "MCF-LICENSE-SECRET-CHANGE-ME-ONCE-AND-KEEP-STABLE";

        private static string LicenseFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyCoinFlow");

        private static string LicensePath =>
            Path.Combine(LicenseFolder, "license.json");

        public record LicensePayload(
            string Edition,
            string Customer,
            DateTime IssuedUtc,
            DateTime? ExpiresUtc);

        public record ModuleLicensePayload(
            string Key,
            string Customer,
            DateTime IssuedUtc,
            DateTime? ExpiresUtc,
            bool Finance,
            bool Property,
            bool Wealth,
            bool Home,
            string Signature);

        private record LicenseFile(string Key, DateTime SavedUtc);

        public bool TryLoadAndApply(out string message)
        {
            message = "";

            try
            {
                if (!File.Exists(LicensePath))
                {
                    AppEdition.SetPlus(false);
                    AppModules.ResetToBasic();
                    message = "Keine Lizenz gefunden. Nur Finanzen aktiv.";
                    return false;
                }

                var json = File.ReadAllText(LicensePath);

                if (json.Contains("\"Signature\"", StringComparison.OrdinalIgnoreCase) &&
                    json.Contains("\"MCF2-", StringComparison.OrdinalIgnoreCase))
                {
                    return TryLoadMcf2(json, out message);
                }

                return TryLoadMcf1(json, out message);
            }
            catch (Exception ex)
            {
                AppEdition.SetPlus(false);
                AppModules.ResetToBasic();
                message = "Lizenzprüfung fehlgeschlagen. Nur Finanzen aktiv: " + ex.Message;
                return false;
            }
        }

        public void SaveKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key darf nicht leer sein.", nameof(key));

            Directory.CreateDirectory(LicenseFolder);

            var file = new LicenseFile(key.Trim(), DateTime.UtcNow);
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(LicensePath, json);
        }

        public bool ImportLicenseFile(Window? owner, out string message)
        {
            message = "";

            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "MyCoinFlow Lizenzdatei importieren",
                    Filter = "MyCoinFlow Lizenz (*.json)|*.json|Alle Dateien (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                var result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
                if (result != true)
                {
                    message = "Import abgebrochen.";
                    return false;
                }

                var json = File.ReadAllText(dlg.FileName);

                if (!TryValidateModuleLicenseJson(json, out var payload, out var err))
                {
                    message = "Lizenzdatei ungültig: " + err;
                    return false;
                }

                Directory.CreateDirectory(LicenseFolder);
                File.WriteAllText(LicensePath, json);

                ApplyModulePayload(payload);

                message =
                    $"Lizenz importiert.\n" +
                    $"Kunde: {payload.Customer}\n\n" +
                    AppModules.GetStatusText();

                return true;
            }
            catch (Exception ex)
            {
                message = "Lizenzimport fehlgeschlagen: " + ex.Message;
                return false;
            }
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
                    error = "Nur alte MCF1-Keys können direkt eingegeben werden. MCF2 wird über Lizenzdatei importiert.";
                    return false;
                }

                var rest = key.Substring(5);
                var parts = rest.Split('.', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length != 2)
                {
                    error = "Falsches Format.";
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

                if (parsed.ExpiresUtc.HasValue &&
                    parsed.ExpiresUtc.Value.ToUniversalTime() < DateTime.UtcNow)
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

        public string GenerateKey(string customer, bool plus, DateTime? expiresUtc)
        {
            var payload = new LicensePayload(
                Edition: plus ? "PLUS" : "BASIC",
                Customer: customer ?? "",
                IssuedUtc: DateTime.UtcNow,
                ExpiresUtc: expiresUtc?.ToUniversalTime());

            var payloadJson = JsonSerializer.Serialize(payload);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            var sigBytes = Hmac(payloadBytes);

            return "MCF1-" + Base64UrlEncode(payloadBytes) + "." + Base64UrlEncode(sigBytes);
        }

        public ModuleLicensePayload GenerateModuleLicense(
            string customer,
            bool finance,
            bool property,
            bool wealth,
            bool home,
            DateTime? expiresUtc)
        {
            var key = GenerateShortKey();

            var unsigned = new ModuleLicensePayload(
                Key: key,
                Customer: customer ?? "",
                IssuedUtc: DateTime.UtcNow,
                ExpiresUtc: expiresUtc?.ToUniversalTime(),
                Finance: true,
                Property: property,
                Wealth: wealth,
                Home: home,
                Signature: "");

            var signature = SignModulePayload(unsigned);

            return unsigned with { Signature = signature };
        }

        public void SaveModuleLicenseToFile(ModuleLicensePayload payload, string filePath)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Dateipfad fehlt.", nameof(filePath));

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        private bool TryLoadMcf1(string json, out string message)
        {
            message = "";

            var file = JsonSerializer.Deserialize<LicenseFile>(json);

            if (file == null || string.IsNullOrWhiteSpace(file.Key))
            {
                AppEdition.SetPlus(false);
                AppModules.ResetToBasic();
                message = "Lizenzdatei ungültig. Nur Finanzen aktiv.";
                return false;
            }

            if (!TryValidate(file.Key, out var payload, out var err))
            {
                AppEdition.SetPlus(false);
                AppModules.ResetToBasic();
                message = "Lizenz ungültig: " + err;
                return false;
            }

            var isPlus = payload.Edition.Equals("PLUS", StringComparison.OrdinalIgnoreCase);

            AppEdition.SetPlus(isPlus);

            AppModules.SetModules(
                finance: true,
                property: isPlus,
                wealth: false,
                home: false);

            message = isPlus
                ? $"Lizenz OK (MCF1 Plus). Kunde: {payload.Customer}\n\n{AppModules.GetStatusText()}"
                : $"Lizenz OK (MCF1 Basic). Kunde: {payload.Customer}\n\n{AppModules.GetStatusText()}";

            return true;
        }

        private bool TryLoadMcf2(string json, out string message)
        {
            message = "";

            if (!TryValidateModuleLicenseJson(json, out var payload, out var err))
            {
                AppEdition.SetPlus(false);
                AppModules.ResetToBasic();
                message = "Lizenzdatei ungültig: " + err;
                return false;
            }

            ApplyModulePayload(payload);

            message =
                $"Lizenz OK (MCF2).\n" +
                $"Kunde: {payload.Customer}\n\n" +
                AppModules.GetStatusText();

            return true;
        }

        private void ApplyModulePayload(ModuleLicensePayload payload)
        {
            var plusCompat = payload.Property || payload.Wealth || payload.Home;

            AppEdition.SetPlus(plusCompat);

            AppModules.SetModules(
                finance: true,
                property: payload.Property,
                wealth: payload.Wealth,
                home: payload.Home);
        }

        private bool TryValidateModuleLicenseJson(
            string json,
            out ModuleLicensePayload payload,
            out string error)
        {
            payload = new ModuleLicensePayload("", "", DateTime.MinValue, null, true, false, false, false, "");
            error = "";

            try
            {
                var parsed = JsonSerializer.Deserialize<ModuleLicensePayload>(json);

                if (parsed == null)
                {
                    error = "Datei konnte nicht gelesen werden.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(parsed.Key) ||
                    !parsed.Key.StartsWith("MCF2-", StringComparison.OrdinalIgnoreCase))
                {
                    error = "MCF2-Key fehlt.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(parsed.Signature))
                {
                    error = "Signatur fehlt.";
                    return false;
                }

                if (parsed.ExpiresUtc.HasValue &&
                    parsed.ExpiresUtc.Value.ToUniversalTime() < DateTime.UtcNow)
                {
                    error = "Lizenz abgelaufen.";
                    return false;
                }

                var expected = SignModulePayload(parsed with { Signature = "" });

                if (!FixedTimeEquals(
                        Encoding.UTF8.GetBytes(parsed.Signature),
                        Encoding.UTF8.GetBytes(expected)))
                {
                    error = "Signatur stimmt nicht.";
                    return false;
                }

                payload = parsed with { Finance = true };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string SignModulePayload(ModuleLicensePayload payload)
        {
            var text =
                $"{payload.Key}|{payload.Customer}|{payload.IssuedUtc.ToUniversalTime():O}|" +
                $"{(payload.ExpiresUtc.HasValue ? payload.ExpiresUtc.Value.ToUniversalTime().ToString("O") : "")}|" +
                $"{true}|{payload.Property}|{payload.Wealth}|{payload.Home}";

            var bytes = Encoding.UTF8.GetBytes(text);
            var sig = Hmac(bytes);

            return Base64UrlEncode(sig);
        }

        private static string GenerateShortKey()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

            string Part()
            {
                Span<byte> bytes = stackalloc byte[4];
                RandomNumberGenerator.Fill(bytes);

                var sb = new StringBuilder(4);
                for (int i = 0; i < 4; i++)
                    sb.Append(chars[bytes[i] % chars.Length]);

                return sb.ToString();
            }

            return $"MCF2-{Part()}-{Part()}-{Part()}";
        }

        private static byte[] Hmac(byte[] payloadBytes)
        {
            var secretBytes = Encoding.UTF8.GetBytes(Secret);
            using var h = new HMACSHA256(secretBytes);
            return h.ComputeHash(payloadBytes);
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

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
                case 2:
                    s += "==";
                    break;
                case 3:
                    s += "=";
                    break;
            }

            return Convert.FromBase64String(s);
        }
    }
}