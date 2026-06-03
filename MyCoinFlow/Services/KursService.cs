using System;
using System.Collections.Generic;
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

        public async Task<List<SymbolSucheResult>> SucheInstrumenteAsync(
            string suchtext,
            string apiKey)
        {
            var list = new List<SymbolSucheResult>();

            if (string.IsNullOrWhiteSpace(suchtext))
                return list;

            if (string.IsNullOrWhiteSpace(apiKey))
                return list;

            var url =
                $"https://eodhd.com/api/search/{Uri.EscapeDataString(suchtext.Trim())}" +
                $"?api_token={Uri.EscapeDataString(apiKey)}&fmt=json";

            try
            {
                using var response = await _http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return list;

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return list;

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var code = ReadString(item, "Code");
                    var exchange = ReadString(item, "Exchange");
                    var name = ReadString(item, "Name");
                    var type = ReadString(item, "Type");
                    var country = ReadString(item, "Country");
                    var currency = ReadString(item, "Currency");
                    var isin = ReadString(item, "ISIN");

                    if (string.IsNullOrWhiteSpace(code))
                        continue;

                    list.Add(new SymbolSucheResult
                    {
                        Symbol = code.Trim().ToUpperInvariant(),
                        Boerse = MapEodhdExchangeToMyCoinFlow(exchange),
                        EodhdExchange = exchange,
                        Titel = name,
                        Typ = type,
                        Land = country,
                        Waehrung = currency,
                        ISIN = isin
                    });
                }

                return list;
            }
            catch
            {
                return list;
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

        private static string MapEodhdExchangeToMyCoinFlow(string exchange)
        {
            var e = (exchange ?? "").Trim().ToUpperInvariant();

            return e switch
            {
                "SW" => "SIX",
                "US" => "NASDAQ",
                "XETRA" => "XETRA",
                "PA" => "EURONEXT",
                "LSE" => "LSE",
                _ => e
            };
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var prop))
                return "";

            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? "";

            return prop.ToString();
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

    public class SymbolSucheResult
    {
        public string Titel { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Boerse { get; set; } = "";
        public string EodhdExchange { get; set; } = "";
        public string ISIN { get; set; } = "";
        public string Typ { get; set; } = "";
        public string Land { get; set; } = "";
        public string Waehrung { get; set; } = "";

        public string AnzeigeText =>
            $"{Titel} · {Symbol} · {Boerse} · {ISIN}".Trim();
    }
}