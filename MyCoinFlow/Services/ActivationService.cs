using System;

namespace MyCoinFlow.Services
{
    /// <summary>
    /// Zentrale Aktivierungsprüfung für MyCoinFlow.
    /// Entscheidet, ob die App gestartet werden darf:
    ///
    /// - gültige Lizenz
    /// - aktive Testversion
    /// - keine/abgelaufene/ungültige Aktivierung
    /// </summary>
    public sealed class ActivationService
    {
        private readonly LicenseService _license = new();
        private readonly TrialService _trial = new();
        

        public AppActivationStatus GetStatus(out string message)
        {
            message = "";

            try
            {
                if (_license.TryLoadAndApply(out var licenseMessage))
                {
                    message = licenseMessage;
                    return AppActivationStatus.Licensed;
                }

                if (_trial.IsTrialActive())
                {
                    // Testversion: ALLE Module freigeschaltet (inkl. DMS und Abo-Verwaltung)
                    AppModules.SetModules(
                        finance: true,
                        property: true,
                        wealth: true,
                        home: true,
                        dms: true,
                        abos: true);

                    message = $"Testversion aktiv. Verbleibende Tage: {_trial.GetRemainingDays()}";
                    return AppActivationStatus.Trial;
                }

                if (_trial.HasTrial())
                {
                    AppModules.ResetToBasic();
                    message = "Die Testversion ist abgelaufen.";
                    return AppActivationStatus.Expired;
                }

                AppModules.ResetToBasic();
                message = "Keine Lizenz oder Testversion vorhanden.";
                return AppActivationStatus.None;
            }
            catch (Exception ex)
            {
                AppModules.ResetToBasic();
                message = "Aktivierungsprüfung fehlgeschlagen: " + ex.Message;
                return AppActivationStatus.Invalid;
            }
        }

        public bool StartTrial(out string message)
        {
            message = "";

            try
            {
                if (!_trial.StartTrial())
                {
                    message = _trial.HasTrial()
                        ? "Die Testversion wurde auf diesem Benutzerprofil bereits aktiviert."
                        : "Die Testversion konnte nicht aktiviert werden.";

                    return false;
                }

                // Testversion: ALLE Module freigeschaltet (inkl. DMS und Abo-Verwaltung)
                AppModules.SetModules(
                    finance: true,
                    property: true,
                    wealth: true,
                    home: true,
                    dms: true,
                    abos: true);

                message = $"Testversion aktiviert. Verbleibende Tage: {_trial.GetRemainingDays()}";
                return true;
            }
            catch (Exception ex)
            {
                message = "Testversion konnte nicht aktiviert werden: " + ex.Message;
                return false;
            }
        }
    }
}