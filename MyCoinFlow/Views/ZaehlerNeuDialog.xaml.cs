using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MyCoinFlow.Views
{
    public partial class ZaehlerNeuDialog : Window, INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();
        private readonly DateTime _stichtag;

        public StweZaehler Model { get; } = new();

        // Wird vom Caller nach dem Dialog gespeichert (Replace)
        public List<(int EigentuemerId, decimal AnteilProzent)> ResultLines { get; private set; } = new();

        private readonly ObservableCollection<StweEigentuemer> _owners = new();

        public string HeaderText => Model.Id > 0 ? "Zähler bearbeiten" : "Zähler neu";

        public string LinesInfo
        {
            get
            {
                if (ResultLines.Count == 0) return "keine Zeilen";
                var sum = ResultLines.Sum(x => x.AnteilProzent);
                return $"{ResultLines.Count} Zeile(n), Summe {sum:N4}%";
            }
        }

        public ObservableCollection<string> TypOptions { get; } = new()
        {
            "DIREKT",
            "ALLG",
            "HEIZ",
            "EVU"
        };

        public ObservableCollection<StweEinheit> Einheiten { get; } = new();

        private string _selectedTyp = "DIREKT";
        public string SelectedTyp
        {
            get => _selectedTyp;
            set
            {
                _selectedTyp = (value ?? "DIREKT").Trim().ToUpperInvariant();
                Model.Typ = _selectedTyp;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEinheitEnabled));
                OnPropertyChanged(nameof(EinheitOpacity));

                if (!IsEinheitEnabled)
                {
                    SelectedEinheit = null;
                    Model.EinheitId = null;
                }

                // Best-Workflow: bei DIREKT ggf. 100%-Zeile automatisch setzen
                EnsureDirectAutoLinesIfPossible();
            }
        }

        private StweEinheit? _selectedEinheit;
        public StweEinheit? SelectedEinheit
        {
            get => _selectedEinheit;
            set
            {
                _selectedEinheit = value;
                OnPropertyChanged();
                Model.EinheitId = _selectedEinheit?.Id;

                // Best-Workflow: bei DIREKT + Einheit ggf. 100%-Zeile automatisch setzen
                EnsureDirectAutoLinesIfPossible();
            }
        }

        public bool IsEinheitEnabled => string.Equals(SelectedTyp, "DIREKT", StringComparison.OrdinalIgnoreCase);
        public double EinheitOpacity => IsEinheitEnabled ? 1.0 : 0.5;

        /// <summary>
        /// Stichtag wird für die automatische Eigentümer-Ermittlung bei DIREKT-Zählern verwendet.
        /// Best-Workflow: Heute.
        /// </summary>
        public ZaehlerNeuDialog(int liegenschaftId,
                                DateTime stichtag,
                                ObservableCollection<StweEinheit> einheiten,
                                ObservableCollection<StweEigentuemer> owners,
                                IEnumerable<StweZaehlerLine>? existingLines = null)
        {
            InitializeComponent();

            _stichtag = stichtag.Date;

            if (einheiten != null)
                foreach (var e in einheiten) Einheiten.Add(e);

            if (owners != null)
                foreach (var o in owners) _owners.Add(o);

            Model.LiegenschaftId = liegenschaftId;
            Model.Typ = "DIREKT";
            _selectedTyp = "DIREKT";

            if (existingLines != null)
                ResultLines = existingLines.Select(x => (x.EigentuemerId, x.AnteilProzent)).ToList();

            DataContext = this;
            OnPropertyChanged(nameof(LinesInfo));

            // falls DIREKT+Einheit bereits vorbelegt wäre
            EnsureDirectAutoLinesIfPossible();
        }

        // Für Bearbeiten: Model-Werte übernehmen
        public void SetModel(StweZaehler existing)
        {
            if (existing == null) return;

            Model.Id = existing.Id;
            Model.LiegenschaftId = existing.LiegenschaftId;
            Model.Name = existing.Name ?? "";
            Model.Typ = (existing.Typ ?? "").Trim().ToUpperInvariant();
            Model.EinheitId = existing.EinheitId;
            Model.Notiz = existing.Notiz;

            SelectedTyp = string.IsNullOrWhiteSpace(Model.Typ) ? "DIREKT" : Model.Typ;

            if (Model.EinheitId.HasValue)
                SelectedEinheit = Einheiten.FirstOrDefault(x => x.Id == Model.EinheitId.Value);
            else
                SelectedEinheit = null;

            OnPropertyChanged(nameof(HeaderText));

            // Bei Bearbeiten nie überschreiben, falls Zeilen existieren – Methode ist defensiv.
            EnsureDirectAutoLinesIfPossible();
        }

        private void EditLines_Click(object sender, RoutedEventArgs e)
        {
            if (_owners.Count == 0)
            {
                MessageBox.Show("Keine Eigentümer vorhanden. Bitte zuerst Eigentümer erfassen.",
                    "Zähler", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // SchluesselZeilenDialog erwartet StweSchluesselLine – wir mappen die Zählerzeilen darauf.
            var existing = ResultLines.Select(x => new StweSchluesselLine
            {
                SchluesselId = 0,
                EigentuemerId = x.EigentuemerId,
                EigentuemerName = _owners.FirstOrDefault(o => o.Id == x.EigentuemerId)?.Name ?? "",
                AnteilProzent = x.AnteilProzent
            }).ToList();

            var dlg = new SchluesselZeilenDialog($"Zähler: {Model.Name}", _owners, existing);
            TrySetOwner(dlg);

            if (dlg.ShowDialog() == true)
            {
                ResultLines = dlg.Rows.Select(r => (r.EigentuemerId, r.AnteilProzent)).ToList();
                OnPropertyChanged(nameof(LinesInfo));
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate())
                return;

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private bool Validate()
        {
            Model.Name = (Model.Name ?? "").Trim();
            Model.Typ = (Model.Typ ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Model.Name))
            {
                MessageBox.Show("Bitte einen Namen erfassen.", "Zähler",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (!TypOptions.Contains(Model.Typ))
            {
                MessageBox.Show("Bitte einen gültigen Typ wählen (DIREKT/ALLG/HEIZ/EVU).", "Zähler",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (string.Equals(Model.Typ, "DIREKT", StringComparison.OrdinalIgnoreCase))
            {
                if (!Model.EinheitId.HasValue || Model.EinheitId.Value <= 0)
                {
                    MessageBox.Show("Bei Typ DIREKT muss eine Einheit gewählt werden.", "Zähler",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }
            else
            {
                Model.EinheitId = null;
            }

            // Best-Workflow: falls DIREKT + Einheit, aber User hat keine Zeilen erfasst -> automatisch setzen
            EnsureDirectAutoLinesIfPossible();

            // Verteilzeilen:
            // - EVU: keine Zeilen nötig (nur Statistik / Referenz)
            // - alle anderen: Zeilen Pflicht (Summe 100%)
            if (!string.Equals(Model.Typ, "EVU", StringComparison.OrdinalIgnoreCase))
            {
                if (ResultLines.Count == 0)
                {
                    MessageBox.Show("Bitte Verteilzeilen erfassen (Summe 100%).", "Zähler",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                var sumPct = ResultLines.Sum(x => x.AnteilProzent);
                if (Math.Abs((double)(sumPct - 100m)) > 0.0001)
                {
                    MessageBox.Show($"Summe der Verteilzeilen muss 100.0000% ergeben. Aktuell: {sumPct:N4}%.", "Zähler",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                // Doppelte Eigentümer verhindern
                var dupOwner = ResultLines.GroupBy(x => x.EigentuemerId).FirstOrDefault(g => g.Count() > 1);
                if (dupOwner != null)
                {
                    MessageBox.Show("Ein Eigentümer darf im Zähler nur einmal vorkommen.", "Zähler",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Best-Workflow:
        /// Wenn Typ DIREKT und Einheit gewählt ist, und noch keine Zeilen vorhanden sind,
        /// dann setzen wir automatisch 100% auf den Eigentümer dieser Einheit am Stichtag.
        /// Überschreibt nie bestehende Zeilen.
        /// </summary>
        private void EnsureDirectAutoLinesIfPossible()
        {
            if (!string.Equals(Model.Typ, "DIREKT", StringComparison.OrdinalIgnoreCase))
                return;

            if (!Model.EinheitId.HasValue || Model.EinheitId.Value <= 0)
                return;

            // User hat schon Zeilen erfasst -> nichts anfassen
            if (ResultLines != null && ResultLines.Count > 0)
                return;

            int? ownerId = null;
            try
            {
                ownerId = _db.StweEigentuemerGetByEinheitAtDate(Model.EinheitId.Value, _stichtag);
            }
            catch
            {
                ownerId = null;
            }

            if (!ownerId.HasValue)
                return;

            ResultLines = new List<(int EigentuemerId, decimal AnteilProzent)>
            {
                (ownerId.Value, 100m)
            };

            OnPropertyChanged(nameof(LinesInfo));
        }

        private static void TrySetOwner(Window dlg)
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                    dlg.Owner = Application.Current.MainWindow;
            }
            catch { }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
