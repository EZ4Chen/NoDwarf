"""Deterministic alpha crops and common attachment frames for generated modular art."""
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[3]
ART=Path(__file__).resolve().parent
OUT=ROOT/'source/Assets/Characters/ModularDwarf'
OUT.mkdir(exist_ok=True,parents=True)
def crop(im,box):
    part=im.crop(box);a=np.array(part.getchannel('A'));ys,xs=np.where(a>128)
    return part.crop((int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)))
def place(part,name,size,rect):
    x,y,w,h=rect;sheet=Image.new('RGBA',size);sheet.alpha_composite(part.resize((w,h),Image.Resampling.LANCZOS),(x,y));sheet.save(OUT/f'{name}.png')
def three(file):
    im=Image.open(ART/file).convert('RGBA')
    return [crop(im,(i*im.width//3,0,(i+1)*im.width//3,im.height)) for i in range(3)]
for i,part in enumerate(three('heads-source.png')):
    place(part,'head-'+['square','round','lean'][i],(320,400),(30,20,260,260))
if (ART/'hair-source.png').exists():
    for i,part in enumerate(three('hair-source.png')):
        place(part,'hair-'+['swept','waves','braids'][i],(320,400),[(38,-3,244,142),(9,-8,302,282),(4,-8,312,342)][i])
if (ART/'beards-source.png').exists():
    for i,part in enumerate(three('beards-source.png')):
        place(part,'beard-'+['short','full','braids'][i],(320,400),[(55,190,210,104),(36,186,248,165),(48,190,224,172)][i])
if (ART/'bodies-source.png').exists():
    im=Image.open(ART/'bodies-source.png').convert('RGBA')
    for i,name in enumerate(['male','broad','female']):
        part=crop(im,(i*im.width//3,0,(i+1)*im.width//3,int(im.height*.66)))
        w=[260,300,250][i];place(part,'body-'+name,(320,260),((320-w)//2,10,w,240))
    crop(im,(0,int(im.height*.66),im.width//2,im.height)).save(OUT/'bare-hand.png')
    crop(im,(im.width//2,int(im.height*.66),im.width,im.height)).save(OUT/'bare-foot.png')
if (ART/'outfits-source.png').exists():
    im=Image.open(ART/'outfits-source.png').convert('RGBA')
    for row,outfit in enumerate(['work','apron']):
        for i,name in enumerate(['male','broad','female']):
            part=crop(im,(i*im.width//3,row*im.height//2,(i+1)*im.width//3,(row+1)*im.height//2))
            w=[264,304,254][i];place(part,'outfit-'+outfit+'-'+name,(320,260),((320-w)//2,8,w,244))
# Render a parts assembly contact sheet using the same ordering as the game.
if (OUT/'body-male.png').exists():
    sheet=Image.new('RGBA',(320*3,500*2))
    for row in range(2):
        for i,body in enumerate(['male','broad','female']):
            tile=Image.new('RGBA',(320,500))
            names=[('body-'+body,(0,240)),('outfit-work-'+body,(0,240)),('head-'+['square','lean','round'][i],(0,-10)),('hair-'+['swept','waves','braids'][i],(0,-10)),('beard-'+['short','full','braids'][i],(0,-10))]
            for name,pos in names:
                if row==0 and not name.startswith(('body','head')):continue
                if (OUT/f'{name}.png').exists():tile.alpha_composite(Image.open(OUT/f'{name}.png'),pos)
            sheet.alpha_composite(tile,(i*320,row*500))
    sheet.save(ART/'assembly-check.png')
