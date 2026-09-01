from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfgen import canvas as report_canvas
from reportlab.platypus import (
    BaseDocTemplate,
    Frame,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    Table,
    TableStyle,
)
from pypdf import PdfReader, PdfWriter


ROOT = Path(r"C:\DEV\MyCoinFlow")
OUT = ROOT / "output" / "pdf" / "Zahlungsserien_Designvorschau.pdf"
BASE = ROOT / "tmp" / "pdfs" / "payment-series-base.pdf"
OVERLAY = ROOT / "tmp" / "pdfs" / "payment-series-overlay.pdf"
OUT.parent.mkdir(parents=True, exist_ok=True)

INK = colors.HexColor("#22232A")
MUTED = colors.HexColor("#565664")
PURPLE = colors.HexColor("#5B2DA9")
PURPLE_DARK = colors.HexColor("#3D1F70")
PURPLE_SOFT = colors.HexColor("#E6DCF6")
GREEN = colors.HexColor("#1F7A48")
GREEN_SOFT = colors.HexColor("#DDF2E4")
RED = colors.HexColor("#B1423B")
RED_SOFT = colors.HexColor("#F7E2E0")
TEAL = colors.HexColor("#167E91")
TEAL_SOFT = colors.HexColor("#DDF2F2")
AMBER = colors.HexColor("#B06C1F")
AMBER_SOFT = colors.HexColor("#FAEBD6")
GRAY = colors.HexColor("#F1F1F5")
RULE = colors.HexColor("#C4C2CC")

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name="Brand", fontName="Helvetica-Bold", fontSize=8.5, leading=10, textColor=PURPLE))
styles.add(ParagraphStyle(name="CoverTitle", fontName="Helvetica-Bold", fontSize=27, leading=31, textColor=PURPLE_DARK))
styles.add(ParagraphStyle(name="CoverSub", fontName="Helvetica-Bold", fontSize=14, leading=18, textColor=INK))
styles.add(ParagraphStyle(name="PageTitle", fontName="Helvetica-Bold", fontSize=22, leading=26, textColor=PURPLE_DARK))
styles.add(ParagraphStyle(name="Direction", fontName="Helvetica-Bold", fontSize=16, leading=19, textColor=INK))
styles.add(ParagraphStyle(name="Section", fontName="Helvetica-Bold", fontSize=12.5, leading=15, textColor=INK))
styles.add(ParagraphStyle(name="Body", fontName="Helvetica", fontSize=9.2, leading=13, textColor=INK))
styles.add(ParagraphStyle(name="Small", fontName="Helvetica", fontSize=8.3, leading=11, textColor=MUTED))
styles.add(ParagraphStyle(name="KpiLabel", fontName="Helvetica-Bold", fontSize=7.5, leading=9, textColor=MUTED))
styles.add(ParagraphStyle(name="KpiValue", fontName="Helvetica-Bold", fontSize=14, leading=17, textColor=INK))
styles.add(ParagraphStyle(name="Table", fontName="Helvetica", fontSize=7.3, leading=9, textColor=INK))
styles.add(ParagraphStyle(name="TableBold", parent=styles["Table"], fontName="Helvetica-Bold"))


def p(text, style="Body"):
    return Paragraph(text, styles[style])


def header_footer(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica-Bold", 7.5)
    canvas.setFillColor(PURPLE)
    canvas.drawString(20 * mm, A4[1] - 13 * mm, "MYCOINFLOW  /  ZAHLUNGSSERIEN")
    canvas.setStrokeColor(RULE)
    canvas.setLineWidth(0.4)
    canvas.line(20 * mm, A4[1] - 16 * mm, A4[0] - 20 * mm, A4[1] - 16 * mm)
    canvas.setFont("Helvetica", 7.2)
    canvas.setFillColor(MUTED)
    canvas.drawString(20 * mm, 11 * mm, "Designvorschau mit Beispieldaten")
    canvas.drawRightString(A4[0] - 20 * mm, 11 * mm, f"Seite {doc.page}")
    canvas.restoreState()


doc = BaseDocTemplate(
    str(BASE),
    pagesize=A4,
    leftMargin=20 * mm,
    rightMargin=20 * mm,
    topMargin=26 * mm,
    bottomMargin=18 * mm,
    title="MyCoinFlow Zahlungsserien",
)
frame = Frame(doc.leftMargin, doc.bottomMargin, doc.width, doc.height, id="normal")
doc.addPageTemplates(PageTemplate(id="report", frames=frame))


def kpis(items):
    cells = []
    for label, value, background in items:
        cells.append(Table(
            [[p(label, "KpiLabel")], [p(value, "KpiValue")]],
            colWidths=[doc.width / len(items) - 4 * mm],
            style=TableStyle([
                ("BACKGROUND", (0, 0), (-1, -1), background),
                ("LEFTPADDING", (0, 0), (-1, -1), 11),
                ("RIGHTPADDING", (0, 0), (-1, -1), 11),
                ("TOPPADDING", (0, 0), (-1, 0), 9),
                ("BOTTOMPADDING", (0, 0), (-1, 0), 1),
                ("TOPPADDING", (0, 1), (-1, 1), 2),
                ("BOTTOMPADDING", (0, 1), (-1, 1), 9),
            ]),
        ))
    return Table([cells], colWidths=[doc.width / len(items)] * len(items), style=TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 2),
        ("RIGHTPADDING", (0, 0), (-1, -1), 2),
    ]))


def direction_header(title, subtitle, count, background, accent):
    return Table(
        [[p(title, "Direction"), p(f"{count} Serien", "Section")], [p(subtitle, "Small"), ""]],
        colWidths=[135 * mm, 35 * mm],
        style=TableStyle([
            ("BACKGROUND", (0, 0), (-1, -1), background),
            ("TEXTCOLOR", (0, 0), (0, 0), accent),
            ("TEXTCOLOR", (1, 0), (1, 0), accent),
            ("ALIGN", (1, 0), (1, 0), "RIGHT"),
            ("SPAN", (1, 0), (1, 1)),
            ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
            ("LEFTPADDING", (0, 0), (-1, -1), 12),
            ("RIGHTPADDING", (0, 0), (-1, -1), 12),
            ("TOPPADDING", (0, 0), (-1, 0), 9),
            ("BOTTOMPADDING", (0, 0), (-1, 0), 1),
            ("TOPPADDING", (0, 1), (-1, 1), 1),
            ("BOTTOMPADDING", (0, 1), (-1, 1), 9),
        ]),
    )


def category_header(title, subtitle, count, background, accent):
    return Table(
        [[p(title, "Section"), p(f"{count} Serie(n)", "Small")], [p(subtitle, "Small"), ""]],
        colWidths=[135 * mm, 35 * mm],
        style=TableStyle([
            ("BACKGROUND", (0, 0), (-1, -1), background),
            ("TEXTCOLOR", (0, 0), (0, 0), accent),
            ("ALIGN", (1, 0), (1, 0), "RIGHT"),
            ("SPAN", (1, 0), (1, 1)),
            ("LEFTPADDING", (0, 0), (-1, -1), 10),
            ("RIGHTPADDING", (0, 0), (-1, -1), 10),
            ("TOPPADDING", (0, 0), (-1, 0), 7),
            ("BOTTOMPADDING", (0, 0), (-1, 0), 1),
            ("TOPPADDING", (0, 1), (-1, 1), 1),
            ("BOTTOMPADDING", (0, 1), (-1, 1), 7),
        ]),
    )


def overview(rows, accent):
    headers = ["Serie", "Status", "Rhythmus", "Monat", "Jahr", "Nächste", "Kündigen bis"]
    data = [[p(x, "TableBold") for x in headers]]
    data += [[p(str(value), "Table") for value in row] for row in rows]
    return Table(
        data,
        colWidths=[42 * mm, 17 * mm, 22 * mm, 18 * mm, 19 * mm, 24 * mm, 28 * mm],
        repeatRows=1,
        style=TableStyle([
            ("BACKGROUND", (0, 0), (-1, 0), accent),
            ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
            ("GRID", (0, 0), (-1, -1), 0.35, RULE),
            ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
            ("LEFTPADDING", (0, 0), (-1, -1), 5),
            ("RIGHTPADDING", (0, 0), (-1, -1), 5),
            ("TOPPADDING", (0, 0), (-1, -1), 6),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
            ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, GRAY]),
        ]),
    )


story = []
hero = Table(
    [[p("MYCOINFLOW", "Brand")], [p("Zahlungsserien", "CoverTitle")],
     [p("Einnahmen, Ausgaben und Vertragsübersicht", "CoverSub")],
     [p("Regelmässige Zahlungsfolgen, sauber nach Geldrichtung und Serienart geordnet.", "Small")]],
    colWidths=[doc.width],
    style=TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), PURPLE_SOFT),
        ("LEFTPADDING", (0, 0), (-1, -1), 22),
        ("RIGHTPADDING", (0, 0), (-1, -1), 22),
        ("TOPPADDING", (0, 0), (-1, 0), 20),
        ("BOTTOMPADDING", (0, 0), (-1, 0), 3),
        ("TOPPADDING", (0, 1), (-1, 1), 5),
        ("BOTTOMPADDING", (0, 1), (-1, 1), 2),
        ("TOPPADDING", (0, 2), (-1, 2), 2),
        ("BOTTOMPADDING", (0, 2), (-1, 2), 7),
        ("TOPPADDING", (0, 3), (-1, 3), 1),
        ("BOTTOMPADDING", (0, 3), (-1, 3), 20),
    ]),
)
story += [p("MYCOINFLOW / ZAHLUNGSSERIEN", "Brand"), Spacer(1, 12 * mm), hero, Spacer(1, 12 * mm), p("Stand 28.08.2026, 18:30", "CoverSub"), Spacer(1, 10 * mm)]
story.append(kpis([
    ("AKTIVE SERIEN", "12", PURPLE_SOFT),
    ("EINNAHMEN PRO JAHR", "CHF 43'800.00", GREEN_SOFT),
    ("AUSGABEN PRO JAHR", "CHF 18'426.40", RED_SOFT),
]))
story += [Spacer(1, 14 * mm), Table([
    [p("Dieser Bericht beantwortet", "Section")],
    [p("Welche regelmässigen Einnahmen und Ausgaben bestehen?", "Body")],
    [p("Welche davon sind Verträge, Lizenzen, Streaming oder sonstige Serien?", "Body")],
    [p("Wo bestehen Lücken oder anstehende Vertrags- und Kündigungstermine?", "Body")],
    [p("Dokumente und Korrespondenz werden weiterhin im DMS verwaltet.", "Small")],
], colWidths=[doc.width], style=TableStyle([
    ("BACKGROUND", (0, 0), (-1, -1), GRAY),
    ("LEFTPADDING", (0, 0), (-1, -1), 14),
    ("RIGHTPADDING", (0, 0), (-1, -1), 14),
    ("TOPPADDING", (0, 0), (-1, 0), 12),
    ("BOTTOMPADDING", (0, 0), (-1, 0), 7),
    ("TOPPADDING", (0, 1), (-1, -2), 3),
    ("BOTTOMPADDING", (0, 1), (-1, -2), 3),
    ("TOPPADDING", (0, -1), (-1, -1), 10),
    ("BOTTOMPADDING", (0, -1), (-1, -1), 12),
]))]

story.append(PageBreak())
story += [p("MYCOINFLOW / ZAHLUNGSSERIEN", "Brand"), Spacer(1, 2 * mm), p("Übersicht nach Richtung und Art", "PageTitle"), p("Einnahmen werden vollständig vor den Ausgaben ausgegeben", "Small"), Spacer(1, 7 * mm)]
story.append(direction_header("Einnahmen", "Regelmässige Zahlungseingänge", 4, GREEN_SOFT, GREEN))
income_contracts = [
    ["Miete MFH West-Strasse", "Aktiv", "Monatlich", "3'200.00", "38'400.00", "01.09.2026", "-"],
    ["Pacht Parkplatz 3", "Aktiv", "Monatlich", "350.00", "4'200.00", "05.09.2026", "31.12.2026"],
]
income_other = [
    ["Lizenzbeteiligung", "Aktiv", "Quartalsweise", "100.00", "1'200.00", "30.09.2026", "-"],
    ["Servicegutschrift", "Aktiv", "Jährlich", "0.00", "0.00", "15.01.2027", "-"],
]
story += [Spacer(1, 7 * mm), category_header("Verträge", "Miete, Pacht und weitere vertragliche Einnahmen", len(income_contracts), AMBER_SOFT, AMBER), Spacer(1, 2 * mm), overview(income_contracts, AMBER)]
story += [Spacer(1, 7 * mm), category_header("Sonstige Serien", "Weitere regelmässige Einnahmen", len(income_other), GRAY, MUTED), Spacer(1, 2 * mm), overview(income_other, MUTED)]

story.append(PageBreak())
story += [p("MYCOINFLOW / ZAHLUNGSSERIEN", "Brand"), Spacer(1, 2 * mm), p("Übersicht nach Richtung und Art", "PageTitle"), p("Fortsetzung: regelmässige Zahlungsausgänge", "Small"), Spacer(1, 7 * mm)]
story.append(direction_header("Ausgaben", "Regelmässige Zahlungsausgänge", 8, RED_SOFT, RED))
expense_contracts = [
    ["Mobiliar Hausrat", "Aktiv", "Jährlich", "73.42", "881.00", "01.03.2027", "31.01.2027"],
    ["Swisscom Internet", "Aktiv", "Monatlich", "89.90", "1'078.80", "12.09.2026", "Nicht geplant"],
]
licenses = [
    ["Microsoft 365", "Aktiv", "Jährlich", "9.92", "119.00", "10.01.2027", "10.12.2026"],
    ["Adobe Foto", "Aktiv", "Monatlich", "14.00", "168.00", "02.09.2026", "02.10.2026"],
]
streaming = [
    ["Netflix Premium", "Aktiv", "Monatlich", "24.90", "298.80", "05.09.2026", "15.11.2026"],
    ["Spotify Family", "Aktiv", "Monatlich", "21.90", "262.80", "12.09.2026", "Nicht geplant"],
]
other = [
    ["Gemeindesteuern Raten", "Aktiv", "Monatlich", "1'250.00", "15'000.00", "28.09.2026", "-"],
    ["Sparplan", "Aktiv", "Monatlich", "50.00", "600.00", "28.09.2026", "-"],
]
for title, subtitle, rows, surface, accent in [
    ("Verträge", "Versicherung, Telekom und weitere Verträge", expense_contracts, AMBER_SOFT, AMBER),
    ("Lizenzen & Software", "Apps, Cloud-Dienste und digitale Lizenzen", licenses, TEAL_SOFT, TEAL),
    ("Streaming", "Video, Musik, Games und digitale Inhalte", streaming, PURPLE_SOFT, PURPLE),
]:
    story += [Spacer(1, 5 * mm), category_header(title, subtitle, len(rows), surface, accent), Spacer(1, 2 * mm), overview(rows, accent)]

story.append(PageBreak())
story += [p("MYCOINFLOW / ZAHLUNGSSERIEN", "Brand"), Spacer(1, 2 * mm), p("Ausgaben - Fortsetzung", "PageTitle"), p("Weitere regelmässige Zahlungsausgänge", "Small"), Spacer(1, 7 * mm)]
story += [category_header("Sonstige Serien", "Weitere regelmässige Ausgaben", len(other), GRAY, MUTED), Spacer(1, 2 * mm), overview(other, MUTED)]

story.append(PageBreak())
story += [p("MYCOINFLOW / ZAHLUNGSSERIEN", "Brand"), Spacer(1, 2 * mm), p("Vertrags- und Terminplanung", "PageTitle"), p("Kündigungswege und Endtermine aller relevanten Serien", "Small"), Spacer(1, 8 * mm)]
story.append(kpis([
    ("AKTIVE SERIEN", "12", PURPLE_SOFT),
    ("PLANUNG VOLLSTÄNDIG", "5", GREEN_SOFT),
    ("ANGABEN FEHLEN", "7", AMBER_SOFT),
]))
story += [Spacer(1, 8 * mm), p("Nicht erfasst bedeutet, dass Vertragsende oder Kündigungsweg in der Zahlungsserie noch ergänzt werden sollte.", "Small"), Spacer(1, 4 * mm)]
headers = ["Serie", "Vertragsende", "Kündigen bis", "Frist", "Kündigungsweg", "Status"]
planning = [
    ["Pacht Parkplatz 3", "31.01.2027", "31.12.2026", "31 Tage", "Schriftlich", "Geplant"],
    ["Adobe Foto", "02.11.2026", "02.10.2026", "30 Tage", "Online-Kundenkonto", "Jetzt prüfen"],
    ["Netflix Premium", "15.12.2026", "15.11.2026", "30 Tage", "Online-Kundenkonto", "Geplant"],
    ["Microsoft 365", "10.01.2027", "10.12.2026", "31 Tage", "Microsoft-Konto", "Geplant"],
    ["Mobiliar Hausrat", "01.03.2027", "31.01.2027", "29 Tage", "E-Mail", "Geplant"],
    ["Swisscom Internet", "Nicht erfasst", "Nicht erfasst", "-", "Kundenkonto", "Angaben fehlen"],
    ["Spotify Family", "Nicht erfasst", "Nicht erfasst", "-", "Google Play Store", "Angaben fehlen"],
]
data = [[p(x, "TableBold") for x in headers]] + [[p(x, "Table") for x in row] for row in planning]
story.append(Table(data, colWidths=[38 * mm, 27 * mm, 27 * mm, 18 * mm, 40 * mm, 20 * mm], repeatRows=1, style=TableStyle([
    ("BACKGROUND", (0, 0), (-1, 0), AMBER),
    ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
    ("GRID", (0, 0), (-1, -1), 0.35, RULE),
    ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
    ("LEFTPADDING", (0, 0), (-1, -1), 5),
    ("RIGHTPADDING", (0, 0), (-1, -1), 5),
    ("TOPPADDING", (0, 0), (-1, -1), 7),
    ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
    ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, GRAY]),
])))
story += [Spacer(1, 10 * mm), Table([[p("Tipp: Verwaltungsseite und Kündigungsweg direkt bei der Zahlungsserie speichern. Vertragsunterlagen bleiben sauber im DMS.", "TableBold")]], colWidths=[doc.width], style=TableStyle([
    ("BACKGROUND", (0, 0), (-1, -1), TEAL_SOFT),
    ("TEXTCOLOR", (0, 0), (-1, -1), TEAL),
    ("LEFTPADDING", (0, 0), (-1, -1), 12),
    ("RIGHTPADDING", (0, 0), (-1, -1), 12),
    ("TOPPADDING", (0, 0), (-1, -1), 11),
    ("BOTTOMPADDING", (0, 0), (-1, -1), 11),
]))]

doc.build(story)

base_reader = PdfReader(str(BASE))
overlay_canvas = report_canvas.Canvas(str(OVERLAY), pagesize=A4)
for page_number in range(1, len(base_reader.pages) + 1):
    overlay_canvas.setFont("Helvetica", 7.2)
    overlay_canvas.setFillColor(MUTED)
    overlay_canvas.drawRightString(A4[0] - 20 * mm, 11 * mm, f"Seite {page_number}")
    overlay_canvas.showPage()
overlay_canvas.save()

overlay_reader = PdfReader(str(OVERLAY))
writer = PdfWriter()
for base_page, overlay_page in zip(base_reader.pages, overlay_reader.pages):
    base_page.merge_page(overlay_page)
    writer.add_page(base_page)
with OUT.open("wb") as stream:
    writer.write(stream)

BASE.unlink(missing_ok=True)
OVERLAY.unlink(missing_ok=True)
print(OUT)
