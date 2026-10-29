"""Extract disconnected generated parts and align the pick's handle pivot. No painted pixels are redrawn."""
from pathlib import Path
from collections import deque
import numpy as np
from PIL import Image, ImageFilter, ImageChops
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'source/Assets/Characters/CompactPrototype'
im=Image.open(Path(__file__).with_name('parts-source.png')).convert('RGBA')
a=np.array(im.getchannel('A')); h,w=a.shape; seen=np.zeros((h,w),dtype=bool); parts=[]
for y,x in zip(*np.where(a>128)):
    if seen[y,x]:continue
    q=deque([(int(x),int(y))]);seen[y,x]=True;points=[]
    while q:
        xx,yy=q.popleft();points.append((xx,yy))
        for nx,ny in ((xx-1,yy),(xx+1,yy),(xx,yy-1),(xx,yy+1)):
            if 0<=nx<w and 0<=ny<h and not seen[ny,nx] and a[ny,nx]>128:
                seen[ny,nx]=True;q.append((nx,ny))
    if len(points)>1000:
        xs,ys=zip(*points);parts.append((len(points),(min(xs),min(ys),max(xs)+1,max(ys)+1),points))
parts=sorted(parts,reverse=True)[:4]
for size,box,points in parts:
    cx=(box[0]+box[2])/2;cy=(box[1]+box[3])/2
    name='body' if cx<780 else 'pickaxe' if cx>1150 else 'hand' if cy<550 else 'boot'
    mask=Image.new('L',im.size);mask.putdata([0]*(w*h));arr=np.zeros((h,w),dtype=np.uint8)
    for x,y in points:arr[y,x]=255
    mask=Image.fromarray(arr).filter(ImageFilter.MaxFilter(5))
    layer=im.copy();layer.putalpha(ImageChops.multiply(im.getchannel('A'),mask))
    box=layer.getbbox();part=layer.crop(box)
    if name=='pickaxe':
        # Center the actual shaft, not the asymmetrical silhouette of the pick head.
        row=np.array(part.getchannel('A'))[int(part.height*.78)];indices=np.where(row>128)[0]
        grip=round((int(indices.min())+int(indices.max()))/2)
        width=2*max(grip,part.width-grip);canvas=Image.new('RGBA',(width,part.height))
        canvas.alpha_composite(part,(width//2-grip,0));part=canvas
    part.thumbnail((768,768),Image.Resampling.LANCZOS);part.save(OUT/f'{name}.png')
    print(name,box,part.size)
old=Image.open(ROOT/'art/dwarf/sprites4/male-clothed-preview.png').convert('RGBA').crop((0,0,256,384))
old=old.crop(old.getbbox());old.save(OUT/'old-reference.png')
assert all((OUT/f'{n}.png').exists() for n in ['body','hand','boot','pickaxe'])
