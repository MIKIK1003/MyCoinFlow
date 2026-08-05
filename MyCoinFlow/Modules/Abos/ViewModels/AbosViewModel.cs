using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MyCoinFlow.ViewModels
{
    /// <summary>Anzeigezeile im Abo-Grid inkl. Ampel und Aggregaten.</summary>
    public class AboRow
    {
        public Abo Abo { get; set; } = new();

        public int Id => Abo.Id;
        public string Name => Abo.Name;
        public string? AdresseName => Abo.AdresseName;
        public string PeriodizitaetAnzeige => AboPerioden.Anzeige(Abo.Periodizitaet);

        public string StatusAnzeige => Abo.Status switch
        {
            AboStatus.Gekuendigt => "Gekündigt",
            AboStatus.Beendet => "Beendet",
            _ => "Aktiv"
        };

        public Brush AmpelBrush { get; set; } = Brushes.Gray;
        public string AmpelText { get; set; } = "";

        public DateTime? LetzteZahlung { get; set; }
        public decimal? LetzterBetrag { get; set; }
        public DateTime? NaechsteZahlung { get; set; }
        public DateTime? KuendigenBis => Abo.SpaetesterKuendigungsTermin;
        public int AnzahlZahlungen { get; set; }
        public decimal? Jahreskosten { get; set; }
        public string? KontoAnzeige { get; set; }
        public string? HinweisText { get; set; }
        public bool HatHinweis => !string.IsNullOrWhiteSpace(HinweisText);
        public bool HatWebseite => !string.IsNullOrWhiteSpace(Abo.WebseiteUrl);
    }

    /// <summary>Zeile in der Detailliste (Zahlungen des gewählten Abos).</summary>
    public class AboZahlungRow
    {
        public int TransaktionId { get; set; }
        public DateTime Datum { get; set; }
        public decimal Betrag { get; set; }
        public string? KontoAnzeige { get; set; }
        public string? BankName { get; set; }
        public string? Notiz { get; set; }
        public bool ManuellZugeordnet { get; set; }
        public string ZuordnungAnzeige => ManuellZugeordnet ? "manuell" : "automatisch";
    }

    public class AbosViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        private List<Abo> _alleAbos = new();
        private Dictionary<int, List<AboZahlung>> _zahlungenProAbo = new();
        private Dictionary<int, string> _kontoMap = new();

        public ObservableCollection<AboRow> Abos { get; } = new();
        public ObservableCollection<AboZahlungRow> Zahlungen { get; } = new();

        public ObservableCollection<string> StatusFilterListe { get; } =
            new() { "Alle", "Aktiv", "Gekündigt", "Beendet" };

        private string _statusFilter = "Alle";
        public string StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (_statusFilter == value) return;
                _statusFilter = value;
                OnPropertyChanged();
                FuelleListe();
            }
        }

        private AboRow? _ausgewaehltesAbo;
        public AboRow? AusgewaehltesAbo
        {
            get => _ausgewaehltesAbo;
            set
            {
                _ausgewaehltesAbo = value;
                OnPropertyChanged();
                FuelleZahlungen();
                (BearbeitenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private int _anzahlAktiv;
        public int AnzahlAktiv
        {
            get => _anzahlAktiv;
            set { _anzahlAktiv = value; OnPropertyChanged(); }
        }

        private decimal _jahreskostenTotal;
        public decimal JahreskostenTotal
        {
            get => _jahreskostenTotal;
            set { _jahreskostenTotal = value; OnPropertyChanged(); }
        }

        public ICommand KandidatenSuchenCommand { get; }
        public ICommand NeueZahlungenCommand { get; }
        public ICommand NeuesAboCommand { get; }
        public ICommand BearbeitenCommand { get; }
        public ICommand GekuendigtCommand { get; }
        public ICommand LoeschenCommand { get; }
        public ICommand HomepageCommand { get; }
        public ICommand RechercheCommand { get; }
        public ICommand TransaktionZuordnenCommand { get; }
        public ICommand KontenBereinigenCommand { get; }
        public ICommand LueckenFuellenCommand { get; }
        public ICommand ZahlungEntfernenCommand { get; }
        public ICommand AktualisierenCommand { get; }

        public AbosViewModel()
        {
            KandidatenSuchenCommand = new RelayCommand(_ => KandidatenSuchen());
            NeueZahlungenCommand = new RelayCommand(_ => NeueZahlungenZuordnen());
            NeuesAboCommand = new RelayCommand(_ => NeuesAbo());
            BearbeitenCommand = new RelayCommand(_ => Bearbeiten(), _ => AusgewaehltesAbo != null);
            GekuendigtCommand = new RelayCommand(_ => GekuendigtMarkieren(), _ => AusgewaehltesAbo != null);
            LoeschenCommand = new RelayCommand(_ => Loeschen(), _ => AusgewaehltesAbo != null);
            HomepageCommand = new RelayCommand(_ => HomepageOeffnen(), _ => AusgewaehltesAbo != null);
            RechercheCommand = new RelayCommand(_ => Recherche(), _ => AusgewaehltesAbo != null);
            TransaktionZuordnenCommand = new RelayCommand(_ => TransaktionZuordnen(), _ => AusgewaehltesAbo != null);
            KontenBereinigenCommand = new RelayCommand(_ => KontenBereinigen(), _ => AusgewaehltesAbo != null);
            LueckenFuellenCommand = new RelayCommand(_ => LueckenFuellen(), _ => AusgewaehltesAbo != null);
            ZahlungEntfernenCommand = new RelayCommand(p => ZahlungEntfernen(p));
            AktualisierenCommand = new RelayCommand(_ => LadeDaten());

            try
            {
                _db.EnsureAboSchema();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Abo-Schema konnte nicht initialisiert werden:\n" + ex.Message,
                    "Abo-Verwaltung", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LadeDaten();
        }

        // ================= Laden & Anzeige =================

        private void LadeDaten()
        {
            try
            {
                _kontoMap = new Dictionary<int, string>();
                foreach (var k in _db.LadeKontenplan())
                {
                    string unter = string.IsNullOrWhiteSpace(k.Untergruppe) ? "" : $"  {k.Untergruppe}";
                    _kontoMap[k.Id] = $"{k.Kontonummer:D4}{unter}  {k.Detail}";
                }

                _alleAbos = _db.AbosLaden();
                _zahlungenProAbo = _db.AboZahlungenLaden()
                    .GroupBy(z => z.AboId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                FuelleListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Abos konnten nicht geladen werden:\n" + ex.Message,
                    "Abo-Verwaltung", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FuelleListe()
        {
            var vorherId = AusgewaehltesAbo?.Id;

            Abos.Clear();

            foreach (var abo in _alleAbos)
            {
                var row = BaueRow(abo);

                bool sichtbar = StatusFilter switch
                {
                    "Aktiv" => abo.Status == AboStatus.Aktiv,
                    "Gekündigt" => abo.Status == AboStatus.Gekuendigt,
                    "Beendet" => abo.Status == AboStatus.Beendet,
                    _ => true
                };

                if (sichtbar)
                    Abos.Add(row);
            }

            AnzahlAktiv = _alleAbos.Count(a => a.Status == AboStatus.Aktiv);
            JahreskostenTotal = _alleAbos
                .Where(a => a.Status == AboStatus.Aktiv)
                .Sum(a => JahreskostenVon(a));

            AusgewaehltesAbo = Abos.FirstOrDefault(r => r.Id == vorherId) ?? Abos.FirstOrDefault();
        }

        private decimal JahreskostenVon(Abo abo)
        {
            var zahlungen = _zahlungenProAbo.TryGetValue(abo.Id, out var z) ? z : new List<AboZahlung>();

            var betrag = abo.ErwarteterBetrag
                         ?? (zahlungen.Count > 0 ? Math.Abs(zahlungen[0].Betrag) : 0m);

            var proJahr = 365m / AboPerioden.Tage(abo.Periodizitaet);
            return Math.Round(Math.Abs(betrag) * proJahr, 2);
        }

        private AboRow BaueRow(Abo abo)
        {
            var zahlungen = _zahlungenProAbo.TryGetValue(abo.Id, out var z)
                ? z.OrderByDescending(x => x.Datum).ToList()
                : new List<AboZahlung>();

            var letzte = zahlungen.FirstOrDefault();
            var periodeTage = AboPerioden.Tage(abo.Periodizitaet);
            var heute = DateTime.Today;

            DateTime? naechste = null;
            if (abo.Status == AboStatus.Aktiv && letzte != null)
                naechste = letzte.Datum.Date.AddDays(periodeTage);

            var row = new AboRow
            {
                Abo = abo,
                LetzteZahlung = letzte?.Datum,
                LetzterBetrag = letzte != null ? Math.Abs(letzte.Betrag) : (decimal?)null,
                NaechsteZahlung = naechste,
                AnzahlZahlungen = zahlungen.Count,
                Jahreskosten = JahreskostenVon(abo)
            };

            // Buchungskonto der Zahlungen (für Konsistenz-Check "gleiches Konto?")
            var kontoIds = zahlungen
                .Select(x => x.BuchungsKontoId)
                .Where(k => k.HasValue)
                .Select(k => k!.Value)
                .Distinct()
                .ToList();

            var anzeigeKontoId = abo.ErwartetesKontoId ?? kontoIds.FirstOrDefault();
            row.KontoAnzeige = anzeigeKontoId > 0 && _kontoMap.TryGetValue(anzeigeKontoId, out var kn)
                ? kn
                : null;

            var hinweise = new List<string>();

            if (kontoIds.Count > 1)
                hinweise.Add($"Zahlungen auf {kontoIds.Count} verschiedenen Konten verbucht");

            if (abo.ErwartetesKontoId.HasValue
                && letzte?.BuchungsKontoId != null
                && letzte.BuchungsKontoId != abo.ErwartetesKontoId)
                hinweise.Add("Letzte Zahlung nicht auf dem erwarteten Konto");

            bool betragWeichtAb = false;
            if (abo.ErwarteterBetrag.HasValue && abo.ErwarteterBetrag.Value != 0m && letzte != null)
            {
                var toleranz = Math.Max(0m, abo.BetragToleranzProzent) / 100m;
                betragWeichtAb =
                    Math.Abs(Math.Abs(letzte.Betrag) - Math.Abs(abo.ErwarteterBetrag.Value))
                    > Math.Abs(abo.ErwarteterBetrag.Value) * toleranz;

                if (betragWeichtAb)
                    hinweise.Add($"Letzter Betrag {Math.Abs(letzte.Betrag):N2} weicht vom erwarteten Betrag {Math.Abs(abo.ErwarteterBetrag.Value):N2} ab");
            }

            // Kündigungsplanung: gewünschtes Ende gesetzt => Termin überwachen
            bool kuendigungVerpasst = false;
            bool kuendigungNaht = false;

            if (abo.Status == AboStatus.Aktiv && abo.SpaetesterKuendigungsTermin.HasValue)
            {
                var kb = abo.SpaetesterKuendigungsTermin.Value;

                if (heute > kb)
                {
                    kuendigungVerpasst = true;
                    hinweise.Insert(0, $"Kündigungstermin {kb:dd.MM.yyyy} verpasst – Abo verlängert sich (geplantes Ende war {abo.KuendigenZum:dd.MM.yyyy})");
                }
                else if ((kb - heute).TotalDays <= 30)
                {
                    kuendigungNaht = true;
                    hinweise.Insert(0, $"Bis {kb:dd.MM.yyyy} kündigen, um per {abo.KuendigenZum:dd.MM.yyyy} auszusteigen");
                }
            }

            // ===== Ampel (Priorität: Rot > Orange > Gelb > Grün/Grau) =====
            if (abo.Status == AboStatus.Beendet)
            {
                row.AmpelBrush = Brushes.Gray;
                row.AmpelText = "Beendet";
            }
            else if (abo.Status == AboStatus.Gekuendigt)
            {
                var abbuchungNachKuendigung = abo.GekuendigtAm.HasValue
                    && letzte != null
                    && letzte.Datum.Date > abo.GekuendigtAm.Value.Date;

                if (abbuchungNachKuendigung)
                {
                    row.AmpelBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // Rot
                    row.AmpelText = $"Abbuchung trotz Kündigung! Letzte Zahlung {letzte!.Datum:dd.MM.yyyy}, gekündigt am {abo.GekuendigtAm:dd.MM.yyyy}";
                    hinweise.Insert(0, "Abbuchung nach Kündigungsdatum prüfen");
                }
                else
                {
                    row.AmpelBrush = Brushes.Gray;
                    row.AmpelText = abo.GekuendigtAm.HasValue
                        ? $"Gekündigt am {abo.GekuendigtAm:dd.MM.yyyy} – keine Abbuchung mehr"
                        : "Gekündigt";
                }
            }
            else if (kuendigungVerpasst)
            {
                row.AmpelBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // Orange
                row.AmpelText = $"Kündigungstermin verpasst – spätester Termin war {abo.SpaetesterKuendigungsTermin:dd.MM.yyyy}";
            }
            else if (kuendigungNaht)
            {
                row.AmpelBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // Gelb
                row.AmpelText = $"Jetzt kündigen: bis {abo.SpaetesterKuendigungsTermin:dd.MM.yyyy} für Ende per {abo.KuendigenZum:dd.MM.yyyy}";
            }
            else if (letzte == null)
            {
                row.AmpelBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // Gelb
                row.AmpelText = "Noch keine Zahlungen zugeordnet";
            }
            else if (naechste.HasValue && heute > naechste.Value.AddDays(periodeTage * 0.25))
            {
                row.AmpelBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // Orange
                row.AmpelText = $"Zahlung überfällig – erwartet am {naechste:dd.MM.yyyy}";
            }
            else if (betragWeichtAb)
            {
                row.AmpelBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // Orange
                row.AmpelText = "Betrag weicht von der Erwartung ab";
            }
            else if (naechste.HasValue && (naechste.Value - heute).TotalDays <= abo.VorwarnTage)
            {
                row.AmpelBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // Gelb
                row.AmpelText = $"Zahlung steht an: {naechste:dd.MM.yyyy}";
            }
            else
            {
                row.AmpelBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Grün
                row.AmpelText = naechste.HasValue
                    ? $"Aktiv – nächste Zahlung erwartet am {naechste:dd.MM.yyyy}"
                    : "Aktiv";
            }

            row.HinweisText = hinweise.Count > 0 ? string.Join("\n", hinweise) : null;

            return row;
        }

        private void FuelleZahlungen()
        {
            Zahlungen.Clear();

            if (AusgewaehltesAbo == null) return;

            if (!_zahlungenProAbo.TryGetValue(AusgewaehltesAbo.Id, out var liste))
                return;

            foreach (var z in liste.OrderByDescending(x => x.Datum))
            {
                string? konto = null;
                if (z.BuchungsKontoId.HasValue && _kontoMap.TryGetValue(z.BuchungsKontoId.Value, out var kn))
                    konto = kn;

                Zahlungen.Add(new AboZahlungRow
                {
                    TransaktionId = z.TransaktionId,
                    Datum = z.Datum,
                    Betrag = z.Betrag,
                    KontoAnzeige = konto,
                    BankName = z.BankName,
                    Notiz = z.Notiz,
                    ManuellZugeordnet = z.ManuellZugeordnet
                });
            }
        }

        // ================= Aktionen =================

        private void KandidatenSuchen()
        {
            try
            {
                var alle = _db.AboLadeTransaktionenMitAdresse();
                var zugeordnet = _db.AboZugeordneteTransaktionIds();
                var adressenMitAbo = _alleAbos
                    .Where(a => a.AdresseId.HasValue)
                    .Select(a => a.AdresseId!.Value)
                    .ToHashSet();

                var kandidaten = AboErkennungService.FindeKandidaten(alle, zugeordnet, adressenMitAbo);

                if (kandidaten.Count == 0)
                {
                    MessageBox.Show(
                        "Keine neuen Abo-Kandidaten gefunden.\n\n" +
                        "Tipp: Die Erkennung braucht mindestens 2–3 regelmässige Zahlungen " +
                        "an dieselbe Adresse mit ähnlichem Betrag.",
                        "Abo-Kandidaten", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new AboKandidatenDialog(kandidaten) { Owner = AktivesFenster() };
                if (dlg.ShowDialog() != true) return;

                int uebernommen = 0;
                foreach (var k in kandidaten.Where(k => k.Uebernehmen))
                {
                    // Bei mehreren Verträgen beim gleichen Anbieter den Namen
                    // über den Betrag unterscheidbar machen
                    bool mehrereProAdresse = k.AdresseHatAbo
                        || kandidaten.Count(x => x.AdresseId == k.AdresseId) > 1;

                    var abo = new Abo
                    {
                        Name = mehrereProAdresse
                            ? $"{k.AdresseName} (ca. {k.MedianBetrag:N2})"
                            : k.AdresseName,
                        AdresseId = k.AdresseId,
                        Periodizitaet = k.Periodizitaet,
                        ErwarteterBetrag = k.MedianBetrag,
                        ErwartetesKontoId = k.HaeufigstesKontoId,
                        Status = AboStatus.Aktiv
                    };

                    var id = _db.AboInsert(abo);

                    foreach (var tid in k.TransaktionIds)
                        _db.AboTransaktionZuordnen(id, tid, manuell: false);

                    uebernommen++;
                }

                LadeDaten();

                MessageBox.Show($"{uebernommen} Abo(s) übernommen.",
                    "Abo-Kandidaten", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kandidaten-Suche fehlgeschlagen:\n" + ex.Message,
                    "Abo-Kandidaten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NeueZahlungenZuordnen()
        {
            try
            {
                var alle = _db.AboLadeTransaktionenMitAdresse();
                var zugeordnet = _db.AboZugeordneteTransaktionIds();

                var abosNachAdresse = _alleAbos
                    .Where(a => a.Status != AboStatus.Beendet && a.AdresseId.HasValue)
                    .GroupBy(a => a.AdresseId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                int gefunden = 0;
                foreach (var t in alle)
                {
                    if (zugeordnet.Contains(t.Id)) continue;
                    if (!t.AdresseId.HasValue) continue;
                    if (!abosNachAdresse.TryGetValue(t.AdresseId.Value, out var kandidatenAbos)) continue;

                    // Bei mehreren Abos derselben Adresse (z.B. zwei Telefon-Verträge)
                    // gewinnt das Abo mit dem nächstliegenden erwarteten Betrag.
                    Abo? bestes = null;
                    decimal besteDiff = decimal.MaxValue;

                    foreach (var abo in kandidatenAbos)
                    {
                        if (abo.ErwarteterBetrag == null || abo.ErwarteterBetrag.Value == 0m)
                        {
                            // Ohne erwarteten Betrag nur zuordnen, wenn die Adresse eindeutig ist
                            if (kandidatenAbos.Count == 1 && bestes == null)
                                bestes = abo;
                            continue;
                        }

                        var erwartet = Math.Abs(abo.ErwarteterBetrag.Value);
                        var toleranz = Math.Max(0m, abo.BetragToleranzProzent) / 100m;
                        var diff = Math.Abs(Math.Abs(t.Betrag) - erwartet);

                        if (diff <= erwartet * toleranz && diff < besteDiff)
                        {
                            bestes = abo;
                            besteDiff = diff;
                        }
                    }

                    if (bestes != null)
                    {
                        _db.AboTransaktionZuordnen(bestes.Id, t.Id, manuell: false);
                        zugeordnet.Add(t.Id);
                        gefunden++;
                    }
                }

                LadeDaten();

                MessageBox.Show(
                    gefunden > 0
                        ? $"{gefunden} neue Zahlung(en) zugeordnet."
                        : "Keine neuen Zahlungen gefunden.",
                    "Neue Zahlungen", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Zuordnung fehlgeschlagen:\n" + ex.Message,
                    "Neue Zahlungen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NeuesAbo()
        {
            var dlg = new AboDialog() { Owner = AktivesFenster() };
            if (dlg.ShowDialog() != true || dlg.Ergebnis == null) return;

            try
            {
                _db.AboInsert(dlg.Ergebnis);
                LadeDaten();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Speichern fehlgeschlagen:\n" + ex.Message,
                    "Neues Abo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Bearbeiten()
        {
            if (AusgewaehltesAbo == null) return;

            var dlg = new AboDialog(AusgewaehltesAbo.Abo) { Owner = AktivesFenster() };
            if (dlg.ShowDialog() != true || dlg.Ergebnis == null) return;

            try
            {
                _db.AboUpdate(dlg.Ergebnis);
                LadeDaten();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Speichern fehlgeschlagen:\n" + ex.Message,
                    "Abo bearbeiten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GekuendigtMarkieren()
        {
            if (AusgewaehltesAbo == null) return;

            var abo = AusgewaehltesAbo.Abo;

            var ask = MessageBox.Show(
                $"\"{abo.Name}\" als gekündigt markieren (per heute)?\n\n" +
                "Neue Abbuchungen nach diesem Datum werden dann rot markiert.\n" +
                "Das Kündigungsdatum kann unter \"Bearbeiten\" angepasst werden.",
                "Als gekündigt markieren", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ask != MessageBoxResult.Yes) return;

            try
            {
                abo.Status = AboStatus.Gekuendigt;
                abo.GekuendigtAm = DateTime.Today;
                _db.AboUpdate(abo);
                LadeDaten();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Speichern fehlgeschlagen:\n" + ex.Message,
                    "Als gekündigt markieren", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Loeschen()
        {
            if (AusgewaehltesAbo == null) return;

            var ask = MessageBox.Show(
                $"Abo \"{AusgewaehltesAbo.Name}\" wirklich löschen?\n\n" +
                "Die Transaktionen selbst bleiben erhalten, nur die Abo-Zuordnung wird entfernt.",
                "Abo löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            try
            {
                _db.AboDelete(AusgewaehltesAbo.Id);
                LadeDaten();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Löschen fehlgeschlagen:\n" + ex.Message,
                    "Abo löschen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HomepageOeffnen()
        {
            if (AusgewaehltesAbo == null) return;

            var abo = AusgewaehltesAbo.Abo;

            try
            {
                string url;
                if (!string.IsNullOrWhiteSpace(abo.WebseiteUrl))
                {
                    url = abo.WebseiteUrl.Trim();
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        url = "https://" + url;
                }
                else
                {
                    // Keine URL hinterlegt: Suche nach dem Anbieter öffnen
                    var query = (abo.AdresseName ?? abo.Name) + " login konto";
                    url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Webseite konnte nicht geöffnet werden:\n" + ex.Message,
                    "Homepage", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Recherche()
        {
            if (AusgewaehltesAbo == null) return;

            try
            {
                var letzteNotiz = Zahlungen.FirstOrDefault()?.Notiz;

                if (!WebRechercheService.OpenSearch(letzteNotiz, AusgewaehltesAbo.AdresseName ?? AusgewaehltesAbo.Name))
                    MessageBox.Show("Kein verwertbarer Text für die Recherche vorhanden.",
                        "Web-Recherche", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Recherche konnte nicht geöffnet werden:\n" + ex.Message,
                    "Web-Recherche", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TransaktionZuordnen()
        {
            if (AusgewaehltesAbo == null) return;

            var dlg = new AboTransaktionZuordnenDialog(
                AusgewaehltesAbo.AdresseName ?? AusgewaehltesAbo.Name)
            { Owner = AktivesFenster() };
            if (dlg.ShowDialog() != true || dlg.AusgewaehlteTransaktionIds.Count == 0) return;

            try
            {
                foreach (var tid in dlg.AusgewaehlteTransaktionIds)
                    _db.AboTransaktionZuordnen(AusgewaehltesAbo.Id, tid, manuell: true);

                LadeDaten();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Zuordnung fehlgeschlagen:\n" + ex.Message,
                    "Transaktion zuordnen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void KontenBereinigen()
        {
            if (AusgewaehltesAbo == null) return;

            var abo = AusgewaehltesAbo.Abo;

            var zahlungen = _zahlungenProAbo.TryGetValue(abo.Id, out var z)
                ? z
                : new List<AboZahlung>();

            var ohneKonto = zahlungen.Count(x => !x.BuchungsKontoId.HasValue);

            // Verteilung der Buchungskonten über die Zahlungen ermitteln
            var optionen = zahlungen
                .Where(x => x.BuchungsKontoId.HasValue)
                .GroupBy(x => x.BuchungsKontoId!.Value)
                .Select(g => new AboKontoWahlDialog.KontoOption
                {
                    KontoId = g.Key,
                    Anzeige = _kontoMap.TryGetValue(g.Key, out var kn) ? kn : $"Konto #{g.Key}",
                    Anzahl = g.Count(),
                    LetzteZahlung = g.Max(x => x.Datum)
                })
                .OrderByDescending(o => o.Anzahl)
                .ThenByDescending(o => o.LetzteZahlung)
                .ToList();

            // Erwartetes Konto als Option anbieten, auch wenn (noch) keine Zahlung darauf liegt
            if (abo.ErwartetesKontoId.HasValue
                && optionen.All(o => o.KontoId != abo.ErwartetesKontoId.Value))
            {
                optionen.Insert(0, new AboKontoWahlDialog.KontoOption
                {
                    KontoId = abo.ErwartetesKontoId.Value,
                    Anzeige = _kontoMap.TryGetValue(abo.ErwartetesKontoId.Value, out var kn)
                        ? kn
                        : $"Konto #{abo.ErwartetesKontoId.Value}",
                    Anzahl = 0,
                    LetzteZahlung = null
                });
            }

            if (optionen.Count == 0)
            {
                MessageBox.Show(
                    "Die Zahlungen dieses Abos haben kein Buchungskonto – hier gibt es nichts zu bereinigen.",
                    "Konten bereinigen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (optionen.Count == 1
                && (!abo.ErwartetesKontoId.HasValue || abo.ErwartetesKontoId.Value == optionen[0].KontoId))
            {
                MessageBox.Show(
                    ohneKonto > 0
                        ? $"Alle Zahlungen sind auf demselben Konto verbucht.\n\nHinweis: {ohneKonto} Zahlung(en) haben gar kein Buchungskonto und bleiben unverändert."
                        : "Alle Zahlungen sind bereits auf demselben Konto verbucht – nichts zu bereinigen.",
                    "Konten bereinigen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Aktive Entscheidung durch den Benutzer: Zielkonto wählen
            // (Vorauswahl: erwartetes Konto, sonst das häufigste)
            var vorauswahl = abo.ErwartetesKontoId ?? optionen[0].KontoId;

            var dlg = new AboKontoWahlDialog(optionen, vorauswahl, abo.Name)
            {
                Owner = AktivesFenster()
            };

            if (dlg.ShowDialog() != true || !dlg.GewaehltesKontoId.HasValue) return;

            var zielKontoId = dlg.GewaehltesKontoId.Value;
            var zielKontoName = _kontoMap.TryGetValue(zielKontoId, out var zn)
                ? zn
                : $"Konto #{zielKontoId}";

            var abweichend = zahlungen
                .Where(x => x.BuchungsKontoId.HasValue && x.BuchungsKontoId.Value != zielKontoId)
                .ToList();

            try
            {
                foreach (var zahlung in abweichend)
                    _db.AboSetzeBuchungsKonto(zahlung.TransaktionId, zielKontoId);

                // Gewähltes Konto als erwartetes Konto am Abo speichern,
                // damit künftige Zahlungen daran gemessen werden
                if (abo.ErwartetesKontoId != zielKontoId)
                {
                    abo.ErwartetesKontoId = zielKontoId;
                    _db.AboUpdate(abo);
                }

                LadeDaten();

                MessageBox.Show(
                    abweichend.Count > 0
                        ? $"{abweichend.Count} Zahlung(en) auf \"{zielKontoName}\" umgebucht."
                        : $"\"{zielKontoName}\" als erwartetes Konto gespeichert – alle Zahlungen lagen bereits darauf.",
                    "Konten bereinigen", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Umbuchen fehlgeschlagen:\n" + ex.Message,
                    "Konten bereinigen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LueckenFuellen()
        {
            if (AusgewaehltesAbo == null) return;

            var abo = AusgewaehltesAbo.Abo;

            var zahlungen = _zahlungenProAbo.TryGetValue(abo.Id, out var z)
                ? z
                : new List<AboZahlung>();

            if (zahlungen.Count < 2)
            {
                MessageBox.Show(
                    "Für die Lücken-Suche braucht das Abo mindestens 2 zugeordnete Zahlungen.",
                    "Lücken füllen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var periodeTage = AboPerioden.Tage(abo.Periodizitaet);
                var daten = zahlungen.Select(x => x.Datum).ToList();

                var luecken = AboErkennungService.FindeLuecken(daten, periodeTage);

                if (luecken.Count == 0)
                {
                    MessageBox.Show(
                        "Keine Lücken in der Zahlungsreihe erkennbar – die Abstände passen zum Rhythmus.",
                        "Lücken füllen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Referenzbetrag: erwarteter Betrag, sonst letzte Zahlung
                var referenz = abo.ErwarteterBetrag ?? Math.Abs(zahlungen
                    .OrderByDescending(x => x.Datum)
                    .First().Betrag);

                // Suchfenster über alle Lücken spannen
                var fenster = Math.Max(7, periodeTage / 3);
                var von = luecken.Min().AddDays(-fenster);
                var bis = luecken.Max().AddDays(fenster);

                var nichtZugeordnet = _db.AboLadeNichtZugeordneteTransaktionen(von, bis);

                var kandidaten = AboErkennungService.FindeLueckenKandidaten(
                    abo, luecken, referenz, nichtZugeordnet);

                var lueckenMitKandidat = kandidaten.Select(k => k.ErwartetAm).Distinct().ToHashSet();
                var lueckenOhne = luecken.Where(l => !lueckenMitKandidat.Contains(l)).ToList();

                if (kandidaten.Count == 0)
                {
                    MessageBox.Show(
                        $"{luecken.Count} Lücke(n) erkannt ({string.Join(", ", luecken.OrderBy(d => d).Select(d => d.ToString("dd.MM.yyyy")))}), " +
                        "aber keine passende Transaktion gefunden.\n\n" +
                        "Mögliche Gründe: Zahlung über ein noch nicht importiertes Konto/Karte, " +
                        "stark abweichender Betrag oder tatsächlich ausgefallene Zahlung.\n" +
                        "Über «Zahlung zuordnen» kann manuell und breiter gesucht werden.",
                        "Lücken füllen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new AboLueckenDialog(kandidaten, lueckenOhne) { Owner = AktivesFenster() };
                if (dlg.ShowDialog() != true) return;

                var auswahl = kandidaten.Where(k => k.Uebernehmen).ToList();

                // Schutz: pro Lücke nur einen Treffer übernehmen (den zuerst gelisteten)
                var proLuecke = auswahl
                    .GroupBy(k => k.ErwartetAm)
                    .Select(g => g.First())
                    .ToList();

                foreach (var k in proLuecke)
                    _db.AboTransaktionZuordnen(abo.Id, k.TransaktionId, manuell: true);

                LadeDaten();

                if (proLuecke.Count > 0)
                    MessageBox.Show($"{proLuecke.Count} Zahlung(en) zugeordnet.",
                        "Lücken füllen", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lücken-Suche fehlgeschlagen:\n" + ex.Message,
                    "Lücken füllen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ZahlungEntfernen(object? param)
        {
            if (AusgewaehltesAbo == null || param is not AboZahlungRow row) return;

            try
            {
                _db.AboTransaktionEntfernen(AusgewaehltesAbo.Id, row.TransaktionId);
                LadeDaten();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Entfernen fehlgeschlagen:\n" + ex.Message,
                    "Zahlung entfernen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static Window? AktivesFenster()
        {
            try
            {
                return Application.Current?.Windows
                           .OfType<Window>()
                           .FirstOrDefault(w => w.IsActive)
                       ?? Application.Current?.MainWindow;
            }
            catch
            {
                return null;
            }
        }
    }
}
