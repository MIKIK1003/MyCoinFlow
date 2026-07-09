using System.Globalization;
using MyCoinFlow.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("MyCoinFlow License Generator V2");
Console.WriteLine("--------------------------------");
Console.WriteLine();

var license = new LicenseService();

string ReadNonEmpty(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        var s = (Console.ReadLine() ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(s))
            return s;

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

        if (string.IsNullOrEmpty(s))
            return defaultYes;

        if (s is "j" or "ja" or "y" or "yes")
            return true;

        if (s is "n" or "nein" or "no")
            return false;

        Console.WriteLine("Bitte J oder N.");
    }
}

DateTime? ReadOptionalUtcDate(string prompt)
{
    Console.Write($"{prompt} (leer = kein Ablauf): ");
    var s = (Console.ReadLine() ?? "").Trim();

    if (string.IsNullOrWhiteSpace(s))
        return null;

    if (!DateTime.TryParseExact(
            s,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var d))
    {
        Console.WriteLine("Ungültig. Bitte Format YYYY-MM-DD verwenden.");
        return ReadOptionalUtcDate(prompt);
    }

    return new DateTime(d.Year, d.Month, d.Day, 23, 59, 59, DateTimeKind.Utc);
}

static string CleanFilePart(string value)
{
    var invalid = Path.GetInvalidFileNameChars();

    foreach (var c in invalid)
        value = value.Replace(c, '_');

    return value.Trim().Replace(" ", "_");
}

var customer = ReadNonEmpty("Kunde (frei wählbar): ");

Console.WriteLine();
Console.WriteLine("Module");
Console.WriteLine("------");

var finance = true;
Console.WriteLine("Finanzen: immer aktiv");

var property = ReadYesNo("Immobilien aktivieren?", defaultYes: true);
var wealth = ReadYesNo("Wealth aktivieren?", defaultYes: false);
var home = ReadYesNo("Haushalt aktivieren?", defaultYes: false);
var dms = ReadYesNo("DMS aktivieren?", defaultYes: false);

var expiresUtc = ReadOptionalUtcDate("Ablaufdatum (UTC, YYYY-MM-DD)");

var payload = license.GenerateModuleLicense(
    customer: customer,
    finance: finance,
    property: property,
    wealth: wealth,
    home: home,
    expiresUtc: expiresUtc,
    dms: dms);

var outDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
    "MyCoinFlow_Lizenzen");

Directory.CreateDirectory(outDir);

var fileName =
    $"license_{CleanFilePart(customer)}_{payload.Key}.json";

var filePath = Path.Combine(outDir, fileName);

license.SaveModuleLicenseToFile(payload, filePath);

Console.WriteLine();
Console.WriteLine("KEY:");
Console.WriteLine(payload.Key);

Console.WriteLine();
Console.WriteLine("DATEI:");
Console.WriteLine(filePath);

Console.WriteLine();
Console.WriteLine("Module:");
Console.WriteLine($"Finanzen:   aktiv");
Console.WriteLine($"Immobilien: {(payload.Property ? "aktiv" : "nicht aktiv")}");
Console.WriteLine($"Wealth:     {(payload.Wealth ? "aktiv" : "nicht aktiv")}");
Console.WriteLine($"Haushalt:   {(payload.Home ? "aktiv" : "nicht aktiv")}");
Console.WriteLine($"DMS:        {(payload.Dms ? "aktiv" : "nicht aktiv")}");

Console.WriteLine();
Console.WriteLine("→ Diese JSON-Datei in MyCoinFlow unter Einstellungen → Lizenz importieren.");
Console.WriteLine();
Console.WriteLine("Taste drücken zum Beenden...");
Console.ReadKey();