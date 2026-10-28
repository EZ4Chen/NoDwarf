"""Reproducible v2 adaptation of the preserved v1 authoring pipeline."""
from pathlib import Path
import sys
root=Path(__file__).resolve().parents[2]
sex='female' if '--female' in sys.argv else 'male'
s=(root/'art/dwarf/v1/build_dwarf.py').read_text()
s=s.replace("HERE = Path(__file__).resolve().parent",f"HERE = Path({str(root/'art/dwarf/v2')!r}) / {sex!r}")
s=s.replace('ROOT = HERE.parent.parent',f'ROOT = Path({str(root)!r})')
s=s.replace("OUT = ROOT / 'source/Assets/Characters/Dwarf'",f"OUT = ROOT / 'source/Assets/Characters/DwarfV2/{sex}'")
s=s.replace("ellipsoid('Head',(0,-.008,1.355),(.247,.205,.245),'skin','BASE','head',14,9)","box('Head',(0,-.008,1.35),(.448,.372,.438),'skin','BASE','head',.085)")
s=s.replace("ellipsoid('Deltoid '+side,(s*.305,0,1.075),(.135,.14,.128),'skin','BASE',u,10,5)","box('Deltoid '+side,(s*.305,0,1.075),(.255,.27,.24),'skin','BASE',u,.05)")
s=s.replace('(1.57,.21,.18),(1.625,.08,.07)', '(1.61,.25,.215),(1.67,.08,.07)')
# Remove only upper underwear for the male body, before slot meshes are joined.
remove="""
for ob in list(parts['BASE']):
    if ob.name.startswith(('Underwear tank','Tank shoulder strap')):
        parts['BASE'].remove(ob);bpy.data.objects.remove(ob,do_unlink=True)
""" if sex=='male' else """
# Angular hair at the back leaves the helmet fit unchanged.
box('Female hair back',(0,.13,1.32),(.43,.13,.32),'hair','BASE','head',.055)
box('Female hair crown',(0,0,1.53),(.45,.38,.12),'hair','BASE','head',.045)
box('Female mouth',(0,-.199,1.245),(.08,.015,.018),'nose','BASE','head',.005)
for side in [-1,1]:box('Female hair side',(side*.215,.015,1.32),(.045,.16,.25),'hair','BASE','head',.018)
"""
s=s.replace('# Join by slot:',remove+'\n# Join by slot:',1)
warp="""
# A piecewise proportion map, applied to mesh vertices AND all bone rest positions.
# Keep feet substantial; shorten the leg section 32%, broaden upper body 18%.
def warp(p):
    x,y,z=p
    width=1.10 if z<.65 else (1.18 if z<1.18 else 1.02)
    nz=z if z<=.12 else (.12+(z-.12)*.68 if z<=.70 else z-.1856)
    return Vector((x*width,y*(1.10 if z<1.18 else 1.0),nz))
def unwarp(p):
    x,y,z=p
    oz=z if z<=.12 else (.12+(z-.12)/.68 if z<=.5144 else z+.1856)
    width=1.10 if oz<.65 else (1.18 if oz<1.18 else 1.02)
    return Vector((x/width,y/(1.10 if oz<1.18 else 1.0),oz))
def handheld_point(p):
    # Preserve straight handles; the body's piecewise scaling is not a tool deformation.
    return warp(grip) + Quaternion((0,0,1),math.pi/2) @ (Vector(p)-grip)
for ob in joined.values():
    for v in ob.data.vertices:
        if ob in [joined['PICKAXE'],joined['SWORD']]:
            v.co=handheld_point(v.co);continue
        if ob == joined.get('HELMET'):v.co.x*=1.12;v.co.y*=1.12
        v.co=warp(v.co)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
for b in armdata.edit_bones:
    b.head=warp(b.head);b.tail=warp(b.tail)
bpy.ops.object.mode_set(mode='OBJECT')
bones={b.name:(tuple(b.head_local),tuple(b.tail_local)) for b in armdata.bones}
"""
s=s.replace('# Use face-corner vertex colors',warp+'\n# Use face-corner vertex colors',1)
s=s.replace('Vector(target)-pb.head','warp(target)-pb.head')
s=s.replace('c=face.calc_center_median()','c=unwarp(face.calc_center_median())')
s=s.replace("tip=grip+Vector((0,0,.35 if name=='Mine' else .58))","tip=handheld_point(grip+Vector((0,0,.35 if name=='Mine' else .58)))")
s=s.replace("'height_m':1.625,'body_height_m':1.60","'height_m':1.4844,'body_height_m':1.4144")
if sex=='female':
    s=s.replace("['cosmetic_beard']","[]")
    s=s.replace("['cosmetic_beard','", "['")
    s=s.replace("['BASE','COSMETIC_BEARD']","['BASE']")
if '--fast' in sys.argv:s=s.replace('bpy.ops.render.render(write_still=True)','None')
# Persist the generated source too, so each file is inspectable without this wrapper.
(root/'art/dwarf/v2'/sex/'generated_build.py').write_text(s)
exec(compile(s,str(root/'art/dwarf/v2'/sex/'generated_build.py'),'exec'))
