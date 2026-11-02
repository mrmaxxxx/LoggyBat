# Regenerates every package/app icon from the approved logo (docs/images/loggybat-logo.webp, variant 01).
# Developer tool (needs Pillow), not part of the build: run it, review the PNGs, commit them.
#   python scripts/New-Icons.py
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent.parent
logo = Image.open(root / 'docs/images/loggybat-logo.webp').convert('RGBA')
logo = logo.crop(logo.getbbox())  # drop the transparent margin so small icons use the whole square
side = max(logo.size)
square = Image.new('RGBA', (side, side))
square.paste(logo, ((side - logo.width) // 2, (side - logo.height) // 2))

images = root / 'src/LoggyBat.Package/Images'


def fit(width, height, share):
    """Logo centred on a transparent canvas, its side = share of the shorter canvas side."""
    size = max(1, round(min(width, height) * share))
    canvas = Image.new('RGBA', (width, height))
    canvas.paste(square.resize((size, size), Image.LANCZOS), ((width - size) // 2, (height - size) // 2))
    return canvas


# Same file names and sizes the template had, so Package.appxmanifest stays unchanged.
for path in sorted(images.glob('*.png')):
    with Image.open(path) as old:
        width, height = old.size
    share = 1.0 if width == height else 0.8  # wide tile and splash screen: logo in the middle
    if path.name.startswith(('MediumTile', 'LargeTile', 'SmallTile')):
        share = 0.8  # Start tiles get a little air around the plate
    fit(width, height, share).save(path)

square.resize((256, 256), Image.LANCZOS).save(
    root / 'src/LoggyBat/LoggyBat.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
print('icons written')
