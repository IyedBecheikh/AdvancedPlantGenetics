# Writes simple placeholder textures for any texture path that doesn't exist yet. Never overwrites real art.
import os
from PIL import Image, ImageDraw

root = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Textures")
buildings = {"CrossingBench": (2, 1), "PropagationBench": (2, 1), "GeneAnalyzer": (1, 1), "GeneBank": (1, 1),
             "TissueCultureLab": (2, 2), "MutagenChamber": (2, 2), "GeneSplicer": (2, 2), "BioinformaticsCore": (1, 1),
             "SeedSynthesizer": (2, 2), "GenesisSynthesizer": (3, 3)}
items = {"SeedPacket": (150, 120, 70), "CultureMedium": (200, 220, 160), "GeneReagent": (90, 160, 230), "ArchogeneticCatalyst": (200, 120, 230)}
ui = ["CollectSeeds", "SeedStock", "SaveSeeds", "NewExperiment", "ChooseResult", "MainTab", "SeedCategory", "SuppliesCategory", "CollectSeedsDesignation"]


def save(img, rel):
    path = os.path.join(root, rel)
    if os.path.exists(path):
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


for name, (w, h) in buildings.items():
    img = Image.new("RGBA", (128 * w, 128 * h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([6, 6, 128 * w - 7, 128 * h - 7], 10, fill=(90, 100, 95, 255), outline=(40, 45, 42, 255), width=4)
    d.ellipse([128 * w / 2 - 20, 128 * h / 2 - 20, 128 * w / 2 + 20, 128 * h / 2 + 20], fill=(110, 200, 120, 255))
    save(img, f"Things/Building/APG/{name}.png")

for name, col in items.items():
    for i, s in enumerate("abc"):
        img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        for k in range(i + 1):
            x = 30 + k * 22
            d.rounded_rectangle([x, 40 + k * 6, x + 40, 96 + k * 6], 6, fill=col + (255,), outline=(40, 40, 40, 255), width=3)
        save(img, f"Things/Item/APG/{name}/{name}_{s}.png")

for name in ui:
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([16, 16, 112, 112], fill=(120, 190, 110, 255), outline=(30, 60, 30, 255), width=6)
    save(img, f"UI/APG/{name}.png")
print("ok")
