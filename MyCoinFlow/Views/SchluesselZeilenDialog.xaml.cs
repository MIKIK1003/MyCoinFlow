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
            private int _eigentuemerId;
            private string _eigentuemerName = "";
            private decimal _anteilProzent;

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
        public ObservableCollection<RowVm> Rows { get; } = new();

        public string TitleText { get; private set; } = "Schlüssel-Zeilen";

        public string SumInfo
            => $"Summe: {Rows.Sum(r => r.AnteilProzent):N4}%  (muss 100.0000% ergeben)";

        public SchluesselZeilenDialog(string schluesselName,
                                      System.Collections.Generic.IEnumerable<StweEigentuemer> owners,
                                      System.Collections.Generic.IEnumerable<StweSchluesselLine> existing)
        {
            InitializeComponent();

            TitleText = $"Schlüssel: {schluesselName} (Fix %)";

            foreach (var o in owners)
                Owners.Add(o);

            foreach (var e in existing)
            {
                Rows.Add(new RowVm
                {
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
                SyncOwnerNames();
                RaiseSumInfo();
            }));
        }

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            Rows.Add(new RowVm
            {
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
            SyncOwnerNames();

            if (!Validate())
                return;

            DialogResult = true;
        }

        private void SyncOwnerNames()
        {
            foreach (var r in Rows)
            {
                var o = Owners.FirstOrDefault(x => x.Id == r.EigentuemerId);
                r.EigentuemerName = o?.Name ?? "";
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
                MessageBox.Show("Bitte in allen Zeilen einen Eigentümer wählen.", "Schlüssel",
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

            var dup = Rows.GroupBy(r => r.EigentuemerId).FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
            {
                MessageBox.Show("Ein Eigentümer darf im Schlüssel nur einmal vorkommen.", "Schlüssel",
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