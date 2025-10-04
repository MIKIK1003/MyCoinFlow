using System;
using System.IO;
using System.Threading.Tasks;
using MyCoinFlow.Services;
using MyCoinFlow.Services.Update;

namespace MyCoinFlow.Bootstrap
{
    /// <summary>
    /// Führt beim ersten Start die DB-Erzeugung durch, indem aus einer Template-DB geklont wird.
    /// Nutzt deine bestehenden Services (ConnectionStrings, DbProvisioner).
    /// </summary>
    public static class FirstRunProvisioning
    {
        // Pfad, unter dem das Setup die Template-DB mitliefert (z. B. %ProgramFiles%\MyCoinFlow\Templates\...)
        // Für die Dev-Phase kannst du die MDF/LDF auch im App-Verzeichnis /Templates ablegen.
        public static string TemplateFolder =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");

        public static string TemplateMdf => Path.Combine(TemplateFolder, "MyCoinFlowDB_Template.mdf");

        public static async Task EnsureDatabaseAsync(string targetDbName = "MyCoinFlowDB")
        {
            // Falls bereits ein aktiver Name gesetzt ist, nichts tun
            var active = ConnectionStrings.ActiveDatabaseName;
            if (!string.IsNullOrWhiteSpace(active))
                return;

            // Falls keine Template vorhanden ist, nur leere DB anlegen
            if (!File.Exists(TemplateMdf))
            {
                await DatabaseCloneOrCreateAsync(targetDbName, useTemplate: false);
                ConnectionStrings.SetActiveDatabase(targetDbName);
                return;
            }

            // Mit Template arbeiten
            await DatabaseCloneOrCreateAsync(targetDbName, useTemplate: true);
            ConnectionStrings.SetActiveDatabase(targetDbName);
        }

        private static async Task DatabaseCloneOrCreateAsync(string targetDbName, bool useTemplate)
        {
            var provisioner = new DbProvisioner(); // vorhanden in deinem Projekt
            if (useTemplate)
            {
                // Klonen des Schemas (du hast SMO/Transfer bereits integriert)
                // Quelle: "MyCoinFlowDB_Template", Ziel: targetDbName
                // Der Provisioner kennt in deinem Projekt die nötigen Verbindungsdetails.
                await provisioner.CloneSchemaFromTemplateAsync("MyCoinFlowDB_Template", targetDbName);
            }
            else
            {
                await provisioner.CreateDatabaseAsync(targetDbName);
            }
        }
    }
}
