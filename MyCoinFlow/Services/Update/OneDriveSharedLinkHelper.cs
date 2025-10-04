using System;

namespace MyCoinFlow.Services.Update
{
    /// <summary>
    /// Kleine Helferfunktionen rund um OneDrive-Share-Links.
    /// Wir erwarten IMMER Dateilinks (nicht Ordnerlinks).
    /// </summary>
    public static class OneDriveSharedLinkHelper
    {
        /// <summary>
        /// OneDrive-Links liefern oft eine Vorschau-Seite.
        /// Viele Links akzeptieren "?download=1" für den direkten Download.
        /// Wir hängen das defensiv an, wenn noch kein Query-Teil vorhanden ist.
        /// </summary>
        public static string EnsureDirectDownload(string sharedUrl)
        {
            if (string.IsNullOrWhiteSpace(sharedUrl)) return sharedUrl;
            if (sharedUrl.Contains("?")) return sharedUrl; // bereits Query-Parameter, nicht anfassen
            return sharedUrl + "?download=1";
        }
    }
}
