using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Data;
using MyCoinFlow.WinUI.Models;
using Windows.Graphics;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingMasterDataWindow : PersistentWindow
{
    private readonly InvoicingMasterDataRepository _repository = new();
    private InvoicingMasterDataSnapshot? _snapshot;
    private bool _editorOpen;
    private bool _loading;

    public InvoicingMasterDataWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1320, 840));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 760;
            presenter.PreferredMinimumHeight = 620;
        }
        RootGrid.SizeChanged += OnRootSizeChanged;
        Activated += OnActivated;
    }

    public bool Changed { get; private set; }

    private InvoicingArticleRecord? SelectedArticle =>
        ArticlesList.SelectedItem as InvoicingArticleRecord;

    private InvoicingUnitProfileRecord? SelectedProfile =>
        ProfilesList.SelectedItem as InvoicingUnitProfileRecord;

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_snapshot is not null || _loading) return;
        ApplyResponsiveLayout(RootGrid.ActualWidth);
        await ReloadAsync();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= 820;
        Grid.SetColumnSpan(ArticleListCard, wide ? 1 : 2);
        Grid.SetRow(ArticleDetailCard, wide ? 0 : 1);
        Grid.SetColumn(ArticleDetailCard, wide ? 1 : 0);
        Grid.SetColumnSpan(ArticleDetailCard, wide ? 1 : 2);
        ArticleDetailCard.MaxHeight = wide ? double.PositiveInfinity : 190;

        Grid.SetColumnSpan(ProfileListCard, wide ? 1 : 2);
        Grid.SetRow(ProfileDetailCard, wide ? 0 : 1);
        Grid.SetColumn(ProfileDetailCard, wide ? 1 : 0);
        Grid.SetColumnSpan(ProfileDetailCard, wide ? 1 : 2);
        ProfileDetailCard.MaxHeight = wide ? double.PositiveInfinity : 220;
    }

    private async Task ReloadAsync(int? articleId = null, int? unitId = null, int? usageId = null)
    {
        if (_loading) return;
        _loading = true;
        LoadingOverlay.Visibility = Visibility.Visible;
        StatusInfoBar.IsOpen = false;
        try
        {
            articleId ??= SelectedArticle?.Id;
            unitId ??= SelectedProfile?.UnitId;
            usageId ??= SelectedProfile?.UsageId;
            _snapshot = await _repository.LoadAsync();
            ApplyFilter(articleId, unitId, usageId);
            var activeArticles = _snapshot.Articles.Count(article => article.IsActive);
            var profiledUnits = _snapshot.UnitProfiles
                .Where(profile => profile.UsageId.HasValue)
                .Select(profile => profile.UnitId)
                .Distinct()
                .Count();
            SummaryText.Text =
                $"{activeArticles:N0} aktive Artikel / Leistungen · {profiledUnits:N0} Einheiten mit Nutzung";
            FooterStatusText.Text =
                $"{_snapshot.Articles.Count:N0} Artikel / Leistungen · " +
                $"{_snapshot.UnitProfiles.Count:N0} Immobilien-/Zeitraumzeilen · Schema v{InvoicingSchema.CurrentVersion}";
        }
        catch (Exception exception)
        {
            _snapshot = null;
            ArticlesList.ItemsSource = null;
            ProfilesList.ItemsSource = null;
            ShowStatus(
                "Fakturierungsstammdaten konnten nicht geladen werden: " + exception.Message,
                InfoBarSeverity.Error);
            SummaryText.Text = "Ladefehler";
            FooterStatusText.Text = "Keine Daten wurden verändert.";
        }
        finally
        {
            _loading = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            UpdateActionsAndDetails();
        }
    }

    private void ApplyFilter(int? articleId = null, int? unitId = null, int? usageId = null)
    {
        if (_snapshot is null) return;
        var search = SearchBox.Text.Trim();
        var articles = _snapshot.Articles
            .Where(article => Matches(search,
                article.ArticleNumber,
                article.Designation,
                article.Description,
                article.Category,
                article.RevenueAccountDisplay,
                article.AncillaryDisplay))
            .ToList();
        var profiles = _snapshot.UnitProfiles
            .Where(profile => Matches(search,
                profile.PropertyName,
                profile.UnitName,
                profile.UnitType,
                profile.UsageDisplay,
                profile.OwnerName,
                profile.OwnerBillingAddress,
                profile.TenantAddress,
                profile.ContractReference))
            .ToList();

        ArticlesList.ItemsSource = articles;
        ArticlesList.SelectedItem = articleId.HasValue
            ? articles.FirstOrDefault(article => article.Id == articleId.Value)
            : articles.FirstOrDefault();
        ProfilesList.ItemsSource = profiles;
        ProfilesList.SelectedItem = unitId.HasValue
            ? profiles.FirstOrDefault(profile =>
                profile.UnitId == unitId.Value && profile.UsageId == usageId)
                ?? profiles.FirstOrDefault(profile => profile.UnitId == unitId.Value)
            : profiles.FirstOrDefault();
        UpdateActionsAndDetails();
    }

    private static bool Matches(string search, params string[] values) =>
        string.IsNullOrWhiteSpace(search) ||
        values.Any(value => value.Contains(search, StringComparison.CurrentCultureIgnoreCase));

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnArticleSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateActionsAndDetails();

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateActionsAndDetails();

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateActionsAndDetails();

    private void UpdateActionsAndDetails()
    {
        var articleTab = MasterDataTabs.SelectedIndex == 0;
        var canEdit = !_loading && !_editorOpen;
        NewArticleButton.IsEnabled = canEdit;
        EditArticleButton.IsEnabled = canEdit && articleTab && SelectedArticle is not null;
        NewRevenueAccountButton.IsEnabled = canEdit && CurrentUserContext.IsAdmin;
        NewUsageButton.IsEnabled = canEdit && !articleTab && SelectedProfile is not null;
        EditUsageButton.IsEnabled =
            canEdit && !articleTab && SelectedProfile?.UsageId.HasValue == true;
        DeleteUsageButton.IsEnabled = EditUsageButton.IsEnabled;
        RenderArticleDetail();
        RenderProfileDetail();
    }

    private void RenderArticleDetail()
    {
        var article = SelectedArticle;
        ArticleDetailTitle.Text = article is null
            ? "Kein Artikel ausgewählt"
            : $"{article.ArticleNumber} · {article.Designation}";
        ArticleDetailDescription.Text = article is null
            ? "Die Suche liefert keine Auswahl oder es sind noch keine Artikel vorhanden."
            : string.IsNullOrWhiteSpace(article.Description)
                ? "Keine zusätzliche Beschreibung"
                : article.Description;
        ArticlePriceText.Text = article?.PriceDisplay ?? "—";
        ArticleVatText.Text = article?.VatDisplay ?? "—";
        ArticleRevenueText.Text = article?.RevenueAccountDisplay ?? "—";
        ArticleClassificationText.Text = article is null
            ? "—"
            : $"{article.AncillaryDisplay} · {article.ActiveDisplay}";
    }

    private void RenderProfileDetail()
    {
        var profile = SelectedProfile;
        ProfileDetailTitle.Text = profile?.UnitName ?? "Keine Einheit ausgewählt";
        ProfileChainPropertyText.Text = profile?.PropertyAndUnit ?? "—";
        ProfileChainUsageText.Text = profile?.UsageDisplay ?? "—";
        ProfileChainOwnerText.Text = profile is null
            ? "—"
            : $"{profile.ResponsiblePartyDisplay} · " +
              (string.IsNullOrWhiteSpace(profile.OwnerBillingAddress)
                  ? "Rechnungsadresse fehlt"
                  : profile.OwnerBillingAddress);
        ProfileChainRecipientText.Text = profile is null
            ? "—"
            : string.IsNullOrWhiteSpace(profile.OwnerBillingAddress)
                ? "Nicht bereit · Eigentümer-Rechnungsadresse fehlt"
                : $"{profile.OwnerBillingAddress} · Eigentümer ist sicherer Standard";
        ProfileRentalText.Text = profile is null || profile.UsageType != InvoicingUsageTypes.Rented
            ? "Keine Mieter-Direktfakturierung für Selbstnutzung, Leerstand oder fehlende Nutzung."
            : $"{(string.IsNullOrWhiteSpace(profile.TenantAddress) ? "Mieteradresse fehlt" : profile.TenantAddress)} · " +
              $"{InvoicingAncillaryModes.DisplayName(profile.AncillaryMode)} · " +
              $"{(profile.DirectBillingAllowed ? "Direktfakturierung dokumentiert" : "Direktfakturierung nicht freigegeben")} · " +
              $"{(string.IsNullOrWhiteSpace(profile.ContractReference) ? "Vertragsreferenz fehlt" : profile.ContractReference)}";
    }

    private async void OnNewArticleClick(object sender, RoutedEventArgs e) =>
        await EditArticleAsync(null);

    private async void OnEditArticleClick(object sender, RoutedEventArgs e) =>
        await EditArticleAsync(SelectedArticle);

    private async Task EditArticleAsync(InvoicingArticleRecord? article)
    {
        if (_snapshot is null || _editorOpen) return;
        _editorOpen = true;
        UpdateActionsAndDetails();
        try
        {
            var editor = new InvoicingArticleEditorWindow(
                _snapshot,
                article?.ToDraft(),
                _repository);
            if (!await editor.ShowAsync() || !editor.Saved) return;
            Changed = true;
            await ReloadAsync(articleId: editor.SavedArticleId);
            ShowStatus(
                article is null ? "Artikel / Leistung gespeichert." : "Artikel / Leistung aktualisiert.",
                InfoBarSeverity.Success);
        }
        finally
        {
            _editorOpen = false;
            UpdateActionsAndDetails();
        }
    }

    private async void OnNewRevenueAccountClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentUserContext.IsAdmin)
        {
            ShowStatus(
                "Neue Ertragskonten dürfen nur von Administratorinnen und Administratoren angelegt werden.",
                InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var before = (await _repository.LoadAccountCandidatesAsync())
                .Select(account => account.Id)
                .ToHashSet();
            var dialog = new AccountEditorDialog { XamlRoot = RootGrid.XamlRoot };
            await dialog.InitializeAsync();
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !dialog.Saved) return;

            var added = (await _repository.LoadAccountCandidatesAsync())
                .Where(account => !before.Contains(account.Id))
                .ToList();
            if (added.Count != 1)
                throw new InvalidOperationException(
                    "Das neu angelegte Konto konnte nicht eindeutig ermittelt werden. Bitte unter Finanzen als Ertragskonto zulassen.");

            await _repository.RegisterRevenueAccountAsync(added[0].Id);
            Changed = true;
            await ReloadAsync();
            ShowStatus(
                $"Ertragskonto {added[0].Display} wurde angelegt und für Fakturieren zugelassen.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus("Ertragskonto konnte nicht angelegt werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnNewUsageClick(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile is null || _snapshot is null) return;
        var draft = new InvoicingUnitProfileDraft
        {
            UnitId = profile.UnitId,
            UsageType = InvoicingUsageTypes.OwnerOccupied,
            ValidFrom = new DateTimeOffset(DateTime.Today),
            OwnerId = profile.OwnerId,
            OwnerBillingAddressId = profile.OwnerBillingAddressId
        };
        await EditUsageAsync(profile, draft);
    }

    private async void OnEditUsageClick(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile?.UsageId is null) return;
        await EditUsageAsync(profile, profile.ToDraft());
    }

    private async Task EditUsageAsync(
        InvoicingUnitProfileRecord profile,
        InvoicingUnitProfileDraft draft)
    {
        if (_snapshot is null || _editorOpen) return;
        _editorOpen = true;
        UpdateActionsAndDetails();
        try
        {
            var editor = new InvoicingUnitProfileEditorWindow(
                profile.PropertyAndUnit,
                _snapshot,
                draft,
                _repository);
            if (!await editor.ShowAsync() || !editor.Saved) return;
            Changed = true;
            await ReloadAsync(unitId: profile.UnitId, usageId: draft.UsageId);
            ShowStatus(
                profile.UsageId.HasValue ? "Nutzungs-/Empfängerprofil aktualisiert." : "Nutzungs-/Empfängerprofil gespeichert.",
                InfoBarSeverity.Success);
        }
        finally
        {
            _editorOpen = false;
            UpdateActionsAndDetails();
        }
    }

    private async void OnDeleteUsageClick(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile?.UsageId is null) return;
        var confirmation = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Nutzungszeitraum löschen?",
            Content =
                $"{profile.PropertyAndUnit}{Environment.NewLine}{profile.UsageDisplay}{Environment.NewLine}{Environment.NewLine}" +
                "Ein zugehöriges Mietverhältnis wird ebenfalls entfernt. Adressen, Eigentümer und Immobilien bleiben erhalten.",
            PrimaryButtonText = "Löschen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _repository.DeleteUnitProfilePeriodAsync(profile.UsageId.Value, profile.TenancyId);
            Changed = true;
            await ReloadAsync(unitId: profile.UnitId);
            ShowStatus("Nutzungszeitraum und zugehöriges Mietverhältnis wurden gelöscht.", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus("Nutzungszeitraum konnte nicht gelöscht werden: " + exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnRefreshShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ReloadAsync();
    }

    private void OnSearchShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnNewShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (MasterDataTabs.SelectedIndex == 0)
            await EditArticleAsync(null);
        else
            OnNewUsageClick(this, new RoutedEventArgs());
    }

    private void OnCloseShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}
