"""Generate the code-native Code Tabs artwork and multi-resolution Windows icon."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent / "assets"
SCALE = 4
im = Image.new("RGBA", (256 * SCALE, 256 * SCALE))
d = ImageDraw.Draw(im)

def box(rect, radius, color):
    d.rounded_rectangle(tuple(v*SCALE for v in rect), radius*SCALE, fill=color)

def polygon(points, color):
    d.polygon([(x*SCALE, y*SCALE) for x,y in points], fill=color)

# Stacked editor tabs surrounding an angular blue coding ribbon.
box((30, 12, 214, 194), 27, "#0876B9")
box((18, 27, 230, 218), 28, "#169DE0")
box((8, 45, 248, 244), 30, "#102D48")
box((24, 57, 87, 70), 6, "#52C7FF")
box((96, 57, 144, 70), 6, "#365875")
polygon([(175,86),(212,101),(212,198),(175,215),(175,86)], "#28B9F7")
polygon([(175,86),(67,170),(44,151),(44,181),(68,196),(190,112)], "#168CD8")
polygon([(45,112),(68,98),(191,190),(175,215),(67,127),(45,145)], "#49C8FF")
polygon([(175,86),(212,101),(212,198),(175,215),(175,184),(188,176),(188,121),(175,115)], "#21ABED")

im = im.resize((256,256), Image.Resampling.LANCZOS)
im.save(ROOT / "CodeTabs.ico", sizes=[(n,n) for n in (16,20,24,32,40,48,64,128,256)])
im.save(ROOT / "CodeTabs-icon.png")
