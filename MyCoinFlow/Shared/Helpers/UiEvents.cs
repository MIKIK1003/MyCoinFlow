using System;

namespace MyCoinFlow.Helpers
{
    /// <summary>
    /// Sehr kleines zentrales Event, um UI-Teile neu zu laden (ohne Fremdbibliotheken).
    /// Admin-Views lösen es aus, AccountsViewModel hört darauf.
    /// </summary>
    public static class UiEvents
    {
        public static event Action? ReloadKontenplanRequested;

        public static void RaiseReloadKontenplan() => ReloadKontenplanRequested?.Invoke();
    }
}
