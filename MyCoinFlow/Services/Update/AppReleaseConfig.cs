using System;

namespace MyCoinFlow.Services.Update
{
    /// <summary>
    /// Zentrale Release-Konfiguration für Updatefeed und Downloadverhalten.
    /// </summary>
    public static class AppReleaseConfig
    {
        // TODO: HIER deinen OneDrive-FREIGABE-LINK auf DIE DATEI "version.json" eintragen (kein Ordnerlink).
        // Beispiel: https://1drv.ms/t/s!AbCdEf...   (OneDrive-> Rechtsklick auf version.json -> Teilen -> "Jeder mit dem Link")
        public static string VersionFeedUrl { get; set; } =
            "https://REPLACE_WITH_SHARED_LINK_TO_version.json";

        // Lokaler Download-Zwischenordner für Setup-Datei.
        public static string LocalDownloadFolder =>
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MyCoinFlow", "Update");

        // Optional: Dateiname der Setup-EXE, falls der Server keine sprechenden Namen liefert.
        public static string DefaultSetupFileName => "MyCoinFlow-Setup.exe";

        // App-Anzeige-Name im UI
        public static string ProductName => "MyCoinFlow";

        // Falls du Self-Contained publishst, kein .NET-Runtime-Check nötig. 
        public static bool IsSelfContained => true;
    }
}
