using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using MyCoinFlow.Services;

namespace MyCoinFlow.Views
{
    public partial class AttachmentsDialog : Window
    {
        private readonly int _transaktionId;
        private readonly DatabaseService _db = new();
        private readonly AttachmentService _svc = new();

        private bool _changed;

        public AttachmentsDialog(int transaktionId)
        {
            _transaktionId = transaktionId;
            InitializeComponent();
            LoadList();
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string FileName { get; set; } = "";
            public string FolderRel { get; set; } = "";
            public string? OcrStatus { get; set; }
            public long? SizeBytes { get; set; }
            public string SizeDisplay => SizeBytes.HasValue
                ? (SizeBytes.Value >= 1024 * 1024
                    ? $"{(SizeBytes.Value / (1024.0 * 1024.0)):0.0} MB"
                    : $"{(SizeBytes.Value / 1024.0):0} KB")
                : "";
        }

        private void LoadList()
        {
            try
            {
                var details = _db.LoadAttachmentDetailsByTransaktionId(_transaktionId);
                var rows = details.Select(d => new Row
                {
                    Id = d.Id,
                    FileName = d.FileName,
                    FolderRel = d.FolderRel,
                    OcrStatus = d.OcrStatus,
                    SizeBytes = d.SizeBytes
                }).ToList();

                GridFiles.ItemsSource = rows;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Anhänge konnten nicht geladen werden: " + ex.Message,
                    "Anhänge verwalten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (GridFiles.SelectedItem is not Row row) return;
            try
            {
                var (root, _) = _db.GetAttachmentSettings();
                if (string.IsNullOrWhiteSpace(root))
                {
                    var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    root = Path.Combine(doc, "MyCoinFlow", "Attachments");
                }

                var full = Path.Combine(root, row.FolderRel, row.FileName);
                if (File.Exists(full))
                {
                    Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
                }
                else
                {
                    var dir = Path.Combine(root, row.FolderRel);
                    if (Directory.Exists(dir))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Öffnen fehlgeschlagen: " + ex.Message,
                    "Anhänge verwalten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (GridFiles.SelectedItem is not Row row) return;

            var ask = MessageBox.Show(
                $"Anhang „{row.FileName}“ wirklich löschen?\nDie Datei wird vom Datenträger entfernt.",
                "Löschen bestätigen",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ask != MessageBoxResult.Yes) return;

            try
            {
                _svc.DeleteAttachment(row.Id);
                _changed = true;
                LoadList();

                // wenn leer -> Dialog schließen (komfortabel)
                if ((GridFiles.Items?.Count ?? 0) == 0)
                {
                    this.DialogResult = true;
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Löschen fehlgeschlagen: " + ex.Message,
                    "Anhänge verwalten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = _changed ? true : false;
            this.Close();
        }
    }
}
