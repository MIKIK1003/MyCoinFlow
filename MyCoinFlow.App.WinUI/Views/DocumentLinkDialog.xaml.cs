using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using System.Collections.ObjectModel;
using Windows.System;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class DocumentLinkDialog : ContentDialog
{
    private readonly TransactionToolsRepository _repository;
    public ObservableCollection<UnlinkedDocumentRecord> Documents { get; } = new();
    public UnlinkedDocumentRecord? SelectedDocument => DocumentsList.SelectedItem as UnlinkedDocumentRecord;
    public UnlinkedDocumentRecord? ConfirmedDocument { get; private set; }

    public DocumentLinkDialog(TransactionToolsRepository repository) { InitializeComponent(); _repository=repository;DocumentsList.ItemsSource=Documents; }
    public async Task InitializeAsync()=>await ReloadAsync();
    private async Task ReloadAsync(){try{Documents.Clear();foreach(var row in await _repository.GetUnlinkedDocumentsAsync(SearchBox.Text))Documents.Add(row);MessageBar.IsOpen=Documents.Count==0;MessageBar.Severity=InfoBarSeverity.Informational;MessageBar.Message="Keine freien DMS-Dokumente gefunden. Du kannst stattdessen „Datei auswählen“ verwenden.";}catch(Exception ex){MessageBar.IsOpen=true;MessageBar.Severity=InfoBarSeverity.Error;MessageBar.Message=ex.Message;}}
    private async void OnSearchClick(object sender,RoutedEventArgs e)=>await ReloadAsync();
    private async void OnSearchKeyDown(object sender,KeyRoutedEventArgs e){if(e.Key==VirtualKey.Enter){e.Handled=true;await ReloadAsync();}}
    private void OnPrimaryButtonClick(ContentDialog sender,ContentDialogButtonClickEventArgs args){if(SelectedDocument is not null)return;args.Cancel=true;MessageBar.IsOpen=true;MessageBar.Severity=InfoBarSeverity.Warning;MessageBar.Message="Bitte zuerst ein Dokument auswählen.";}
    private void OnDoubleTapped(object sender,DoubleTappedRoutedEventArgs e){if(SelectedDocument is null)return;ConfirmedDocument=SelectedDocument;Hide();}
}
