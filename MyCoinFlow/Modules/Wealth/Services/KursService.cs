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

        public async Task<KursResult?> HoleAktuellenKursAsync(string symbol, string boerse, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(apiKey))
                return null;

            var eodhdCode = BuildEodhdCode(symbol, boerse);
            if (string.IsNullOrWhiteSpace(eodhdCode))
                return null;

            return await HoleRealtimeKursAsync(eodhdCode, symbol, boerse, apiKey);
        }

        public async Task<FxKursResult?> HoleFxKursAsync(string vonWaehrung, string nachWaehrung, string apiKey)
        {
            var von = (vonWaehrung ?? "").Trim().ToUpperInvariant();
            var nach = (nachWaehrung ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(von) || string.IsNullOrWhiteSpace(nach) || string.IsNullOrWhiteSpace(apiKey))
                return null;

            if (von == nach)
            {
                return new FxKursResult
                {
                    VonWaehrung = von,
                    NachWaehrung = nach,
                    EodhdCode = $"{von}{nach}.FOREX",
                    Kurs = 1m,
                    KursDatum = DateTime.Today
                };
            }

            var eodhdCode = $"{von}{nach}.FOREX";
            var result = await HoleRealtimeKursAsync(eodhdCode, von, "FOREX", apiKey);

            if (result == null)
                return null;

            return new FxKursResult
            {
                VonWaehrung = von,
                NachWaehrung = nach,
                EodhdCode = eodhdCode,
                Kurs = result.Kurs,
                KursDatum = result.KursDatum
            };
        }

        // Historische Tagesschlusskurse über den EOD-Endpunkt.
        // Liefert alle verfügbaren Handelstage im Bereich [von, bis] mit einem einzigen API-Aufruf.
        public async Task<List<EodKursTag>> HoleEodHistorieAsync(
            string symbol,
            string boerse,
            string apiKey,
            DateTime von,
            DateTime bis)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(apiKey))
                return new List<EodKursTag>();

            var eodhdCode = BuildEodhdCode(symbol, boerse);
            if (string.IsNullOrWhiteSpace(eodhdCode))
                return new List<EodKursTag>();

            return await HoleEodHistorieInternAsync(eodhdCode, apiKey, von, bis);
        }

        public async Task<List<EodKursTag>> HoleFxHistorieAsync(
            string vonWaehrung,
            string nachWaehrung,
            string apiKey,
            DateTime von,
            DateTime bis)
        {
            var v = (vonWaehrung ?? "").Trim().ToUpperInvariant();
            var n = (nachWaehrung ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(n) ||
                v == n || string.IsNullOrWhiteSpace(apiKey))
                return new List<EodKursTag>();

            return await HoleEodHistorieInternAsync($"{v}{n}.FOREX", apiKey, von, bis);
        }

        private static async Task<List<EodKursTag>> HoleEodHistorieInternAsync(
            string eodhdCode,
            string apiKey,
            DateTime von,
            DateTime bis)
        {
            var list = new List<EodKursTag>();

            var url =
                $"https://eodhd.com/api/eod/{Uri.EscapeDataString(eodhdCode)}" +
                $"?api_token={Uri.EscapeDataString(apiKey)}&fmt=json&period=d" +
                $"&from={von:yyyy-MM-dd}&to={bis:yyyy-MM-dd}";

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
                    var datumText = ReadString(item, "date");
                    if (!DateTime.TryParseExact(
                            datumText,
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var datum))
                        continue;

                    var close = ReadDecimal(item, "close");
                    if (!close.HasValue || close.Value <= 0)
                        continue;

                    list.Add(new EodKursTag
                    {
                        Datum = datum.Date,
                        Kurs = close.Value
                    });
                }

                return list;
            }
            catch
            {
                return list;
            }
        }

        public async Task<List<SymbolSucheResult>> SucheInstrumenteAsync(string suchtext, string apiKey)
        {
            var list = new List<SymbolSucheResult>();

            if (string.IsNullOrWhiteSpace(suchtext) || string.IsNullOrWhiteSpace(apiKey))
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
                    if (string.IsNullOrWhiteSpace(code))
                        continue;

                    var exchange = ReadString(item, "Exchange");

                    list.Add(new SymbolSucheResult
                    {
                        Symbol = code.Trim().ToUpperInvariant(),
                        Boerse = MapEodhdExchangeToMyCoinFlow(exchange),
                        EodhdExchange = exchange,
                        Titel = ReadString(item, "Name"),
                        Typ = ReadString(item, "Type"),
                        Land = ReadString(item, "Country"),
                        Waehrung = ReadString(item, "Currency"),
                        ISIN = ReadString(item, "ISIN")
                    });
                }

                return list;
            }
            catch
            {
                return list;
            }
        }

        private static async Task<KursResult?> HoleRealtimeKursAsync(
            string eodhdCode,
            string symbol,
            string boerse,
            string apiKey)
        {
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

            return prop.ValueKind == JsonValueKind.String
                ? prop.GetString() ?? ""
                : prop.ToString();
        }

        private static decimal? ReadDecimal(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var prop))
                return null;

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var value))
                return value;

            if (prop.ValueKind == JsonValueKind.String &&
                decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
        }

        private static long? ReadUnixTimestamp(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var prop))
                return null;

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var value))
                return value;

            if (prop.ValueKind == JsonValueKind.String &&
                long.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
        }
    }

    public class EodKursTag
    {
        public DateTime Datum { get; set; }
        public decimal Kurs { get; set; }
    }

    public class KursResult
    {
        public string Symbol { get; set; } = "";
        public string Boerse { get; set; } = "";
        public string EodhdCode { get; set; } = "";
        public decimal Kurs { get; set; }
        public DateTime KursDatum { get; set; }
    }

    public class FxKursResult
    {
        public string VonWaehrung { get; set; } = "";
        public string NachWaehrung { get; set; } = "";
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

        public string AnzeigeText => $"{Titel} · {Symbol} · {Boerse} · {ISIN}".Trim();
    }
}