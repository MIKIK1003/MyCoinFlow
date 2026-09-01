from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
pages = sorted((root / "rendered").glob("page-*.png"))
thumb_w = 420
gap = 18
label_h = 26
thumbs = []
for page in pages:
    with Image.open(page) as image:
        image = image.convert("RGB")
        thumb_h = round(image.height * thumb_w / image.width)
        image.thumbnail((thumb_w, thumb_h), Image.Resampling.LANCZOS)
        thumbs.append((page.name, image.copy()))

cols = 3
rows = (len(thumbs) + cols - 1) // cols
cell_h = max(img.height for _, img in thumbs) + label_h
sheet = Image.new("RGB", (cols * thumb_w + (cols + 1) * gap, rows * cell_h + (rows + 1) * gap), "#dce7eb")
draw = ImageDraw.Draw(sheet)
for idx, (name, image) in enumerate(thumbs):
    col = idx % cols
    row = idx // cols
    x = gap + col * (thumb_w + gap)
    y = gap + row * (cell_h + gap)
    draw.text((x, y), name, fill="#0a2a43")
    sheet.paste(image, (x, y + label_h))
sheet.save(root / "contact-sheet.png", optimize=True)
