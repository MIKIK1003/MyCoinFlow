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

        public ObservableCollection<HaushaltStandortAuswahlVm> Standorte { get; } = new();
        public ObservableCollection<HaushaltRaumTileVm> Raeume { get; } = new();
        public ObservableCollection<HaushaltObjektTileVm> Objekte { get; } = new();

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

        public string Titel { get; private set; } = "Haushalt";
        public string StatusText { get; private set; } = "";
        public string InhaltsTitel { get; private set; } = "Räume";

        public bool IsRaeumeAnsicht => GeoeffneterRaum == null && GeoeffnetesObjekt == null;
        public bool IsRaumAnsicht => GeoeffneterRaum != null && GeoeffnetesObjekt == null;
        public bool IsObjektAnsicht => GeoeffneterRaum != null && GeoeffnetesObjekt != null;

        public Visibility RaeumeVisibility => IsRaeumeAnsicht ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RaumVisibility => IsRaumAnsicht ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ObjektVisibility => IsObjektAnsicht ? Visibility.Visible : Visibility.Collapsed;

        public bool HatEchtenStandort => SelectedStandort != null && SelectedStandort.Id > 0;
        public bool HatAusgewaehltenRaum => SelectedRaum != null;
        public bool HatAusgewaehltesObjekt => SelectedObjekt != null;

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

        public ICommand KategorienVerwaltenCommand { get; }



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

                GeoeffnetesObjekt = objekt;
            });

            ZurueckZuRaeumenCommand = new RelayCommand(_ =>
            {
                SelectedObjekt = null;
                GeoeffnetesObjekt = null;
                GeoeffneterRaum = null;
                LadeRaeume();
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

            KategorienVerwaltenCommand = new RelayCommand(_ => KategorienVerwalten());

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

                    Bezeichnung = o.Bezeichnung,
                    Icon = string.IsNullOrWhiteSpace(o.KategorieIconKey)
                        ? "PackageVariantClosed"
                        : o.KategorieIconKey,

                    Kategorie = string.IsNullOrWhiteSpace(o.KategorieBezeichnung)
                        ? "Ohne Kategorie"
                        : o.KategorieBezeichnung,

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

        private void KategorienVerwalten()
        {
            var dlg = new HaushaltObjektKategorieVerwaltungDialog
            {
                Owner = Application.Current?.MainWindow
            };

            dlg.ShowDialog();
        }

        private void AktualisiereAnsicht()
        {
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
                StatusText = "Objekt-Detailansicht mit späteren Eigenschaften und Arbeitsanweisungen.";
            }

            OnPropertyChanged(nameof(Titel));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(InhaltsTitel));
            OnPropertyChanged(nameof(IsRaeumeAnsicht));
            OnPropertyChanged(nameof(IsRaumAnsicht));
            OnPropertyChanged(nameof(IsObjektAnsicht));
            OnPropertyChanged(nameof(RaeumeVisibility));
            OnPropertyChanged(nameof(RaumVisibility));
            OnPropertyChanged(nameof(ObjektVisibility));
            OnPropertyChanged(nameof(HatEchtenStandort));
            OnPropertyChanged(nameof(HatAusgewaehltenRaum));
            OnPropertyChanged(nameof(HatAusgewaehltesObjekt));
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
            set { _isSelected = value; OnPropertyChanged(); }
        }
    }
}