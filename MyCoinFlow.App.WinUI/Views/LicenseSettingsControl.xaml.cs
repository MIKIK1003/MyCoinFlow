using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Services;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class LicenseSettingsControl : UserControl
{
    private readonly LicenseService _license = new();
    public LicenseSettingsControl() { InitializeComponent(); Refresh(); }
    private void Refresh() { _license.TryLoadAndApply(out var message); LicenseInfoText.Text = message; ModulesText.Text = AppModules.GetStatusText(); }
    private void OnImportClick(object sender, RoutedEventArgs e) { var success = _license.ImportLicenseFile(null, out var message); Show(message, success ? InfoBarSeverity.Success : InfoBarSeverity.Warning); Refresh(); }
    private void OnSaveKeyClick(object sender, RoutedEventArgs e) { var key = LicenseKeyBox.Text.Trim(); if (!_license.TryValidate(key, out var payload, out var error)) { Show("Lizenz ungültig: " + error, InfoBarSeverity.Warning); return; } try { _license.SaveKey(key); Show($"Lizenz gespeichert: {payload.Edition}, Kunde {payload.Customer}. Änderungen werden beim nächsten Start wirksam.", InfoBarSeverity.Success); Refresh(); } catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); } }
    private void Show(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
}
