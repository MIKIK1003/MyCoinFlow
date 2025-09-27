// Datei: Import/BankImportItem.Labels.cs
using System;
using System.ComponentModel;
using MyCoinFlow.Services;

namespace MyCoinFlow.Import
{
    // Ergänzt die bestehende Klasse um Label-Properties + Notifier
    public sealed partial class BankImportItem : INotifyPropertyChanged
    {
        // Menschlich lesbare Labels aus Caches
        public string? NachKontoLabel =>
            VorschlagNachKontoId.HasValue ? BankImportLabelCache.KontoLabel(VorschlagNachKontoId.Value) : null;

        public string? VonKontoLabel =>
            VorschlagVonKontoId.HasValue ? BankImportLabelCache.KontoLabel(VorschlagVonKontoId.Value) : null;

        public string? GeldinstitutLabel =>
            VorschlagGeldinstitutId.HasValue ? BankImportLabelCache.GeldinstitutLabel(VorschlagGeldinstitutId.Value) : null;

        public string? AdresseLabel =>
            VorschlagAdresseId.HasValue ? BankImportLabelCache.AdresseLabel(VorschlagAdresseId.Value) : null;

        /// <summary>
        /// Optional: manuell aufrufen, wenn du die Label-Spalten gezielt aktualisieren willst.
        /// (Setter in BankImportItem.cs lösen das bereits automatisch aus.)
        /// </summary>
        public void NotifyLabelPropertiesChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NachKontoLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VonKontoLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GeldinstitutLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AdresseLabel)));
        }
    }

    /// <summary>
    /// Einfache, statische Label-Caches. Werden beim ersten Zugriff befüllt.
    /// </summary>
    internal static class BankImportLabelCache
    {
        private static readonly object _sync = new();

        private static System.Collections.Generic.Dictionary<int, string>? _konto;
        private static System.Collections.Generic.Dictionary<int, string>? _gi;
        private static System.Collections.Generic.Dictionary<int, string>? _adr;

        static BankImportLabelCache()
        {
            Refresh(); // einmalig initial füllen
        }

        public static void Refresh()
        {
            lock (_sync)
            {
                var db = new DatabaseService();

                // Konten
                _konto = new System.Collections.Generic.Dictionary<int, string>();
                foreach (var k in db.LadeKontoLookup())
                    _konto[k.Id] = k.Anzeige;

                // Geldinstitute
                _gi = new System.Collections.Generic.Dictionary<int, string>();
                foreach (var g in db.LadeGeldinstitute())
                {
                    var label = g.Name;
                    if (!string.IsNullOrWhiteSpace(g.IBAN))
                    {
                        var flat = g.IBAN.Replace(" ", "");
                        if (flat.Length >= 4) label += $" ({flat[^4..]})";
                    }
                    _gi[g.Id] = label;
                }

                // Adressen
                _adr = new System.Collections.Generic.Dictionary<int, string>();
                foreach (var a in db.LadeAdressen())
                    _adr[a.Id] = a.Name;
            }
        }

        public static string? KontoLabel(int id)
        {
            lock (_sync)
            {
                if (_konto != null && _konto.TryGetValue(id, out var s)) return s;
            }
            return null;
        }

        public static string? GeldinstitutLabel(int id)
        {
            lock (_sync)
            {
                if (_gi != null && _gi.TryGetValue(id, out var s)) return s;
            }
            return null;
        }

        public static string? AdresseLabel(int id)
        {
            lock (_sync)
            {
                if (_adr != null && _adr.TryGetValue(id, out var s)) return s;
            }
            return null;
        }
    }
}
