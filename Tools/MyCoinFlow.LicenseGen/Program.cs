using System.Globalization;
using MyCoinFlow.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("MyCoinFlow License Generator");
Console.WriteLine("----------------------------");
Console.WriteLine();

var license = new LicenseService();

string ReadNonEmpty(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        var s = (Console.ReadLine() ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(s)) return s;
        Console.WriteLine("Bitte Eingabe machen.");
    }
}

bool ReadYesNo(string prompt, bool defaultYes)
{
    var def = defaultYes ? "J/n" : "j/N";
    while (true)
    {
        Console.Write($"{prompt} ({def}): ");
        var s = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) return defaultYes;
        if (s is "j" or "ja" or "y" or "yes") return true;
        if (s is "n" or "nein" or "no") return false;
        Console.WriteLine("Bitte J oder N.");
    }
}

DateTime? ReadOptionalUtcDate(string prompt)
{
    Console.Write($"{prompt} (leer = kein Ablauf): ");
    var s = (Console.ReadLine() ?? "").Trim();
    if (string.IsNullOrWhiteSpace(s)) return null;

    if (!DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
    {
        Console.WriteLine("Ungültig. Bitte Format YYYY-MM-DD verwenden.");
        return ReadOptionalUtcDate(prompt);
    }

    return new DateTime(d.Year, d.Month, d.Day, 23, 59, 59, DateTimeKind.Utc);
}

var customer = ReadNonEmpty("Kunde (frei wählbar): ");
var isPlus = ReadYesNo("Edition PLUS?", defaultYes: true);
var expiresUtc = ReadOptionalUtcDate("Ablaufdatum (UTC, YYYY-MM-DD)");

var key = license.GenerateKey(customer, isPlus, expiresUtc);

Console.WriteLine();
Console.WriteLine("KEY:");
Console.WriteLine(key);
Console.WriteLine();
Console.WriteLine("→ Diesen Key in MyCoinFlow im Admin → Lizenz einfügen.");
