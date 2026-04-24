using System;
using System.Collections.ObjectModel;
using MyCoinFlow.Models;
using MyCoinFlow.UI.Base; // NEU
using System.Windows;
using MessageBox = System.Windows.MessageBox; // Fix Mehrdeutigkeit

namespace MyCoinFlow.Views
{
    public partial class EigentumZuordnenDialog : BaseWindow // NEU
    {
        public ObservableCollection<StweEigentuemer> Owners { get; } = new();

        public StweEigentuemer? SelectedOwner { get; set; }

        public DateTime? Von { get; set; } = DateTime.Today;
        public DateTime? Bis { get; set; } = null;

        public EigentumZuordnenDialog(System.Collections.Generic.IEnumerable<StweEigentuemer> owners)
        {
            InitializeComponent();

            foreach (var o in owners)
                Owners.Add(o);

            DataContext = this;

            Loaded += (_, __) =>
            {
                try { OwnerBox?.Focus(); } catch { }
            };
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedOwner == null)
            {
                MessageBox.Show("Bitte Eigentümer auswählen.", "Zuordnung",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!Von.HasValue)
            {
                MessageBox.Show("Bitte 'Von' setzen.", "Zuordnung",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Bis.HasValue && Bis.Value.Date < Von.Value.Date)
            {
                MessageBox.Show("'Bis' darf nicht vor 'Von' liegen.", "Zuordnung",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        }
    }
}