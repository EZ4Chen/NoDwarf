"""Deterministic sprite crops/attachment frames; all artwork comes from preserved imagegen sources."""
from pathlib import Path
from PIL import Image, ImageFilter
ART=Path(__file__).resolve().parent
ROOT=ART.parents[2]
OLD=ROOT/'source/Assets/Characters/ModularDwarf'
LEGACY=ROOT/'source/Assets/Characters/Sprites4'
COMPACT=ROOT/'source/Assets/Characters/CompactPrototype'
OUT=ROOT/'source/Assets/Characters/ModularFour'
OUT.mkdir(exist_ok=True)
L=Image.Resampling.LANCZOS

def read(path):return Image.open(path).convert('RGBA')
def tight(im):
    mask=im.getchannel('A').point(lambda a:255 if a>128 else 0)
    core=mask.filter(ImageFilter.MinFilter(9)).getbbox()
    box=(max(0,core[0]-4),max(0,core[1]-4),min(im.width,core[2]+4),min(im.height,core[3]+4)) if core else mask.getbbox()
    if not box:raise ValueError('Empty alpha crop')
    return im.crop(box)
def cell(file,row,col,bounds):
    im=read(ART/file)
    # The side-view toe crosses the cell divider; start the rear-foot crop past that neighbor.
    left=round(im.width*.58) if file=='limb-turns.png' and row==1 and col==1 else col*im.width//2
    return tight(im.crop((left,round(bounds[row]*im.height),(col+1)*im.width//2,round(bounds[row+1]*im.height))))
def frame():return Image.new('RGBA',(400,560))
def paste(im,part,rect):
    x,y,w,h=map(round,rect);im.alpha_composite(part.resize((w,h),L),(x,y))
def atlas(name,frames):
    out=Image.new('RGBA',(1200,560))
    for i,im in enumerate(frames):out.alpha_composite(im,(400*i,0))
    out.resize((600,280),L).save(OUT/(name+'.png'))
def front_layer(name,head=False):
    out=frame();paste(out,read(OLD/(name+'.png')),(96,75,208,260) if head else (59,217,282,264));return out

widths=[238*.88,274*.88,286*.88]
for row,name in enumerate(['standard','broad','round']):
    frames=[front_layer('body-shared-'+name)]
    for col in range(2):
        out=frame();w=[132,154,174][row] if col==0 else widths[row]
        paste(out,cell('body-turns.png',row,col,[0,410/1536,810/1536,1216/1536,1]),(200-w/2,226,w,246));frames.append(out)
    atlas('body-'+name,frames)
    # One common undershirt scaled by body shape for both views.
    fronts=frame();paste(fronts,read(OLD/'underwear-shared.png'),(200-141*widths[row]/widths[0],217,282*widths[row]/widths[0],264))
    frames=[fronts]
    for col in range(2):
        out=frame();w=([132,154,174][row]*.91 if col==0 else widths[row]*.875)
        paste(out,cell('body-turns.png',3,col,[0,410/1536,810/1536,1216/1536,1]),(200-w/2,249,w,123));frames.append(out)
    atlas('underwear-'+name,frames)
    for r,kind in enumerate(['work','apron']):
        part=tight(read(OLD/f"outfit-{kind}-{'male' if row==0 else 'broad'}.png"))
        w=[232,268,280][row];out=frame();paste(out,part,(200-w/2,226,w,246));frames=[out]
        for col in range(2):
            out=frame();w=([158,180,196][row] if col==0 else [232,268,280][row]);paste(out,cell('outfit-turns.png',r,col,[0,.49,1]),(200-w/2,226,w,246));frames.append(out)
        atlas(kind+'-'+name,frames)
    for gear in ['trousers','armor']:
        raw=read(LEGACY/(gear+'.png'));frames=[]
        for d in range(3):
            part=tight(raw.crop((d*256,0,(d+1)*256,384)));out=frame()
            w=([132,154,174][row] if d==1 else widths[row])*(1.06 if gear=='armor' else 1.02)
            paste(out,part,(200-w/2,226 if gear=='armor' else 396,w,206 if gear=='armor' else 76));frames.append(out)
        atlas(gear+'-'+name,frames)
for row,name in enumerate(['square','round','lean']):
    frames=[front_layer('head-'+name,True)]
    for col in range(2):
        out=frame();paste(out,cell('heads-turns.png',row,col,[0,506/1536,982/1536,1]),(129,88,174,169) if col==0 else (115,88,170,169));frames.append(out)
    atlas('head-'+name,frames)
for row,name in enumerate(['swept','waves','braids']):
    frames=[front_layer('hair-'+name,True)]
    for col in range(2):
        out=frame();rect=([(122,72,150,140),(116,70,170,205),(110,70,164,235)][row] if col==0 else [(115,72,170,172),(100,72,200,217),(100,72,200,247)][row])
        paste(out,cell('hair-turns.png',row,col,[0,450/1536,982/1536,1]),rect);frames.append(out)
    atlas('hair-'+name,frames)
raw=read(ART/'beard-turns.png')
for i,name in enumerate(['short','full','braids']):
    part=tight(raw.crop((i*raw.width//3,0,(i+1)*raw.width//3,raw.height)))
    side=frame();paste(side,part,[(218,205,83,68),(207,205,102,105),(207,205,98,110)][i])
    atlas('beard-'+name,[front_layer('beard-'+name,True),side,frame()])
for gear in ['helmet','cap']:
    raw=read(LEGACY/(gear+'.png'));frames=[]
    for d in range(3):
        out=frame();part=tight(raw.crop((d*256,0,(d+1)*256,384)))
        paste(out,part,(108 if d!=1 else 123,65,184,90 if gear=='helmet' else 78));frames.append(out)
    atlas(gear,frames)
for row,name in enumerate(['hand-bare','foot-bare','hand-glove','foot-boot']):
    part=tight(read([OLD/'bare-hand-v2.png',OLD/'bare-foot-v2.png',COMPACT/'hand.png',COMPACT/'boot.png'][row]))
    for d in range(3):
        p=part if d==0 else cell('limb-turns.png',row,d-1,[0,.25,.45,.71,1])
        if row==3 and d==2 and (ART/'heel.png').exists():p=tight(read(ART/'heel.png'))
        p.thumbnail((192,192),L);p.save(OUT/f'{name}-{d}.png')
print('Packed directional bodies, apparel, heads, hair, beards, headgear, independent hands and feet.')
