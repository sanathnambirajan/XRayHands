"""Build the specification sheet: prepare figures, print HTML to PDF, render PNG previews.

Usage (from the repo root):
    uv run --with pillow --with pymupdf python docs/spec_sheet/build.py

Inputs : docs/spec_sheet/spec.html, spec.css, img/raw/*.png (from Unity: FigureShotRunner)
Outputs: docs/spec_sheet/07_XRayHands.pdf, docs/spec_sheet/preview/page-N.png
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import fitz  # pymupdf
from PIL import Image

HERE = Path(__file__).resolve().parent
RAW = HERE / "img" / "raw"
FIG = HERE / "img" / "fig"
PDF = HERE / "07_XRayHands.pdf"
PREVIEW = HERE / "preview"
CHROME_CANDIDATES = [
    Path(r"C:/Program Files/Google/Chrome/Application/chrome.exe"),
    Path(r"C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"),
]
BACKGROUND_TOLERANCE = 18  # grey levels above the render background that count as content
PAD_FRACTION = 0.04


ICON_ASPECT = 2.2  # must match .g .icon aspect-ratio in spec.css


def autocrop(src: Path, dst: Path, aspect: float | None = None, pad: float = PAD_FRACTION) -> None:
    """Crop the dark render background around the content (plus padding), optionally to an aspect."""
    im = Image.open(src).convert("RGB")
    bg = im.getpixel((2, 2))
    mask = Image.eval(im.convert("L"), lambda v: 255 if v > max(bg) + BACKGROUND_TOLERANCE else 0)
    box = mask.getbbox() or (0, 0, im.width, im.height)
    x0, y0, x1, y1 = box
    pad = int(pad * max(x1 - x0, y1 - y0))
    x0, y0, x1, y1 = max(0, x0 - pad), max(0, y0 - pad), min(im.width, x1 + pad), min(im.height, y1 + pad)
    if aspect:
        w, h = x1 - x0, y1 - y0
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        if w / h < aspect:
            w = h * aspect
        else:
            h = w / aspect
        w, h = min(w, im.width), min(h, im.height)
        x0 = int(min(max(cx - w / 2, 0), im.width - w))
        y0 = int(min(max(cy - h / 2, 0), im.height - h))
        x1, y1 = int(x0 + w), int(y0 + h)
    im.crop((x0, y0, x1, y1)).save(dst, optimize=True)


# name -> aspect ratio (width / height) of the frame it is placed in (frame sizes in spec.html):
# page width 275 mm content, 7 mm column gap; page 1 columns 43:57, pages 2-3 1:1.
COL1_RIGHT = (275 - 7) * 0.57          # 152.8 mm
COL_HALF = (275 - 7) / 2               # 134 mm
FIGURES = {
    "fig_cut": COL1_RIGHT / 66,
    "fig_peel": (COL1_RIGHT - 2.4) / 2 / 39,
    "fig_lens": (COL1_RIGHT - 2.4) / 2 / 39,
    "fig_extract_flight": COL_HALF / 31,
    "fig_chart_linked": COL_HALF / 46,
    "fig_groundtruth": (COL_HALF - 2.4) / 2 / 31,
    "fig_enigma_peel": (COL_HALF - 2.4) / 2 / 31,
    "fig_probe": 3 / 2, "fig_enigma_solid": 1.0,
}


def prepare_figures() -> None:
    FIG.mkdir(parents=True, exist_ok=True)
    for name, aspect in FIGURES.items():
        src = RAW / f"{name}.png"
        if src.exists():
            autocrop(src, FIG / f"{name}.png", aspect)
    for src in RAW.glob("g_*.png"):
        autocrop(src, FIG / src.name, ICON_ASPECT, pad=0.16)


def print_pdf() -> None:
    chrome = next((c for c in CHROME_CANDIDATES if c.exists()), None)
    if chrome is None:
        sys.exit("Chrome or Edge not found")
    url = (HERE / "spec.html").as_uri()
    subprocess.run([
        str(chrome), "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
        "--virtual-time-budget=15000", "--run-all-compositor-stages-before-draw",
        f"--print-to-pdf={PDF}", url,
    ], check=True)


def check_layout() -> list[dict]:
    """Ask headless Chrome how far each column ends below the page footer (mm; >0 = overflow)."""
    import json
    import re

    chrome = next(c for c in CHROME_CANDIDATES if c.exists())
    url = (HERE / "spec.html").as_uri() + "?check=1"
    dom = subprocess.run([str(chrome), "--headless=new", "--disable-gpu", "--virtual-time-budget=15000",
                          "--window-size=1123,794", "--dump-dom", url],
                         check=True, capture_output=True, text=True, encoding="utf-8").stdout
    m = re.search(r'<pre id="layout-check">(.*?)</pre>', dom, re.S)
    if not m:
        sys.exit("layout check did not run")
    result = json.loads(m.group(1))
    for page in result:
        flag = "OVERFLOW" if any(v > 0 for v in page["cols"]) else "ok"
        print(f"page {page['page']}: column ends relative to footer (mm) {page['cols']} -> {flag}")
    return result


def render_previews(dpi: int = 110) -> int:
    PREVIEW.mkdir(exist_ok=True)
    doc = fitz.open(PDF)
    for i, page in enumerate(doc, start=1):
        page.get_pixmap(dpi=dpi).save(PREVIEW / f"page-{i}.png")
    fonts = sorted({f[3] for p in doc for f in p.get_fonts()})
    print(f"{PDF.name}: {doc.page_count} pages, {page.rect.width / 72 * 25.4:.0f} x {page.rect.height / 72 * 25.4:.0f} mm")
    print("embedded fonts:", ", ".join(fonts))
    return doc.page_count


if __name__ == "__main__":
    prepare_figures()
    layout = check_layout()
    print_pdf()
    n = render_previews()
    if n != 3:
        sys.exit(f"expected exactly 3 pages, got {n}")
    if any(v > 0 for p in layout for v in p["cols"]):
        sys.exit("content overflows into the footer: fix before submitting")
