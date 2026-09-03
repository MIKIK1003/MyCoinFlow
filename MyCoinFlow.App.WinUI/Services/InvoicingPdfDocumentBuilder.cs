using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using MyCoinFlow.WinUI.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QRCoder;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MdColor = MigraDoc.DocumentObjectModel.Color;
using MdDocument = MigraDoc.DocumentObjectModel.Document;
using MdSection = MigraDoc.DocumentObjectModel.Section;

namespace MyCoinFlow.WinUI.Services;

public static class InvoicingPdfDocumentBuilder
{
    public const int TemplateVersion = InvoicingOutputTemplateVersions.Current;
    private const double PointsPerMillimeter = 72d / 25.4d;
    private static readonly CultureInfo SwissCulture = CultureInfo.GetCultureInfo("de-CH");
    private static readonly MdColor Accent = MdColor.FromRgb(13, 87, 76);
    private static readonly MdColor Muted = MdColor.FromRgb(91, 100, 112);
    private static readonly MdColor Border = MdColor.FromRgb(207, 214, 221);
    private static readonly MdColor LightFill = MdColor.FromRgb(241, 246, 245);

    public static InvoicingPdfArtifact Build(InvoicingOutputWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.RequiresPaymentSnapshot && workspace.Snapshot is null)
            throw Validation("Vor der PDF-Erzeugung muss ein Zahlungskonto verbindlich gewählt werden.");

        var renderer = new PdfDocumentRenderer()
        {
            Document = CreateDocument(workspace.Document)
        };
        renderer.RenderDocument();
        var pdf = renderer.PdfDocument;
        if (workspace.Snapshot is { } snapshot)
        {
            if (snapshot.HasSwissQr) AppendSwissQrPage(pdf, workspace.Document, snapshot);
            else AppendAlternativePaymentPage(pdf, workspace.Document, snapshot);
        }

        var identityHash = CreateIdentityHash(workspace);
        NormalizeMetadata(pdf, workspace, identityHash);
        var pageCount = pdf.PageCount;
        using var stream = new MemoryStream();
        pdf.Save(stream, closeStream: false);
        var content = NormalizeSerializedPdf(stream.ToArray(), identityHash);
        return new InvoicingPdfArtifact(
            content,
            workspace.SuggestedFileName,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            pageCount,
            workspace.Snapshot?.QrPayload ?? string.Empty);
    }

    private static MdDocument CreateDocument(InvoicingDocumentRecord record)
    {
        var document = new MdDocument();
        document.Info.Title = $"{record.DocumentTypeDisplay} {record.DocumentNumber}";
        document.Info.Subject = record.Subject;
        document.Info.Author = record.IssuerName;
        ConfigureStyles(document);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.TopMargin = Unit.FromMillimeter(16);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(17);
        section.PageSetup.LeftMargin = Unit.FromMillimeter(18);
        section.PageSetup.RightMargin = Unit.FromMillimeter(18);
        AddFooter(section, record);
        AddHeader(section, record);
        AddRecipient(section, record);
        AddTitle(section, record);
        AddPositions(section, record);
        AddFinancialSummary(section, record);
        AddClosing(section, record);
        return document;
    }

    private static void ConfigureStyles(MdDocument document)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = Unit.FromPoint(9);
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(3);
        var h1 = document.Styles[StyleNames.Heading1]!;
        h1.Font.Name = "Arial";
        h1.Font.Size = Unit.FromPoint(19);
        h1.Font.Bold = true;
        h1.Font.Color = Accent;
        h1.ParagraphFormat.SpaceBefore = Unit.FromPoint(8);
        h1.ParagraphFormat.SpaceAfter = Unit.FromPoint(8);
        var h2 = document.Styles[StyleNames.Heading2]!;
        h2.Font.Name = "Arial";
        h2.Font.Size = Unit.FromPoint(11);
        h2.Font.Bold = true;
        h2.Font.Color = Accent;
        h2.ParagraphFormat.SpaceBefore = Unit.FromPoint(10);
        h2.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
    }

    private static void AddFooter(MdSection section, InvoicingDocumentRecord record)
    {
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Name = "Arial";
        footer.Format.Font.Size = Unit.FromPoint(7.5);
        footer.Format.Font.Color = Muted;
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.AddText($"{record.IssuerName} · {record.DocumentNumber} · Seite ");
        footer.AddPageField();
        footer.AddText(" von ");
        footer.AddNumPagesField();
    }

    private static void AddHeader(MdSection section, InvoicingDocumentRecord record)
    {
        var table = section.AddTable();
        table.Borders.Visible = false;
        table.AddColumn(Unit.FromMillimeter(112));
        table.AddColumn(Unit.FromMillimeter(62));
        var row = table.AddRow();
        var name = row.Cells[0].AddParagraph(record.IssuerName);
        name.Format.Font.Size = Unit.FromPoint(13);
        name.Format.Font.Bold = true;
        name.Format.Font.Color = Accent;
        AddLine(row.Cells[0], record.IssuerStreet, 8, Muted);
        AddLine(row.Cells[0], JoinLocation(record.IssuerPostalCode, record.IssuerCity), 8, Muted);
        AddLine(row.Cells[0], record.IssuerCountryCode, 8, Muted);
        var contact = row.Cells[1].AddParagraph();
        contact.Format.Alignment = ParagraphAlignment.Right;
        contact.Format.Font.Size = Unit.FromPoint(8);
        contact.Format.Font.Color = Muted;
        AddTextLine(contact, record.IssuerEmail);
        AddTextLine(contact, record.IssuerPhone);
        if (!string.IsNullOrWhiteSpace(record.IssuerVatNumber))
            AddTextLine(contact, $"MWST {record.IssuerVatNumber}");
        var divider = section.AddParagraph();
        divider.Format.SpaceBefore = Unit.FromPoint(5);
        divider.Format.SpaceAfter = Unit.FromPoint(10);
        divider.Format.Borders.Bottom.Width = Unit.FromPoint(1.2);
        divider.Format.Borders.Bottom.Color = Accent;
    }

    private static void AddRecipient(MdSection section, InvoicingDocumentRecord record)
    {
        var label = section.AddParagraph("Empfänger");
        label.Format.Font.Size = Unit.FromPoint(7.5);
        label.Format.Font.Bold = true;
        label.Format.Font.Color = Muted;
        var recipient = section.AddParagraph();
        recipient.Format.SpaceAfter = Unit.FromPoint(12);
        recipient.AddFormattedText(record.RecipientName, TextFormat.Bold);
        AddTextLine(recipient, record.RecipientStreet);
        AddTextLine(recipient, JoinLocation(record.RecipientPostalCode, record.RecipientCity));
        AddTextLine(recipient, record.RecipientCountry);
    }

    private static void AddTitle(MdSection section, InvoicingDocumentRecord record)
    {
        if (record.Status == InvoicingDocumentStatusCodes.Draft)
        {
            var warning = section.AddParagraph("ENTWURF · NICHT ZUR ZAHLUNG VERWENDEN");
            warning.Format.Font.Size = Unit.FromPoint(9);
            warning.Format.Font.Bold = true;
            warning.Format.Font.Color = Colors.DarkRed;
            warning.Format.Shading.Color = MdColor.FromRgb(255, 235, 235);
            warning.Format.Borders.Width = Unit.FromPoint(0.7);
            warning.Format.Borders.Color = MdColor.FromRgb(190, 40, 40);
            warning.Format.LeftIndent = Unit.FromMillimeter(3);
            warning.Format.SpaceAfter = Unit.FromPoint(9);
        }
        section.AddParagraph($"{record.DocumentTypeDisplay} {record.DocumentNumber}", StyleNames.Heading1);
        if (!string.IsNullOrWhiteSpace(record.Subject))
        {
            var subject = section.AddParagraph(record.Subject);
            subject.Format.Font.Size = Unit.FromPoint(11);
            subject.Format.Font.Bold = true;
            subject.Format.SpaceAfter = Unit.FromPoint(8);
        }
        var metadata = section.AddTable();
        metadata.Borders.Visible = false;
        metadata.AddColumn(Unit.FromMillimeter(30));
        metadata.AddColumn(Unit.FromMillimeter(57));
        metadata.AddColumn(Unit.FromMillimeter(30));
        metadata.AddColumn(Unit.FromMillimeter(57));
        AddMetadataRow(metadata, "Dokumentdatum", record.DateDisplay, "Status", record.StatusDisplay);
        AddMetadataRow(metadata, "Vorgang", record.ContextTitleSnapshot, "Währung", record.CurrencyCode);
        AddMetadataRow(metadata, "Bezug", record.ContextSubtitleSnapshot, "Vorgänger", NullDash(record.PreviousDocumentNumber));
    }

    private static void AddPositions(MdSection section, InvoicingDocumentRecord record)
    {
        section.AddParagraph("Positionen", StyleNames.Heading2);
        var table = section.AddTable();
        table.Format.Font.Size = Unit.FromPoint(8);
        table.Borders.Color = Border;
        table.Borders.Width = Unit.FromPoint(0.4);
        table.Rows.LeftIndent = Unit.Zero;
        table.AddColumn(Unit.FromMillimeter(11));
        table.AddColumn(Unit.FromMillimeter(76));
        table.AddColumn(Unit.FromMillimeter(23));
        table.AddColumn(Unit.FromMillimeter(29));
        table.AddColumn(Unit.FromMillimeter(35));
        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Shading.Color = LightFill;
        SetCell(header.Cells[0], "Pos.", ParagraphAlignment.Left);
        SetCell(header.Cells[1], "Bezeichnung", ParagraphAlignment.Left);
        SetCell(header.Cells[2], "Menge", ParagraphAlignment.Right);
        SetCell(header.Cells[3], "Einzelpreis", ParagraphAlignment.Right);
        SetCell(header.Cells[4], "Betrag", ParagraphAlignment.Right);

        foreach (var position in record.Positions.Where(value => !value.IsFooter).OrderBy(value => value.SequenceNumber))
        {
            if (position.IsTextPosition)
            {
                var textRow = table.AddRow();
                textRow.Cells[0].MergeRight = 4;
                var paragraph = textRow.Cells[0].AddParagraph(
                    JoinText(position.Designation, position.MainTextPlain, position.AdditionalTextPlain));
                paragraph.Format.Font.Italic = true;
                paragraph.Format.Font.Color = Muted;
                continue;
            }
            var row = table.AddRow();
            row.TopPadding = Unit.FromPoint(3);
            row.BottomPadding = Unit.FromPoint(3);
            SetCell(row.Cells[0], position.SequenceNumber.ToString(CultureInfo.InvariantCulture), ParagraphAlignment.Left);
            var description = row.Cells[1].AddParagraph();
            description.AddFormattedText(position.Designation, TextFormat.Bold);
            AppendMuted(row.Cells[1], position.MainTextPlain);
            AppendMuted(row.Cells[1], position.AdditionalTextPlain);
            SetCell(row.Cells[2], $"{position.Quantity.ToString("N2", SwissCulture)} {position.Unit}", ParagraphAlignment.Right);
            var hidePrices = record.DocumentType == InvoicingDocumentTypeCodes.Delivery;
            SetCell(row.Cells[3], hidePrices ? "—" : Money(position.UnitPrice), ParagraphAlignment.Right);
            SetCell(
                row.Cells[4],
                hidePrices ? "—" : $"{Money(position.LineTotal)}\n{VatLabel(position)}",
                ParagraphAlignment.Right);
        }
        if (record.Positions.All(value => value.IsFooter))
        {
            var row = table.AddRow();
            row.Cells[0].MergeRight = 4;
            SetCell(row.Cells[0], "Keine abrechenbaren Positionen vorhanden.", ParagraphAlignment.Left);
        }
        foreach (var position in record.Positions.Where(value => value.IsFooter).OrderBy(value => value.SequenceNumber))
        {
            var value = JoinText(position.Designation, position.MainTextPlain, position.AdditionalTextPlain);
            if (string.IsNullOrWhiteSpace(value)) continue;
            var paragraph = section.AddParagraph(value);
            paragraph.Format.SpaceBefore = Unit.FromPoint(6);
            paragraph.Format.Font.Size = Unit.FromPoint(8.5);
            paragraph.Format.Font.Color = Muted;
        }
    }

    private static void AddFinancialSummary(MdSection section, InvoicingDocumentRecord record)
    {
        if (record.DocumentType == InvoicingDocumentTypeCodes.Delivery) return;
        section.AddParagraph("Zusammenfassung", StyleNames.Heading2);
        var table = section.AddTable();
        table.Borders.Visible = false;
        table.Rows.LeftIndent = Unit.FromMillimeter(84);
        table.AddColumn(Unit.FromMillimeter(55));
        table.AddColumn(Unit.FromMillimeter(35));
        var financial = record.Financial;
        if (financial is null)
        {
            AddAmountRow(table, "Positionswert", record.PositionsTotal, record.CurrencyCode, true);
            return;
        }
        AddAmountRow(table, "Netto", financial.NetAmount, record.CurrencyCode);
        if (financial.DiscountAmount != 0m)
            AddAmountRow(table, $"Rabatt {financial.DiscountPercent:N2} %", -financial.DiscountAmount, record.CurrencyCode);
        AddAmountRow(table, "MWST", financial.VatAmount, record.CurrencyCode);
        if (financial.RoundingAdjustment != 0m)
            AddAmountRow(table, "Rundung", financial.RoundingAdjustment, record.CurrencyCode);
        AddAmountRow(table, "Gesamtbetrag", financial.GrossAmount, record.CurrencyCode, true, true);
        var terms = section.AddParagraph();
        terms.Format.SpaceBefore = Unit.FromPoint(8);
        terms.Format.Font.Size = Unit.FromPoint(8.5);
        if (financial.DueDate is { } dueDate) AddTextLine(terms, $"Zahlbar bis {dueDate:dd.MM.yyyy}.");
        if (financial.SkontoPercent is { } skonto &&
            financial.SkontoDueDate is { } skontoDue &&
            financial.SkontoAmount is { } skontoAmount)
        {
            AddTextLine(
                terms,
                $"{skonto.ToString("N2", SwissCulture)} % Skonto bis {skontoDue:dd.MM.yyyy}: " +
                $"{Money(financial.GrossAmount - skontoAmount)} {record.CurrencyCode}.");
        }
        foreach (var installment in financial.Installments.OrderBy(value => value.SequenceNumber))
            AddTextLine(terms, $"Rate {installment.SequenceNumber}: {installment.DueDate:dd.MM.yyyy} · {Money(installment.Amount)} {record.CurrencyCode} · {installment.Label}");
        if (financial.IsAdjustment)
        {
            AddTextLine(terms, $"Bezug: Rechnung #{financial.ReferenceInvoiceDocumentId?.ToString() ?? "—"}");
            AddTextLine(terms, $"Grund: {NullDash(financial.AdjustmentReason)}");
        }
    }

    private static void AddClosing(MdSection section, InvoicingDocumentRecord record)
    {
        var paragraph = section.AddParagraph();
        paragraph.Format.SpaceBefore = Unit.FromPoint(12);
        paragraph.Format.Font.Size = Unit.FromPoint(8);
        paragraph.Format.Font.Color = Muted;
        AddTextLine(paragraph, $"Erstellt durch MyCoinFlow · Vorlagenstand {TemplateVersion}");
        if (record.ExchangeRateToBase != 1m)
            AddTextLine(paragraph, $"Eingefrorener Kurs: 1 {record.CurrencyCode} = {record.ExchangeRateToBase.ToString("N6", SwissCulture)} Basiswährung ({record.ExchangeRateSource}).");
    }

    private static void AppendSwissQrPage(PdfDocument pdf, InvoicingDocumentRecord document, InvoicingOutputSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.QrPayload))
            throw Validation("Die eingefrorene Swiss-QR-Nutzlast fehlt.");
        var page = pdf.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var graphics = XGraphics.FromPdfPage(page);
        DrawPaymentHeader(graphics, document, "Swiss QR Rechnung");
        var top = Mm(192);
        var receiptWidth = Mm(62);
        var dashed = new XPen(XColors.Black, 0.5) { DashStyle = XDashStyle.Dash };
        graphics.DrawLine(dashed, 0, top, Mm(210), top);
        graphics.DrawLine(dashed, receiptWidth, top, receiptWidth, Mm(297));
        var titleFont = Font(11, XFontStyleEx.Bold);
        var headingFont = Font(6, XFontStyleEx.Bold);
        var bodyFont = Font(8);
        var smallFont = Font(6);
        graphics.DrawString("Empfangsschein", titleFont, XBrushes.Black, Mm(5), top + Mm(5), XStringFormats.TopLeft);
        graphics.DrawString("Zahlteil", titleFont, XBrushes.Black, receiptWidth + Mm(5), top + Mm(5), XStringFormats.TopLeft);
        DrawReceipt(graphics, document, snapshot, top, headingFont, bodyFont, smallFont);

        var qrX = receiptWidth + Mm(5);
        var qrY = top + Mm(18);
        DrawVectorQr(graphics, snapshot.QrPayload, qrX, qrY, Mm(46));
        var infoX = qrX + Mm(51);
        var infoWidth = Mm(82);
        var y = top + Mm(18);
        y = DrawLabel(graphics, "Konto / Zahlbar an", FormatCreditor(snapshot, document), infoX, y, infoWidth, headingFont, bodyFont) + Mm(2);
        y = DrawLabel(graphics, "Referenz", snapshot.ReferenceDisplay, infoX, y, infoWidth, headingFont, bodyFont) + Mm(2);
        y = DrawLabel(graphics, "Zusätzliche Informationen", $"Rechnung {document.DocumentNumber}", infoX, y, infoWidth, headingFont, bodyFont) + Mm(2);
        DrawLabel(graphics, "Zahlbar durch", FormatDebtor(document), infoX, y, infoWidth, headingFont, bodyFont);
        var amountY = top + Mm(71);
        graphics.DrawString("Währung", headingFont, XBrushes.Black, qrX, amountY, XStringFormats.TopLeft);
        graphics.DrawString("Betrag", headingFont, XBrushes.Black, qrX + Mm(17), amountY, XStringFormats.TopLeft);
        graphics.DrawString(document.CurrencyCode, bodyFont, XBrushes.Black, qrX, amountY + Mm(4), XStringFormats.TopLeft);
        graphics.DrawString(Money(document.Financial!.GrossAmount), bodyFont, XBrushes.Black, qrX + Mm(17), amountY + Mm(4), XStringFormats.TopLeft);
    }

    private static void DrawReceipt(
        XGraphics graphics,
        InvoicingDocumentRecord document,
        InvoicingOutputSnapshot snapshot,
        double top,
        XFont headingFont,
        XFont bodyFont,
        XFont smallFont)
    {
        var x = Mm(5);
        var y = top + Mm(18);
        y = DrawLabel(graphics, "Konto / Zahlbar an", FormatCreditor(snapshot, document), x, y, Mm(52), headingFont, smallFont) + Mm(1.5);
        y = DrawLabel(graphics, "Referenz", snapshot.ReferenceDisplay, x, y, Mm(52), headingFont, smallFont) + Mm(1.5);
        DrawLabel(graphics, "Zahlbar durch", FormatDebtor(document), x, y, Mm(52), headingFont, smallFont);
        var amountY = top + Mm(78);
        graphics.DrawString("Währung", headingFont, XBrushes.Black, x, amountY, XStringFormats.TopLeft);
        graphics.DrawString("Betrag", headingFont, XBrushes.Black, x + Mm(17), amountY, XStringFormats.TopLeft);
        graphics.DrawString(document.CurrencyCode, bodyFont, XBrushes.Black, x, amountY + Mm(4), XStringFormats.TopLeft);
        graphics.DrawString(Money(document.Financial!.GrossAmount), bodyFont, XBrushes.Black, x + Mm(17), amountY + Mm(4), XStringFormats.TopLeft);
        graphics.DrawString("Annahmestelle", headingFont, XBrushes.Black, Mm(56), top + Mm(98), XStringFormats.BottomRight);
    }

    private static void AppendAlternativePaymentPage(PdfDocument pdf, InvoicingDocumentRecord document, InvoicingOutputSnapshot snapshot)
    {
        var page = pdf.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var graphics = XGraphics.FromPdfPage(page);
        DrawPaymentHeader(graphics, document, "Zahlungsangaben");
        var x = Mm(20);
        var y = Mm(58);
        var width = Mm(170);
        var heading = Font(8, XFontStyleEx.Bold);
        var body = Font(11);
        graphics.DrawString("Alternative Zahlungsangaben", Font(18, XFontStyleEx.Bold), AccentBrush(), x, y, XStringFormats.TopLeft);
        var boundaryNotice = document.CurrencyCode is "CHF" or "EUR"
            ? "Kein Swiss QR Code: Das gewählte Zahlungskonto besitzt keine QR-kompatible CH-/LI-IBAN."
            : $"Kein Swiss QR Code: Die Rechnungswährung {document.CurrencyCode} liegt ausserhalb der CHF-/EUR-Grenze.";
        y = DrawWrapped(
            graphics,
            boundaryNotice,
            Font(9),
            x,
            y + Mm(10),
            width,
            Mm(4)) + Mm(5);
        y = DrawLabel(graphics, "Zahlungsempfänger", document.IssuerDisplay, x, y, width, heading, body) + Mm(5);
        y = DrawLabel(graphics, "Konto", snapshot.PaymentAccountName, x, y, width, heading, body) + Mm(5);
        y = DrawLabel(graphics, "IBAN", snapshot.IbanDisplay, x, y, width, heading, body) + Mm(5);
        if (!string.IsNullOrWhiteSpace(snapshot.Bic))
            y = DrawLabel(graphics, "BIC / SWIFT", snapshot.Bic, x, y, width, heading, body) + Mm(5);
        if (!string.IsNullOrWhiteSpace(snapshot.AccountNumber))
            y = DrawLabel(graphics, "Kontonummer", snapshot.AccountNumber, x, y, width, heading, body) + Mm(5);
        y = DrawLabel(graphics, "Verwendungszweck", snapshot.ReferenceDisplay, x, y, width, heading, body) + Mm(5);
        DrawLabel(graphics, "Betrag", $"{Money(document.Financial!.GrossAmount)} {document.CurrencyCode}", x, y, width, heading, body);
    }

    private static void DrawPaymentHeader(XGraphics graphics, InvoicingDocumentRecord document, string label)
    {
        graphics.DrawString(document.IssuerName, Font(13, XFontStyleEx.Bold), AccentBrush(), Mm(18), Mm(16), XStringFormats.TopLeft);
        graphics.DrawString($"{document.DocumentTypeDisplay} {document.DocumentNumber} · {label}", Font(9), XBrushes.Black, Mm(18), Mm(26), XStringFormats.TopLeft);
        graphics.DrawLine(new XPen(XColor.FromArgb(13, 87, 76), 1.2), Mm(18), Mm(34), Mm(192), Mm(34));
    }

    private static void DrawVectorQr(XGraphics graphics, string payload, double x, double y, double size)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(
            payload,
            QRCodeGenerator.ECCLevel.M,
            forceUtf8: true,
            utf8BOM: false,
            eciMode: QRCodeGenerator.EciMode.Utf8,
            requestedVersion: -1);
        const int quiet = 4;
        var count = data.ModuleMatrix.Count - (quiet * 2);
        if (count <= 0) throw Validation("Der Swiss QR Code konnte nicht aufgebaut werden.");
        var moduleSize = size / count;
        for (var row = quiet; row < data.ModuleMatrix.Count - quiet; row++)
        {
            var modules = data.ModuleMatrix[row];
            for (var column = quiet; column < modules.Length - quiet; column++)
            {
                if (!modules[column]) continue;
                graphics.DrawRectangle(
                    XBrushes.Black,
                    x + ((column - quiet) * moduleSize),
                    y + ((row - quiet) * moduleSize),
                    moduleSize + 0.01,
                    moduleSize + 0.01);
            }
        }
        DrawSwissCross(graphics, x + (size / 2), y + (size / 2));
    }

    private static void DrawSwissCross(XGraphics graphics, double x, double y)
    {
        var outer = Mm(7);
        var inner = Mm(5.5);
        graphics.DrawRectangle(XBrushes.White, x - outer / 2, y - outer / 2, outer, outer);
        graphics.DrawRectangle(XBrushes.Black, x - inner / 2, y - inner / 2, inner, inner);
        var arm = Mm(1.05);
        var length = Mm(3.7);
        graphics.DrawRectangle(XBrushes.White, x - arm / 2, y - length / 2, arm, length);
        graphics.DrawRectangle(XBrushes.White, x - length / 2, y - arm / 2, length, arm);
    }

    private static double DrawLabel(
        XGraphics graphics,
        string label,
        string text,
        double x,
        double y,
        double width,
        XFont labelFont,
        XFont textFont)
    {
        graphics.DrawString(label, labelFont, XBrushes.Black, x, y, XStringFormats.TopLeft);
        return DrawWrapped(graphics, NullDash(text), textFont, x, y + Mm(3.3), width, Mm(3.5));
    }

    private static double DrawWrapped(XGraphics graphics, string text, XFont font, double x, double y, double width, double lineHeight)
    {
        foreach (var sourceLine in text.Replace("\r", string.Empty).Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in sourceLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
                if (graphics.MeasureString(candidate, font).Width <= width || string.IsNullOrEmpty(line))
                {
                    line = candidate;
                    continue;
                }
                graphics.DrawString(line, font, XBrushes.Black, x, y, XStringFormats.TopLeft);
                y += lineHeight;
                line = word;
            }
            if (string.IsNullOrEmpty(line)) continue;
            graphics.DrawString(line, font, XBrushes.Black, x, y, XStringFormats.TopLeft);
            y += lineHeight;
        }
        return y;
    }

    private static byte[] CreateIdentityHash(InvoicingOutputWorkspace workspace)
    {
        var record = workspace.Document;
        var identity = string.Join(
            "|",
            record.Id.ToString(CultureInfo.InvariantCulture),
            record.DocumentNumber,
            record.CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture),
            workspace.Snapshot?.CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            TemplateVersion.ToString(CultureInfo.InvariantCulture));
        return SHA256.HashData(Encoding.UTF8.GetBytes(identity));
    }

    private static void NormalizeMetadata(
        PdfDocument pdf,
        InvoicingOutputWorkspace workspace,
        byte[] identityHash)
    {
        var record = workspace.Document;
        var stableTime = DateTime.SpecifyKind(record.Financial?.FinalizedAt ?? record.CreatedAt, DateTimeKind.Unspecified);
        pdf.Info.Title = $"{record.DocumentTypeDisplay} {record.DocumentNumber}";
        pdf.Info.Subject = record.Subject;
        pdf.Info.Author = record.IssuerName;
        pdf.Info.Creator = $"MyCoinFlow PDF-Vorlage {TemplateVersion}";
        pdf.Info.CreationDate = stableTime;
        pdf.Info.ModificationDate = stableTime;
        pdf.Internals.FirstDocumentID = Convert.ToHexString(identityHash.AsSpan(0, 16));
        pdf.Internals.SecondDocumentID = Convert.ToHexString(identityHash.AsSpan(16, 16));
    }

    private static byte[] NormalizeSerializedPdf(byte[] content, byte[] identityHash)
    {
        // PDFsharp erzeugt zufällige, aber rein technische Font-Subset-Präfixe und XMP-UUIDs.
        // Gleich lange Ersetzungen halten Streamlängen und XRef-Offsets unverändert.
        var serialized = Encoding.Latin1.GetString(content);
        serialized = Regex.Replace(
            serialized,
            @"[A-Z]{6}(?=\+Arial(?:[,/]))",
            "MCFLOW",
            RegexOptions.CultureInvariant);
        var documentId = new Guid(identityHash.AsSpan(0, 16)).ToString();
        var instanceId = new Guid(identityHash.AsSpan(16, 16)).ToString();
        serialized = Regex.Replace(
            serialized,
            @"(?<=<xmpMM:DocumentID>uuid:)[0-9a-fA-F-]{36}",
            documentId,
            RegexOptions.CultureInvariant);
        serialized = Regex.Replace(
            serialized,
            @"(?<=<xmpMM:InstanceID>uuid:)[0-9a-fA-F-]{36}",
            instanceId,
            RegexOptions.CultureInvariant);
        return Encoding.Latin1.GetBytes(serialized);
    }

    private static void AddMetadataRow(Table table, string label1, string value1, string label2, string value2)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(1.5);
        row.BottomPadding = Unit.FromPoint(1.5);
        SetMetadata(row.Cells[0], label1);
        SetCell(row.Cells[1], NullDash(value1), ParagraphAlignment.Left);
        SetMetadata(row.Cells[2], label2);
        SetCell(row.Cells[3], NullDash(value2), ParagraphAlignment.Left);
    }

    private static void SetMetadata(Cell cell, string value)
    {
        var paragraph = cell.AddParagraph(value);
        paragraph.Format.Font.Size = Unit.FromPoint(8);
        paragraph.Format.Font.Bold = true;
        paragraph.Format.Font.Color = Muted;
    }

    private static void AddAmountRow(Table table, string label, decimal amount, string currency, bool bold = false, bool topBorder = false)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(2.5);
        row.BottomPadding = Unit.FromPoint(2.5);
        if (topBorder)
        {
            row.Borders.Top.Width = Unit.FromPoint(1);
            row.Borders.Top.Color = Accent;
        }
        var left = row.Cells[0].AddParagraph(label);
        left.Format.Font.Bold = bold;
        var right = row.Cells[1].AddParagraph($"{Money(amount)} {currency}");
        right.Format.Alignment = ParagraphAlignment.Right;
        right.Format.Font.Bold = bold;
        if (!bold) return;
        left.Format.Font.Size = Unit.FromPoint(10.5);
        right.Format.Font.Size = Unit.FromPoint(10.5);
    }

    private static void SetCell(Cell cell, string text, ParagraphAlignment alignment)
    {
        var paragraph = cell.AddParagraph(text);
        paragraph.Format.Alignment = alignment;
    }

    private static void AppendMuted(Cell cell, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var paragraph = cell.AddParagraph(text.Trim());
        paragraph.Format.Font.Size = Unit.FromPoint(7.5);
        paragraph.Format.Font.Color = Muted;
    }

    private static void AddLine(Cell cell, string value, double size, MdColor color)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var paragraph = cell.AddParagraph(value.Trim());
        paragraph.Format.Font.Size = Unit.FromPoint(size);
        paragraph.Format.Font.Color = color;
    }

    private static void AddTextLine(Paragraph paragraph, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (paragraph.Elements.Count > 0) paragraph.AddLineBreak();
        paragraph.AddText(value.Trim());
    }

    private static string VatLabel(InvoicingDocumentPositionRecord value) =>
        value.VatRatePercentSnapshot is { } rate
            ? $"inkl. {rate.ToString("N2", SwissCulture)} % MWST"
            : "ohne MWST-Satz";

    private static string FormatCreditor(InvoicingOutputSnapshot snapshot, InvoicingDocumentRecord document) =>
        string.Join("\n", new[]
        {
            snapshot.IbanDisplay,
            document.IssuerName,
            document.IssuerStreet,
            JoinLocation(document.IssuerPostalCode, document.IssuerCity),
            document.IssuerCountryCode
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string FormatDebtor(InvoicingDocumentRecord document) =>
        string.Join("\n", new[]
        {
            document.RecipientName,
            document.RecipientStreet,
            JoinLocation(document.RecipientPostalCode, document.RecipientCity),
            document.RecipientCountry
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string JoinLocation(string postalCode, string city) =>
        string.Join(" ", new[] { postalCode, city }.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string JoinText(params string[] values) =>
        string.Join("\n", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
    private static string NullDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    private static string Money(decimal value) => value.ToString("N2", SwissCulture);
    private static double Mm(double value) => value * PointsPerMillimeter;
    private static XFont Font(double size, XFontStyleEx style = XFontStyleEx.Regular) => new("Arial", size, style);
    private static XBrush AccentBrush() => new XSolidBrush(XColor.FromArgb(13, 87, 76));
    private static InvoicingOutputValidationException Validation(string message) => new([message]);
}
