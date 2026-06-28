using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MyCoinFlow.ViewModels
{
    public class HaushaltViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        private HaushaltStandortAuswahlVm? _selectedStandort;
        private HaushaltRaumTileVm? _selectedRaum;
        private HaushaltRaumTileVm? _geoeffneterRaum;
        private HaushaltObjektTileVm? _selectedObjekt;
        private HaushaltObjektTileVm? _geoeffnetesObjekt;
        private HaushaltAufgabeTileVm? _selectedAufgabe;

        public ObservableCollection<HaushaltStandortAuswahlVm> Standorte { get; } = new();
        public ObservableCollection<HaushaltRaumTileVm> Raeume { get; } = new();
        public ObservableCollection<HaushaltObjektTileVm> Objekte { get; } = new();

        public ObservableCollection<HaushaltAufgabeTileVm> Aufgaben { get; } = new();


        public HaushaltStandortAuswahlVm? SelectedStandort
        {
            get => _selectedStandort;
            set
            {
                _selectedStandort = value;
                OnPropertyChanged();

                SelectedRaum = null;
                GeoeffneterRaum = null;
                SelectedObjekt = null;
                GeoeffnetesObjekt = null;

                LadeRaeume();
                AktualisiereAnsicht();
            }
        }

        public HaushaltRaumTileVm? SelectedRaum
        {
            get => _selectedRaum;
            set
            {
                _selectedRaum = value;
                OnPropertyChanged();
                AktualisiereRaumAuswahl();
                AktualisiereAnsicht();
            }
        }

        public HaushaltRaumTileVm? GeoeffneterRaum
        {
            get => _geoeffneterRaum;
            set
            {
                _geoeffneterRaum = value;
                OnPropertyChanged();
                AktualisiereAnsicht();
            }
        }

        public HaushaltObjektTileVm? SelectedObjekt
        {
            get => _selectedObjekt;
            set
            {
                _selectedObjekt = value;
                OnPropertyChanged();
                AktualisiereObjektAuswahl();
                AktualisiereAnsicht();
            }
        }

        public HaushaltObjektTileVm? GeoeffnetesObjekt
        {
            get => _geoeffnetesObjekt;
            set
            {
                _geoeffnetesObjekt = value;
                OnPropertyChanged();
                AktualisiereAnsicht();
            }
        }

        public HaushaltAufgabeTileVm? SelectedAufgabe
        {
            get => _selectedAufgabe;
            set
            {
                _selectedAufgabe = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HatAusgewaehlteAufgabe));
            }
        }


        public string Titel { get; private set; } = "Haushalt";
        public string StatusText { get; private set; } = "";
        public string InhaltsTitel { get; private set; } = "Räume";

        public bool IsRaeumeAnsicht => !IsAufgabenAnsicht && GeoeffneterRaum == null && GeoeffnetesObjekt == null;
        public bool IsRaumAnsicht => !IsAufgabenAnsicht && GeoeffneterRaum != null && GeoeffnetesObjekt == null;
        public bool IsObjektAnsicht => !IsAufgabenAnsicht && GeoeffneterRaum != null && GeoeffnetesObjekt != null;

        public bool IsAufgabenAnsicht { get; private set; }
        public bool KannZurueckZuRaeumen => IsRaumAnsicht || IsObjektAnsicht || IsAufgabenAnsicht;

        public Visibility RaeumeVisibility => IsRaeumeAnsicht ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RaumVisibility => IsRaumAnsicht ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ObjektVisibility => IsObjektAnsicht ? Visibility.Visible : Visibility.Collapsed;

        public Visibility AufgabenVisibility => IsAufgabenAnsicht ? Visibility.Visible : Visibility.Collapsed;

        public bool HatEchtenStandort => SelectedStandort != null && SelectedStandort.Id > 0;
        public bool HatAusgewaehltenRaum => SelectedRaum != null;
        public bool HatAusgewaehltesObjekt => SelectedObjekt != null;
        public bool HatAusgewaehlteAufgabe => SelectedAufgabe != null;


        public ICommand RaumMarkierenCommand { get; }
        public ICommand RaumOeffnenCommand { get; }


        public ICommand ObjektMarkierenCommand { get; }
        public ICommand ObjektOeffnenCommand { get; }

        public ICommand ZurueckZuRaeumenCommand { get; }
        public ICommand ZurueckZumRaumCommand { get; }

        public ICommand NeuerStandortCommand { get; }
        public ICommand StandortBearbeitenCommand { get; }
        public ICommand StandortLoeschenCommand { get; }

        public ICommand NeuerRaumCommand { get; }
        public ICommand RaumBearbeitenCommand { get; }
        public ICommand RaumLoeschenCommand { get; }

        public ICommand NeuesObjektCommand { get; }
        public ICommand ObjektBearbeitenCommand { get; }
        public ICommand ObjektLoeschenCommand { get; }
        public ICommand ObjektAlsErledigtMarkierenCommand { get; }

        public ICommand KategorienVerwaltenCommand { get; }

        public ICommand ArbeitsanweisungenVerwaltenCommand { get; }
        public ICommand ZeitintervalleVerwaltenCommand { get; }
        public ICommand AufgabenAktualisierenCommand { get; }

        public ICommand AufgabenAnzeigenCommand { get; }
        public ICommand AufgabeErledigenCommand { get; }
        public ICommand AufgabeMarkierenCommand { get; }




        public HaushaltViewModel()
        {
            RaumMarkierenCommand = new RelayCommand(p =>
            {
                if (p is not HaushaltRaumTileVm raum)
                    return;

                SelectedRaum = SelectedRaum != null && SelectedRaum.Id == raum.Id
                    ? null
                    : raum;
            });

            RaumOeffnenCommand = new RelayCommand(p =>
            {
                if (p is not HaushaltRaumTileVm raum)
                    return;

                IsAufgabenAnsicht = false;

                GeoeffneterRaum = raum;
                SelectedObjekt = null;
                GeoeffnetesObjekt = null;
                LadeObjekteFuerRaum(raum.Id);
            });

            ObjektMarkierenCommand = new RelayCommand(p =>
            {
                if (p is not HaushaltObjektTileVm objekt)
                    return;

                SelectedObjekt = SelectedObjekt != null && SelectedObjekt.Id == objekt.Id
                    ? null
                    : objekt;
            });

            ObjektOeffnenCommand = new RelayCommand(p =>
            {
                if (p is not HaushaltObjektTileVm objekt)
                    return;

                IsAufgabenAnsicht = false;

                GeoeffnetesObjekt = objekt;
                AktualisiereAnsicht();
            });

            ZurueckZuRaeumenCommand = new RelayCommand(_ =>
            {
                IsAufgabenAnsicht = false;

                SelectedAufgabe = null;
                SelectedObjekt = null;
                GeoeffnetesObjekt = null;
                GeoeffneterRaum = null;

                LadeRaeume();
                AktualisiereAnsicht();
            });

            ZurueckZumRaumCommand = new RelayCommand(_ =>
            {
                GeoeffnetesObjekt = null;

                if (GeoeffneterRaum != null)
                    LadeObjekteFuerRaum(GeoeffneterRaum.Id);
            });

            NeuerStandortCommand = new RelayCommand(_ => NeuerStandort());
            StandortBearbeitenCommand = new RelayCommand(_ => StandortBearbeiten());
            StandortLoeschenCommand = new RelayCommand(_ => StandortLoeschen());

            NeuerRaumCommand = new RelayCommand(_ => NeuerRaum());
            RaumBearbeitenCommand = new RelayCommand(_ => RaumBearbeiten());
            RaumLoeschenCommand = new RelayCommand(_ => RaumLoeschen());

            NeuesObjektCommand = new RelayCommand(_ => NeuesObjekt());
            ObjektBearbeitenCommand = new RelayCommand(_ => ObjektBearbeiten());
            ObjektLoeschenCommand = new RelayCommand(_ => ObjektLoeschen());
            ObjektAlsErledigtMarkierenCommand = new RelayCommand(_ => ObjektAlsErledigtMarkieren());

            KategorienVerwaltenCommand = new RelayCommand(_ => KategorienVerwalten());
            ArbeitsanweisungenVerwaltenCommand = new RelayCommand(_ => ArbeitsanweisungenVerwalten());
            ZeitintervalleVerwaltenCommand = new RelayCommand(_ => ZeitintervalleVerwalten());
            AufgabenAktualisierenCommand = new RelayCommand(_ => AufgabenAktualisieren());

            AufgabenAnzeigenCommand = new RelayCommand(_ => AufgabenAnzeigen());
            AufgabeErledigenCommand = new RelayCommand(_ => AufgabeErledigen());

            AufgabeMarkierenCommand = new RelayCommand(p =>
            {
                if (p is not HaushaltAufgabeTileVm aufgabe)
                    return;

                SelectedAufgabe = SelectedAufgabe != null && SelectedAufgabe.Id == aufgabe.Id
                    ? null
                    : aufgabe;

                AktualisiereAufgabeAuswahl();
            });


            _db.EnsureHaushaltSchema();

            LadeStandorte();
            LadeRaeume();
            AktualisiereAnsicht();
        }

        private void NeuerStandort()
        {
            var dlg = new HaushaltStandortDialog { Owner = Application.Current?.MainWindow };

            if (dlg.ShowDialog() != true)
                return;

            var newId = _db.HaushaltStandortInsert(dlg.Ergebnis);

            LadeStandorte();

            SelectedStandort = Standorte.FirstOrDefault(x => x.Id == newId)
                               ?? Standorte.FirstOrDefault();

            LadeRaeume();
            AktualisiereAnsicht();
        }

        private void StandortBearbeiten()
        {
            if (!HatEchtenStandort)
            {
                MessageBox.Show("Bitte zuerst einen Standort auswählen.", "Standort bearbeiten",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var model = new HaushaltStandort
            {
                Id = SelectedStandort!.Id,
                Bezeichnung = SelectedStandort.Bezeichnung,
                IconKey = SelectedStandort.IconKey,
                FarbeKey = SelectedStandort.FarbeKey,
                Bemerkung = SelectedStandort.Bemerkung,
                IstAktiv = true
            };

            var dlg = new HaushaltStandortDialog(model) { Owner = Application.Current?.MainWindow };

            if (dlg.ShowDialog() != true)
                return;

            _db.HaushaltStandortUpdate(dlg.Ergebnis);

            var keepId = dlg.Ergebnis.Id;

            LadeStandorte();

            SelectedStandort = Standorte.FirstOrDefault(x => x.Id == keepId)
                               ?? Standorte.FirstOrDefault();

            LadeRaeume();
            AktualisiereAnsicht();
        }

        private void StandortLoeschen()
        {
            if (!HatEchtenStandort)
            {
                MessageBox.Show("Bitte zuerst einen Standort auswählen.", "Standort löschen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var aktiveRaeume = _db.HaushaltRaeumeGetAll()
                .Count(x => x.StandortId == SelectedStandort!.Id);

            if (aktiveRaeume > 0)
            {
                MessageBox.Show(
                    $"Der Standort \"{SelectedStandort!.Bezeichnung}\" enthält noch Räume und kann deshalb nicht gelöscht werden.\n\nBitte löschen oder verschieben Sie zuerst die Räume.",
                    "Standort löschen nicht möglich",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var antwort = MessageBox.Show(
                $"Soll der Standort \"{SelectedStandort!.Bezeichnung}\" wirklich gelöscht werden?",
                "Standort löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (antwort != MessageBoxResult.Yes)
                return;

            _db.HaushaltStandortDelete(SelectedStandort.Id);

            SelectedStandort = null;
            SelectedRaum = null;
            GeoeffneterRaum = null;
            SelectedObjekt = null;
            GeoeffnetesObjekt = null;

            LadeStandorte();
            LadeRaeume();
            AktualisiereAnsicht();
        }

        private void NeuerRaum()
        {
            if (!IsRaeumeAnsicht)
                return;

            if (!HatEchtenStandort)
            {
                MessageBox.Show(
                    "Bitte zuerst einen Standort auswählen oder neu erfassen.\n\nRäume können nur innerhalb eines Standorts erstellt werden.",
                    "Standort erforderlich",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dlg = new HaushaltRaumDialog { Owner = Application.Current?.MainWindow };

            if (dlg.ShowDialog() != true)
                return;

            dlg.Ergebnis.StandortId = SelectedStandort!.Id;

            _db.HaushaltRaumInsert(dlg.Ergebnis);

            SelectedRaum = null;
            LadeRaeume();
            AktualisiereAnsicht();
        }

        private void RaumBearbeiten()
        {
            if (!IsRaeumeAnsicht || SelectedRaum == null)
            {
                MessageBox.Show(
                    "Bitte zuerst einen Raum mit Rechtsklick auf die Kachel markieren.",
                    "Raum bearbeiten",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var model = new HaushaltRaum
            {
                Id = SelectedRaum.Id,
                StandortId = SelectedRaum.StandortId,
                Bezeichnung = SelectedRaum.Bezeichnung,
                IconKey = SelectedRaum.Icon,
                Bemerkung = SelectedRaum.Bemerkung,
                IstAktiv = true
            };

            var dlg = new HaushaltRaumDialog(model) { Owner = Application.Current?.MainWindow };

            if (dlg.ShowDialog() != true)
                return;

            dlg.Ergebnis.StandortId = SelectedRaum.StandortId;

            _db.HaushaltRaumUpdate(dlg.Ergebnis);

            var keepId = SelectedRaum.Id;

            LadeRaeume();
            SelectedRaum = Raeume.FirstOrDefault(x => x.Id == keepId);

            AktualisiereAnsicht();
        }

        private void RaumLoeschen()
        {
            if (!IsRaeumeAnsicht || SelectedRaum == null)
            {
                MessageBox.Show(
                    "Bitte zuerst einen Raum mit Rechtsklick auf die Kachel markieren.",
                    "Raum löschen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var objektCount = _db.HaushaltObjekteGetByRaum(SelectedRaum.Id).Count;

            if (objektCount > 0)
            {
                MessageBox.Show(
                    $"Der Raum \"{SelectedRaum.Bezeichnung}\" enthält noch Objekte und kann deshalb nicht gelöscht werden.\n\nBitte löschen Sie zuerst die Objekte.",
                    "Raum löschen nicht möglich",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var antwort = MessageBox.Show(
                $"Soll der Raum \"{SelectedRaum.Bezeichnung}\" wirklich gelöscht werden?",
                "Raum löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (antwort != MessageBoxResult.Yes)
                return;

            _db.HaushaltRaumDelete(SelectedRaum.Id);

            SelectedRaum = null;
            LadeRaeume();
            AktualisiereAnsicht();
        }

        private void NeuesObjekt()
        {
            if (!IsRaumAnsicht || GeoeffneterRaum == null)
                return;

            var dlg = new HaushaltObjektDialog
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() != true)
                return;

            dlg.Ergebnis.RaumId = GeoeffneterRaum.Id;
            dlg.Ergebnis.Bezeichnung = $"{GeoeffneterRaum.Bezeichnung} {dlg.Ergebnis.KategorieBezeichnung}".Trim();

            _db.HaushaltObjektInsert(dlg.Ergebnis);

            SelectedObjekt = null;
            LadeObjekteFuerRaum(GeoeffneterRaum.Id);
            AktualisiereAnsicht();
        }

        private void ObjektBearbeiten()
        {
            if (!IsRaumAnsicht || GeoeffneterRaum == null || SelectedObjekt == null)
            {
                MessageBox.Show(
                    "Bitte zuerst ein Objekt mit Rechtsklick auf die Kachel markieren.",
                    "Objekt bearbeiten",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var model = new HaushaltObjekt
            {
                Id = SelectedObjekt.Id,
                RaumId = SelectedObjekt.RaumId,

                KategorieId = SelectedObjekt.KategorieId,
                KategorieBezeichnung = SelectedObjekt.Kategorie,
                KategorieIconKey = SelectedObjekt.Icon,

                ArbeitsanweisungId = SelectedObjekt.ArbeitsanweisungId,
                ArbeitsanweisungBezeichnung = SelectedObjekt.ArbeitsanweisungBezeichnung,
                ArbeitsanweisungBeschreibung = SelectedObjekt.ArbeitsanweisungBeschreibung,

                ZeitintervallId = SelectedObjekt.ZeitintervallId,
                ZeitintervallBezeichnung = SelectedObjekt.ZeitintervallBezeichnung,
                ZeitintervallTage = SelectedObjekt.ZeitintervallTage,

                VorlaufTage = SelectedObjekt.VorlaufTage,
                LetzteAusfuehrungAm = SelectedObjekt.LetzteAusfuehrungAm,

                Bezeichnung = SelectedObjekt.Bezeichnung,
                Kategorie = SelectedObjekt.Kategorie,
                IconKey = SelectedObjekt.Icon,

                Hersteller = SelectedObjekt.Hersteller,
                Modell = SelectedObjekt.Modell,
                Seriennummer = SelectedObjekt.Seriennummer,
                Kaufdatum = SelectedObjekt.Kaufdatum,
                Kaufpreis = SelectedObjekt.Kaufpreis,
                Bemerkung = SelectedObjekt.Bemerkung,
                IstAktiv = true
            };

            var dlg = new HaushaltObjektDialog(model)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() != true)
                return;

            dlg.Ergebnis.RaumId = GeoeffneterRaum.Id;
            dlg.Ergebnis.Bezeichnung = $"{GeoeffneterRaum.Bezeichnung} {dlg.Ergebnis.KategorieBezeichnung}".Trim();

            _db.HaushaltObjektUpdate(dlg.Ergebnis);

            var keepId = SelectedObjekt.Id;

            LadeObjekteFuerRaum(GeoeffneterRaum.Id);
            SelectedObjekt = Objekte.FirstOrDefault(x => x.Id == keepId);

            AktualisiereAnsicht();
        }

        private void ObjektLoeschen()
        {
            if (!IsRaumAnsicht || GeoeffneterRaum == null || SelectedObjekt == null)
            {
                MessageBox.Show(
                    "Bitte zuerst ein Objekt mit Rechtsklick auf die Kachel markieren.",
                    "Objekt löschen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var antwort = MessageBox.Show(
                $"Soll das Objekt \"{SelectedObjekt.Bezeichnung}\" wirklich gelöscht werden?",
                "Objekt löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (antwort != MessageBoxResult.Yes)
                return;

            _db.HaushaltObjektDelete(SelectedObjekt.Id);

            SelectedObjekt = null;
            LadeObjekteFuerRaum(GeoeffneterRaum.Id);
            AktualisiereAnsicht();
        }

        private void ObjektAlsErledigtMarkieren()
        {
            if (!IsObjektAnsicht || GeoeffneterRaum == null || GeoeffnetesObjekt == null)
            {
                MessageBox.Show(
                    "Bitte zuerst ein Objekt öffnen.",
                    "Als erledigt markieren",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var antwort = MessageBox.Show(
                $"Soll \"{GeoeffnetesObjekt.Bezeichnung}\" als heute erledigt markiert werden?",
                "Als erledigt markieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (antwort != MessageBoxResult.Yes)
                return;

            var heute = DateTime.Today;

            var aktiveAufgabe = _db.HaushaltAktiveAufgabeGetByObjekt(GeoeffnetesObjekt.Id);

            if (aktiveAufgabe != null)
                _db.HaushaltAufgabeErledigen(aktiveAufgabe.Id, heute);

            _db.HaushaltObjektLetzteAusfuehrungSetzen(GeoeffnetesObjekt.Id, heute);

            LadeObjekteFuerRaum(GeoeffneterRaum.Id);

            GeoeffnetesObjekt = Objekte.FirstOrDefault(x => x.Id == GeoeffnetesObjekt.Id);

            if (GeoeffnetesObjekt != null)
            {
                var objektModel = new HaushaltObjekt
                {
                    Id = GeoeffnetesObjekt.Id,
                    Bezeichnung = GeoeffnetesObjekt.Bezeichnung,
                    ArbeitsanweisungBezeichnung = GeoeffnetesObjekt.ArbeitsanweisungBezeichnung,
                    ZeitintervallTage = GeoeffnetesObjekt.ZeitintervallTage,
                    VorlaufTage = GeoeffnetesObjekt.VorlaufTage,
                    LetzteAusfuehrungAm = GeoeffnetesObjekt.LetzteAusfuehrungAm
                };

                var service = new HaushaltAufgabenService(_db);
                service.AufgabeFuerObjektErzeugenWennNoetig(objektModel);
            }

            LadeObjekteFuerRaum(GeoeffneterRaum.Id);
            GeoeffnetesObjekt = Objekte.FirstOrDefault(x => x.Id == GeoeffnetesObjekt?.Id);

            AktualisiereAnsicht();
        }

        private void LadeStandorte()
        {
            var selectedId = SelectedStandort?.Id ?? 0;

            Standorte.Clear();

            Standorte.Add(new HaushaltStandortAuswahlVm
            {
                Id = 0,
                Bezeichnung = "Alle Standorte",
                IconKey = "HomeCityOutline",
                FarbeKey = "DeepPurple"
            });

            foreach (var s in _db.HaushaltStandorteGetAll())
            {
                Standorte.Add(new HaushaltStandortAuswahlVm
                {
                    Id = s.Id,
                    Bezeichnung = s.Bezeichnung,
                    IconKey = s.IconKey,
                    FarbeKey = s.FarbeKey,
                    Bemerkung = s.Bemerkung
                });
            }

            SelectedStandort = Standorte.FirstOrDefault(x => x.Id == selectedId)
                               ?? Standorte.FirstOrDefault();
        }

        private void LadeRaeume()
        {
            var selectedRaumId = SelectedRaum?.Id ?? 0;

            Raeume.Clear();

            var alle = _db.HaushaltRaeumeGetAll()
                .Where(x => x.StandortId.HasValue && x.StandortId.Value > 0)
                .ToList();

            if (SelectedStandort != null && SelectedStandort.Id > 0)
                alle = alle.Where(x => x.StandortId == SelectedStandort.Id).ToList();

            foreach (var r in alle)
            {
                var objektCount = _db.HaushaltObjekteGetByRaum(r.Id).Count;

                Raeume.Add(new HaushaltRaumTileVm
                {
                    Id = r.Id,
                    StandortId = r.StandortId,
                    StandortBezeichnung = r.StandortBezeichnung,
                    StandortFarbeKey = r.StandortFarbeKey,
                    Bezeichnung = r.Bezeichnung,
                    Icon = string.IsNullOrWhiteSpace(r.IconKey) ? "HomeOutline" : r.IconKey,
                    Bemerkung = r.Bemerkung,
                    ObjektAnzahlText = objektCount == 1 ? "1 Objekt" : $"{objektCount} Objekte",
                    IsSelected = r.Id == selectedRaumId
                });
            }

            if (selectedRaumId > 0)
                SelectedRaum = Raeume.FirstOrDefault(x => x.Id == selectedRaumId);
        }

        private void LadeObjekteFuerRaum(int raumId)
        {
            var selectedObjektId = SelectedObjekt?.Id ?? 0;

            Objekte.Clear();

            foreach (var o in _db.HaushaltObjekteGetByRaum(raumId))
            {
                Objekte.Add(new HaushaltObjektTileVm
                {
                    Id = o.Id,
                    RaumId = o.RaumId,

                    KategorieId = o.KategorieId,

                    ArbeitsanweisungId = o.ArbeitsanweisungId,
                    ArbeitsanweisungBezeichnung = o.ArbeitsanweisungBezeichnung,
                    ArbeitsanweisungBeschreibung = o.ArbeitsanweisungBeschreibung,

                    ZeitintervallId = o.ZeitintervallId,
                    ZeitintervallBezeichnung = o.ZeitintervallBezeichnung,
                    ZeitintervallTage = o.ZeitintervallTage,

                    VorlaufTage = o.VorlaufTage,
                    LetzteAusfuehrungAm = o.LetzteAusfuehrungAm,

                    Bezeichnung = o.Bezeichnung,
                    Icon = string.IsNullOrWhiteSpace(o.KategorieIconKey) ? "PackageVariantClosed" : o.KategorieIconKey,

                    Kategorie = string.IsNullOrWhiteSpace(o.KategorieBezeichnung) ? "Ohne Kategorie" : o.KategorieBezeichnung,

                    Hersteller = o.Hersteller,
                    Modell = o.Modell,
                    Seriennummer = o.Seriennummer,
                    Kaufdatum = o.Kaufdatum,
                    Kaufpreis = o.Kaufpreis,
                    Bemerkung = o.Bemerkung,

                    StandortFarbeKey = GeoeffneterRaum?.StandortFarbeKey ?? "DeepPurple",
                    IsSelected = o.Id == selectedObjektId
                });
            }

            if (selectedObjektId > 0)
                SelectedObjekt = Objekte.FirstOrDefault(x => x.Id == selectedObjektId);
            
            if (GeoeffnetesObjekt != null)
            {
                GeoeffnetesObjekt =
                    Objekte.FirstOrDefault(x => x.Id == GeoeffnetesObjekt.Id);
            }
        }

        private void AktualisiereRaumAuswahl()
        {
            foreach (var raum in Raeume)
                raum.IsSelected = SelectedRaum != null && raum.Id == SelectedRaum.Id;

            OnPropertyChanged(nameof(HatAusgewaehltenRaum));
        }

        private void AktualisiereObjektAuswahl()
        {
            foreach (var objekt in Objekte)
                objekt.IsSelected = SelectedObjekt != null && objekt.Id == SelectedObjekt.Id;

            OnPropertyChanged(nameof(HatAusgewaehltesObjekt));
        }

        private void AktualisiereAufgabeAuswahl()
        {
            foreach (var aufgabe in Aufgaben)
                aufgabe.IsSelected = SelectedAufgabe != null && aufgabe.Id == SelectedAufgabe.Id;

            OnPropertyChanged(nameof(HatAusgewaehlteAufgabe));
        }

        private void KategorienVerwalten()
        {
            var dlg = new HaushaltObjektKategorieVerwaltungDialog
            {
                Owner = Application.Current?.MainWindow
            };

            dlg.ShowDialog();
        }

        private void ArbeitsanweisungenVerwalten()
        {
            var dlg = new HaushaltArbeitsanweisungVerwaltungDialog
            {
                Owner = Application.Current?.MainWindow
            };

            dlg.ShowDialog();
        }

        private void ZeitintervalleVerwalten()
        {
            var dlg = new HaushaltZeitintervallVerwaltungDialog
            {
                Owner = Application.Current?.MainWindow
            };

            dlg.ShowDialog();
        }

        private void AufgabenAktualisieren()
        {
            var objekte = _db.HaushaltObjekteGetAll();

            var service = new HaushaltAufgabenService(_db);
            var erstellt = service.AufgabenFuerAlleObjekteAktualisieren(objekte);

            MessageBox.Show(
                $"Aufgaben aktualisiert.\n\nNeu erstellt: {erstellt}",
                "Haushalt Aufgaben",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (GeoeffneterRaum != null)
                LadeObjekteFuerRaum(GeoeffneterRaum.Id);

            AktualisiereAnsicht();
        }

        private void AufgabenAnzeigen()
        {
            SelectedRaum = null;
            GeoeffneterRaum = null;
            SelectedObjekt = null;
            GeoeffnetesObjekt = null;

            LadeAufgaben();

            IsAufgabenAnsicht = true;
            AktualisiereAnsicht();
        }

        private void AufgabeErledigen()
        {
            if (!IsAufgabenAnsicht || SelectedAufgabe == null)
            {
                MessageBox.Show(
                    "Bitte zuerst eine Aufgabe auswählen.",
                    "Aufgabe erledigen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var antwort = MessageBox.Show(
                $"Soll die Aufgabe \"{SelectedAufgabe.Titel}\" als heute erledigt markiert werden?",
                "Aufgabe erledigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (antwort != MessageBoxResult.Yes)
                return;

            var heute = DateTime.Today;

            _db.HaushaltAufgabeErledigen(SelectedAufgabe.Id, heute);
            _db.HaushaltObjektLetzteAusfuehrungSetzen(SelectedAufgabe.ObjektId, heute);

            SelectedAufgabe = null;

            AufgabenAktualisieren();
            LadeAufgaben();

            AktualisiereAnsicht();
        }

        private void LadeAufgaben()
        {
            var selectedAufgabeId = SelectedAufgabe?.Id ?? 0;

            Aufgaben.Clear();

            foreach (var a in _db.HaushaltOffeneAufgabenGetAll())
            {
                Aufgaben.Add(new HaushaltAufgabeTileVm
                {
                    Id = a.Id,
                    ObjektId = a.ObjektId,
                    Titel = a.Titel,
                    Status = a.Status,
                    AktivAb = a.AktivAb,
                    FaelligAm = a.FaelligAm,
                    IsSelected = a.Id == selectedAufgabeId
                });
            }

            if (selectedAufgabeId > 0)
                SelectedAufgabe = Aufgaben.FirstOrDefault(x => x.Id == selectedAufgabeId);
        }

        private void NotifyAnsicht()
        {
            OnPropertyChanged(nameof(Titel));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(InhaltsTitel));

            OnPropertyChanged(nameof(IsRaeumeAnsicht));
            OnPropertyChanged(nameof(IsRaumAnsicht));
            OnPropertyChanged(nameof(IsObjektAnsicht));
            OnPropertyChanged(nameof(IsAufgabenAnsicht));

            OnPropertyChanged(nameof(RaeumeVisibility));
            OnPropertyChanged(nameof(RaumVisibility));
            OnPropertyChanged(nameof(ObjektVisibility));
            OnPropertyChanged(nameof(AufgabenVisibility));

            OnPropertyChanged(nameof(HatEchtenStandort));
            OnPropertyChanged(nameof(HatAusgewaehltenRaum));
            OnPropertyChanged(nameof(HatAusgewaehltesObjekt));
            OnPropertyChanged(nameof(HatAusgewaehlteAufgabe));
            OnPropertyChanged(nameof(KannZurueckZuRaeumen));
        }


        private void AktualisiereAnsicht()
        {
            if (IsAufgabenAnsicht)
            {
                Titel = "Haushalt > Aufgaben";
                InhaltsTitel = "Offene Aufgaben";
                StatusText = Aufgaben.Any()
                    ? "Offene Aufgaben nach Fälligkeit sortiert."
                    : "Keine offenen Aufgaben vorhanden.";

                NotifyAnsicht();
                return;
            }

            if (IsRaeumeAnsicht)
            {
                Titel = "Haushalt";
                InhaltsTitel = "Räume";

                if (!Standorte.Any(x => x.Id > 0))
                    StatusText = "Noch keine Standorte vorhanden. Erfassen Sie zuerst einen Standort.";
                else if (!HatEchtenStandort)
                    StatusText = "Wählen Sie einen Standort aus. Räume können nur innerhalb eines Standorts erstellt werden.";
                else if (!Raeume.Any())
                    StatusText = $"Noch keine Räume für \"{SelectedStandort!.Bezeichnung}\" vorhanden. Erfassen Sie den ersten Raum.";
                else
                    StatusText = SelectedRaum == null
                        ? "Linksklick öffnet den Raum. Rechtsklick markiert ihn für Bearbeiten/Löschen."
                        : $"Markierter Raum: {SelectedRaum.Bezeichnung}";
            }
            else if (IsRaumAnsicht)
            {
                Titel = string.IsNullOrWhiteSpace(GeoeffneterRaum!.StandortBezeichnung)
                    ? $"Haushalt > {GeoeffneterRaum.Bezeichnung}"
                    : $"Haushalt > {GeoeffneterRaum.StandortBezeichnung} > {GeoeffneterRaum.Bezeichnung}";

                InhaltsTitel = $"Objekte in {GeoeffneterRaum.Bezeichnung}";

                StatusText = SelectedObjekt == null
                    ? "Linksklick öffnet das Objekt. Rechtsklick markiert es für Bearbeiten/Löschen."
                    : $"Markiertes Objekt: {SelectedObjekt.Bezeichnung}";
            }
            else if (IsObjektAnsicht)
            {
                Titel = $"Haushalt > {GeoeffneterRaum!.StandortBezeichnung} > {GeoeffneterRaum.Bezeichnung} > {GeoeffnetesObjekt!.Bezeichnung}";
                InhaltsTitel = GeoeffnetesObjekt.Bezeichnung;
                StatusText = "Objekt-Detailansicht mit Aufgabenbasis.";
            }

            NotifyAnsicht();
        }
    }

    public class HaushaltStandortAuswahlVm
    {
        public int Id { get; set; }
        public string Bezeichnung { get; set; } = "";
        public string IconKey { get; set; } = "HomeCityOutline";
        public string FarbeKey { get; set; } = "DeepPurple";
        public string Bemerkung { get; set; } = "";
    }

    public class HaushaltRaumTileVm : BaseViewModel
    {
        private bool _isSelected;

        public int Id { get; set; }
        public int? StandortId { get; set; }
        public string StandortBezeichnung { get; set; } = "";
        public string StandortFarbeKey { get; set; } = "DeepPurple";
        public string Bezeichnung { get; set; } = "";
        public string Icon { get; set; } = "HomeOutline";
        public string Bemerkung { get; set; } = "";
        public string ObjektAnzahlText { get; set; } = "";

        public Brush StandortBrush => BrushFromKey(StandortFarbeKey);

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        protected static Brush BrushFromKey(string key) => key switch
        {
            "Amber" => Brushes.DarkGoldenrod,
            "Orange" => Brushes.DarkOrange,
            "Red" => Brushes.IndianRed,
            "Blue" => Brushes.SteelBlue,
            "Teal" => Brushes.Teal,
            "Green" => Brushes.SeaGreen,
            "BlueGrey" => Brushes.SlateGray,
            _ => Brushes.MediumPurple
        };
    }

    public class HaushaltObjektTileVm : BaseViewModel
    {
        private bool _isSelected;

        public int Id { get; set; }
        public int RaumId { get; set; }
        public int? KategorieId { get; set; }

        public int? ArbeitsanweisungId { get; set; }
        public string ArbeitsanweisungBezeichnung { get; set; } = "";
        public string ArbeitsanweisungBeschreibung { get; set; } = "";

        public int? ZeitintervallId { get; set; }
        public string ZeitintervallBezeichnung { get; set; } = "";
        public int? ZeitintervallTage { get; set; }

        public string TaetigkeitText =>
    string.IsNullOrWhiteSpace(ArbeitsanweisungBezeichnung)
        ? "Keine Tätigkeit"
        : ArbeitsanweisungBezeichnung;

        public string IntervallText =>
            string.IsNullOrWhiteSpace(ZeitintervallBezeichnung)
                ? "Kein Intervall"
                : VorlaufTage > 0
                    ? $"{ZeitintervallBezeichnung} · {VorlaufTage} Tage Vorlauf"
                    : ZeitintervallBezeichnung;

        public int VorlaufTage { get; set; }
        public DateTime? LetzteAusfuehrungAm { get; set; }

        public DateTime? FaelligAm =>
    LetzteAusfuehrungAm.HasValue && ZeitintervallTage.HasValue
        ? LetzteAusfuehrungAm.Value.Date.AddDays(ZeitintervallTage.Value)
        : null;

        public DateTime? AktivAb =>
            FaelligAm.HasValue
                ? FaelligAm.Value.Date.AddDays(-VorlaufTage)
                : null;

        public string AufgabenStatus
        {
            get
            {
                if (!FaelligAm.HasValue || !AktivAb.HasValue)
                    return "Nicht geplant";

                var heute = DateTime.Today;

                if (heute < AktivAb.Value.Date)
                    return "Ruhe";

                if (heute > FaelligAm.Value.Date)
                    return "Überfällig";

                var restTage = (FaelligAm.Value.Date - heute).Days;

                if (restTage <= 2)
                    return "Bald fällig";

                return "Aktiv";
            }
        }

        public string AktivAbText =>
            AktivAb.HasValue ? AktivAb.Value.ToString("dd.MM.yyyy") : "–";

        public string FaelligAmText =>
            FaelligAm.HasValue ? FaelligAm.Value.ToString("dd.MM.yyyy") : "–";

        public string LetzteAusfuehrungText =>
            LetzteAusfuehrungAm.HasValue ? LetzteAusfuehrungAm.Value.ToString("dd.MM.yyyy") : "Noch nie";

        public string Bezeichnung { get; set; } = "";
        public string Icon { get; set; } = "PackageVariantClosed";
        public string Kategorie { get; set; } = "";
        public string Hersteller { get; set; } = "";
        public string Modell { get; set; } = "";
        public string Seriennummer { get; set; } = "";
        public DateTime? Kaufdatum { get; set; }
        public decimal? Kaufpreis { get; set; }
        public string Bemerkung { get; set; } = "";
        public string StandortFarbeKey { get; set; } = "DeepPurple";

        public Brush StandortBrush => StandortFarbeKey switch
        {
            "Amber" => Brushes.DarkGoldenrod,
            "Orange" => Brushes.DarkOrange,
            "Red" => Brushes.IndianRed,
            "Blue" => Brushes.SteelBlue,
            "Teal" => Brushes.Teal,
            "Green" => Brushes.SeaGreen,
            "BlueGrey" => Brushes.SlateGray,
            _ => Brushes.MediumPurple
        };

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged();
            }

        }
     
        
    }

    public class HaushaltAufgabeTileVm : BaseViewModel
    {
        private bool _isSelected;

        public int Id { get; set; }
        public int ObjektId { get; set; }

        public string Titel { get; set; } = "";
        public string Status { get; set; } = "";

        public DateTime AktivAb { get; set; }
        public DateTime FaelligAm { get; set; }

        public string AktivAbText => AktivAb.ToString("dd.MM.yyyy");
        public string FaelligAmText => FaelligAm.ToString("dd.MM.yyyy");

        public int RestTage => (FaelligAm.Date - DateTime.Today).Days;

        public string StatusAmpelText
        {
            get
            {
                if (DateTime.Today > FaelligAm.Date)
                    return "Überfällig";

                if (RestTage <= 2)
                    return "Bald fällig";

                return "Aktiv";
            }
        }

        public string StatusIcon => "Circle";

        public Brush StatusBrush
        {
            get
            {
                if (DateTime.Today > FaelligAm.Date)
                    return Brushes.IndianRed;

                if (RestTage <= 2)
                    return Brushes.DarkOrange;

                return Brushes.SeaGreen;
            }
        }

        public string RestTageText
        {
            get
            {
                if (DateTime.Today > FaelligAm.Date)
                    return $"Überfällig seit {Math.Abs(RestTage)} Tag(en)";

                if (RestTage == 0)
                    return "Heute fällig";

                if (RestTage == 1)
                    return "Morgen fällig";

                return $"Noch {RestTage} Tage";
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

}