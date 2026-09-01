using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyCoinFlow.WinUI.Security;

public sealed record ActivationResult(bool IsAllowed, string Message, bool CanStartTrial);

public sealed class ActivationService
{
    private const string Secret = "MCF-LICENSE-SECRET-CHANGE-ME-ONCE-AND-KEEP-STABLE";
    private const int TrialDays = 30;

    private static string LicensePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MyCoinFlow",
        "license.json");

    private static string TrialPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MyCoinFlow",
        "trial.json");

    public ActivationResult Check()
    {
        if (TryValidateStoredLicense(out var licenseMessage))
            return new ActivationResult(true, licenseMessage, false);

        var trial = ReadTrial();
        if (trial is not null && trial.ExpiresUtc > DateTime.UtcNow)
        {
            var days = Math.Max(0, (int)Math.Ceiling((trial.ExpiresUtc - DateTime.UtcNow).TotalDays));
            return new ActivationResult(true, $"Testversion aktiv · {days} Tage verbleibend", false);
        }

        if (trial is not null)
            return new ActivationResult(false, "Die Testversion ist abgelaufen. Bitte hinterlegen Sie in MyCoinFlow 2 eine gültige Lizenz.", false);

        return new ActivationResult(false, "Keine gültige Lizenz oder Testversion vorhanden.", true);
    }

    public ActivationResult StartTrial()
    {
        if (File.Exists(TrialPath)) return Check();
        var folder = Path.GetDirectoryName(TrialPath)!;
        Directory.CreateDirectory(folder);
        var info = new TrialInfo(DateTime.UtcNow, DateTime.UtcNow.AddDays(TrialDays));
        File.WriteAllText(TrialPath, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        return Check();
    }

    private static bool TryValidateStoredLicense(out string message)
    {
        message = string.Empty;
        try
        {
            if (!File.Exists(LicensePath)) return false;
            var json = File.ReadAllText(LicensePath);
            if (json.Contains("\"Signature\"", StringComparison.OrdinalIgnoreCase) &&
                json.Contains("\"MCF2-", StringComparison.OrdinalIgnoreCase))
            {
                var payload = JsonSerializer.Deserialize<ModuleLicensePayload>(json);
                if (payload is null || string.IsNullOrWhiteSpace(payload.Key) ||
                    !payload.Key.StartsWith("MCF2-", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(payload.Signature)) return false;
                if (payload.ExpiresUtc.HasValue && payload.ExpiresUtc.Value.ToUniversalTime() < DateTime.UtcNow) return false;
                var expected = SignModulePayload(payload with { Signature = string.Empty });
                if (!FixedTimeEquals(Encoding.UTF8.GetBytes(payload.Signature), Encoding.UTF8.GetBytes(expected))) return false;
                message = $"Lizenz aktiv · {payload.Customer}";
                return true;
            }

            var file = JsonSerializer.Deserialize<LegacyLicenseFile>(json);
            if (file is null || !TryValidateLegacyKey(file.Key, out var payloadLegacy)) return false;
            message = $"Lizenz aktiv · {payloadLegacy.Customer}";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryValidateLegacyKey(string? key, out LegacyLicensePayload payload)
    {
        payload = new LegacyLicensePayload("BASIC", string.Empty, DateTime.MinValue, null);
        key = key?.Trim();
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("MCF1-", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = key[5..].Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        var payloadBytes = Base64UrlDecode(parts[0]);
        var signature = Base64UrlDecode(parts[1]);
        if (!FixedTimeEquals(signature, Hmac(payloadBytes))) return false;
        var parsed = JsonSerializer.Deserialize<LegacyLicensePayload>(Encoding.UTF8.GetString(payloadBytes));
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Edition)) return false;
        if (parsed.ExpiresUtc.HasValue && parsed.ExpiresUtc.Value.ToUniversalTime() < DateTime.UtcNow) return false;
        payload = parsed;
        return true;
    }

    private static string SignModulePayload(ModuleLicensePayload payload)
    {
        var text =
            $"{payload.Key}|{payload.Customer}|{payload.IssuedUtc.ToUniversalTime():O}|" +
            $"{(payload.ExpiresUtc.HasValue ? payload.ExpiresUtc.Value.ToUniversalTime().ToString("O") : string.Empty)}|" +
            $"{true}|{payload.Property}|{payload.Wealth}|{payload.Home}";
        return Base64UrlEncode(Hmac(Encoding.UTF8.GetBytes(text)));
    }

    private static TrialInfo? ReadTrial()
    {
        try
        {
            return File.Exists(TrialPath)
                ? JsonSerializer.Deserialize<TrialInfo>(File.ReadAllText(TrialPath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Hmac(byte[] data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return hmac.ComputeHash(data);
    }

    private static bool FixedTimeEquals(byte[] first, byte[] second) =>
        first.Length == second.Length && CryptographicOperations.FixedTimeEquals(first, second);

    private static string Base64UrlEncode(byte[] data) => Convert.ToBase64String(data)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        if (value.Length % 4 == 2) value += "==";
        else if (value.Length % 4 == 3) value += "=";
        return Convert.FromBase64String(value);
    }

    private sealed record LegacyLicenseFile(string Key, DateTime SavedUtc);
    private sealed record LegacyLicensePayload(string Edition, string Customer, DateTime IssuedUtc, DateTime? ExpiresUtc);
    private sealed record TrialInfo(DateTime StartedUtc, DateTime ExpiresUtc);
    private sealed record ModuleLicensePayload(
        string Key,
        string Customer,
        DateTime IssuedUtc,
        DateTime? ExpiresUtc,
        bool Finance,
        bool Property,
        bool Wealth,
        bool Home,
        string Signature,
        bool Dms = false,
        bool Abos = false);
}
