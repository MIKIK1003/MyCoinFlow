using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using System.Collections.ObjectModel;

namespace MyCoinFlow.WinUI.Views;
public sealed partial class DuplicatesDialog:ContentDialog
{
 private readonly TransactionToolsRepository _tools;private readonly TransactionRepository _transactions;public ObservableCollection<DuplicateRecord> Rows{get;}=new();public bool Changed{get;private set;}
 public DuplicatesDialog(TransactionToolsRepository tools,TransactionRepository transactions){InitializeComponent();_tools=tools;_transactions=transactions;RowsList.ItemsSource=Rows;}
 public async Task InitializeAsync()=>await ReloadAsync();
 private async Task ReloadAsync(){try{Rows.Clear();foreach(var row in await _tools.FindDuplicatesAsync(SameNoteCheck.IsChecked==true))Rows.Add(row);var groups=Rows.Select(x=>x.Group).Distinct().Count();StatusBar.Severity=InfoBarSeverity.Informational;StatusBar.Message=groups==0?"Keine doppelten Transaktionen gefunden.":$"{groups} Gruppen mit {Rows.Count} Transaktionen gefunden. Spalte rechts: Anzahl Belege.";}catch(Exception ex){StatusBar.Severity=InfoBarSeverity.Error;StatusBar.Message=ex.Message;}}
 private async void OnFilterClick(object sender,RoutedEventArgs e)=>await ReloadAsync();
 private void OnMarkNewerClick(object sender,RoutedEventArgs e){foreach(var group in Rows.GroupBy(x=>x.Group)){var original=group.Min(x=>x.Id);foreach(var row in group)row.Delete=row.Id!=original;}}
 private void OnClearClick(object sender,RoutedEventArgs e){foreach(var row in Rows)row.Delete=false;}
 private async void OnDeleteClick(object sender,RoutedEventArgs e){var selected=Rows.Where(x=>x.Delete).ToList();if(selected.Count==0){StatusBar.Message="Keine Transaktionen markiert.";return;}var complete=Rows.GroupBy(x=>x.Group).Count(g=>g.All(x=>x.Delete));var warning=complete>0?$" In {complete} Gruppe(n) würde auch das Original verschwinden.":"";var confirm=new ContentDialog{XamlRoot=XamlRoot,Title=$"{selected.Count} Transaktionen löschen?",Content="Abo-Zuordnungen werden entfernt; geschützte STWE-Verknüpfungen werden übersprungen."+warning,PrimaryButtonText="Löschen",CloseButtonText="Abbrechen",DefaultButton=ContentDialogButton.Close};if(await confirm.ShowAsync()!=ContentDialogResult.Primary)return;var deleted=0;var errors=new List<string>();foreach(var row in selected){try{await _transactions.DeleteAsync(row.Id);deleted++;Changed=true;}catch(Exception ex){errors.Add($"#{row.Id}: {ex.Message.Split('\n')[0]}");}}await ReloadAsync();StatusBar.Severity=errors.Count==0?InfoBarSeverity.Success:InfoBarSeverity.Warning;StatusBar.Message=$"{deleted} Transaktion(en) gelöscht."+(errors.Count==0?"":$" {errors.Count} geschützt/nicht gelöscht: {string.Join(" · ",errors.Take(3))}");}
}
