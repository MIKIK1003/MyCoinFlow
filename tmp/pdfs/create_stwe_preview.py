from reportlab.lib import colors
from reportlab.lib.enums import TA_RIGHT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import (
    BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer, Table, TableStyle,
    PageBreak
)

OUT = r"C:\DEV\MyCoinFlow\output\pdf\STWE_Bericht_Designvorschau.pdf"
W, H = A4
PURPLE = colors.HexColor("#5B2DA9")
PURPLE_DARK = colors.HexColor("#3F1E77")
PURPLE_PALE = colors.HexColor("#DCCCF1")
BLUE = colors.HexColor("#C6E1EF")
GREEN = colors.HexColor("#CAE6C8")
ROSE = colors.HexColor("#F4C7CF")
GRAY = colors.HexColor("#F1F1F5")
INK = colors.HexColor("#23222A")
MUTED = colors.HexColor("#4E4A58")
RULE = colors.HexColor("#C2BECA")

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name="HeroBrand", fontName="Helvetica-Bold", fontSize=9, textColor=PURPLE, leading=11))
styles.add(ParagraphStyle(name="Hero", fontName="Helvetica-Bold", fontSize=28, textColor=PURPLE_DARK, leading=32))
styles.add(ParagraphStyle(name="Property", fontName="Helvetica-Bold", fontSize=17, textColor=INK, leading=21))
styles.add(ParagraphStyle(name="Section", fontName="Helvetica-Bold", fontSize=14, textColor=PURPLE_DARK, leading=18, spaceAfter=2))
styles.add(ParagraphStyle(name="Sub", fontName="Helvetica", fontSize=8.8, textColor=MUTED, leading=12, spaceAfter=7))
styles.add(ParagraphStyle(name="Body", fontName="Helvetica", fontSize=9.2, textColor=INK, leading=13))
styles.add(ParagraphStyle(name="Small", fontName="Helvetica", fontSize=8, textColor=MUTED, leading=10))
styles.add(ParagraphStyle(name="Right", fontName="Helvetica", fontSize=8.5, textColor=INK, alignment=TA_RIGHT))


class Report(BaseDocTemplate):
    def __init__(self, path):
        super().__init__(path, pagesize=A4, leftMargin=20*mm, rightMargin=20*mm,
                         topMargin=21*mm, bottomMargin=18*mm)
        self.addPageTemplates(PageTemplate(id="main", frames=[Frame(
            self.leftMargin, self.bottomMargin, self.width, self.height, id="frame")],
            onPage=self.chrome))

    def chrome(self, canvas, doc):
        canvas.saveState()
        canvas.setStrokeColor(RULE)
        canvas.setLineWidth(.6)
        canvas.line(20*mm, H-15*mm, W-20*mm, H-15*mm)
        canvas.line(20*mm, 13*mm, W-20*mm, 13*mm)
        canvas.setFillColor(PURPLE_DARK)
        canvas.setFont("Helvetica", 8.3)
        canvas.drawString(20*mm, H-11*mm, "MyCoinFlow  |  STWE Bericht")
        canvas.setFillColor(MUTED)
        canvas.drawString(20*mm, 8.5*mm, "Zeitraum 01.01.2026 - 31.12.2026")
        canvas.drawRightString(W-20*mm, 8.5*mm, f"Seite {doc.page}")
        canvas.restoreState()


def p(text, style="Body"):
    return Paragraph(text, styles[style])


def kpis(items):
    cells = []
    for label, value, bg in items:
        cells.append(Table([[p(label, "Small")], [Paragraph(f"<b>{value}</b>", ParagraphStyle(
            "kv", parent=styles["Body"], fontSize=15.5, leading=18, textColor=INK))]],
            colWidths=[51*mm], style=TableStyle([
                ("BACKGROUND", (0,0), (-1,-1), bg), ("BOX", (0,0), (-1,-1), .4, bg),
                ("LEFTPADDING", (0,0), (-1,-1), 11), ("RIGHTPADDING", (0,0), (-1,-1), 11),
                ("TOPPADDING", (0,0), (-1,-1), 7), ("BOTTOMPADDING", (0,0), (-1,-1), 7),
            ])))
    return Table([cells], colWidths=[55*mm]*3, hAlign="LEFT", style=TableStyle([
        ("VALIGN", (0,0), (-1,-1), "TOP"), ("LEFTPADDING", (0,0), (-1,-1), 0),
        ("RIGHTPADDING", (0,0), (-1,-1), 4),
    ]))


def section(title, subtitle):
    return [Spacer(1, 8), p(title, "Section"), p(subtitle, "Sub")]


def data_table(headers, rows, widths, total=None):
    body = [[Paragraph(f"<b>{h}</b>", styles["Small"]) for h in headers]]
    for row in rows:
        body.append([p(str(v), "Small") for v in row])
    if total:
        body.append([Paragraph(f"<b>{v}</b>", styles["Small"]) for v in total])
    style = [
        ("BACKGROUND", (0,0), (-1,0), PURPLE_PALE), ("TEXTCOLOR", (0,0), (-1,0), PURPLE_DARK),
        ("GRID", (0,0), (-1,-1), .35, RULE), ("VALIGN", (0,0), (-1,-1), "TOP"),
        ("LEFTPADDING", (0,0), (-1,-1), 6), ("RIGHTPADDING", (0,0), (-1,-1), 6),
        ("TOPPADDING", (0,0), (-1,-1), 5), ("BOTTOMPADDING", (0,0), (-1,-1), 5),
    ]
    for idx in range(1, len(body) - (1 if total else 0)):
        if idx % 2 == 0:
            style.append(("BACKGROUND", (0,idx), (-1,idx), GRAY))
    if total:
        style.extend([("BACKGROUND", (0,-1), (-1,-1), BLUE), ("SPAN", (0,-1), (1,-1))])
    return Table(body, colWidths=widths, repeatRows=1, style=TableStyle(style))


doc = Report(OUT)
story = []

hero = Table([[p("MYCOINFLOW", "HeroBrand")], [p("STWE Bericht", "Hero")],
              [p("MFH West-Strasse", "Property")], [p("West-Strasse 13  |  3273 Kappelen", "Sub")]],
             colWidths=[170*mm], style=TableStyle([
                 ("BACKGROUND", (0,0), (-1,-1), PURPLE_PALE),
                 ("BOX", (0,0), (-1,-1), .5, PURPLE_PALE),
                 ("LEFTPADDING", (0,0), (-1,-1), 22), ("RIGHTPADDING", (0,0), (-1,-1), 22),
                 ("TOPPADDING", (0,0), (-1,-1), 6), ("BOTTOMPADDING", (0,0), (-1,-1), 6),
             ]))
story += [Spacer(1, 18), hero, Spacer(1, 22), p("Zeitraum 01.01.2026 - 31.12.2026", "Property"),
          p("Erstellt am 28.08.2026 um 14:30", "Sub"), Spacer(1, 15),
          kpis([("EIGENTÜMER", "2", BLUE), ("DETAILPOSITIONEN", "6", GREEN),
                ("GESAMTSALDO", "CHF 1'091.85", ROSE)]), Spacer(1, 28)]
contents = Table([[p("<b>Inhalt</b>", "Section")], [p("-  Eigentümerübersicht und Gesamtsalden")],
                  [p("-  Detailauflistung der aufgeteilten Positionen")],
                  [p("-  Original-Transaktionen mit Totalbetrag")],
                  [p("-  Energie-Grundlagen und Diagramme, sofern Daten vorhanden")]],
                 colWidths=[170*mm], style=TableStyle([
                     ("BACKGROUND", (0,0), (-1,-1), GRAY), ("BOX", (0,0), (-1,-1), .4, RULE),
                     ("LEFTPADDING", (0,0), (-1,-1), 14), ("RIGHTPADDING", (0,0), (-1,-1), 14),
                     ("TOPPADDING", (0,0), (-1,-1), 6), ("BOTTOMPADDING", (0,0), (-1,-1), 6),
                 ]))
story += [contents, PageBreak()]

story += [Table([[p("STWE-Auswertung", "Hero")], [p("MFH West-Strasse", "Property")]],
                colWidths=[170*mm], style=TableStyle([
                    ("BACKGROUND", (0,0), (-1,-1), PURPLE_PALE),
                    ("LEFTPADDING", (0,0), (-1,-1), 16), ("RIGHTPADDING", (0,0), (-1,-1), 16),
                    ("TOPPADDING", (0,0), (-1,-1), 8), ("BOTTOMPADDING", (0,0), (-1,-1), 8),
                ]))]
story += section("Übersicht pro Eigentümer", "Salden und Anzahl der im Zeitraum zugeordneten Detailpositionen")
story += [kpis([("EIGENTÜMER", "2", BLUE), ("POSITIONEN", "6", GREEN),
                ("SALDO", "CHF 1'091.85", ROSE)]), Spacer(1, 10)]
story += [data_table(["Eigentümer", "Positionen", "Saldo"],
                     [["Anne-Marie Schreiber", "3", "CHF 393.06"],
                      ["Thomas und Theres Hänggi", "3", "CHF 698.79"]],
                     [90*mm, 35*mm, 45*mm], ["Gesamtsaldo", "", "CHF 1'091.85"])]
story += section("Original-Transaktionen", "Ausgangsbeträge der in STWE-Sets aufgeteilten Transaktionen")
story += [data_table(["Datum", "Typ", "ID", "Total CHF", "Notiz"],
                     [["31.01.2026", "Ausgabe", "11370", "CHF 314.40", "Stromrechnung Januar"],
                      ["30.06.2026", "Ausgabe", "12603", "CHF 427.80", "Allgemeinstrom und Heizung"],
                      ["31.12.2026", "Ausgabe", "14288", "CHF 349.65", "Jahresabrechnung Energie"]],
                     [24*mm, 22*mm, 20*mm, 31*mm, 73*mm],
                     ["Summe", "", "", "CHF 1'091.85", ""]), PageBreak()]

owners = [
    ("Anne-Marie Schreiber", "CHF 393.06", [
        ["31.01.2026", "Strom Januar", "MEA", "CHF 79.16", "Auto (MEA): Nach Nett..."],
        ["30.06.2026", "Heizung / Allgemein", "ENERGIE", "CHF 290.88", "Verteilung gemäss Set"],
        ["31.12.2026", "Nebenkosten", "FIX", "CHF 23.02", "Fixer Eigentümeranteil"],
    ]),
    ("Thomas und Theres Hänggi", "CHF 698.79", [
        ["31.01.2026", "Strom Januar", "MEA", "CHF 140.74", "Auto (MEA): Nach Nett..."],
        ["30.06.2026", "Heizung / Allgemein", "ENERGIE", "CHF 477.92", "Verteilung gemäss Set"],
        ["31.12.2026", "Nebenkosten", "FIX", "CHF 80.13", "Fixer Eigentümeranteil"],
    ]),
]
for idx, (name, total, rows) in enumerate(owners):
    head = Table([[p(name, "Section"), Paragraph(f"<b>3 Positionen  |  {total}</b>", styles["Right"])]],
                 colWidths=[115*mm, 55*mm], style=TableStyle([
                     ("BACKGROUND", (0,0), (-1,-1), PURPLE_PALE),
                     ("LEFTPADDING", (0,0), (-1,-1), 12), ("RIGHTPADDING", (0,0), (-1,-1), 12),
                     ("TOPPADDING", (0,0), (-1,-1), 9), ("BOTTOMPADDING", (0,0), (-1,-1), 9),
                     ("VALIGN", (0,0), (-1,-1), "MIDDLE"),
                 ]))
    story += [head, Spacer(1, 7), data_table(
        ["Datum", "Titel", "Schlüssel", "Betrag CHF", "Notiz"], rows,
        [24*mm, 43*mm, 26*mm, 31*mm, 46*mm],
        ["Summe", "", "", total, "Fehlbetrag"])]
    if idx < len(owners)-1:
        story.append(PageBreak())

doc.build(story)
print(OUT)
