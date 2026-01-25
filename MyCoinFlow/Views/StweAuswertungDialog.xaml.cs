using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MyCoinFlow.Views
{
    public partial class StweAuswertungDialog : Window
    {
        private readonly DatabaseService _db = new();
        private readonly StweLiegenschaft _liegenschaft;

        public string TitleText => $"Auswertung – {_liegenschaft.Name}";
        public string DetailTitle => SelectedOwnerRow == null
            ? "Details"
            : $"Details – {SelectedOwnerRow.EigentuemerName}";

        public DateTime? Von { get; set; }
        public DateTime? Bis { get; set; }

        public ObservableCollection<StweOwnerSummaryRow> OwnerRows { get; } = new();
        public ObservableCollection<StweOwnerDetailRow> DetailRows { get; } = new();

        public StweOwnerSummaryRow? SelectedOwnerRow { get; set; }

        public StweAuswertungDialog(StweLiegenschaft liegenschaft)
        {
            InitializeComponent();
            _liegenschaft = liegenschaft ?? throw new ArgumentNullException(nameof(liegenschaft));

            DataContext = this;

            // Initial laden ohne Filter
            LoadOwnerSummary();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadOwnerSummary();
        }

        private void OwnerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedOwnerRow != null)
                LoadOwnerDetails(SelectedOwnerRow.EigentuemerId);
            else
                DetailRows.Clear();

            // Title aktualisieren
            try { Title = Title; } catch { }
        }

        private void LoadOwnerSummary()
        {
            OwnerRows.Clear();
            DetailRows.Clear();
            SelectedOwnerRow = null;

            var rows = _db.StweReportOwnerSummary(_liegenschaft.Id, Von, Bis);
            foreach (var r in rows)
                OwnerRows.Add(r);

            // Auto-Select erster Eigentümer
            if (OwnerRows.Count > 0)
            {
                SelectedOwnerRow = OwnerRows[0];
                LoadOwnerDetails(SelectedOwnerRow.EigentuemerId);
            }
        }

        private void LoadOwnerDetails(int eigentuemerId)
        {
            DetailRows.Clear();
            var rows = _db.StweReportOwnerDetails(_liegenschaft.Id, eigentuemerId, Von, Bis);
            foreach (var r in rows)
                DetailRows.Add(r);
        }
    }
}
