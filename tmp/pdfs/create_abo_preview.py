from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT, TA_RIGHT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import mm
from reportlab.platypus import (
    BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer, Table, TableStyle,
    PageBreak, KeepTogether
)
from pathlib import Path

ROOT = Path(r"C:\DEV\MyCoinFlow")
OUT = ROOT / "output" / "pdf" / "Abo_Uebersicht_Designvorschau.pdf"
OUT.parent.mkdir(parents=True, exist_ok=True)

INK = colors.HexColor("#22232A")
MUTED = colors.HexColor("#565664")
PURPLE = colors.HexColor("#5B2DA9")
PURPLE_DARK = colors.HexColor("#3D1F70")
PURPLE_SOFT = colors.HexColor("#E6DCF6")
TEAL = colors.HexColor("#167E91")
TEAL_SOFT = colors.HexColor("#DCF1F1")
GREEN_SOFT = colors.HexColor("#DEF1E1")
AMBER = colors.HexColor("#B06C1F")
AMBER_SOFT = colors.HexColor("#FAEBD6")
GRAY = colors.HexColor("#F1F1F5")
RULE = colors.HexColor("#C4C2CC")

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name="Brand", parent=styles["Normal"], fontName="Helvetica-Bold", fontSize=8.5, leading=10, textColor=PURPLE))
styles.add(ParagraphStyle(name="CoverTitle", parent=styles["Normal"], fontName="Helvetica-Bold", fontSize=27, leading=31, textColor=PURPLE_DARK))
styles.add(ParagraphStyle(name="CoverSub", parent=styles["Normal"], fontName="Helvetica-Bold", fontSize=14, leading=18, textColor=INK))
styles.add(ParagraphStyle(name="PageTitle", parent=styles["Normal"], fontName="Helvetica-Bold", fontSize=22, leading=26, textColor=PURPLE_DARK))
styles.add(ParagraphStyle(name="Section", parent=styles["Normal"], fontName="Helvetica-Bold", fontSize=13, leading=16, textColor=INK))
styles.add(ParagraphStyle(name="Body", parent=styles["Normal"], fontName="Helvetica", fontSize=9.2, leading=13, textColor=INK))
styles.add(ParagraphStyle(name="Small", parent=styles["Normal"], fontName="Helvetica", fontSize=8.3, leading=11, textColor=MUTED))
styles.add(ParagraphStyle(name="KpiLabel", parent=styles["Normal"], fontName="Helvetica-Bold", fontSize=7.5, leading=9, textColor=MUTED))
styles.add(ParagraphStyle(name="KpiValue", parent=styles["Normal"], fontName="Helvetica-Bold", fontSize=14, leading=17, textColor=INK))
styles.add(ParagraphStyle(name="Table", parent=styles["Normal"], fontName="Helvetica", fontSize=7.4, leading=9.2, textColor=INK))
styles.add(ParagraphStyle(name="TableBold", parent=styles["Table"], fontName="Helvetica-Bold"))
styles.add(ParagraphStyle(name="Right", parent=styles["Body"], alignment=TA_RIGHT))


def para(text, style="Body"):
    return Paragraph(text, styles[style])


def header_footer(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica-Bold", 7.5)
    canvas.setFillColor(PURPLE)
    canvas.drawString(20 * mm, A4[1] - 13 * mm, "MYCOINFLOW  /  STREAMING & LIZENZEN")
    canvas.setStrokeColor(RULE)
    canvas.setLineWidth(0.4)
    canvas.line(20 * mm, A4[1] - 16 * mm, A4[0] - 20 * mm, A4[1] - 16 * mm)
    canvas.setFont("Helvetica", 7.2)
    canvas.setFillColor(MUTED)
    canvas.drawString(20 * mm, 11 * mm, "Designvorschau mit Beispieldaten")
    canvas.drawRightString(A4[0] - 20 * mm, 11 * mm, f"Seite {doc.page}")
    canvas.restoreState()


doc = BaseDocTemplate(
    str(OUT), pagesize=A4,
    leftMargin=20 * mm, rightMargin=20 * mm,
    topMargin=22 * mm, bottomMargin=18 * mm,
    title="MyCoinFlow Streaming & Lizenzen"
)
frame = Frame(doc.leftMargin, doc.bottomMargin, doc.width, doc.height, id="normal")
doc.addPageTemplates(PageTemplate(id="report", frames=frame, onPage=header_footer))


def kpis(items):
    data = []
    cells = []
    for label, value, background in items:
        cell = Table(
            [[para(label, "KpiLabel")], [para(value, "KpiValue")]],
            colWidths=[doc.width / len(items) - 4 * mm],
            style=TableStyle([
                ("BACKGROUND", (0, 0), (-1, -1), background),
                ("LEFTPADDING", (0, 0), (-1, -1), 11),
                ("RIGHTPADDING", (0, 0), (-1, -1), 11),
                ("TOPPADDING", (0, 0), (-1, 0), 9),
                ("BOTTOMPADDING", (0, 0), (-1, 0), 1),
                ("TOPPADDING", (0, 1), (-1, 1), 2),
                ("BOTTOMPADDING", (0, 1), (-1, 1), 9),
            ])
        )
        cells.append(cell)
    data.append(cells)
    return Table(data, colWidths=[doc.width / len(items)] * len(items), style=TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 2),
        ("RIGHTPADDING", (0, 0), (-1, -1), 2),
    ]))


def section_header(title, subtitle, count, background, accent):
    return Table(
        [[para(title, "Section"), para(f"{count} Verträge", "Right")],
         [para(subtitle, "Small"), ""]],
        colWidths=[130 * mm, 40 * mm],
        style=TableStyle([
            ("BACKGROUND", (0, 0), (-1, -1), background),
            ("TEXTCOLOR", (0, 0), (0, 0), accent),
            ("SPAN", (1, 0), (1, 1)),
            ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
            ("LEFTPADDING", (0, 0), (-1, -1), 11),
            ("RIGHTPADDING", (0, 0), (-1, -1), 11),
            ("TOPPADDING", (0, 0), (-1, 0), 8),
            ("BOTTOMPADDING", (0, 0), (-1, 0), 1),
            ("TOPPADDING", (0, 1), (-1, 1), 1),
            ("BOTTOMPADDING", (0, 1), (-1, 1), 8),
        ])
    )


def overview_table(rows, accent):
    headers = ["Abo", "Status", "Rhythmus", "Monat", "Jahr", "Nächste", "Kündigen bis"]
    data = [[para(x, "TableBold") for x in headers]]
    for row in rows:
        data.append([para(str(x), "Table") for x in row])
    return Table(data, colWidths=[42*mm, 17*mm, 22*mm, 18*mm, 19*mm, 24*mm, 28*mm], repeatRows=1,
                 style=TableStyle([
                     ("BACKGROUND", (0,0), (-1,0), accent),
                     ("TEXTCOLOR", (0,0), (-1,0), colors.white),
                     ("GRID", (0,0), (-1,-1), 0.35, RULE),
                     ("VALIGN", (0,0), (-1,-1), "MIDDLE"),
                     ("LEFTPADDING", (0,0), (-1,-1), 5),
                     ("RIGHTPADDING", (0,0), (-1,-1), 5),
                     ("TOPPADDING", (0,0), (-1,-1), 6),
                     ("BOTTOMPADDING", (0,0), (-1,-1), 6),
                     ("ROWBACKGROUNDS", (0,1), (-1,-1), [colors.white, GRAY]),
                 ]))


story = []

hero = Table(
    [[para("MYCOINFLOW", "Brand")],
     [para("Streaming & Lizenzen", "CoverTitle")],
     [para("Kosten- und Kündigungsübersicht", "CoverSub")],
     [para("Digitale Abonnemente - ohne Versicherungen, Telefon, Miete oder andere wiederkehrende Alltagskosten.", "Small")]],
    colWidths=[doc.width],
    style=TableStyle([
        ("BACKGROUND", (0,0), (-1,-1), PURPLE_SOFT),
        ("LEFTPADDING", (0,0), (-1,-1), 22),
        ("RIGHTPADDING", (0,0), (-1,-1), 22),
        ("TOPPADDING", (0,0), (-1,0), 20),
        ("BOTTOMPADDING", (0,0), (-1,0), 3),
        ("TOPPADDING", (0,1), (-1,1), 5),
        ("BOTTOMPADDING", (0,1), (-1,1), 2),
        ("TOPPADDING", (0,2), (-1,2), 2),
        ("BOTTOMPADDING", (0,2), (-1,2), 7),
        ("TOPPADDING", (0,3), (-1,3), 1),
        ("BOTTOMPADDING", (0,3), (-1,3), 20),
    ])
)
story += [Spacer(1, 12*mm), hero, Spacer(1, 12*mm), para("Stand 28.08.2026, 17:00", "CoverSub"), Spacer(1, 10*mm)]
story.append(kpis([
    ("AKTIVE ABOS", "7", PURPLE_SOFT),
    ("KOSTEN PRO JAHR", "CHF 1'234.40", TEAL_SOFT),
    ("KÜNDIGUNGEN / 90 TAGE", "2", AMBER_SOFT),
]))
story += [Spacer(1, 14*mm), Table([
    [para("Dieser Bericht beantwortet", "Section")],
    [para("Welche Streaming- und Lizenzverträge bestehen?", "Body")],
    [para("Was kosten sie monatlich und jährlich?", "Body")],
    [para("Bis wann und über welchen Weg kann gekündigt werden?", "Body")],
    [para("Vertragsdokumente und Korrespondenz werden weiterhin im DMS verwaltet.", "Small")],
], colWidths=[doc.width], style=TableStyle([
    ("BACKGROUND", (0,0), (-1,-1), GRAY),
    ("LEFTPADDING", (0,0), (-1,-1), 14), ("RIGHTPADDING", (0,0), (-1,-1), 14),
    ("TOPPADDING", (0,0), (-1,0), 12), ("BOTTOMPADDING", (0,0), (-1,0), 7),
    ("TOPPADDING", (0,1), (-1,-2), 3), ("BOTTOMPADDING", (0,1), (-1,-2), 3),
    ("TOPPADDING", (0,-1), (-1,-1), 10), ("BOTTOMPADDING", (0,-1), (-1,-1), 12),
]))]

story.append(PageBreak())
story += [para("MYCOINFLOW", "Brand"), Spacer(1, 2*mm), para("Kostenübersicht", "PageTitle"), para("Aktive und bereits gekündigte digitale Abonnemente", "Small"), Spacer(1, 8*mm)]
story.append(kpis([
    ("MONATLICHER GEGENWERT", "CHF 102.87", GREEN_SOFT),
    ("STREAMING / JAHR", "CHF 678.40", PURPLE_SOFT),
    ("SOFTWARE / JAHR", "CHF 556.00", TEAL_SOFT),
]))
streaming = [
    ["Netflix Premium", "Aktiv", "Monatlich", "24.90", "298.80", "05.09.2026", "15.11.2026"],
    ["Spotify Family", "Aktiv", "Monatlich", "21.90", "262.80", "12.09.2026", "Nicht geplant"],
    ["DAZN", "Gekündigt", "Jährlich", "9.73", "116.80", "-", "30.09.2026"],
]
software = [
    ["Microsoft 365", "Aktiv", "Jährlich", "9.92", "119.00", "10.01.2027", "10.12.2026"],
    ["Adobe Foto-Abo", "Aktiv", "Monatlich", "14.00", "168.00", "02.09.2026", "02.10.2026"],
    ["1Password", "Aktiv", "Jährlich", "4.08", "49.00", "18.04.2027", "18.03.2027"],
    ["ChatGPT Plus", "Aktiv", "Monatlich", "18.33", "220.00", "24.09.2026", "Nicht geplant"],
]
story += [Spacer(1, 9*mm), section_header("Streaming", "Video, Musik, Games und digitale Inhalte", len(streaming), PURPLE_SOFT, PURPLE), Spacer(1, 3*mm), overview_table(streaming, PURPLE)]
story += [Spacer(1, 8*mm), section_header("Software & Lizenzen", "Apps, Cloud-Dienste, Software und digitale Lizenzen", len(software), TEAL_SOFT, TEAL), Spacer(1, 3*mm), overview_table(software, TEAL)]

story.append(PageBreak())
story += [para("MYCOINFLOW", "Brand"), Spacer(1, 2*mm), para("Kündigungsplan", "PageTitle"), para("Termine und konkrete Kündigungswege auf einen Blick", "Small"), Spacer(1, 8*mm)]
story.append(kpis([
    ("AKTIVE VERTRÄGE", "6", PURPLE_SOFT),
    ("PLANUNG VOLLSTÄNDIG", "4", GREEN_SOFT),
    ("ANGABEN FEHLEN", "2", AMBER_SOFT),
]))
story += [Spacer(1, 8*mm), para("Nicht erfasst bedeutet, dass Kündigungsdatum oder Kündigungsweg im Abo noch ergänzt werden sollte.", "Small"), Spacer(1, 4*mm)]
cancel_rows = [
    ["Netflix Premium", "15.12.2026", "15.11.2026", "30 Tage", "Online-Kundenkonto", "Geplant"],
    ["Adobe Foto-Abo", "02.11.2026", "02.10.2026", "30 Tage", "Online-Kundenkonto", "Jetzt prüfen"],
    ["Microsoft 365", "10.01.2027", "10.12.2026", "31 Tage", "Microsoft-Konto", "Geplant"],
    ["1Password", "18.04.2027", "18.03.2027", "31 Tage", "Online-Kundenkonto", "Geplant"],
    ["Spotify Family", "Nicht erfasst", "Nicht erfasst", "-", "Google Play Store", "Angaben fehlen"],
    ["ChatGPT Plus", "Nicht erfasst", "Nicht erfasst", "-", "Nicht erfasst", "Angaben fehlen"],
]
headers = ["Abo", "Vertragsende", "Kündigen bis", "Frist", "Kündigungsweg", "Status"]
data = [[para(h, "TableBold") for h in headers]] + [[para(v, "Table") for v in row] for row in cancel_rows]
story.append(Table(data, colWidths=[38*mm, 27*mm, 27*mm, 18*mm, 40*mm, 20*mm], repeatRows=1, style=TableStyle([
    ("BACKGROUND", (0,0), (-1,0), AMBER), ("TEXTCOLOR", (0,0), (-1,0), colors.white),
    ("GRID", (0,0), (-1,-1), 0.35, RULE), ("VALIGN", (0,0), (-1,-1), "MIDDLE"),
    ("LEFTPADDING", (0,0), (-1,-1), 5), ("RIGHTPADDING", (0,0), (-1,-1), 5),
    ("TOPPADDING", (0,0), (-1,-1), 7), ("BOTTOMPADDING", (0,0), (-1,-1), 7),
    ("ROWBACKGROUNDS", (0,1), (-1,-1), [colors.white, GRAY]),
])))
story += [Spacer(1, 10*mm), Table([[para("Tipp: Verwaltungs- oder Kündigungsseite und Kündigungsweg direkt beim Abo speichern. Die Vertragsunterlagen bleiben sauber im DMS.", "TableBold")]], colWidths=[doc.width], style=TableStyle([
    ("BACKGROUND", (0,0), (-1,-1), TEAL_SOFT), ("TEXTCOLOR", (0,0), (-1,-1), TEAL),
    ("LEFTPADDING", (0,0), (-1,-1), 12), ("RIGHTPADDING", (0,0), (-1,-1), 12),
    ("TOPPADDING", (0,0), (-1,-1), 11), ("BOTTOMPADDING", (0,0), (-1,-1), 11),
]))]

doc.build(story)
print(OUT)
