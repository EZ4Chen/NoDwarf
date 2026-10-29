"""Deterministic crop/atlas packing only. Artwork and alpha are imagegen outputs."""
from pathlib import Path
from PIL import Image
import numpy as np
ROOT=Path(__file__).resolve().parents[3]
ART=ROOT/'art/dwarf/sprites4'
OUT=ROOT/'source/Assets/Characters/Sprites4'
OUT.mkdir(parents=True,exist_ok=True)
W,H=256,384
for sex in ['male','female']:
    image=Image.open(ART/f'{sex}-source.png').convert('RGBA')
    sheet=Image.new('RGBA',(W*3,H))
    splits=[0,600,935 if sex=='male' else 950,1536]
    for i in range(3):
        tile=image.crop((splits[i],0,splits[i+1],image.height))
        alpha=np.array(tile.getchannel('A'))
        ys,xs=np.where(alpha>128)
        bounds=(int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1))
        tile=tile.crop(bounds)
        scale=min(352/tile.height,244/tile.width)
        tile=tile.resize((round(tile.width*scale),round(tile.height*scale)),Image.Resampling.LANCZOS)
        sheet.alpha_composite(tile,(i*W+(W-tile.width)//2,368-tile.height))
    sheet.save(OUT/f'{sex}.png')
    print(sex,sheet.size)
