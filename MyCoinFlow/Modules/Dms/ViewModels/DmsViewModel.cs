using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;

namespace MyCoinFlow.ViewModels
{
    public class DmsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();
        private readonly AttachmentService _attachSvc = new();

        private ObservableCollection<DmsDocument> _dokumente = new();
        public ObservableCollection<DmsDocument> Dokumente
        {
            get => _dokumente;
            set { _dokumente = value; OnPropertyChanged(); }
        }

        private DmsDocument? _ausgewaehltesDokument;
        public DmsDocument? AusgewaehltesDokument
        {
            get => _ausgewaehltesDokument;
            set { _ausgewaehltesDokument = value; OnPropertyChanged(); }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); }
        }

        private string _kategorieFilter = "Alle";
        public string KategorieFilter
        {
            get => _kategorieFilter;
            set { _kategorieFilter = value; OnPropertyChanged(); Laden(); }
        }

        private ObservableCollection<string> _kategorien = new();
        public ObservableCollection<string> Kategorien
        {
            get => _kategorien;
            set { _kategorien = value; OnPropertyChanged(); }
        }

        public ICommand SuchenCommand { get; }
        public ICommand HochladenCommand { get; }
        public ICommand BearbeitenCommand { get; }
        public ICommand OeffnenCommand { get; }
        public ICommand LoeschenCommand { get; }

        public DmsViewModel()
        {
            _db.EnsureAttachmentsSchema();

            SuchenCommand = new RelayCommand(_ => Laden());
            HochladenCommand = new RelayCommand(_ => Hochladen());
            BearbeitenCommand = new RelayCommand(p => Bearbeiten(p as DmsDocument ?? AusgewaehltesDokument));
            OeffnenCommand = new RelayCommand(p => Oeffnen(p as DmsDocument ?? AusgewaehltesDokument));
            LoeschenCommand = new RelayCommand(p => Loeschen(p as DmsDocument ?? AusgewaehltesDokument));

            LadeKategorien();
            Laden();
        }

        private void LadeKategorien()
        {
            var liste = new ObservableCollection<string> { "Alle" };
            foreach (var k in _db.GetDistinctKategorien())
                liste.Add(k);
            Kategorien = liste;
        }

        private void Laden()
        {
            var kategorie = KategorieFilter == "Alle" ? null : KategorieFilter;
            var rows = _db.LoadAllDocuments(SearchText, kategorie);
            Dokumente = new ObservableCollection<DmsDocument>(rows);
        }

        private void Hochladen()
        {
            var dlg = new DmsDocumentDialog(null) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;

            try
            {
                _attachSvc.AttachFreestanding(dlg.AusgewaehlteDateiPfad!, dlg.Titel, dlg.Kategorie);
                LadeKategorien();
                Laden();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Hochladen fehlgeschlagen: " + ex.Message,
                    "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Bearbeiten(DmsDocument? doc)
        {
            if (doc == null) return;

            var dlg = new DmsDocumentDialog(doc) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;

            try
            {
                _db.UpdateAttachmentMeta(doc.Id, dlg.Titel, dlg.Kategorie);
                LadeKategorien();
                Laden();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Speichern fehlgeschlagen: " + ex.Message,
                    "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Oeffnen(DmsDocument? doc)
        {
            if (doc == null) return;
            try
            {
                _attachSvc.OpenAttachment(doc.Id);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Öffnen fehlgeschlagen: " + ex.Message,
                    "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Loeschen(DmsDocument? doc)
        {
            if (doc == null) return;

            var ask = MessageBox.Show(Application.Current?.MainWindow,
                $"Dokument „{doc.TitelAnzeige}“ wirklich löschen?\nDie Datei wird vom Datenträger entfernt.",
                "Löschen bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;

            try
            {
                _attachSvc.DeleteAttachment(doc.Id);
                Laden();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, "Löschen fehlgeschlagen: " + ex.Message,
                    "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
