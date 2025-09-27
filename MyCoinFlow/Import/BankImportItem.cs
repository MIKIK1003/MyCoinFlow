using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MyCoinFlow.Import
{
    public enum KreditDebit { Credit, Debit }

    /// <summary>
    /// Neutrales Staging-Objekt für eine Bankbuchung.
    /// Meldet Änderungen der Vorschlags-Felder an die UI.
    /// </summary>
    public sealed partial class BankImportItem : INotifyPropertyChanged
    {
        // --- Staging ---
        public int? StagingId { get; set; }

        // --- Rohdaten ---
        public string AccountIban { get; set; } = "";
        public string Currency { get; set; } = "CHF";
        public DateTime BookingDate { get; set; }
        public DateTime? ValueDate { get; set; }
        public decimal Amount { get; set; }
        public KreditDebit Direction { get; set; }
        public string ServiceRef { get; set; } = "";
        public string Text { get; set; } = "";
        public string? CounterpartyName { get; set; }
        public string? CounterpartyIban { get; set; }
        public string? Uetr { get; set; }
        public string? PurposeCode { get; set; }

        // --- Umbuchungs-Flag (Bank <-> Bank), vom VM gesetzt ---
        private bool _istUmbuchung;
        public bool IstUmbuchung
        {
            get => _istUmbuchung;
            set
            {
                if (_istUmbuchung == value) return;
                _istUmbuchung = value;
                OnPropertyChanged();
                // UI soll sofort auf Vollständigkeitssprung reagieren
                OnPropertyChanged(nameof(IstVollstaendig));
            }
        }

        // --- Vorschläge (mit Änderungsbenachrichtigung) ---
        private int? _vorschlagAdresseId;
        public int? VorschlagAdresseId
        {
            get => _vorschlagAdresseId;
            set
            {
                if (SetProposal(ref _vorschlagAdresseId, value, nameof(VorschlagAdresseId)))
                    OnPropertyChanged(nameof(IstVollstaendig));
            }
        }

        private int? _vorschlagNachKontoId;
        public int? VorschlagNachKontoId
        {
            get => _vorschlagNachKontoId;
            set
            {
                if (SetProposal(ref _vorschlagNachKontoId, value, nameof(VorschlagNachKontoId)))
                    OnPropertyChanged(nameof(IstVollstaendig));
            }
        }

        private int? _vorschlagVonKontoId;
        public int? VorschlagVonKontoId
        {
            get => _vorschlagVonKontoId;
            set => SetProposal(ref _vorschlagVonKontoId, value, nameof(VorschlagVonKontoId));
        }

        private int? _vorschlagGeldinstitutId;
        public int? VorschlagGeldinstitutId
        {
            get => _vorschlagGeldinstitutId;
            set
            {
                if (SetProposal(ref _vorschlagGeldinstitutId, value, nameof(VorschlagGeldinstitutId)))
                    OnPropertyChanged(nameof(IstVollstaendig));
            }
        }

        // --- Dedupe-Key (für Staging) ---
        public string UniqKey => $"{BookingDate:yyyyMMdd}|{Amount:0.00}|{ServiceRef}";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Einheitlicher Setter für die vier Vorschlags-IDs + Label-Refresh
        private bool SetProposal(ref int? field, int? value, string propertyName)
        {
            if (field == value) return false;
            field = value;
            OnPropertyChanged(propertyName);

            // Zugehörige Label-Spalte aktualisieren (ohne harte Abhängigkeit auf die Labels-Teildatei)
            if (propertyName == nameof(VorschlagNachKontoId))
                OnPropertyChanged(nameof(NachKontoLabel));
            else if (propertyName == nameof(VorschlagVonKontoId))
                OnPropertyChanged(nameof(VonKontoLabel));
            else if (propertyName == nameof(VorschlagGeldinstitutId))
                OnPropertyChanged(nameof(GeldinstitutLabel));
            else if (propertyName == nameof(VorschlagAdresseId))
                OnPropertyChanged(nameof(AdresseLabel));

            return true;
        }
    }
}
