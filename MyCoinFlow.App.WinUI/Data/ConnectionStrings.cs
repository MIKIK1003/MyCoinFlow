using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace MyCoinFlow.WinUI.Data;

public static class ConnectionStrings
{
    private const string DefaultServer = @".\SQLEXPRESS";
    private const string DefaultDatabase = "MyCoinFlowDB";
    private static string? _activeDatabase;

    public static string ActiveDatabaseName => _activeDatabase ??= LoadActiveDatabase();

    public static string Current => new SqlConnectionStringBuilder
    {
        DataSource = DefaultServer,
        InitialCatalog = ActiveDatabaseName,
        IntegratedSecurity = true,
        Encrypt = false,
        TrustServerCertificate = true
    }.ConnectionString;

    public static string Master => new SqlConnectionStringBuilder
    {
        DataSource = DefaultServer,
        InitialCatalog = "master",
        IntegratedSecurity = true,
        Encrypt = false,
        TrustServerCertificate = true
    }.ConnectionString;

    public static void SetActiveDatabase(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Der Datenbankname darf nicht leer sein.", nameof(databaseName));

        _activeDatabase = databaseName.Trim();
        SqlConnection.ClearAllPools();

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MyCoinFlow");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "config.json");
        var json = JsonSerializer.Serialize(
            new { ActiveDatabaseName = _activeDatabase },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string LoadActiveDatabase()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MyCoinFlow",
                "config.json");

            if (!File.Exists(path))
            {
                return DefaultDatabase;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("ActiveDatabaseName", out var property))
            {
                var value = property.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Die Vorschau folgt demselben defensiven Fallback wie MyCoinFlow 2.
        }

        return DefaultDatabase;
    }
}
