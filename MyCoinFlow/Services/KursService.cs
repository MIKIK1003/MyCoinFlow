using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyCoinFlow.Services
{
    public class KursService
    {
        private static readonly HttpClient _http = new();

        public async Task<KursResult?> HoleAktuellenKursAsync(
            string symbol,
            string boerse,
            string apiKey)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            if (string.IsNullOrWhiteSpace(apiKey))
                return null;

            var eodhdCode = BuildEodhdCode(symbol, boerse);
            if (string.IsNullOrWhiteSpace(eodhdCode))
                return null;

            var url =
                $"https://eodhd.com/api/real-time/{Uri.EscapeDataString(eodhdCode)}" +
                $"?api_token={Uri.EscapeDataString(apiKey)}&fmt=json";

            try
            {
                using var response = await _http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var close = ReadDecimal(root, "close");
                if (!close.HasValue || close.Value <= 0)
                    close = ReadDecimal(root, "previousClose");

                if (!close.HasValue || close.Value <= 0)
                    return null;

                var timestamp = ReadUnixTimestamp(root, "timestamp");
                var kursDatum = timestamp.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).LocalDateTime.Date
                    : DateTime.Today;

                return new KursResult
                {
                    Symbol = symbol.Trim().ToUpperInvariant(),
                    Boerse = (boerse ?? "").Trim().ToUpperInvariant(),
                    EodhdCode = eodhdCode,
                    Kurs = close.Value,
                    KursDatum = kursDatum
                };
            }
            catch
            {
                return null;
            }
        }

        private static string BuildEodhdCode(string symbol, string boerse)
        {
            var s = (symbol ?? "").Trim().ToUpperInvariant();
            var b = (boerse ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(s))
                return "";

            if (s.Contains('.'))
                return s;

            return b switch
            {
                "SIX" => $"{s}.SW",
                "NYSE" => $"{s}.US",
                "NASDAQ" => $"{s}.US",
                "XETRA" => $"{s}.XETRA",
                "EURONEXT" => $"{s}.PA",
                "LSE" => $"{s}.LSE",
                _ => s
            };
        }

        private static decimal? ReadDecimal(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var prop))
                return null;

            if (prop.ValueKind == JsonValueKind.Number &&
                prop.TryGetDecimal(out var value))
            {
                return value;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString();

                if (decimal.TryParse(
                        text,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static long? ReadUnixTimestamp(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var prop))
                return null;

            if (prop.ValueKind == JsonValueKind.Number &&
                prop.TryGetInt64(out var value))
            {
                return value;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString();

                if (long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }

            return null;
        }
    }

    public class KursResult
    {
        public string Symbol { get; set; } = "";
        public string Boerse { get; set; } = "";
        public string EodhdCode { get; set; } = "";
        public decimal Kurs { get; set; }
        public DateTime KursDatum { get; set; }
    }
}