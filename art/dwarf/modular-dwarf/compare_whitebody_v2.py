"""Compose actual Godot captures for reviewing the revision, without repainting assets."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ART = Path(__file__).resolve().parent
ROOT = ART.parents[2]
font_path = ROOT / 'source/Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf'
font = ImageFont.truetype(str(font_path), 20)
small = ImageFont.truetype(str(font_path), 18)
background = '#202d33'
gallery = Image.new('RGB', (840, 350), background)
draw = ImageDraw.Draw(gallery)
for i, (frame, label) in enumerate([(0, '男 · 新白身'), (70, '男 · 工作服'), (130, '女 · 新白身')]):
    image = Image.open(ROOT / f'source/.local/modular-film/{frame:04d}.png').convert('RGB')
    gallery.paste(image.crop((330, 300, 590, 615)), (i*280+10, 0))
    draw.text((i*280+140, 330), label, fill='#e5eded', font=font, anchor='mm')
gallery.save(ART / 'whitebody-v2-gallery.png')

comparison = Image.new('RGB', (900, 790), background)
draw = ImageDraw.Draw(comparison)
draw.text((30, 17), '修改前', font=small, fill='#a8b6b6')
comparison.paste(Image.open(ART / 'proportions-revised.png').convert('RGB'), (30, 45))
draw.text((30, 395), '修改后', font=small, fill='#a8b6b6')
comparison.paste(gallery, (30, 425))
comparison.save(ART / 'whitebody-v2-comparison.png')
