from __future__ import annotations

import html
import json
import os
import re
import sys
import textwrap
import time
import urllib.parse
import urllib.request
import urllib.error
from dataclasses import dataclass
from datetime import date
from pathlib import Path

from PIL import Image as PILImage, ImageOps
from PIL import ImageChops
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph


ROOT = Path(__file__).resolve().parent
ASSET_DIR = ROOT / "assets"
OUTPUT_DIR = ROOT.parents[2] / "output" / "pdf"
OUTPUT_PDF = OUTPUT_DIR / "Bielersee_Fischfuehrer_2026-08-29.pdf"
META_JSON = ROOT / "image_credits.json"

PAGE_W, PAGE_H = A4
MARGIN = 13 * mm

NAVY = colors.HexColor("#0A2A43")
BLUE = colors.HexColor("#0E7490")
CYAN = colors.HexColor("#DDF5F5")
INK = colors.HexColor("#132735")
MUTED = colors.HexColor("#5D7280")
PALE = colors.HexColor("#F4F8FA")
GREEN = colors.HexColor("#16845B")
GREEN_PALE = colors.HexColor("#DDF5E9")
ORANGE = colors.HexColor("#D97706")
ORANGE_PALE = colors.HexColor("#FFF0D8")
RED = colors.HexColor("#B42318")
RED_PALE = colors.HexColor("#FEE4E2")
WHITE = colors.white


@dataclass(frozen=True)
class Fish:
    name: str
    scientific: str
    wiki_title: str
    regulation: str
    closed: str
    limit: str
    identify: str
    group: str
    note: str = ""


FISHES = [
    Fish("Felchen", "Coregonus spp.", "Coregonus lavaretus", "23 cm", "01.11.-31.12.", "20 pro Tag", "Silbrig, schlank, kleine zahnlose Schnauze; Fettflosse zwischen Rücken- und Schwanzflosse.", "Hauptfisch"),
    Fish("Flussbarsch (Egli)", "Perca fluviatilis", "Perca fluviatilis", "15 cm", "keine", "100 pro Tag", "Dunkle Querbänder, zwei Rückenflossen; Bauch- und Afterflossen meist orange bis rot.", "Hauptfisch"),
    Fish("Seeforelle", "Salmo trutta", "Salmo trutta", "45 cm", "01.09.-31.01.", "max. 3 Forellen", "Silbriger Forellenkörper mit schwarzen Punkten, oft auch auf dem Kiemendeckel; Fettflosse vorhanden.", "Hauptfisch", "Morgen noch offen - Schonzeit beginnt am 1. September."),
    Fish("Seesaibling", "Salvelinus alpinus", "Salvelinus alpinus", "22 cm", "01.11.-31.12.", "mit Forellen/Saiblingen max. 6", "Helle Punkte auf dunklem Grund; weisse Vorderkante an Bauch- und Afterflossen.", "Hauptfisch"),
    Fish("Hecht", "Esox lucius", "Esox lucius", "45 cm", "01.03.-30.04.", "5 pro Tag", "Sehr langer Körper, flacher entenschnabelartiger Kopf; Rückenflosse weit hinten.", "Hauptfisch"),
    Fish("Zander", "Sander lucioperca", "Sander lucioperca", "kein Mindestmass", "01.04.-31.05.", "5 pro Tag", "Langgestreckt, zwei Rückenflossen, dunkle Sattelbänder; grosse glasige Augen und Fangzähne.", "Hauptfisch"),
    Fish("Wels", "Silurus glanis", "Silurus glanis", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Breiter flacher Kopf, sehr langer schuppenloser Körper; lange Bartfäden am Oberkiefer.", "Weitere Fangart"),
    Fish("Trüsche", "Lota lota", "Lota lota", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Marmorierter, langgestreckter Körper; nur ein Bartfaden am Kinn, zwei Rückenflossen.", "Weitere Fangart"),
    Fish("Barbe", "Barbus barbus", "Barbus barbus", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Spindelförmig, unterständiges Maul mit vier Barteln; kräftige, hohe Rückenflosse.", "Weitere Fangart"),
    Fish("Alet (Döbel)", "Squalius cephalus", "Squalius cephalus", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Kräftiger, fast zylindrischer Weissfisch; grosser Kopf, endständiges breites Maul, dunkle Schuppenränder.", "Weitere Fangart"),
    Fish("Karpfen", "Cyprinus carpio", "Cyprinus carpio", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Hochrückig, lange Rückenflosse; zwei Paar Barteln am vorstülpbaren Maul.", "Weitere Fangart"),
    Fish("Schleie", "Tinca tinca", "Tinca tinca", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Olivgrün bis bronzefarben, winzige Schuppen und dicke Flossen; kleine Bartel an jedem Mundwinkel.", "Weitere Fangart"),
    Fish("Brachse (Brachsmen)", "Abramis brama", "Abramis brama", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Sehr hochrückig und seitlich flach; lange Afterflosse, kleines vorstülpbares Maul.", "Weissfisch"),
    Fish("Blicke (Güster)", "Blicca bjoerkna", "Blicca bjoerkna", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Brachsenähnlich, aber grösseres Auge; Brust- und Bauchflossen oft rötlich, Körper stärker silbern.", "Weissfisch"),
    Fish("Rotauge", "Rutilus rutilus", "Rutilus rutilus", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Rote Iris, silbriger Körper; Bauchflossen unter der Vorderkante der Rückenflosse.", "Weissfisch"),
    Fish("Rotfeder", "Scardinius erythrophthalmus", "Scardinius erythrophthalmus", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Goldener Körper, kräftig rote Flossen; Rückenflosse beginnt deutlich hinter den Bauchflossen.", "Weissfisch"),
    Fish("Hasel", "Leuciscus leuciscus", "Leuciscus leuciscus", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Schlank, silbrig, kleine leicht unterständige Maulspalte; Afterflosse meist leicht eingebuchtet.", "Weissfisch"),
    Fish("Laube (Ukelei)", "Alburnus alburnus", "Alburnus alburnus", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Sehr schlank und stark silbrig; oberständiges Maul, lange Afterflosse, scharfe Bauchkante.", "Kleinfisch"),
    Fish("Gründling", "Gobio gobio", "Gobio gobio", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Kleiner Bodenfisch mit dunklen Seitenflecken; unterständiges Maul und ein Bartelpaar.", "Kleinfisch"),
    Fish("Groppe", "Cottus gobio", "Cottus gobio", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Grosser breiter Kopf, keulenförmiger schuppenloser Körper; grosse Brustflossen, lebt am Grund.", "Kleinfisch", "Seltene Verwechslung - bei Unsicherheit sofort schonend zurücksetzen."),
    Fish("Dreistachliger Stichling", "Gasterosteus aculeatus", "Gasterosteus aculeatus", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Winziger Fisch mit drei freien Stacheln vor der Rückenflosse; oft silbrig, Männchen zur Laichzeit rot.", "Kleinfisch"),
    Fish("Bartgrundel (Schmerle)", "Barbatula barbatula", "Barbatula barbatula", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Schlanker Bodenfisch mit marmorierter Zeichnung und sechs kurzen Barteln um das Maul.", "Kleinfisch"),
    Fish("Schneider", "Alburnoides bipunctatus", "Alburnoides bipunctatus", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Kleiner silbriger Fisch mit dunkler Seitenlinie, die wie eine doppelte Punktreihe wirkt.", "Kleinfisch", "Gefährdete Art - nicht gezielt befischen; bei Unsicherheit zurücksetzen."),
    Fish("Sonnenbarsch", "Lepomis gibbosus", "Lepomis gibbosus", "kein spezielles Mindestmass", "keine artspezifische", "keine spezielle Tageslimite", "Bunt, hochrückig, blau-grüne Wellenlinien am Kopf; schwarzer Kiemendeckelfleck mit rotem Rand.", "Kleinfisch"),
]

PREFERRED_COMMONS_FILES = {
    "Coregonus spp.": "File:Coregonus lavaretus.jpg",
    "Salvelinus alpinus": "File:Salvelinus alpinus.jpg",
}


PROTECTED = [
    ("Äsche", "Im Bielersee 2026 ganzjährig verboten"),
    ("Aal", "ganzjährig verboten"),
    ("Nase", "ganzjährig verboten"),
    ("Bitterling", "ganzjährig verboten"),
    ("Bachneunauge", "ganzjährig verboten"),
    ("Strömer", "ganzjährig verboten"),
    ("Moorgrundel / Schlammpeitzger", "ganzjährig verboten"),
]


OFFICIAL_REGS = "https://www.weu.be.ch/content/dam/weu/dokumente/lanat/de/fischerei/wie-wo-fischen/Reglement-Fischerei-DE-2026.pdf"
OFFICIAL_SUMMARY = "https://www.weu.be.ch/de/start/themen/jagd-fischerei/fischerei/fischen-kanton-bern/fischen-ohne-angelpatent.html"
OFFICIAL_LIMITS = "https://www.weu.be.ch/de/start/themen/jagd-fischerei/fischerei/fischen-kanton-bern/schonmassse-schonzeiten.html"
OFFICIAL_STATS = "https://www.weu.be.ch/content/dam/weu/dokumente/lanat/de/fischerei/FI-Jahresbericht-Anhang-2025.pdf"
FISH_SURVEY = "https://www.fischereiberatung.ch/fileadmin/sites/fiber/angebot/andere_publ/projet_lac/Projet_Lac_2018_Befischung_Bielersee.pdf"


def strip_html(value: str) -> str:
    value = html.unescape(value or "")
    value = re.sub(r"<br\s*/?>", ", ", value, flags=re.I)
    value = re.sub(r"<[^>]+>", "", value)
    return re.sub(r"\s+", " ", value).strip()


def api_json(base: str, params: dict[str, str]) -> dict:
    url = base + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"User-Agent": "Codex-Bielersee-Fish-Guide/1.0 (educational PDF; contact via OpenAI Codex)"})
    for attempt in range(6):
        try:
            with urllib.request.urlopen(req, timeout=45) as response:
                data = json.load(response)
            time.sleep(0.9)
            return data
        except urllib.error.HTTPError as exc:
            if exc.code != 429 or attempt == 5:
                raise
            time.sleep(7 + attempt * 4)
    raise RuntimeError("unreachable")


def commons_info(file_title: str) -> dict:
    if not file_title.lower().startswith("file:"):
        file_title = "File:" + file_title
    data = api_json(
        "https://commons.wikimedia.org/w/api.php",
        {
            "action": "query",
            "format": "json",
            "prop": "imageinfo",
            "titles": file_title,
            "iiprop": "url|extmetadata",
            "iiurlwidth": "1600",
        },
    )
    pages = list(data.get("query", {}).get("pages", {}).values())
    if not pages or "imageinfo" not in pages[0]:
        raise RuntimeError(f"No Commons image info for {file_title}")
    ii = pages[0]["imageinfo"][0]
    meta = ii.get("extmetadata", {})
    return {
        "file_title": file_title,
        "download_url": ii.get("thumburl") or ii.get("url"),
        "source_url": ii.get("descriptionurl") or ii.get("url"),
        "artist": strip_html(meta.get("Artist", {}).get("value", "Unbekannt")),
        "license": strip_html(meta.get("LicenseShortName", {}).get("value", "siehe Quelle")),
    }


def resolve_image(wiki_title: str) -> dict:
    data = api_json(
        "https://en.wikipedia.org/w/api.php",
        {
            "action": "query",
            "format": "json",
            "prop": "pageimages",
            "titles": wiki_title,
            "piprop": "name",
            "redirects": "1",
        },
    )
    pages = list(data.get("query", {}).get("pages", {}).values())
    if pages and pages[0].get("pageimage"):
        return commons_info(pages[0]["pageimage"])

    data = api_json(
        "https://commons.wikimedia.org/w/api.php",
        {
            "action": "query",
            "format": "json",
            "generator": "search",
            "gsrsearch": wiki_title,
            "gsrnamespace": "6",
            "gsrlimit": "10",
            "prop": "imageinfo",
            "iiprop": "url|extmetadata",
            "iiurlwidth": "1600",
        },
    )
    pages = list(data.get("query", {}).get("pages", {}).values())
    for page in pages:
        if page.get("imageinfo"):
            ii = page["imageinfo"][0]
            meta = ii.get("extmetadata", {})
            return {
                "file_title": page.get("title", wiki_title),
                "download_url": ii.get("thumburl") or ii.get("url"),
                "source_url": ii.get("descriptionurl") or ii.get("url"),
                "artist": strip_html(meta.get("Artist", {}).get("value", "Unbekannt")),
                "license": strip_html(meta.get("LicenseShortName", {}).get("value", "siehe Quelle")),
            }
    raise RuntimeError(f"No image found for {wiki_title}")


def normalize_asset(source: Path, destination: Path) -> None:
    with PILImage.open(source) as image:
        image = ImageOps.exif_transpose(image).convert("RGB")
        # Remove large, nearly white margins often present around specimen photos.
        white = PILImage.new("RGB", image.size, "white")
        diff = ImageChops.difference(image, white).convert("L")
        diff = diff.point(lambda px: 0 if px < 18 else 255)
        bbox = diff.getbbox()
        if bbox:
            image = image.crop(bbox)
        background = PILImage.new("RGB", (1400, 850), "white")
        image.thumbnail((1320, 770), PILImage.Resampling.LANCZOS)
        x = (background.width - image.width) // 2
        y = (background.height - image.height) // 2
        background.paste(image, (x, y))
        background.save(destination, quality=91, optimize=True)


def download_images() -> dict[str, dict]:
    ASSET_DIR.mkdir(parents=True, exist_ok=True)
    existing = json.loads(META_JSON.read_text(encoding="utf-8")) if META_JSON.exists() else {}
    result: dict[str, dict] = {}
    for index, fish in enumerate(FISHES, start=1):
        key = fish.scientific
        out = ASSET_DIR / f"{index:02d}_{re.sub(r'[^a-z0-9]+', '_', fish.name.lower())}.jpg"
        info = existing.get(key)
        preferred = PREFERRED_COMMONS_FILES.get(key)
        if preferred and (not info or info.get("file_title") != preferred):
            info = commons_info(preferred)
            out.unlink(missing_ok=True)
        elif not info:
            info = resolve_image(fish.wiki_title)
        if not out.exists():
            req = urllib.request.Request(info["download_url"], headers={"User-Agent": "Codex-Bielersee-Fish-Guide/1.0 (educational PDF; contact via OpenAI Codex)"})
            for attempt in range(6):
                try:
                    with urllib.request.urlopen(req, timeout=60) as response:
                        raw = response.read()
                    time.sleep(0.9)
                    break
                except urllib.error.HTTPError as exc:
                    if exc.code != 429 or attempt == 5:
                        raise
                    time.sleep(7 + attempt * 4)
            tmp = out.with_suffix(".download")
            tmp.write_bytes(raw)
            normalize_asset(tmp, out)
            tmp.unlink(missing_ok=True)
        else:
            refreshed = out.with_suffix(".refresh.jpg")
            normalize_asset(out, refreshed)
            refreshed.replace(out)
        info["local_path"] = str(out)
        result[key] = info
        META_JSON.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"[{index:02d}/{len(FISHES)}] {fish.name}: {info['file_title']}")
    META_JSON.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return result


def register_fonts() -> None:
    candidates = [
        ("Segoe", "C:/Windows/Fonts/segoeui.ttf"),
        ("Segoe-Bold", "C:/Windows/Fonts/segoeuib.ttf"),
        ("Segoe-Italic", "C:/Windows/Fonts/segoeuii.ttf"),
    ]
    for name, path in candidates:
        if Path(path).exists():
            pdfmetrics.registerFont(TTFont(name, path))
    if "Segoe" not in pdfmetrics.getRegisteredFontNames():
        pdfmetrics.registerFont(TTFont("Segoe", "C:/Windows/Fonts/arial.ttf"))
        pdfmetrics.registerFont(TTFont("Segoe-Bold", "C:/Windows/Fonts/arialbd.ttf"))
        pdfmetrics.registerFont(TTFont("Segoe-Italic", "C:/Windows/Fonts/ariali.ttf"))


def rounded_box(c: canvas.Canvas, x: float, y: float, w: float, h: float, fill, radius=5 * mm, stroke=None) -> None:
    c.setFillColor(fill)
    c.setStrokeColor(stroke or fill)
    c.roundRect(x, y, w, h, radius, fill=1, stroke=1 if stroke else 0)


def ptext(c: canvas.Canvas, text: str, x: float, y: float, w: float, h: float, style: ParagraphStyle) -> None:
    p = Paragraph(text, style)
    _, ph = p.wrap(w, h)
    p.drawOn(c, x, y + h - ph)


def fit_text(text: str, font_name: str, font_size: float, max_width: float) -> str:
    if pdfmetrics.stringWidth(text, font_name, font_size) <= max_width:
        return text
    suffix = "..."
    while text and pdfmetrics.stringWidth(text + suffix, font_name, font_size) > max_width:
        text = text[:-1]
    return text.rstrip() + suffix


def footer(c: canvas.Canvas, page_num: int) -> None:
    c.setStrokeColor(colors.HexColor("#D5E1E7"))
    c.line(MARGIN, 10 * mm, PAGE_W - MARGIN, 10 * mm)
    c.setFont("Segoe", 7.6)
    c.setFillColor(MUTED)
    c.drawString(MARGIN, 6.2 * mm, "Bielersee Fischführer · Stand 28.08.2026 · Angaben ohne Gewähr")
    c.drawRightString(PAGE_W - MARGIN, 6.2 * mm, str(page_num))


def draw_cover(c: canvas.Canvas) -> None:
    c.setFillColor(NAVY)
    c.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
    c.setFillColor(BLUE)
    c.circle(PAGE_W - 16 * mm, PAGE_H - 25 * mm, 46 * mm, fill=1, stroke=0)
    c.setFillColor(colors.HexColor("#0B6077"))
    c.circle(18 * mm, 30 * mm, 58 * mm, fill=1, stroke=0)

    c.setFillColor(CYAN)
    c.setFont("Segoe-Bold", 10)
    c.drawString(MARGIN, PAGE_H - 30 * mm, "FELDFÜHRER · BIELERSEE")
    c.setFillColor(WHITE)
    c.setFont("Segoe-Bold", 33)
    c.drawString(MARGIN, PAGE_H - 56 * mm, "Fische erkennen.")
    c.drawString(MARGIN, PAGE_H - 72 * mm, "Richtig entscheiden.")
    c.setFont("Segoe", 15)
    c.setFillColor(colors.HexColor("#CFE8EF"))
    c.drawString(MARGIN, PAGE_H - 88 * mm, "Mindestmasse, Schonzeiten und Bilder")

    rounded_box(c, MARGIN, PAGE_H - 137 * mm, PAGE_W - 2 * MARGIN, 31 * mm, colors.HexColor("#123F58"), radius=4 * mm)
    c.setFillColor(CYAN)
    c.setFont("Segoe-Bold", 11)
    c.drawString(MARGIN + 7 * mm, PAGE_H - 118 * mm, "GÜLTIG FÜR MORGEN")
    c.setFillColor(WHITE)
    c.setFont("Segoe-Bold", 20)
    c.drawString(MARGIN + 7 * mm, PAGE_H - 131 * mm, "Samstag, 29. August 2026")

    y = PAGE_H - 160 * mm
    c.setFont("Segoe-Bold", 10)
    c.setFillColor(CYAN)
    c.drawString(MARGIN, y, "SCHNELL-CHECK")
    items = [
        ("Felchen", "ab 23 cm"),
        ("Egli", "ab 15 cm"),
        ("Seeforelle", "ab 45 cm"),
        ("Seesaibling", "ab 22 cm"),
        ("Hecht", "ab 45 cm"),
        ("Zander", "kein Mindestmass"),
    ]
    y -= 10 * mm
    col_w = (PAGE_W - 2 * MARGIN - 6 * mm) / 2
    for i, (name, value) in enumerate(items):
        col = i % 2
        row = i // 2
        x = MARGIN + col * (col_w + 6 * mm)
        yy = y - row * 20 * mm
        rounded_box(c, x, yy - 11 * mm, col_w, 15 * mm, colors.HexColor("#123F58"), radius=3 * mm)
        c.setFillColor(WHITE)
        c.setFont("Segoe-Bold", 10)
        c.drawString(x + 4 * mm, yy - 2.5 * mm, name)
        c.setFillColor(CYAN)
        c.drawRightString(x + col_w - 4 * mm, yy - 2.5 * mm, value)

    c.setFont("Segoe", 8.2)
    c.setFillColor(colors.HexColor("#CFE8EF"))
    c.drawString(MARGIN, 14 * mm, "Offizielle Berner Vorschriften, Stand 1. Januar 2026 · kompakt für unterwegs")


def draw_tomorrow_page(c: canvas.Canvas, styles: dict[str, ParagraphStyle], page_num: int) -> None:
    c.setFillColor(WHITE)
    c.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
    c.setFillColor(NAVY)
    c.setFont("Segoe-Bold", 25)
    c.drawString(MARGIN, PAGE_H - 24 * mm, "Das gilt morgen")
    c.setFillColor(MUTED)
    c.setFont("Segoe", 10.5)
    c.drawString(MARGIN, PAGE_H - 33 * mm, "Samstag, 29. August 2026 · Bielersee (bernische Vorschriften)")

    y = PAGE_H - 50 * mm
    header_h = 11 * mm
    row_h = 12.5 * mm
    x = MARGIN
    w = PAGE_W - 2 * MARGIN
    cols = [62 * mm, 34 * mm, 41 * mm, w - 137 * mm]
    rounded_box(c, x, y - header_h, w, header_h, NAVY, radius=2.5 * mm)
    headers = ["Fischart", "Mindestmass", "Schonzeit", "Tageshöchstzahl"]
    xx = x
    c.setFillColor(WHITE)
    c.setFont("Segoe-Bold", 8.2)
    for label, cw in zip(headers, cols):
        c.drawString(xx + 3 * mm, y - 7.1 * mm, label)
        xx += cw
    y -= header_h
    rows = [
        ("Felchen", "23 cm", "01.11.-31.12.", "20"),
        ("Flussbarsch (Egli)", "15 cm", "keine", "100"),
        ("Seeforelle", "45 cm", "01.09.-31.01.", "3*"),
        ("Hecht", "45 cm", "01.03.-30.04.", "5"),
        ("Zander", "kein", "01.04.-31.05.", "5"),
        ("Seesaibling", "22 cm", "01.11.-31.12.", "6*"),
    ]
    for idx, row in enumerate(rows):
        fill = PALE if idx % 2 == 0 else WHITE
        c.setFillColor(fill)
        c.rect(x, y - row_h, w, row_h, fill=1, stroke=0)
        c.setStrokeColor(colors.HexColor("#D9E5EA"))
        c.line(x, y - row_h, x + w, y - row_h)
        xx = x
        for col_idx, (value, cw) in enumerate(zip(row, cols)):
            c.setFillColor(INK if col_idx != 1 else GREEN)
            c.setFont("Segoe-Bold" if col_idx in (0, 1) else "Segoe", 9)
            c.drawString(xx + 3 * mm, y - 8.1 * mm, value)
            xx += cw
        y -= row_h

    c.setFont("Segoe", 8.4)
    c.setFillColor(MUTED)
    c.drawString(MARGIN, y - 5 * mm, "* Insgesamt höchstens 6 Forellen und Saiblinge pro Tag, davon höchstens 3 Forellen aus dem Bielersee.")

    box_y = 47 * mm
    box_h = 88 * mm
    rounded_box(c, MARGIN, box_y, PAGE_W - 2 * MARGIN, box_h, colors.HexColor("#EAF4F7"), radius=4 * mm)
    c.setFillColor(NAVY)
    c.setFont("Segoe-Bold", 14)
    c.drawString(MARGIN + 6 * mm, box_y + box_h - 11 * mm, "Vor dem ersten Wurf")
    rules = [
        "Ohne Patent: nur vom Ufer, 1 Angelrute, 1 einfacher Haken ohne Widerhaken; lebende und tote Köderfische verboten.",
        "Sommerzeit: Angelfischen ist von 24.00 bis 05.00 Uhr untersagt.",
        "Twannbach-Mündung und markierter Umkreis sowie weitere ausgeschilderte Schongebiete nicht befischen.",
        "Untermassige, geschonte oder geschützte Fische sofort und sorgfältig zurücksetzen.",
        "Massige Fische ausserhalb der Schonzeit dürfen nicht zurückgesetzt werden; tierschutzgerecht betäuben und töten.",
    ]
    yy = box_y + box_h - 29 * mm
    for rule in rules:
        c.setFillColor(BLUE)
        c.circle(MARGIN + 8 * mm, yy + 1.1 * mm, 1.4 * mm, fill=1, stroke=0)
        c.setFillColor(INK)
        c.setFont("Segoe", 8.3)
        c.drawString(MARGIN + 13 * mm, yy - 1.1 * mm, rule)
        yy -= 12.5 * mm

    rounded_box(c, MARGIN, 20 * mm, PAGE_W - 2 * MARGIN, 18 * mm, ORANGE_PALE, radius=3 * mm)
    c.setFillColor(ORANGE)
    c.setFont("Segoe-Bold", 9.5)
    c.drawString(MARGIN + 5 * mm, 31 * mm, "WICHTIG")
    ptext(c, "Bei Beschilderung am Wasser gilt immer die lokale Regel. Diese Übersicht ersetzt Patent, App und amtliche Vorschriften nicht.", MARGIN + 5 * mm, 21 * mm, PAGE_W - 2 * MARGIN - 10 * mm, 9 * mm, styles["small"])
    footer(c, page_num)


def draw_card(c: canvas.Canvas, fish: Fish, image_path: str, y: float, styles: dict[str, ParagraphStyle]) -> None:
    x = MARGIN
    w = PAGE_W - 2 * MARGIN
    h = 79 * mm
    rounded_box(c, x, y, w, h, WHITE, radius=4 * mm, stroke=colors.HexColor("#D7E4E9"))

    image_x = x + 4 * mm
    image_y = y + 4 * mm
    image_w = 75 * mm
    image_h = h - 8 * mm
    rounded_box(c, image_x, image_y, image_w, image_h, PALE, radius=3 * mm)
    c.drawImage(image_path, image_x + 2 * mm, image_y + 2 * mm, image_w - 4 * mm, image_h - 4 * mm, preserveAspectRatio=True, anchor="c", mask="auto")

    tx = image_x + image_w + 6 * mm
    tw = w - image_w - 14 * mm
    tag_fill = GREEN_PALE if fish.group == "Hauptfisch" else CYAN
    tag_color = GREEN if fish.group == "Hauptfisch" else BLUE
    tag_w = min(42 * mm, 5 * mm + pdfmetrics.stringWidth(fish.group.upper(), "Segoe-Bold", 7.5))
    rounded_box(c, tx, y + h - 11 * mm, tag_w, 6.5 * mm, tag_fill, radius=2 * mm)
    c.setFillColor(tag_color)
    c.setFont("Segoe-Bold", 7.5)
    c.drawString(tx + 2.5 * mm, y + h - 8.7 * mm, fish.group.upper())

    c.setFillColor(NAVY)
    c.setFont("Segoe-Bold", 16)
    c.drawString(tx, y + h - 20 * mm, fish.name)
    c.setFillColor(MUTED)
    c.setFont("Segoe-Italic", 8.5)
    c.drawString(tx, y + h - 26 * mm, fish.scientific)

    status_y = y + h - 38 * mm
    rounded_box(c, tx, status_y, tw, 10 * mm, GREEN_PALE, radius=2.5 * mm)
    c.setFillColor(GREEN)
    c.setFont("Segoe-Bold", 9)
    c.drawString(tx + 3 * mm, status_y + 3.4 * mm, "MORGEN FANGBAR")
    c.drawRightString(tx + tw - 3 * mm, status_y + 3.4 * mm, fish.regulation)

    ptext(c, f"<b>Erkennen:</b> {fish.identify}", tx, y + h - 57 * mm, tw, 16 * mm, styles["body"])
    c.setFillColor(MUTED)
    c.setFont("Segoe-Bold", 7.5)
    c.drawString(tx, y + 15 * mm, "SCHONZEIT")
    c.setFont("Segoe", 8.5)
    c.setFillColor(INK)
    c.drawString(tx, y + 10 * mm, fish.closed)
    c.setFillColor(MUTED)
    c.setFont("Segoe-Bold", 7.5)
    c.drawString(tx + tw / 2, y + 15 * mm, "LIMIT")
    c.setFont("Segoe", 8.5)
    c.setFillColor(INK)
    c.drawString(tx + tw / 2, y + 10 * mm, fish.limit)

    if fish.note:
        c.setFont("Segoe", 6.7)
        c.setFillColor(ORANGE if "Unsicherheit" not in fish.note else RED)
        c.drawString(tx, y + 4.1 * mm, fish.note[:100])


def draw_fish_pages(c: canvas.Canvas, credits: dict[str, dict], styles: dict[str, ParagraphStyle], start_page: int) -> int:
    page_num = start_page
    for page_index in range(0, len(FISHES), 3):
        c.setFillColor(PALE)
        c.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
        c.setFillColor(NAVY)
        c.setFont("Segoe-Bold", 20)
        c.drawString(MARGIN, PAGE_H - 18 * mm, "Fische im Bielersee erkennen")
        c.setFillColor(MUTED)
        c.setFont("Segoe", 8.5)
        c.drawRightString(PAGE_W - MARGIN, PAGE_H - 18 * mm, "Mindestmass = Totallänge")

        ys = [PAGE_H - 104 * mm, PAGE_H - 186 * mm, PAGE_H - 268 * mm]
        for fish, y in zip(FISHES[page_index : page_index + 3], ys):
            draw_card(c, fish, credits[fish.scientific]["local_path"], y, styles)
        footer(c, page_num)
        c.showPage()
        page_num += 1
    return page_num


def draw_protected_page(c: canvas.Canvas, styles: dict[str, ParagraphStyle], page_num: int) -> None:
    c.setFillColor(WHITE)
    c.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
    c.setFillColor(RED)
    c.setFont("Segoe-Bold", 24)
    c.drawString(MARGIN, PAGE_H - 23 * mm, "Nicht entnehmen")
    c.setFillColor(MUTED)
    c.setFont("Segoe", 10)
    c.drawString(MARGIN, PAGE_H - 33 * mm, "Ganzjährig geschützt bzw. im Bielersee verboten")

    y = PAGE_H - 52 * mm
    for name, status in PROTECTED:
        rounded_box(c, MARGIN, y - 13 * mm, PAGE_W - 2 * MARGIN, 13 * mm, RED_PALE, radius=3 * mm)
        c.setFillColor(RED)
        c.setFont("Segoe-Bold", 11)
        c.drawString(MARGIN + 5 * mm, y - 8.4 * mm, name)
        c.setFont("Segoe", 9)
        c.drawRightString(PAGE_W - MARGIN - 5 * mm, y - 8.4 * mm, status)
        y -= 17 * mm

    rounded_box(c, MARGIN, 71 * mm, PAGE_W - 2 * MARGIN, 38 * mm, colors.HexColor("#EAF4F7"), radius=4 * mm)
    c.setFillColor(NAVY)
    c.setFont("Segoe-Bold", 13)
    c.drawString(MARGIN + 6 * mm, 98 * mm, "Wenn du unsicher bist")
    ptext(c, "Fisch im Wasser lassen oder nur mit nassen Händen und so kurz wie möglich anfassen. Geschützte, geschonte und untermassige Fische sofort und sorgfältig zurücksetzen. Im Zweifel nicht entnehmen und die offizielle App «Fischen Bern» bzw. die Beschilderung prüfen.", MARGIN + 6 * mm, 76 * mm, PAGE_W - 2 * MARGIN - 12 * mm, 17 * mm, styles["body"])

    rounded_box(c, MARGIN, 22 * mm, PAGE_W - 2 * MARGIN, 37 * mm, ORANGE_PALE, radius=4 * mm)
    c.setFillColor(ORANGE)
    c.setFont("Segoe-Bold", 12)
    c.drawString(MARGIN + 6 * mm, 48 * mm, "Messen")
    ptext(c, "Totallänge: von der Schnauzenspitze bis zum Ende der natürlich ausgebreiteten Schwanzflosse. Fische mit Mindestmass dürfen vor der Kontrolle nicht verstümmelt werden.", MARGIN + 6 * mm, 28 * mm, PAGE_W - 2 * MARGIN - 12 * mm, 16 * mm, styles["body"])
    footer(c, page_num)


def draw_sources_page(c: canvas.Canvas, credits: dict[str, dict], styles: dict[str, ParagraphStyle], page_num: int) -> None:
    c.setFillColor(WHITE)
    c.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
    c.setFillColor(NAVY)
    c.setFont("Segoe-Bold", 22)
    c.drawString(MARGIN, PAGE_H - 22 * mm, "Quellen & Bildnachweise")

    c.setFillColor(BLUE)
    c.setFont("Segoe-Bold", 11)
    c.drawString(MARGIN, PAGE_H - 35 * mm, "RECHT UND BESTAND")
    sources = [
        ("Kanton Bern - Fischereireglement 2026", OFFICIAL_REGS),
        ("Kanton Bern - Fischen ohne Angelpatent", OFFICIAL_SUMMARY),
        ("Kanton Bern - Schonmasse und Schonzeiten", OFFICIAL_LIMITS),
        ("Fischereiinspektorat - Fangstatistik Bielersee 2025", OFFICIAL_STATS),
        ("Eawag/FIBER - Standardisierte Befischung Bielersee", FISH_SURVEY),
    ]
    y = PAGE_H - 45 * mm
    for label, url in sources:
        c.setFillColor(INK)
        c.setFont("Segoe-Bold", 8.5)
        c.drawString(MARGIN, y, label)
        c.setFillColor(BLUE)
        c.setFont("Segoe", 6.4)
        c.drawString(MARGIN, y - 4 * mm, url)
        c.linkURL(url, (MARGIN, y - 5 * mm, PAGE_W - MARGIN, y + 2 * mm), relative=0)
        y -= 13 * mm

    c.setFillColor(BLUE)
    c.setFont("Segoe-Bold", 11)
    c.drawString(MARGIN, y - 2 * mm, "BILDER")
    y -= 9 * mm

    rows = []
    for fish in FISHES:
        info = credits[fish.scientific]
        author = info.get("artist", "Unbekannt")
        if len(author) > 55:
            author = author[:52] + "..."
        rows.append((fish.name, author, info.get("license", "siehe Quelle"), info.get("source_url", "")))

    col_gap = 8 * mm
    col_w = (PAGE_W - 2 * MARGIN - col_gap) / 2
    line_h = 11.7 * mm
    for idx, (name, author, license_name, url) in enumerate(rows):
        col = idx // 12
        row = idx % 12
        x = MARGIN + col * (col_w + col_gap)
        yy = y - row * line_h
        c.setFillColor(INK)
        c.setFont("Segoe-Bold", 7.2)
        c.drawString(x, yy, name)
        c.setFont("Segoe", 5.9)
        c.setFillColor(MUTED)
        credit_text = f"{author} · {license_name} · Wikimedia Commons"
        credit_text = fit_text(credit_text, "Segoe", 5.9, col_w)
        c.drawString(x, yy - 3.1 * mm, credit_text)
        if url:
            c.linkURL(url, (x, yy - 4 * mm, x + col_w, yy + 2 * mm), relative=0)

    ptext(c, "Bildauswahl und Zuschnitt dienen der Bestimmungshilfe. Farbwirkung und Körperform können je nach Alter, Geschlecht, Jahreszeit und Lebensraum variieren.", MARGIN, 16 * mm, PAGE_W - 2 * MARGIN, 11 * mm, styles["small"])
    footer(c, page_num)


def make_styles() -> dict[str, ParagraphStyle]:
    return {
        "body": ParagraphStyle("body", fontName="Segoe", fontSize=8.1, leading=10.2, textColor=INK, alignment=TA_LEFT),
        "rule": ParagraphStyle("rule", fontName="Segoe", fontSize=8.3, leading=10.2, textColor=INK, alignment=TA_LEFT),
        "small": ParagraphStyle("small", fontName="Segoe", fontSize=7.4, leading=9.1, textColor=MUTED, alignment=TA_LEFT),
    }


def build_pdf(credits: dict[str, dict]) -> Path:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    register_fonts()
    styles = make_styles()
    c = canvas.Canvas(str(OUTPUT_PDF), pagesize=A4, pageCompression=1)
    c.setTitle("Bielersee Fischführer - 29. August 2026")
    c.setAuthor("Erstellt aus amtlichen Quellen des Kantons Bern")
    c.setSubject("Fischbestimmung, Mindestmasse, Schonzeiten und Tagesfangzahlen für den Bielersee")

    draw_cover(c)
    c.showPage()
    draw_tomorrow_page(c, styles, 2)
    c.showPage()
    next_page = draw_fish_pages(c, credits, styles, 3)
    draw_protected_page(c, styles, next_page)
    c.showPage()
    draw_sources_page(c, credits, styles, next_page + 1)
    c.save()
    return OUTPUT_PDF


def main() -> None:
    credits = download_images()
    path = build_pdf(credits)
    print(path)


if __name__ == "__main__":
    main()
