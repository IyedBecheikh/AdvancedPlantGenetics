# Builds About/Preview.png (1280x720): title, building sprites, and two harness UI screenshots.
# usage: python -I tools/make_preview.py SHOTS_DIR
import os
import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
shots = sys.argv[1]
W, H = 1280, 720
img = Image.new("RGBA", (W, H), (24, 30, 28, 255))
d = ImageDraw.Draw(img)
# Soft green vignette.
glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([-200, 380, 900, 1100], fill=(60, 140, 80, 90))
img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(120)))


def font(size, bold=True):
    for name in (["arialbd.ttf", "segoeuib.ttf"] if bold else ["arial.ttf", "segoeui.ttf"]):
        p = os.path.join("C:/Windows/Fonts", name)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


d.text((40, 30), "Advanced Plant Genetics", font=font(58), fill=(235, 240, 230, 255))
d.text((44, 100), "From tribal seed saving to ultratech genome synthesis", font=font(26, False), fill=(170, 210, 170, 255))

# UI screenshots on the right.
def shot(name, box, size, pos):
    p = os.path.join(shots, name)
    if not os.path.exists(p):
        return
    s = Image.open(p).convert("RGBA").crop(box)
    s.thumbnail(size, Image.LANCZOS)
    frame = Image.new("RGBA", (s.width + 6, s.height + 6), (90, 110, 95, 255))
    frame.alpha_composite(s, (3, 3))
    img.alpha_composite(frame, pos)


shot("06_tab_strains.png", (0, 345, 1150, 900), (640, 330), (610, 150))
shot("05_choose_candidate.png", (300, 180, 1620, 600), (640, 210), (610, 495))

# Building sprites on the left, scaled by footprint.
bdir = os.path.join(root, "Textures/Things/Building/APG")
layout = [("CrossingBench", (40, 175), 230), ("PropagationBench", (290, 175), 230),
          ("GeneAnalyzer", (40, 300), 110), ("GeneBank", (160, 300), 110), ("BioinformaticsCore", (280, 300), 110),
          ("TissueCultureLab", (40, 425), 140), ("MutagenChamber", (195, 425), 140), ("GeneSplicer", (350, 425), 140),
          ("GenesisSynthesizer", (40, 575), 135), ("SeedSynthesizer", (190, 585), 120)]
for name, pos, width in layout:
    s = Image.open(os.path.join(bdir, name + ".png")).convert("RGBA")
    s.thumbnail((width, width), Image.LANCZOS)
    img.alpha_composite(s, pos)

idir = os.path.join(root, "Textures/Things/Item/APG")
row = [("SeedPacket", 330), ("CultureMedium", 410), ("GeneReagent", 490)]
for name, px in row:
    s = Image.open(os.path.join(idir, name, name + "_c.png")).convert("RGBA").resize((72, 72), Image.LANCZOS)
    img.alpha_composite(s, (px, 620))

img.convert("RGB").save(os.path.join(root, "About", "Preview.png"))
print("wrote About/Preview.png")
