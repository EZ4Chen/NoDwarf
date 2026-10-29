"""Build a white-body/clothing comparison from actual Godot frames."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ART = Path(__file__).resolve().parent
ROOT = ART.parents[2]
font = str(ROOT / 'source/Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf')
label_font = ImageFont.truetype(font, 19)
heading_font = ImageFont.truetype(font, 22)
sheet = Image.new('RGB', (1120, 780), '#202d33')
draw = ImageDraw.Draw(sheet)
names = ['男 · 标准', '女 · 标准', '男 · 宽壮', '女 · 圆胖']
for row, heading in enumerate(['共用体型 · 内衣基础身', '同一角色 · 穿上工作服']):
    y = row*390
    draw.text((24, y+12), heading, fill='#e5eded', font=heading_font)
    for col, label in enumerate(names):
        frame = (row*4+col)*60
        image = Image.open(ROOT / f'source/.local/modular-film/{frame:04d}.png').convert('RGB')
        sheet.paste(image.crop((330, 300, 590, 615)), (col*280+10, y+45))
        draw.text((col*280+140, y+371), label, fill='#d7e2e2', font=label_font, anchor='mm')
sheet.save(ART / 'shared-body-comparison.png')
