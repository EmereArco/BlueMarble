"""Generates BlueMarble/Assets/BlueMarble.ico — a stylized day/night globe.

Run from the repo root:  python tools/make_icon.py
Produces BlueMarble/Assets/BlueMarble.ico (multi-resolution) used as the
app/tray icon. The .ico is committed, so re-run this only when changing the
artwork. Kept outside the project so it never ships in the build output.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

# Master render size; downsampled to each icon resolution for clean edges.
MASTER = 1024
SIZES = [16, 24, 32, 48, 64, 128, 256]

OCEAN_DAY = (28, 86, 170)
OCEAN_NIGHT = (8, 18, 46)
LAND_DAY = (58, 132, 70)
LAND_NIGHT = (24, 52, 34)
CITY_LIGHT = (255, 214, 130)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


# Rough continent blobs in normalized lon/lat-ish disc coordinates (x,y,r).
LAND_BLOBS = [
    (-0.30, -0.28, 0.34), (-0.18, 0.05, 0.30), (-0.22, 0.34, 0.20),   # Americas
    (0.10, -0.18, 0.22), (0.20, 0.12, 0.30), (0.28, 0.40, 0.18),      # Africa/Europe
    (0.46, -0.22, 0.30), (0.55, 0.30, 0.16),                          # Asia/Oceania
]


def in_land(nx, ny):
    for bx, by, br in LAND_BLOBS:
        d = math.hypot(nx - bx, ny - by)
        if d < br * (0.6 + 0.4 * math.sin(nx * 9 + ny * 7)):
            return True
    return False


def render_master():
    img = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    px = img.load()
    cx = cy = MASTER / 2
    radius = MASTER / 2 - MASTER * 0.03
    # Terminator: day on the left, night on the right, soft seam in the middle.
    term = 0.12  # shift of the day/night boundary
    soft = 0.28  # softness half-width
    for y in range(MASTER):
        for x in range(MASTER):
            dx = (x - cx) / radius
            dy = (y - cy) / radius
            d2 = dx * dx + dy * dy
            if d2 > 1.0:
                continue
            # day factor 1=full day, 0=full night across the disc horizontally
            t = (term - dx) / (2 * soft) + 0.5
            t = max(0.0, min(1.0, t))
            t = t * t * (3 - 2 * t)  # smoothstep
            land = in_land(dx, dy)
            if land:
                col = lerp(LAND_NIGHT, LAND_DAY, t)
            else:
                col = lerp(OCEAN_NIGHT, OCEAN_DAY, t)
            # City lights sprinkled on the night side over land
            if t < 0.35 and land:
                spark = (math.sin(x * 1.7) * math.cos(y * 1.3))
                if spark > 0.93:
                    col = lerp(col, CITY_LIGHT, 0.8)
            # simple spherical shading (limb darkening)
            shade = 1.0 - 0.35 * d2
            col = tuple(int(c * shade) for c in col)
            px[x, y] = (col[0], col[1], col[2], 255)
    # soft atmosphere glow ring
    glow = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse([cx - radius - 12, cy - radius - 12, cx + radius + 12, cy + radius + 12],
               outline=(120, 180, 255, 160), width=18)
    glow = glow.filter(ImageFilter.GaussianBlur(14))
    img = Image.alpha_composite(img, glow)
    return img


def main():
    master = render_master()
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(repo_root, "BlueMarble", "Assets", "BlueMarble.ico")
    # Pillow downsamples the master to every requested size; pass the full-res
    # image (not a pre-shrunk one) so all resolutions are present in the .ico.
    master.save(out, format="ICO", sizes=[(s, s) for s in SIZES])
    print("wrote", out)


if __name__ == "__main__":
    main()
