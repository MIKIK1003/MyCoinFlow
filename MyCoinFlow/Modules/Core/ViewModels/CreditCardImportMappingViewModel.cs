using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using MyCoinFlow.Models;
using ImportSchema = MyCoinFlow.Models.ImportSchema;
using FieldMapping = MyCoinFlow.Models.FieldMapping;
using MyCoinFlow.ViewModels;
using MyCoinFlow.Helpers;

namespace MyCoinFlow.ViewModels
{
    public class CreditCardImportMappingViewModel : BaseViewModel
    {
        private readonly Services.CreditCardImportMappingService _service;

        public ObservableCollection<ImportSchemaVm> Schemas { get; } = new();
        private ImportSchemaVm? _selectedSchema;

        public ImportSchemaVm? SelectedSchema
        {
            get => _selectedSchema;
            set { _selectedSchema = value; OnPropertyChanged(); ReloadFieldMappings(); }
        }

        public ObservableCollection<string> MasterHeaders { get; } = new();
        public ObservableCollection<FieldMappingVm> FieldMappings { get; } = new();

        public ICommand NewSchemaCommand { get; }
        public ICommand DeleteSchemaCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SuggestFromSampleCommand { get; }

        public bool CanSave => SelectedSchema != null && FieldMappings.Count > 0 && FieldMappings.All(m => !string.IsNullOrWhiteSpace(m.MasterHeader) && !string.IsNullOrWhiteSpace(m.SourceHeader));
        public bool CanDeleteSelectedSchema => SelectedSchema != null && !SelectedSchema.IsMaster;

        public CreditCardImportMappingViewModel(Services.CreditCardImportMappingService service)
        {
            _service = service;

            // Master-Header aus Service (das sind die Spalten deines funktionierenden Master-Excels)
            foreach (var h in _service.GetMasterHeaders())
                MasterHeaders.Add(h);

            foreach (var s in _service.GetSchemas())
                Schemas.Add(new ImportSchemaVm(s));

            SelectedSchema = Schemas.FirstOrDefault();
            FieldMappings.CollectionChanged += (_, __) => OnPropertyChanged(nameof(CanSave));
            NewSchemaCommand = new RelayCommand(_ => NewSchema(), _ => true);
            DeleteSchemaCommand = new RelayCommand(_ => DeleteSchema(), _ => CanDeleteSelectedSchema);
            SaveCommand = new RelayCommand(_ => Save(), _ => CanSave);
            SuggestFromSampleCommand = new RelayCommand(_ => SuggestFromSample(), _ => true);
            

        }

        private void ReloadFieldMappings()
        {
            FieldMappings.Clear();
            if (SelectedSchema == null) return;

            var maps = _service.GetFieldMappings(SelectedSchema.Id);
            foreach (var m in maps)
            {
                var vm = new FieldMappingVm { MasterHeader = m.MasterHeader, SourceHeader = m.SourceHeader, DefaultValue = m.DefaultValue };
                vm.PropertyChanged += (_, __) => OnPropertyChanged(nameof(CanSave));
                FieldMappings.Add(vm);

            }

            OnPropertyChanged(nameof(CanSave));
        }


        private void NewSchema()
        {
            var schema = _service.CreateSchema("Neues Schema");
            var vm = new ImportSchemaVm(schema);
            Schemas.Add(vm);
            SelectedSchema = vm;
        }

        private void DeleteSchema()
        {
            if (SelectedSchema == null || SelectedSchema.IsMaster) return;
            _service.DeleteSchema(SelectedSchema.Id);
            Schemas.Remove(SelectedSchema);
            SelectedSchema = Schemas.FirstOrDefault();
        }

        private void Save()
        {
            if (SelectedSchema != null)
                _service.UpdateSchemaName(SelectedSchema.Id, SelectedSchema.Name ?? "Unbenannt");

            if (SelectedSchema == null) return;
            var dedup = FieldMappings
                .GroupBy(f => (f.MasterHeader?.Trim().ToLowerInvariant(), f.SourceHeader?.Trim().ToLowerInvariant()))
                .Where(g => g.Count() > 1)
                .Any();
            if (dedup) return; // defensive: keine Duplikate

            _service.SaveMappings(SelectedSchema.Id,
                FieldMappings.Select(f => new FieldMapping
                {
                MasterHeader = f.MasterHeader!.Trim(),
                SourceHeader = f.SourceHeader!.Trim(),
                DefaultValue = string.IsNullOrWhiteSpace(f.DefaultValue) ? null : f.DefaultValue!.Trim()
             }).ToList());



            // nach Speichern konsistente Benachrichtigung:
            _service.NotifyLabelPropertiesChanged();

            OnPropertyChanged(nameof(CanSave));

        }

        private void SuggestFromSample()
        {
            var sampleHeaders = _service.PickHeadersFromSampleFile(); // öffnet Dateidialog
            if (sampleHeaders == null || sampleHeaders.Count == 0) return;

            // Für jeden Master-Header: vorhandene Zeile finden oder neu erzeugen
            foreach (var master in MasterHeaders)
            {
                var row = FieldMappings.FirstOrDefault(m => string.Equals(m.MasterHeader, master, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                {
                    row = new FieldMappingVm { MasterHeader = master, SourceHeader = "" };
                    row.PropertyChanged += (_, __) => OnPropertyChanged(nameof(CanSave));
                    FieldMappings.Add(row);
                }


                // Nur dann vorschlagen, wenn noch kein SourceHeader gesetzt ist
                if (string.IsNullOrWhiteSpace(row.SourceHeader))
                {
                    var hit = _service.SuggestSourceForMaster(master, sampleHeaders);
                    if (!string.IsNullOrWhiteSpace(hit))
                        row.SourceHeader = hit;
                }


            }

            OnPropertyChanged(nameof(CanSave)); // Buttons aktualisieren
        }


        public class ImportSchemaVm
        {
            public int Id { get; }
            public string Name { get; set; }
            public bool IsMaster { get; }

            public ImportSchemaVm(ImportSchema s)
            {
                Id = s.Id; Name = s.Name; IsMaster = s.IsMaster;
            }

        }

        public class FieldMappingVm : BaseViewModel

        {
            private string? _masterHeader;
            public string? MasterHeader { get => _masterHeader; set { _masterHeader = value; OnPropertyChanged(); } }

            private string? _sourceHeader;
            public string? SourceHeader { get => _sourceHeader; set { _sourceHeader = value; OnPropertyChanged(); } }

            private string? _defaultValue;
            public string? DefaultValue { get => _defaultValue; set { _defaultValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSave)); } }


        }
    }
}
