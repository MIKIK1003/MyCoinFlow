using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MyCoinFlow.Models;    // ImportSchema
using MyCoinFlow.Services;  // DatabaseService

namespace MyCoinFlow.Views
{
    /// <summary>
    /// Schlanker Editor: Neu -> Tabelle (Master | Feld | Fallback) -> Speichern; Dropdown zum Öffnen.
    /// Keine Refresh-/Musterdatei-Funktionen, Standard-UI.
    /// </summary>
    public partial class CreditCardImportMappingView : UserControl
    {
        // Grid-Daten
        public ObservableCollection<MappingRow> Rows { get; } = new();
        // Dropdown-Quelle links
        public ObservableCollection<string> MasterHeaders { get; } = new();

        private readonly DatabaseService _db = new();
        private ImportSchema? _currentSchema;

        public CreditCardImportMappingView()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                await LoadMasterHeadersAsync();
                await LoadSchemasAsync();

                // Sichtbar: sofort vorbefüllen, falls leer
                if (Rows.Count == 0) PrefillRowsWithMasterDefaults();

                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Initialisierung fehlgeschlagen:\n" + ex.Message,
                    "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= UI-Events =================

        private void SchemaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var sel = SchemaCombo.SelectedItem as ImportSchema;
                if (sel == null) return;

                _currentSchema = sel;
                SchemaNameBox.Text = sel.Name;

                LoadMappingsForSchema(sel.Id);
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Schema konnte nicht geladen werden:\n" + ex.Message,
                    "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SchemaNameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateButtons();

        private void NewSchema_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _currentSchema = null;
                SchemaCombo.SelectedItem = null;

                if (string.IsNullOrWhiteSpace(SchemaNameBox.Text))
                    SchemaNameBox.Text = $"Schema {DateTime.Now:yyyyMMdd_HHmm}";

                PrefillRowsWithMasterDefaults();
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Neues Schema konnte nicht vorbereitet werden:\n" + ex.Message,
                    "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveSchema_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // GUARD: reservierter Name "Master" ist nicht erlaubt (wird von der App intern verwendet)
                var typedName = (SchemaNameBox.Text ?? "").Trim();
                if (typedName.Equals("Master", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Der Name 'Master' ist reserviert und kann nicht verwendet werden.",
                        "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 1) Schema sichern (anlegen/umbenennen)
                if (_currentSchema == null)
                {
                    if (typedName.Length < 3)
                    {
                        MessageBox.Show("Bitte gültigen Schema-Namen eingeben (≥ 3 Zeichen).",
                            "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var created = _db.ImportSchemaInsert(new ImportSchema
                    {
                        Name = typedName,
                        IsMaster = false
                    });
                    _currentSchema = created;
                }
                else
                {
                    if (typedName.Length < 3)
                    {
                        MessageBox.Show("Bitte gültigen Schema-Namen eingeben (≥ 3 Zeichen).",
                            "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (!string.Equals(typedName, _currentSchema.Name, StringComparison.Ordinal))
                    {
                        _db.ImportSchemaUpdateName(_currentSchema.Id, typedName);
                        _currentSchema.Name = typedName;
                    }
                }

                // 2) Mappings speichern (unabhängig vom DTO-Namen via Reflection)
                PersistMappingsByReflection(_currentSchema.Id);

                // 3) UI aktualisieren
                await LoadSchemasAsync();
                SchemaCombo.SelectedValue = _currentSchema.Id;

                MessageBox.Show("Schema wurde gespeichert.", "Kreditkarten",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Speichern fehlgeschlagen:\n" + ex.Message,
                    "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void DeleteSchema_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSchema == null)
                {
                    MessageBox.Show("Kein Schema gewählt.", "Löschen",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Master niemals löschen (sollte gar nicht in der Liste sein)
                if (_currentSchema.IsMaster ||
                    string.Equals(_currentSchema.Name, "Master", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Das Master-Schema kann nicht gelöscht werden.", "Löschen",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var ask = MessageBox.Show(
                    $"Schema „{_currentSchema.Name}“ wirklich löschen?",
                    "Löschen bestätigen",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (ask != MessageBoxResult.Yes) return;

                _db.ImportSchemaDelete(_currentSchema.Id);

                // Reset UI
                _currentSchema = null;
                SchemaNameBox.Text = "";
                await LoadSchemasAsync();
                PrefillRowsWithMasterDefaults();
                UpdateButtons();

                MessageBox.Show("Schema wurde gelöscht.", "Kreditkarten",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Löschen fehlgeschlagen:\n" + ex.Message,
                    "Kreditkarten", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================= Daten laden / speichern =================

        private async Task LoadSchemasAsync()
        {
            await Task.Yield();

            var all = _db.ImportSchemasGetAll(); // enthält Master + User
            var list = all
                .Where(s => !s.IsMaster && !string.Equals(s.Name, "Master", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Name)
                .ToList();

            SchemaCombo.ItemsSource = list;

            if (_currentSchema != null)
            {
                SchemaCombo.SelectedValue = _currentSchema.Id;
                SchemaNameBox.Text = _currentSchema.Name;
            }
        }

        private void LoadMappingsForSchema(int schemaId)
        {
            Rows.Clear();

            // 1) _db.ImportFieldMappingsGet(int)
            var mi = typeof(DatabaseService).GetMethod("ImportFieldMappingsGet");
            if (mi != null && mi.GetParameters().Length == 1)
            {
                var result = mi.Invoke(_db, new object[] { schemaId }) as IEnumerable;
                if (FillRowsFromEnumerable(result)) { return; }
            }

            // 2) _db.FieldMappingsGetBySchema(int)
            mi = typeof(DatabaseService).GetMethod("FieldMappingsGetBySchema");
            if (mi != null && mi.GetParameters().Length == 1)
            {
                var result = mi.Invoke(_db, new object[] { schemaId }) as IEnumerable;
                if (FillRowsFromEnumerable(result)) { return; }
            }

            // Fallback: vorbefüllen
            PrefillRowsWithMasterDefaults();
        }

        private bool FillRowsFromEnumerable(IEnumerable? result)
        {
            if (result == null) return false;

            foreach (var item in result)
            {
                var t = item.GetType();
                var master = (t.GetProperty("MasterField")?.GetValue(item) as string)
                             ?? (t.GetProperty("MasterHeader")?.GetValue(item) as string)
                             ?? "";
                var source = (t.GetProperty("SourceField")?.GetValue(item) as string)
                             ?? (t.GetProperty("SourceHeader")?.GetValue(item) as string)
                             ?? "";
                var fallback = (t.GetProperty("FallbackValue")?.GetValue(item) as string)
                               ?? (t.GetProperty("DefaultValue")?.GetValue(item) as string)
                               ?? "";

                Rows.Add(new MappingRow
                {
                    MasterHeader = master,
                    SourceHeader = source,
                    DefaultValue = fallback
                });
            }

            return Rows.Count > 0;
        }

        private void PersistMappingsByReflection(int schemaId)
        {
            var effective = Rows
                .Where(r => !string.IsNullOrWhiteSpace(r.MasterHeader) &&
                            (!string.IsNullOrWhiteSpace(r.SourceHeader) || !string.IsNullOrWhiteSpace(r.DefaultValue)))
                .ToList();

            var mi = typeof(DatabaseService).GetMethod("ImportFieldMappingsReplace")
                     ?? typeof(DatabaseService).GetMethod("FieldMappingsReplace");
            if (mi == null)
                throw new InvalidOperationException("Persistenzmethode nicht gefunden (ImportFieldMappingsReplace/FieldMappingsReplace).");

            var prms = mi.GetParameters();
            if (prms.Length != 2)
                throw new InvalidOperationException("Persistenzmethode hat unerwartete Signatur.");

            var listType = prms[1].ParameterType;
            var elemType = listType.IsGenericType ? listType.GetGenericArguments()[0] : typeof(object);
            var list = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(elemType))!;

            foreach (var r in effective)
            {
                var dto = Activator.CreateInstance(elemType)!;
                SetIfExists(dto, "SchemaId", schemaId);
                SetIfExists(dto, "MasterField", r.MasterHeader);
                SetIfExists(dto, "MasterHeader", r.MasterHeader);
                SetIfExists(dto, "SourceField", r.SourceHeader ?? "");
                SetIfExists(dto, "SourceHeader", r.SourceHeader ?? "");
                SetIfExists(dto, "FallbackValue", string.IsNullOrWhiteSpace(r.DefaultValue) ? null : r.DefaultValue);
                SetIfExists(dto, "DefaultValue", string.IsNullOrWhiteSpace(r.DefaultValue) ? null : r.DefaultValue);
                list.Add(dto);
            }

            mi.Invoke(_db, new object[] { schemaId, list });
        }

        private static void SetIfExists(object obj, string prop, object? value)
        {
            var p = obj.GetType().GetProperty(prop);
            if (p != null && p.CanWrite) p.SetValue(obj, value);
        }

        // ================= Hilfen =================

        private async Task LoadMasterHeadersAsync()
        {
            await Task.Yield();
            MasterHeaders.Clear();

            string[] defaults =
            {
                "Transaktionsdatum",
                "Beschreibung",
                "Betrag",
                "Währung",
                "Karteninhaber",
                "Kartennummer",
                "Kategorie",
                "Referenz"
            };
            foreach (var s in defaults) MasterHeaders.Add(s);
        }

        private void PrefillRowsWithMasterDefaults()
        {
            Rows.Clear();
            foreach (var mf in MasterHeaders)
                Rows.Add(new MappingRow { MasterHeader = mf, SourceHeader = "", DefaultValue = "" });
        }

        private void UpdateButtons()
        {
            SaveButton.IsEnabled =
                (_currentSchema != null) ||
                (!string.IsNullOrWhiteSpace(SchemaNameBox.Text) && SchemaNameBox.Text!.Trim().Length >= 3);

            DeleteButton.IsEnabled = _currentSchema != null && !_currentSchema.IsMaster &&
                                     !string.Equals(_currentSchema.Name, "Master", StringComparison.OrdinalIgnoreCase);
        }
    }

    // Zeilenmodell fürs Grid
    public sealed class MappingRow
    {
        public string MasterHeader { get; set; } = "";
        public string SourceHeader { get; set; } = "";
        public string DefaultValue { get; set; } = "";
    }
}
