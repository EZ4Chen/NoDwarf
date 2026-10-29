"""Alpha-crop the generated shared bodies into stable attachment frames."""
from pathlib import Path
from PIL import Image

ART = Path(__file__).resolve().parent
OUT = ART.parents[2] / 'source/Assets/Characters/ModularDwarf'
atlas = Image.open(ART / 'shared-bodies-source.png').convert('RGBA')


def crop(box):
    part = atlas.crop(box)
    bounds = part.getchannel('A').point(lambda a: 255 if a > 128 else 0).getbbox()
    if bounds is None:
        raise ValueError(f'Empty sprite at {box}')
    return part.crop(bounds)


def pack(part, name, rect):
    x, y, w, h = rect
    frame = Image.new('RGBA', (320, 300))
    frame.alpha_composite(part.resize((w, h), Image.Resampling.LANCZOS), (x, y))
    frame.save(OUT / f'{name}.png')


for i, (name, width) in enumerate([('standard', 238), ('broad', 274), ('round', 286)]):
    part = crop((i*atlas.width//3, 0, (i+1)*atlas.width//3, round(atlas.height*.62)))
    pack(part, 'body-shared-'+name, ((320-width)//2, 10, width, 280))

shirt = crop((0, round(atlas.height*.62), atlas.width, atlas.height))
pack(shirt, 'underwear-shared', (56, 36, 208, 140))
print('Packed three shared body sprites and one independent undershirt.')
