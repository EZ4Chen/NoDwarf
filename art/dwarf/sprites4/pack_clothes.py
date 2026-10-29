"""Align generated modular art to the shared 256 x 384 sprite frame."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageChops
import numpy as np
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'source/Assets/Characters/Sprites4'
im=Image.open(ROOT/'art/dwarf/sprites4/clothes-source.png').convert('RGBA')
def cut(box):
    part=im.crop(box); a=np.array(part.getchannel('A')); y,x=np.where(a>128)
    return part.crop((int(x.min()),int(y.min()),int(x.max()+1),int(y.max()+1)))
# Clothing is calibrated to the body silhouette, rather than the generated sheet layout.
boxes={'shirt':[(0,60,580,460),(590,60,1000,470),(1020,60,1536,450)],
       'trousers':[(0,460,580,755),(590,475,1000,755),(1020,460,1536,755)],
       'boots':[(0,760,580,980),(590,760,1000,980),(1020,760,1536,980)]}
rects={'shirt':[(66,94,125,132),(76,95,107,133),(66,92,126,135)],
       'trousers':[(58,213,141,60),(77,212,102,58),(55,212,146,60)],
       'boots':[(33,307,190,63),(75,305,103,64),(45,307,171,63)]}
for item in boxes:
    atlas=Image.new('RGBA',(768,384))
    for d,(box,rect) in enumerate(zip(boxes[item],rects[item])):
        x,y,w,h=rect; part=cut(box).resize((w,h),Image.Resampling.LANCZOS)
        atlas.alpha_composite(part,(d*256+x,y))
    atlas.save(OUT/f'{item}.png')
# Reuse the exact generated pixels of head/arms as an occlusion layer over clothes.
# Coordinates describe the skin silhouette; they do not generate or repaint artwork.
polys=[[(0,0),(256,0),(256,95),(176,95),(171,111),(86,111),(78,95),(0,95)],
       [(0,0),(256,0),(256,106),(168,106),(147,90),(100,91),(79,112),(0,112)],
       [(0,0),(256,0),(256,94),(177,94),(172,104),(82,104),(76,94),(0,94)]]
arms=[ [[(0,91),(80,91),(76,145),(61,180),(57,260),(0,278)],[(175,91),(256,91),(256,278),(199,260),(193,180),(179,145)]],
       [[(84,104),(123,105),(140,135),(132,165),(149,219),(154,252),(134,264),(109,258),(105,223),(88,195),(77,158)]],
       [[(0,94),(78,94),(75,147),(62,181),(56,264),(0,278)],[(177,94),(256,94),(256,278),(198,264),(193,181),(180,147)]] ]
for sex in ['male','female']:
    body=Image.open(OUT/f'{sex}.png').convert('RGBA'); mask=Image.new('L',body.size); draw=ImageDraw.Draw(mask)
    for d in range(3):
        for poly in [polys[d]]+arms[d]:draw.polygon([(x+d*256,y) for x,y in poly],fill=255)
    body.putalpha(ImageChops.multiply(body.getchannel('A'),mask));body.save(OUT/f'{sex}-foreground.png')
    preview=Image.open(OUT/f'{sex}.png').convert('RGBA')
    for item in ['trousers','boots','shirt']:preview.alpha_composite(Image.open(OUT/f'{item}.png'))
    preview.alpha_composite(body);preview.save(ROOT/f'art/dwarf/sprites4/{sex}-clothed-preview.png')
im=Image.open(ROOT/'art/dwarf/sprites4/gear-source.png').convert('RGBA')
for name,y0,y1,rectangles in [
 ('helmet',0,170,[(80,10,98,57),(75,10,106,60),(77,11,103,57)]),
 ('armor',280,600,[(60,96,139,132),(76,96,107,132),(60,94,139,136)])]:
    atlas=Image.new('RGBA',(768,384))
    for d,(x0,x1) in enumerate([(150,560),(575,960),(990,1400)]):
        x,y,w,h=rectangles[d];part=cut((x0,y0,x1,y1)).resize((w,h),Image.Resampling.LANCZOS)
        atlas.alpha_composite(part,(d*256+x,y))
    atlas.save(OUT/f'{name}.png')
for name,box in [('sword',(250,600,500,1015)),('pickaxe',(570,600,965,1015)),('shield',(985,600,1390,1015))]:
    part=cut(box);part.thumbnail((256,384),Image.Resampling.LANCZOS);part.save(OUT/f'{name}-held.png')

for sex in ['male','female']:
    body=Image.open(OUT/f'{sex}.png').convert('RGBA');mask=Image.new('L',body.size);draw=ImageDraw.Draw(mask)
    for d in range(3):
        if d==1:
            draw.polygon([(256+x,y) for x,y in [(114,220),(134,219),(142,227),(147,236),(147,245),(143,251),(137,252),(135,257),(130,257),(127,254),(123,256),(117,251),(114,242)]],fill=255)
            continue
        for box in ([(13,211,58,262)] if d==0 else [(198,211,243,262)] if d==2 else [(108,216,155,263)]):
            x0,y0,x1,y1=box;draw.rectangle((d*256+x0,y0,d*256+x1,y1),fill=255)
    foreground=Image.open(OUT/f'{sex}-foreground.png').getchannel('A')
    body.putalpha(ImageChops.multiply(foreground,mask));body.save(OUT/f'{sex}-hands.png')
im=Image.open(ROOT/'art/dwarf/sprites4/tools-source.png').convert('RGBA')
for name,box,grip in [('axe',(0,0,385,550),300),('battleaxe',(385,0,805,550),595),('hammer',(805,0,1160,550),978),('shovel',(1160,0,1536,550),1320),('hoe',(0,550,570,1024),333)]:
    part=im.crop(box);a=np.array(part.getchannel('A'));ys,xs=np.where(a>128)
    x0,x1,y0,y1=int(xs.min()),int(xs.max()+1),int(ys.min()),int(ys.max()+1)
    part=part.crop((x0,y0,x1,y1));g=grip-box[0]-x0
    width=2*max(g,part.width-g);canvas=Image.new('RGBA',(width,part.height));canvas.alpha_composite(part,(width//2-g,0))
    canvas.thumbnail((384,384),Image.Resampling.LANCZOS);canvas.save(OUT/f'{name}-held.png')
glove=cut((580,560,940,1024));back=cut((970,560,1400,1024))
atlas=Image.new('RGBA',(768,384))
for d in range(3):
    rects=[(11,207,50,58),(195,207,50,58)] if d!=1 else [(107,211,48,54)]
    for i,(x,y,w,h) in enumerate(rects):
        part=(back if d==2 else glove).resize((w,h),Image.Resampling.LANCZOS)
        if i==1:part=part.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        atlas.alpha_composite(part,(d*256+x,y))
atlas.save(OUT/'gloves.png')
mask=Image.new('L',atlas.size);draw=ImageDraw.Draw(mask)
for d,box in enumerate([(0,200,70,280),(100,200,165,280),(190,200,256,280)]):
    x0,y0,x1,y1=box;draw.rectangle((d*256+x0,y0,d*256+x1,y1),fill=255)
atlas.putalpha(ImageChops.multiply(atlas.getchannel('A'),mask));atlas.save(OUT/'gloves-hands.png')
im=Image.open(ROOT/'art/dwarf/sprites4/cap-source.png').convert('RGBA');atlas=Image.new('RGBA',(768,384))
for d,(x,y,w,h) in enumerate([(78,1,101,60),(76,1,105,63),(78,1,102,67)]):
    part=cut((d*im.width//3,0,(d+1)*im.width//3,im.height)).resize((w,h),Image.Resampling.LANCZOS)
    atlas.alpha_composite(part,(d*256+x,y))
atlas.save(OUT/'cap.png')
