using System;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Modulübergreifende Navigation, ohne dass die Module sich gegenseitig kennen müssen:
    /// Ein Modul meldet den Wunsch an ("zeige mir diese Transaktion"), das Hauptfenster
    /// (MainViewModel) führt ihn aus und wechselt die Ansicht.
    /// </summary>
    public static class AppNavigation
    {
        /// <summary>Wird ausgelöst, wenn eine bestimmte Transaktion angezeigt werden soll.</summary>
        public static event Action<int>? TransaktionAnzeigen;

        public static void ZeigeTransaktion(int transaktionId)
        {
            if (transaktionId > 0)
                TransaktionAnzeigen?.Invoke(transaktionId);
        }
    }
}
