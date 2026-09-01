using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Globalization;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class SubscriptionEditorDialog : PersistentWindow
{
    private sealed record AccountOption(int Id, string Display);
    private readonly Abo? _source;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;

    public SubscriptionEditorDialog(Abo? source = null)
    {
        InitializeComponent();
        _source = source;
        Title = source is null ? "Neue Zahlungsserie" : "Zahlungsserie bearbeiten";
        WindowHeadingText.Text = Title;
        AppWindow.Resize(new SizeInt32(1400, 880));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1220;
            presenter.PreferredMinimumHeight = 800;
        }
        Closed += OnWindowClosed;

        var database = new DatabaseService();
        AddressBox.ItemsSource = database.LadeAdressen();
        AccountBox.ItemsSource = database.LadeKontenplan()
            .Select(value => new AccountOption(value.Id,
                $"{value.Kontonummer:D4}{(string.IsNullOrWhiteSpace(value.Untergruppe) ? "" : "  " + value.Untergruppe)}  {value.Detail}"))
            .ToList();

        var categories = database.AboKategorienLaden(true)
            .Where(value => value.IstAktiv || value.Code == source?.Kategorie)
            .OrderBy(value => value.Sortierung).ThenBy(value => value.Bezeichnung).ToList();
        CategoryBox.ItemsSource = categories;
        CategoryBox.DisplayMemberPath = nameof(AboKategorie.Bezeichnung);
        CategoryBox.SelectedValuePath = nameof(AboKategorie.Code);
        DirectionBox.ItemsSource = new[] { Zahlungsrichtungen.Einnahme, Zahlungsrichtungen.Ausgabe };
        PeriodBox.ItemsSource = new[] { AboPerioden.Monatlich, AboPerioden.Quartalsweise, AboPerioden.Halbjaehrlich, AboPerioden.Jaehrlich };
        StatusBox.ItemsSource = new[] { AboStatus.Aktiv, AboStatus.Gekuendigt, AboStatus.Beendet };
        CancellationRouteBox.ItemsSource = new[]
        {
            "Online-Kundenkonto", "Apple App Store", "Google Play Store",
            "E-Mail", "Support-Chat", "Schriftlich", "Telefonisch"
        };

        NameBox.Text = source?.Name ?? string.Empty;
        AddressBox.SelectedValue = source?.AdresseId;
        CategoryBox.SelectedValue = source?.Kategorie is null or AboKategorien.Pruefen
            ? AboKategorien.Sonstige
            : source.Kategorie;
        DirectionBox.SelectedItem = source?.Richtung is Zahlungsrichtungen.Einnahme or Zahlungsrichtungen.Ausgabe
            ? source.Richtung
            : Zahlungsrichtungen.Ausgabe;
        PeriodBox.SelectedItem = source?.Periodizitaet ?? AboPerioden.Monatlich;
        StatusBox.SelectedItem = source?.Status ?? AboStatus.Aktiv;
        AmountBox.Text = source?.ErwarteterBetrag?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
        ToleranceBox.Text = (source?.BetragToleranzProzent ?? 10m).ToString("0.##", CultureInfo.CurrentCulture);
        CancelledDatePicker.Date = source?.GekuendigtAm;
        EndDatePicker.Date = source?.KuendigenZum;
        NoticeDaysBox.Text = source?.KuendigungsfristTage?.ToString() ?? string.Empty;
        WarningDaysBox.Text = (source?.VorwarnTage ?? 7).ToString();
        AccountBox.SelectedValue = source?.ErwartetesKontoId;
        WebsiteBox.Text = source?.WebseiteUrl ?? string.Empty;
        CancellationRouteBox.Text = source?.Kuendigungsweg ?? string.Empty;
        NoteBox.Text = source?.Notiz ?? string.Empty;
        UpdateHint();
    }

    public Abo? Result { get; private set; }

    public Task<bool> ShowAsync()
    {
        Activate();
        return _completion.Task;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorBar.Message = "Bitte eine Bezeichnung für die Zahlungsserie erfassen.";
            ErrorBar.IsOpen = true;
            return;
        }

        if (CategoryBox.SelectedValue is not string category)
        {
            ErrorBar.Message = "Bitte eine Serienart wählen.";
            ErrorBar.IsOpen = true;
            return;
        }

        if (DirectionBox.SelectedItem is not string direction)
        {
            ErrorBar.Message = "Bitte Einnahme oder Ausgabe als Richtung wählen.";
            ErrorBar.IsOpen = true;
            return;
        }

        var status = StatusBox.SelectedItem as string ?? AboStatus.Aktiv;
        var cancelled = CancelledDatePicker.Date?.Date;
        if (status == AboStatus.Gekuendigt && !cancelled.HasValue)
            cancelled = DateTime.Today;

        var tolerance = ParseDecimal(ToleranceBox.Text);
        var warning = ParseInt(WarningDaysBox.Text);
        Result = new Abo
        {
            Id = _source?.Id ?? 0,
            Name = NameBox.Text.Trim(),
            AdresseId = AddressBox.SelectedValue as int?,
            AdresseName = (AddressBox.SelectedItem as Adresse)?.Name,
            Richtung = direction,
            Kategorie = category,
            Periodizitaet = PeriodBox.SelectedItem as string ?? AboPerioden.Monatlich,
            ErwarteterBetrag = ParseDecimal(AmountBox.Text),
            BetragToleranzProzent = tolerance is >= 0m and <= 100m ? tolerance.Value : 10m,
            Status = status,
            GekuendigtAm = cancelled,
            KuendigungsfristTage = ParseInt(NoticeDaysBox.Text),
            KuendigenZum = EndDatePicker.Date?.Date,
            VorwarnTage = warning is > 0 and <= 365 ? warning.Value : 7,
            ErwartetesKontoId = AccountBox.SelectedValue as int?,
            WebseiteUrl = NullIfEmpty(WebsiteBox.Text),
            Kuendigungsweg = NullIfEmpty(CancellationRouteBox.Text),
            Notiz = NullIfEmpty(NoteBox.Text)
        };
        Complete(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(false);

    private void Complete(bool saved)
    {
        if (_completed) return;
        _completed = true;
        _completion.TrySetResult(saved);
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnWindowClosed;
        if (!_completed)
        {
            _completed = true;
            _completion.TrySetResult(false);
        }
    }

    private void OnCancellationChanged(object sender, object e) => UpdateHint();

    private void UpdateHint()
    {
        if (CancellationHintText is null)
            return;
        if (EndDatePicker.Date is not DateTimeOffset end)
        {
            CancellationHintText.Text = "Vertragsende und Kündigungsfrist erfassen, damit MyCoinFlow den spätesten Kündigungstermin zeigt.";
            return;
        }

        var deadline = end.Date.AddDays(-(ParseInt(NoticeDaysBox.Text) ?? 0));
        var days = (deadline - DateTime.Today).Days;
        CancellationHintText.Text = $"Spätestens kündigen bis {deadline:dd.MM.yyyy}"
            + (days < 0 ? $" – Termin seit {-days} Tag(en) verstrichen" : days == 0 ? " – heute" : $" – noch {days} Tage");
    }

    private static string? NullIfEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var value = text.Trim().Replace("'", string.Empty);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var result)
               || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)
            ? result
            : null;
    }

    private static int? ParseInt(string? text) =>
        int.TryParse(text?.Trim(), out var value) ? value : null;
}
