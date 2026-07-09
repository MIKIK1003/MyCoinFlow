using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.UI.Base;
using MessageBox = System.Windows.MessageBox;

namespace MyCoinFlow.Views
{
    public partial class SchluesselZeilenDialog : BaseWindow, INotifyPropertyChanged
    {
        public sealed class RowVm : INotifyPropertyChanged
        {
            private int? _einheitId;
            private string _einheitBezeichnung = "";
            private int _eigentuemerId;
            private string _eigentuemerName = "";
            private decimal _anteilProzent;

            public int? EinheitId
            {
                get => _einheitId;
                set { _einheitId = value; OnPropertyChanged(); }
            }

            public string EinheitBezeichnung
            {
                get => _einheitBezeichnung;
                set { _einheitBezeichnung = value; OnPropertyChanged(); }
            }

            public int EigentuemerId
            {
                get => _eigentuemerId;
                set { _eigentuemerId = value; OnPropertyChanged(); }
            }

            public string EigentuemerName
            {
                get => _eigentuemerName;
                set { _eigentuemerName = value; OnPropertyChanged(); }
            }

            public decimal AnteilProzent
            {
                get => _anteilProzent;
                set { _anteilProzent = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ObservableCollection<StweEigentuemer> Owners { get; } = new();
        public ObservableCollection<StweEinheit> Einheiten { get; } = new();
        public ObservableCollection<RowVm> Rows { get; } = new();

        public string TitleText { get; private set; } = "Schlüssel-Zeilen";

        public string SumInfo
            => $"Summe: {Rows.Sum(r => r.AnteilProzent):N4}%  (muss 100.0000% ergeben)";

        public SchluesselZeilenDialog(
            string schluesselName,
            System.Collections.Generic.IEnumerable<StweEigentuemer> owners,
            System.Collections.Generic.IEnumerable<StweEinheit> einheiten,
            System.Collections.Generic.IEnumerable<StweSchluesselLine> existing)
        {
            InitializeComponent();

            TitleText = $"Schlüssel: {schluesselName} (Fix %)";

            foreach (var e in einheiten.OrderBy(x => x.Bezeichnung))
                Einheiten.Add(e);

            foreach (var o in owners.OrderBy(x => x.Name))
                Owners.Add(o);

            foreach (var e in existing)
            {
                Rows.Add(new RowVm
                {
                    EinheitId = e.EinheitId,
                    EinheitBezeichnung = e.EinheitBezeichnung,
                    EigentuemerId = e.EigentuemerId,
                    EigentuemerName = e.EigentuemerName,
                    AnteilProzent = e.AnteilProzent
                });
            }

            Rows.CollectionChanged += (_, __) => RaiseSumInfo();

            DataContext = this;
        }

        private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SyncDisplayNames();
                RaiseSumInfo();
            }));
        }

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            Rows.Add(new RowVm
            {
                EinheitId = null,
                EinheitBezeichnung = "",
                EigentuemerId = 0,
                EigentuemerName = "",
                AnteilProzent = 0m
            });

            RaiseSumInfo();
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is RowVm row)
                Rows.Remove(row);

            RaiseSumInfo();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SyncDisplayNames();

            if (!Validate())
                return;

            DialogResult = true;
        }

        private void SyncDisplayNames()
        {
            foreach (var r in Rows)
            {
                var einheit = r.EinheitId.HasValue
                    ? Einheiten.FirstOrDefault(x => x.Id == r.EinheitId.Value)
                    : null;

                r.EinheitBezeichnung = einheit?.Bezeichnung ?? "";

                var owner = Owners.FirstOrDefault(x => x.Id == r.EigentuemerId);
                r.EigentuemerName = owner?.Name ?? "";
            }
        }

        private bool Validate()
        {
            if (Rows.Count == 0)
            {
                MessageBox.Show("Bitte mindestens eine Zeile erfassen.", "Schlüssel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (Rows.Any(r => r.EigentuemerId <= 0))
            {
                MessageBox.Show("Bitte in allen Zeilen einen Eigentümer/Fallback wählen.", "Schlüssel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (Rows.Any(r => r.AnteilProzent < 0m))
            {
                MessageBox.Show("Anteile dürfen nicht negativ sein.", "Schlüssel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var sum = Rows.Sum(r => r.AnteilProzent);
            if (Math.Abs((double)(sum - 100m)) > 0.0001)
            {
                MessageBox.Show($"Summe muss 100.0000% ergeben. Aktuell: {sum:N4}%", "Schlüssel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var dupEinheit = Rows
                .Where(r => r.EinheitId.HasValue)
                .GroupBy(r => r.EinheitId!.Value)
                .FirstOrDefault(g => g.Count() > 1);

            if (dupEinheit != null)
            {
                MessageBox.Show("Eine Einheit darf im Schlüssel nur einmal vorkommen.", "Schlüssel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var dupOwnerWithoutUnit = Rows
                .Where(r => !r.EinheitId.HasValue)
                .GroupBy(r => r.EigentuemerId)
                .FirstOrDefault(g => g.Count() > 1);

            if (dupOwnerWithoutUnit != null)
            {
                MessageBox.Show("Ein Eigentümer ohne Einheit darf im Schlüssel nur einmal vorkommen.", "Schlüssel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        private void RaiseSumInfo()
        {
            OnPropertyChanged(nameof(SumInfo));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}