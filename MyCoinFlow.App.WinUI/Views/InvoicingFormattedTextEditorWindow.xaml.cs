using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using MyCoinFlow.WinUI.Models;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace MyCoinFlow.WinUI.Views;

public sealed partial class InvoicingFormattedTextEditorWindow : PersistentWindow
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly InvoicingFormattedTextSnapshot _initialSnapshot;
    private bool _documentLoaded;
    private bool _initialized;
    private bool _restoringSelection;
    private bool _completed;
    private int _selectionStart;
    private int _selectionEnd;

    public InvoicingFormattedTextEditorWindow(
        InvoicingFormattedTextSnapshot? snapshot = null,
        IReadOnlyList<InvoicingTextTemplateRecord>? textTemplates = null,
        string? heading = null,
        string? description = null)
    {
        InitializeComponent();
        _initialSnapshot = snapshot ?? new InvoicingFormattedTextSnapshot(string.Empty, null);
        if (!string.IsNullOrWhiteSpace(heading))
        {
            Title = heading;
            HeadingText.Text = heading;
        }
        if (!string.IsNullOrWhiteSpace(description))
            DescriptionText.Text = description;

        ConfigureDpiAwareSizing(RootGrid, 1180, 820, 760, 600);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        FontFamilyBox.ItemsSource =
            new[] { "Arial", "Calibri", "Aptos", "Segoe UI", "Times New Roman", "Courier New" };
        FontSizeBox.ItemsSource =
            new[] { 8f, 9f, 10f, 11f, 12f, 14f, 16f, 18f, 20f, 24f, 28f, 32f };
        FontFamilyBox.SelectedItem = "Calibri";
        FontSizeBox.SelectedItem = 10f;
        TemplateBox.ItemsSource = textTemplates;
        TemplateBox.Visibility = textTemplates is { Count: > 0 }
            ? Visibility.Visible
            : Visibility.Collapsed;
        TemplateColumn.Width = textTemplates is { Count: > 0 }
            ? new GridLength(280)
            : new GridLength(0);
        Closed += OnWindowClosed;
    }

    public InvoicingFormattedTextSnapshot? Snapshot { get; private set; }

    public Task<bool> ShowAsync()
    {
        Activate();
        return _completion.Task;
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_documentLoaded) return;
        _documentLoaded = true;
        LoadDocument(_initialSnapshot.PlainText, _initialSnapshot.FormattedText);
        Editor.Focus(FocusState.Programmatic);
    }

    private void LoadDocument(string plainText, string? formattedText, string? source = null)
    {
        _initialized = false;
        var rtfLoaded = false;
        if (InvoicingFormattedText.HasRtfSignature(formattedText))
        {
            try
            {
                Editor.Document.SetText(TextSetOptions.FormatRtf, formattedText!);
                Editor.Document.GetText(TextGetOptions.None, out var loadedText);
                rtfLoaded = string.IsNullOrWhiteSpace(plainText) ||
                    string.Equals(
                        InvoicingFormattedText.NormalizeForComparison(loadedText),
                        InvoicingFormattedText.NormalizeForComparison(plainText),
                        StringComparison.Ordinal);
            }
            catch
            {
                rtfLoaded = false;
            }
        }

        if (!rtfLoaded)
        {
            Editor.Document.SetText(TextSetOptions.None, plainText);
            StatusText.Text = string.IsNullOrWhiteSpace(formattedText)
                ? source ?? "Bereit"
                : $"{source ?? "Text geladen."} Die Formatierung passte nicht zum Klartext und wurde verworfen.";
        }
        else
        {
            StatusText.Text = source is null
                ? "Klartext und Formatierung vollständig geladen."
                : $"{source} Klartext und Formatierung wurden übernommen.";
        }

        var position = Editor.Document.Selection.EndPosition;
        Editor.Document.Selection.SetRange(position, position);
        _selectionStart = position;
        _selectionEnd = position;
        _initialized = true;
    }

    private void OnTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not InvoicingTextTemplateRecord template) return;
        LoadDocument(
            template.PlainText,
            template.FormattedText,
            $"Textbaustein «{template.Name}» geladen.");
        Editor.Focus(FocusState.Programmatic);
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _restoringSelection) return;
        var selection = Editor.Document.Selection;
        if (selection.StartPosition != selection.EndPosition ||
            Editor.FocusState != FocusState.Unfocused)
        {
            _selectionStart = selection.StartPosition;
            _selectionEnd = selection.EndPosition;
        }
    }

    private void OnEditorLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        var selection = Editor.Document.Selection;
        if (selection.StartPosition == selection.EndPosition) return;
        _selectionStart = selection.StartPosition;
        _selectionEnd = selection.EndPosition;
    }

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var selection = Editor.Document.Selection;
        if (selection.StartPosition != selection.EndPosition) return;

        var caret = selection.StartPosition;
        Editor.Document.GetRange(0, caret).GetText(TextGetOptions.None, out var textBeforeCaret);
        var normalized = InvoicingFormattedText.NormalizeLineEndings(textBeforeCaret).TrimEnd('\0');
        var lineStart = normalized.LastIndexOf('\n');
        var currentLine = normalized[(lineStart + 1)..];
        if (!currentLine.StartsWith("• ", StringComparison.Ordinal)) return;

        if (string.IsNullOrWhiteSpace(currentLine[2..]))
        {
            var bulletRange = Editor.Document.GetRange(Math.Max(0, caret - 2), caret);
            bulletRange.Text = string.Empty;
            var newPosition = Math.Max(0, caret - 2);
            selection.SetRange(newPosition, newPosition);
            e.Handled = true;
            FinishFormatting("Aufzählung beendet.");
            return;
        }

        selection.Text = "\r• ";
        selection.Collapse(false);
        e.Handled = true;
        FinishFormatting("Nächster Aufzählungspunkt.");
    }

    private void OnBoldClick(object sender, RoutedEventArgs e)
    {
        RestoreSelection();
        ApplyCharacterFormat(format => format.Bold = FormatEffect.Toggle);
        FinishFormatting("Fettschrift angewendet.");
    }

    private void OnItalicClick(object sender, RoutedEventArgs e)
    {
        RestoreSelection();
        ApplyCharacterFormat(format => format.Italic = FormatEffect.Toggle);
        FinishFormatting("Kursivschrift angewendet.");
    }

    private void OnUnderlineClick(object sender, RoutedEventArgs e)
    {
        RestoreSelection();
        ApplyCharacterFormat(format =>
            format.Underline = format.Underline == UnderlineType.None
                ? UnderlineType.Single
                : UnderlineType.None);
        FinishFormatting("Unterstreichung angewendet.");
    }

    private void OnListClick(object sender, RoutedEventArgs e)
    {
        RestoreSelection();
        var selection = Editor.Document.Selection;
        if (selection.StartPosition == selection.EndPosition)
        {
            selection.Text = "• ";
            selection.Collapse(false);
            FinishFormatting("Aufzählungszeichen eingefügt.");
            return;
        }

        selection.GetText(TextGetOptions.None, out var text);
        var lines = InvoicingFormattedText.NormalizeLineEndings(text).TrimEnd('\0').Split('\n');
        var contentLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        var remove = contentLines.Count > 0 &&
            contentLines.All(line => line.TrimStart().StartsWith("• ", StringComparison.Ordinal));
        selection.Text = string.Join(Environment.NewLine, lines.Select(line =>
        {
            if (string.IsNullOrWhiteSpace(line)) return line;
            if (!remove)
                return line.TrimStart().StartsWith("• ", StringComparison.Ordinal) ? line : $"• {line}";
            var indentation = line[..(line.Length - line.TrimStart().Length)];
            return indentation + line.TrimStart()[2..];
        }));
        FinishFormatting(remove
            ? "Aufzählungszeichen entfernt."
            : "Markierte Zeilen als Aufzählung formatiert.");
    }

    private void OnFontFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || FontFamilyBox.SelectedItem is not string fontName) return;
        RestoreSelection();
        ApplyCharacterFormat(format => format.Name = fontName);
        FinishFormatting($"Schriftart «{fontName}» angewendet.");
    }

    private void OnFontSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || FontSizeBox.SelectedItem is not float fontSize) return;
        RestoreSelection();
        ApplyCharacterFormat(format => format.Size = fontSize);
        FinishFormatting($"Schriftgrösse {fontSize:0.#} angewendet.");
    }

    private void OnTextColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !TryReadColor(Convert.ToString(button.Tag, CultureInfo.InvariantCulture), out var color))
            return;

        var isInputColor = _selectionStart == _selectionEnd;
        if (isInputColor)
        {
            RestoreSelection();
            ApplyCharacterFormat(Editor.Document.Selection, format => format.ForegroundColor = color);
        }
        else
        {
            var range = Editor.Document.GetRange(_selectionStart, _selectionEnd);
            ApplyCharacterFormat(range, format => format.ForegroundColor = color);
            Editor.Document.Selection.SetRange(_selectionStart, _selectionEnd);
        }
        FinishFormatting(isInputColor
            ? "Textfarbe für die folgende Eingabe vorgewählt."
            : "Textfarbe auf die markierte Passage angewendet.");
    }

    private void OnCutClick(object sender, RoutedEventArgs e)
    {
        RestoreSelection();
        if (Editor.Document.Selection.StartPosition == Editor.Document.Selection.EndPosition)
        {
            StatusText.Text = "Zum Ausschneiden zuerst Text markieren.";
            return;
        }
        Editor.Document.Selection.Cut();
        FinishFormatting("Text ausgeschnitten.");
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        RestoreSelection();
        if (Editor.Document.Selection.StartPosition == Editor.Document.Selection.EndPosition)
        {
            StatusText.Text = "Zum Kopieren zuerst Text markieren.";
            return;
        }
        Editor.Document.Selection.Copy();
        FinishFormatting("Text kopiert.");
    }

    private void OnPasteClick(object sender, RoutedEventArgs e)
    {
        RestoreSelection();
        Editor.Document.Selection.Paste(0);
        FinishFormatting("Text eingefügt.");
    }

    private void ApplyCharacterFormat(Action<ITextCharacterFormat> change) =>
        ApplyCharacterFormat(Editor.Document.Selection, change);

    private static void ApplyCharacterFormat(
        ITextRange range,
        Action<ITextCharacterFormat> change)
    {
        var format = range.CharacterFormat.GetClone();
        change(format);
        range.CharacterFormat = format;
    }

    private void RestoreSelection()
    {
        var selection = Editor.Document.Selection;
        if (_selectionStart == _selectionEnd ||
            selection.StartPosition == _selectionStart && selection.EndPosition == _selectionEnd)
            return;
        _restoringSelection = true;
        selection.SetRange(_selectionStart, _selectionEnd);
        _restoringSelection = false;
    }

    private void FinishFormatting(string status)
    {
        var selection = Editor.Document.Selection;
        _selectionStart = selection.StartPosition;
        _selectionEnd = selection.EndPosition;
        StatusText.Text = status;
        Editor.Focus(FocusState.Programmatic);
    }

    private void OnEditorContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var selection = Editor.Document.Selection;
        var hasSelection = selection.StartPosition != selection.EndPosition;
        if (hasSelection)
        {
            _selectionStart = selection.StartPosition;
            _selectionEnd = selection.EndPosition;
        }

        var menu = new MenuFlyout();
        var cut = new MenuFlyoutItem { Text = "Ausschneiden", IsEnabled = hasSelection };
        cut.Click += OnCutClick;
        menu.Items.Add(cut);
        var copy = new MenuFlyoutItem { Text = "Kopieren", IsEnabled = hasSelection };
        copy.Click += OnCopyClick;
        menu.Items.Add(copy);
        var paste = new MenuFlyoutItem { Text = "Einfügen" };
        paste.Click += OnPasteClick;
        menu.Items.Add(paste);
        menu.Items.Add(new MenuFlyoutSeparator());
        var selectAll = new MenuFlyoutItem { Text = "Alles markieren" };
        selectAll.Click += OnSelectAllClick;
        menu.Items.Add(selectAll);

        if (args.TryGetPosition(Editor, out var position))
        {
            menu.ShowAt(Editor, new FlyoutShowOptions
            {
                Position = position,
                ShowMode = FlyoutShowMode.Transient
            });
        }
        else
        {
            menu.ShowAt(Editor);
        }
        args.Handled = true;
        StatusText.Text = "Zwischenablagefunktionen geöffnet.";
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        var documentRange = Editor.Document.GetRange(0, int.MaxValue);
        Editor.Document.Selection.SetRange(documentRange.StartPosition, documentRange.EndPosition);
        FinishFormatting("Gesamten Text markiert.");
    }

    private void OnApplyClick(object sender, RoutedEventArgs e) => ApplyAndClose();

    private void OnApplyShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ApplyAndClose();
    }

    private void ApplyAndClose()
    {
        Editor.Document.GetText(TextGetOptions.None, out var text);
        var plainText = InvoicingFormattedText.CleanPlainText(text);
        if (string.IsNullOrWhiteSpace(plainText))
        {
            Snapshot = new InvoicingFormattedTextSnapshot(string.Empty, null);
            Complete(true);
            return;
        }

        Editor.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
        rtf = rtf.TrimEnd('\0');
        if (!InvoicingFormattedText.HasRtfSignature(rtf) || !RtfMatchesPlainText(rtf, plainText))
        {
            StatusText.Text =
                "Die Formatierung konnte nicht sicher gespeichert werden. Der Editor bleibt geöffnet.";
            return;
        }

        Snapshot = new InvoicingFormattedTextSnapshot(plainText, rtf);
        Complete(true);
    }

    private static bool RtfMatchesPlainText(string rtf, string expectedPlainText)
    {
        try
        {
            var verification = new RichEditBox();
            verification.Document.SetText(TextSetOptions.FormatRtf, rtf);
            verification.Document.GetText(TextGetOptions.None, out var loadedText);
            return string.Equals(
                InvoicingFormattedText.NormalizeForComparison(loadedText),
                InvoicingFormattedText.NormalizeForComparison(expectedPlainText),
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadColor(string? value, out Color color)
    {
        color = ColorHelper.FromArgb(255, 0, 0, 0);
        var hex = (value ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length != 6 ||
            !byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return false;
        color = ColorHelper.FromArgb(255, red, green, blue);
        return true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Complete(false);

    private void OnCancelShortcut(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Complete(false);
    }

    private void Complete(bool accepted)
    {
        if (_completed) return;
        _completed = true;
        _completion.TrySetResult(accepted);
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnWindowClosed;
        if (_completed) return;
        _completed = true;
        _completion.TrySetResult(false);
    }
}
