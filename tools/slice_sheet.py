# Slice a generated sprite sheet into game textures.
# usage: python -I slice_sheet.py SHEET ROWS COLS OUT1@WxH[@fill] OUT2@WxH ...   (row-major; "-" skips a cell)
# Each cell is cropped to its content's bounding box (alpha > threshold), padded to the target aspect and resized.
import sys
from PIL import Image

ALPHA_CUT = 40


def content_box(img):
    a = img.getchannel("A").point(lambda v: 255 if v > ALPHA_CUT else 0)
    return a.getbbox()


def main():
    sheet = Image.open(sys.argv[1]).convert("RGBA")
    # Remove faint halos/shadows.
    r, g, b, a = sheet.split()
    a = a.point(lambda v: 0 if v < ALPHA_CUT else v)
    sheet = Image.merge("RGBA", (r, g, b, a))
    rows, cols = int(sys.argv[2]), int(sys.argv[3])
    cw, ch = sheet.width // cols, sheet.height // rows
    targets = sys.argv[4:]
    for i, spec in enumerate(targets):
        if spec == "-":
            continue
        parts = spec.split("@")
        out, size = parts[0], parts[1]
        fill = float(parts[2]) if len(parts) > 2 else 0.94
        tw, th = map(int, size.split("x"))
        rr, cc = divmod(i, cols)
        cell = sheet.crop((cc * cw, rr * ch, (cc + 1) * cw, (rr + 1) * ch))
        box = content_box(cell)
        if box is None:
            print("empty cell", i)
            continue
        obj = cell.crop(box)
        scale = min(tw * fill / obj.width, th * fill / obj.height)
        obj = obj.resize((max(1, round(obj.width * scale)), max(1, round(obj.height * scale))), Image.LANCZOS)
        canvas = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
        canvas.alpha_composite(obj, ((tw - obj.width) // 2, (th - obj.height) // 2))
        canvas.save(out)
        print("wrote", out, canvas.size)


if __name__ == "__main__":
    main()
