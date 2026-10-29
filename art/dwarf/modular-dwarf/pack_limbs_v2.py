"""Extract the two generated limb sprites without repainting or changing orientation."""
from pathlib import Path
from PIL import Image

ART = Path(__file__).resolve().parent
OUT = ART.parents[2] / 'source/Assets/Characters/ModularDwarf'
atlas = Image.open(ART / 'limbs-v2-source.png').convert('RGBA')
for i, name in enumerate(['bare-hand-v2', 'bare-foot-v2']):
    part = atlas.crop((i*atlas.width//2, 0, (i+1)*atlas.width//2, atlas.height))
    bounds = part.getchannel('A').point(lambda a: 255 if a > 128 else 0).getbbox()
    if bounds is None:
        raise ValueError(f'Empty sprite: {name}')
    part.crop(bounds).save(OUT / f'{name}.png')
    print(name, bounds)
