using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Helpers;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using MyCoinFlow.Views;
using Microsoft.Win32; // für OpenFileDialog



namespace MyCoinFlow.ViewModels
{


    public class TransaktionRowExt
    {
        public int Id { get; set; }
        public DateTime Datum { get; set; }
        public bool HasAttachments { get; set; }      //PDF
        public string? AttachmentsTooltip { get; set; }  // PDF

        public string VonAnzeige { get; set; } = "";
        public string NachAnzeige { get; set; } = "";
        public decimal Betrag { get; set; }

        public string? AdresseName { get; set; }
        public string? BankName { get; set; }
        public string? Notiz { get; set; }

        // Für Bearbeiten
        public int? VonKontoId { get; set; }
        public int? NachKontoId { get; set; }
        public int? AdresseId { get; set; }
        public int? GeldinstitutId { get; set; }

        public int AttachmentCount { get; set; }
        public bool HasMultipleAttachments => AttachmentCount > 1;

    }

    public class TransactionsViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _db = new();

        public ObservableCollection<TransaktionRowExt> Transaktionen { get; } = new();

        private TransaktionRowExt? _ausgewaehlteTransaktion;
        public TransaktionRowExt? AusgewaehlteTransaktion
        {
            get => _ausgewaehlteTransaktion;
            set
            {
                _ausgewaehlteTransaktion = value;
                OnPropertyChanged();
                (BearbeitenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoeschenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand NeuBuchungCommand { get; }
        public ICommand BearbeitenCommand { get; }
        public ICommand LoeschenCommand { get; }
        public ICommand AttachPdfCommand { get; }
        public ICommand OpenAttachmentCommand { get; }
        public ICommand ManageAttachmentsCommand { get; }


        // NEU: Bankimport öffnen
        public ICommand OpenBankImportCommand { get; }

        public TransactionsViewModel()
        {
            NeuBuchungCommand = new RelayCommand(_ => NeueBuchung());
            BearbeitenCommand = new RelayCommand(_ => Bearbeiten(), _ => AusgewaehlteTransaktion != null);
            LoeschenCommand = new RelayCommand(_ => Loeschen(), _ => AusgewaehlteTransaktion != null);
            AttachPdfCommand = new RelayCommand(p => AttachPdfFromRow(p), _ => true);
            OpenAttachmentCommand = new RelayCommand(p => OpenAttachmentFromRow(p), _ => true);
            ManageAttachmentsCommand = new RelayCommand(p => ManageAttachmentsFromRow(p), _ => true);



            // NEU
            OpenBankImportCommand = new RelayCommand(_ => OpenBankImport());

            LadeListe();
        }


        private void NeueBuchung()
        {
            try
            {
                var dlg = new TransactionsDialog();

                System.Windows.Window? owner = null;
                try
                {
                    owner = System.Windows.Application.Current?.Windows
                                .OfType<System.Windows.Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? System.Windows.Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, dlg))
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                if (dlg.ShowDialog() == true)
                    LadeListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dialogfehler (Neue Buchung): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Bearbeiten()
        {
            try
            {
                if (AusgewaehlteTransaktion == null) return;

                var t = new Transaktion
                {
                    Id = AusgewaehlteTransaktion.Id,
                    Datum = AusgewaehlteTransaktion.Datum,
                    VonKontoId = AusgewaehlteTransaktion.VonKontoId,
                    NachKontoId = AusgewaehlteTransaktion.NachKontoId,
                    Betrag = AusgewaehlteTransaktion.Betrag,
                    AdresseId = AusgewaehlteTransaktion.AdresseId,
                    GeldinstitutId = AusgewaehlteTransaktion.GeldinstitutId,
                    Notiz = AusgewaehlteTransaktion.Notiz
                };

                var dlg = new TransactionsDialog(t);

                System.Windows.Window? owner = null;
                try
                {
                    owner = System.Windows.Application.Current?.Windows
                                .OfType<System.Windows.Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? System.Windows.Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, dlg))
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                if (dlg.ShowDialog() == true)
                    LadeListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dialogfehler (Bearbeiten): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void Loeschen()
        {
            if (AusgewaehlteTransaktion == null) return;

            var ask = MessageBox.Show("Ausgewählte Transaktion wirklich löschen?",
                                      "Löschen bestätigen",
                                      MessageBoxButton.YesNo,
                                      MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes) return;

            _db.LoescheTransaktion(AusgewaehlteTransaktion.Id);
            LadeListe();
        }

        private void LadeListe()
        {
            Transaktionen.Clear();

            // Kontobezeichnungen cachen
            var kontoMap = new Dictionary<int, string>();
            foreach (var k in _db.LadeKontenplan())
            {
                string unter = string.IsNullOrWhiteSpace(k.Untergruppe) ? "" : $"  {k.Untergruppe}";
                kontoMap[k.Id] = $"{k.Kontonummer:D4}{unter}  {k.Detail}";
            }

            var list = _db.LadeTransaktionen();

            foreach (var t in list)
            {
                // Von/Nach lesbar aufbereiten (Fallbacks wie gehabt)
                string von = t.VonKontoId.HasValue
                    ? (kontoMap.TryGetValue(t.VonKontoId.Value, out var vk) ? vk : $"Konto #{t.VonKontoId}")
                    : (t.BankName ?? "Bank");

                string nach = t.NachKontoId.HasValue
                    ? (kontoMap.TryGetValue(t.NachKontoId.Value, out var nk) ? nk : $"Konto #{t.NachKontoId}")
                    : (t.BankName ?? "Bank");

                // --- NEU: Attach-Details genau 1x laden und für Zähler + Tooltip nutzen
                var details = _db.LoadAttachmentDetailsByTransaktionId(t.Id);

                string? tooltip = null;
                if (details.Count > 0)
                {
                    var names = details.Select(d => d.FileName).Take(3).ToList();
                    tooltip = string.Join("\n", names);
                    if (details.Count > names.Count)
                        tooltip += $"\n… (+{details.Count - names.Count})";
                }

                Transaktionen.Add(new TransaktionRowExt
                {
                    Id = t.Id,
                    Datum = t.Datum,

                    VonAnzeige = von,
                    NachAnzeige = nach,
                    Betrag = t.Betrag,

                    AdresseName = t.AdresseName,
                    BankName = t.BankName,
                    Notiz = t.Notiz,

                    // IDs für Bearbeiten/Löschen (wie gehabt, falls vorhanden)
                    VonKontoId = t.VonKontoId,
                    NachKontoId = t.NachKontoId,
                    AdresseId = t.AdresseId,
                    GeldinstitutId = t.GeldinstitutId,

                    // --- NEU: Anhänge
                    AttachmentCount = details.Count,         // Zähler
                    HasAttachments = details.Count > 0,     // Flag
                    AttachmentsTooltip = tooltip                // Kurzliste für Tooltip
                });
            }
        }



        private void OpenBankImport()
        {
            try
            {
                var wnd = new BankImportWindow();

                System.Windows.Window? owner = null;
                try
                {
                    owner = System.Windows.Application.Current?.Windows
                                .OfType<System.Windows.Window>()
                                .FirstOrDefault(w => w.IsActive)
                         ?? System.Windows.Application.Current?.MainWindow;
                }
                catch { }

                if (owner != null && !ReferenceEquals(owner, wnd))
                    wnd.Owner = owner;
                else
                    wnd.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                wnd.ShowDialog();

                // Wenn notwendig: Liste neu laden
                // LadeListe();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fensterfehler (Bankimport): {ex.Message}", "Transaktionen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // statt: private static int AsTransaktionIdFromParam(object? p)
        private int AsTransaktionIdFromParam(object? p)
        {
            if (p is int i && i > 0) return i;
            if (p is string s && int.TryParse(s, out var j) && j > 0) return j;
            return AusgewaehlteTransaktion?.Id ?? 0;
        }

        private void AttachPdfFromRow(object? p)
        {
            int id = AsTransaktionIdFromParam(p);
            if (id <= 0) return;

            try
            {
                var dlg = new OpenFileDialog
                {
                    // NEU: PDF + Bilder
                    Filter = "Dokumente und Bilder|*.pdf;*.jpg;*.jpeg;*.png|PDF|*.pdf|Bilder|*.jpg;*.jpeg;*.png|Alle Dateien|*.*",
                    Title = "Datei anhängen",
                    Multiselect = false,
                    CheckFileExists = true
                };
                var ok = dlg.ShowDialog();
                if (ok != true) return;

                var service = new AttachmentService();
                var path = service.AttachAndSave(id, dlg.FileName);

                // Liste neu laden und Selektion auf die gleiche Transaktion zurücksetzen
                var keepId = id;
                LadeListe();
                foreach (var row in Transaktionen)
                    if (row.Id == keepId) { AusgewaehlteTransaktion = row; break; }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Anhängen fehlgeschlagen: " + ex.Message, "Datei anhängen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // Neu: öffnet bei 1 Anhang direkt die Datei, bei >1 den AttachmentsDialog
        private void OpenAttachmentFromRow(object? p)
        {
            int id = AsTransaktionIdFromParam(p);
            if (id <= 0) return;

            try
            {
                var details = _db.LoadAttachmentDetailsByTransaktionId(id);
                if (details.Count == 0)
                {
                    MessageBox.Show("Zu dieser Buchung sind keine Anhänge vorhanden.", "Anhänge öffnen",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var (root, _) = _db.GetAttachmentSettings();
                if (string.IsNullOrWhiteSpace(root))
                {
                    var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    root = System.IO.Path.Combine(doc, "MyCoinFlow", "Attachments");
                }

                if (details.Count == 1)
                {
                    var d = details[0];
                    var full = System.IO.Path.Combine(root, d.FolderRel, d.FileName);
                    if (System.IO.File.Exists(full))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(full) { UseShellExecute = true });
                    }
                    else
                    {
                        var dir = System.IO.Path.Combine(root, d.FolderRel);
                        if (System.IO.Directory.Exists(dir))
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                    }
                    return;
                }

                // Mehrere Anhänge: Dialog im Open-Only-Modus (keine Lösch-Buttons)
                var dlg = new MyCoinFlow.Views.AttachmentsDialog(id, allowDelete: false)
                {
                    Owner = System.Windows.Application.Current?.Windows?.OfType<System.Windows.Window>()?.FirstOrDefault(w => w.IsActive)
                            ?? System.Windows.Application.Current?.MainWindow
                };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Öffnen fehlgeschlagen: " + ex.Message, "Anhänge öffnen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void ManageAttachmentsFromRow(object? p)
        {
            int id = AsTransaktionIdFromParam(p);
            if (id <= 0) return;

            try
            {
                var dlg = new AttachmentsDialog(id)
                {
                    Owner = Application.Current?.Windows?.OfType<Window>()?.FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow
                };

                var result = dlg.ShowDialog();
                // Dialog setzt DialogResult = true, wenn Änderungen (z. B. Löschung) erfolgt sind
                if (result == true)
                {
                    var keepId = id;
                    LadeListe();
                    // Auswahl wiederherstellen
                    foreach (var row in Transaktionen)
                        if (row.Id == keepId) { AusgewaehlteTransaktion = row; break; }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Verwalten fehlgeschlagen: " + ex.Message, "Anhänge verwalten",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    
}
