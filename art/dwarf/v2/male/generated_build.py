"""Rebuild the original NoDwarf modular character with Blender's bundled Python.

Run: Blender --background --factory-startup --python art/dwarf/build_dwarf.py
No add-ons, downloads, or third-party assets are used.
"""
import bpy
import math
import json
import sys
from pathlib import Path
from mathutils import Vector, Quaternion

HERE = Path('/Users/ez4chen/NoDwarf/art/dwarf/v2') / 'male'
ROOT = Path('/Users/ez4chen/NoDwarf')
OUT = ROOT / 'source/Assets/Characters/DwarfV2/male'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for datablocks in (bpy.data.materials, bpy.data.actions):
    for item in list(datablocks):
        datablocks.remove(item)

def material(name, color, metallic=0, roughness=.78):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    return m

M = {
    'skin': material('Skin warm sandstone', (.62, .355, .205)),
    'nose': material('Nose and ears', (.68, .32, .20)),
    'linen': material('Undergarments ivory linen', (.80, .74, .57)),
    'seam': material('Linen seams', (.42, .35, .23)),
    'hair': material('Beard burnt umber', (.19, .068, .028)),
    'hairlight': material('Beard ridges', (.28, .108, .035)),
    'eye': material('Eyes charcoal', (.018, .025, .026)),
    'white': material('Eye whites', (.82, .81, .68)),
    'shirt': material('Overshirt moss green', (.18, .30, .22)),
    'trim': material('Clothing ochre trim', (.48, .33, .14)),
    'pants': material('Trousers slate', (.16, .20, .23)),
    'leather': material('Leather chestnut', (.23, .10, .042)),
    'darkleather': material('Leather dark edges', (.105, .054, .028)),
    'metal': material('Forged iron', (.32, .39, .42), .65, .46),
    'edge': material('Iron bevels', (.52, .59, .59), .7, .36),
    'brass': material('Brass buckles', (.58, .36, .10), .6, .4),
    'wood': material('Tool handles oak', (.34, .18, .07)),
    'woodlight': material('Shield planks', (.46, .28, .11)),
}

collections = {}
for name in ['BASE', 'COSMETIC_BEARD', 'SHIRT', 'TROUSERS', 'BOOTS', 'ARMOR', 'HELMET', 'PICKAXE', 'SWORD', 'SHIELD', 'PRESENTATION']:
    c = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(c)
    collections[name] = c
parts = {k: [] for k in collections}

armdata = bpy.data.armatures.new('DwarfSkeleton')
rig = bpy.data.objects.new('DwarfRig', armdata)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bones = {}
def bone(name, head, tail, parent=None, deform=True):
    b = armdata.edit_bones.new(name)
    b.head = head
    b.tail = tail
    if parent:
        b.parent = armdata.edit_bones[parent]
    b.use_deform = deform
    bones[name] = (tuple(head), tuple(tail))
    return b

bone('root', (0,0,0), (0,0,.18))
bone('pelvis', (0,0,.64), (0,0,.79), 'root')
bone('spine', (0,0,.79), (0,0,.96), 'pelvis')
bone('chest', (0,0,.96), (0,0,1.13), 'spine')
bone('neck', (0,0,1.13), (0,0,1.22), 'chest')
bone('head', (0,0,1.22), (0,0,1.53), 'neck')
for side, s in [('L',1), ('R',-1)]:
    bone('upper_arm.'+side, (s*.32,0,1.09), (s*.51,0,.85), 'chest')
    bone('forearm.'+side, (s*.51,0,.85), (s*.60,-.015,.65), 'upper_arm.'+side)
    bone('hand.'+side, (s*.60,-.015,.65), (s*.63,-.025,.54), 'forearm.'+side)
    bone('thigh.'+side, (s*.155,0,.66), (s*.17,-.015,.37), 'pelvis')
    bone('shin.'+side, (s*.17,-.015,.37), (s*.18,0,.12), 'thigh.'+side)
    bone('foot.'+side, (s*.18,0,.12), (s*.18,-.19,.075), 'shin.'+side)
    bone('slot_hand_'+side.lower(), (s*.615,-.04,.585), (s*.615,-.14,.585), 'hand.'+side, False)
bone('slot_head', (0,0,1.48), (0,0,1.58), 'head', False)
bone('slot_back', (0,.17,1.02), (0,.27,1.02), 'chest', False)
bone('slot_belt', (0,-.20,.74), (0,-.30,.74), 'pelvis', False)
bpy.ops.object.mode_set(mode='OBJECT')
rig.show_in_front = True
rig.display_type = 'WIRE'
for pb in rig.pose.bones:
    pb.rotation_mode = 'XYZ'

def mesh(name, vertices, faces, mat, category, weights=None):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    ob = bpy.data.objects.new(name, data)
    collections[category].objects.link(ob)
    data.materials.append(M[mat])
    parts[category].append(ob)
    if weights:
        groups = {}
        for i, w in enumerate(weights):
            if isinstance(w, str): w = {w: 1}
            for key, value in w.items():
                if key not in groups: groups[key] = ob.vertex_groups.new(name=key)
                if value > 0: groups[key].add([i], value, 'REPLACE')
        mod = ob.modifiers.new('Shared dwarf skeleton', 'ARMATURE')
        mod.object = rig
        ob.parent = rig
    return ob

def ellipsoid(name, center, scale, mat, category, joint, segments=12, rings=7):
    verts=[]; faces=[]
    # No coincident pole rings: actual triangle fans at the two poles.
    verts.append((center[0],center[1],center[2]-scale[2]))
    for j in range(1,rings):
        phi=-math.pi/2+math.pi*j/rings
        for i in range(segments):
            a=2*math.pi*i/segments
            verts.append((center[0]+scale[0]*math.cos(phi)*math.cos(a),center[1]+scale[1]*math.cos(phi)*math.sin(a),center[2]+scale[2]*math.sin(phi)))
    top=len(verts);verts.append((center[0],center[1],center[2]+scale[2]))
    for i in range(segments):faces.append((0,1+(i+1)%segments,1+i))
    for j in range(rings-2):
        for i in range(segments):
            a=1+j*segments+i;b=1+j*segments+(i+1)%segments
            faces.append((a,b,b+segments,a+segments))
    for i in range(segments):faces.append((top,top-segments+i,top-segments+(i+1)%segments))
    return mesh(name,verts,faces,mat,category,[joint]*len(verts))

def tube(name, points, widths, depths, mat, category, weights, n=10, cap=True):
    verts=[];faces=[];w=[]
    for j, point in enumerate(points):
        if j==0:tangent=Vector(points[1])-Vector(point)
        elif j==len(points)-1:tangent=Vector(point)-Vector(points[j-1])
        else:tangent=Vector(points[j+1])-Vector(points[j-1])
        tangent.normalize()
        across=Vector((0,1,0)).cross(tangent).normalized()
        forward=tangent.cross(across).normalized()
        for i in range(n):
            a=2*math.pi*i/n
            v=Vector(point)+across*(widths[j]*math.cos(a))+forward*(depths[j]*math.sin(a))
            verts.append(tuple(v));w.append(weights[j])
    for j in range(len(points)-1):
        for i in range(n):
            a=j*n+i;b=j*n+(i+1)%n
            faces.append((a,b,b+n,a+n))
    if cap:
        faces.append(tuple(reversed(range(n))))
        faces.append(tuple((len(points)-1)*n+i for i in range(n)))
    return mesh(name,verts,faces,mat,category,w)

def box(name, center, size, mat, category, joint, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    ob=bpy.context.object;ob.name=name
    ob.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=ob.modifiers.new('Small forged edges','BEVEL');mod.width=bevel;mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    # bake location so joined skinned pieces share the origin
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    for c in list(ob.users_collection):c.objects.unlink(ob)
    collections[category].objects.link(ob);parts[category].append(ob)
    ob.data.materials.append(M[mat])
    g=ob.vertex_groups.new(name=joint);g.add(list(range(len(ob.data.vertices))),1,'REPLACE')
    ob.parent=rig
    mod=ob.modifiers.new('Shared dwarf skeleton','ARMATURE');mod.object=rig
    return ob

# Body: deliberately broad torso, short weight-bearing legs, large head and nose.
torso_points=[(0,0,.64),(0,0,.73),(0,0,.84),(0,0,.95),(0,0,1.065),(0,0,1.13)]
torso_w=[.245,.29,.32,.325,.335,.24]
torso_d=[.15,.18,.195,.19,.175,.13]
torso_weights=['pelvis',{'pelvis':.8,'spine':.2},'spine',{'spine':.5,'chest':.5},'chest','chest']
tube('Body torso',torso_points,torso_w,torso_d,'skin','BASE',torso_weights,12)
ellipsoid('Neck',(0,0,1.16),(.115,.115,.13),'skin','BASE','neck',10,5)
box('Head',(0,-.008,1.35),(.448,.372,.438),'skin','BASE','head',.085)
ellipsoid('Broad nose',(0,-.212,1.325),(.078,.092,.075),'nose','BASE','head',8,5)
for side,s in [('L',1),('R',-1)]:
    ellipsoid('Ear '+side,(s*.244,-.005,1.36),(.063,.055,.087),'skin','BASE','head',8,5)
    ellipsoid('Ear inset '+side,(s*.281,-.035,1.36),(.018,.026,.045),'nose','BASE','head',7,4)
    ellipsoid('Eye white '+side,(s*.088,-.189,1.388),(.046,.016,.029),'white','BASE','head',8,5)
    ellipsoid('Pupil '+side,(s*.083,-.205,1.389),(.017,.008,.021),'eye','BASE','head',8,5)
    brow=box('Brow '+side,(s*.089,-.191,1.433),(.105,.035,.028),'hair','BASE','head',.008)
    # Slightly arched brows; geometry remains baked before export.
    u='upper_arm.'+side;f='forearm.'+side;h='hand.'+side
    box('Deltoid '+side,(s*.305,0,1.075),(.255,.27,.24),'skin','BASE',u,.05)
    tube('Arm '+side,[(s*.30,0,1.08),(s*.39,0,1.005),(s*.48,0,.89),(s*.515,0,.845),(s*.555,-.005,.76),(s*.60,-.015,.65)], [.12,.126,.10,.093,.10,.069],[.13,.125,.10,.09,.095,.064], 'skin','BASE',[u,u,{u:.8,f:.2},{u:.45,f:.55},f,f],10)
    ellipsoid('Hand '+side,(s*.618,-.023,.594),(.087,.072,.09),'skin','BASE',h,10,6)
    ellipsoid('Thumb '+side,(s*.572,-.068,.605),(.039,.043,.057),'skin','BASE',h,8,5)
    th='thigh.'+side;sh='shin.'+side;ft='foot.'+side
    tube('Leg '+side,[(s*.15,0,.67),(s*.16,0,.55),(s*.17,-.01,.43),(s*.17,-.015,.37),(s*.175,-.005,.29),(s*.18,0,.12)], [.13,.137,.111,.10,.107,.071],[.14,.14,.115,.10,.105,.072],'skin','BASE',[th,th,{th:.8,sh:.2},{th:.5,sh:.5},sh,sh],10)
    ellipsoid('Foot '+side,(s*.18,-.075,.075),(.105,.18,.075),'skin','BASE',ft,10,5)

# Permanent opaque undergarments, included in the base GLB and all body masks.
tube('Underwear tank',[(0,0,.735),(0,0,.84),(0,0,.95),(0,0,1.03)], [.299,.329,.334,.342],[.19,.205,.20,.186], 'linen','BASE',torso_weights[1:5],12,False)
for s in [-1,1]:
    # shoulder straps are slim closed strips, with front and back coverage
    verts=[(s*.14-.045,-.18,1.02),(s*.14+.045,-.18,1.02),(s*.14+.045,-.10,1.12),(s*.14-.045,-.10,1.12),(s*.14-.045,.10,1.12),(s*.14+.045,.10,1.12),(s*.14+.045,.18,1.02),(s*.14-.045,.18,1.02)]
    mesh('Tank shoulder strap',verts,[(0,1,2,3),(3,2,5,4),(4,5,6,7)],'linen','BASE',['chest']*8)
tube('Underwear waist',[(0,0,.63),(0,0,.745)],[.259,.301],[.169,.193],'linen','BASE',['pelvis']*2,12)
tube('Underwear waistband',[(0,0,.728),(0,0,.752)],[.302,.302],[.195,.195],'seam','BASE',['pelvis']*2,12,False)
for side,s in [('L',1),('R',-1)]:
    tube('Underwear short '+side,[(s*.15,0,.55),(s*.15,0,.71)],[.145,.145],[.151,.15],'linen','BASE',['thigh.'+side,{'pelvis':.8,'thigh.'+side:.2}],10)

# A removable appearance part, independent of armor and clothes.
ellipsoid('Moustache L',(.067,-.218,1.265),(.084,.035,.035),'hairlight','COSMETIC_BEARD','head',8,5)
ellipsoid('Moustache R',(-.067,-.218,1.265),(.084,.035,.035),'hairlight','COSMETIC_BEARD','head',8,5)
for i,(x,z,length,width) in enumerate([(-.155,1.245,.17,.065),(-.09,1.22,.235,.075),(0,1.21,.27,.09),(.09,1.22,.235,.075),(.155,1.245,.17,.065)]):
    tube('Beard lock '+str(i),[(x,-.15,z+.065),(x,-.215,z),(x*.75,-.22,z-length*.62),(x*.6,-.19,z-length)], [width*.9,width,width*.72,.008],[.045,.055,.045,.007], 'hairlight' if i%2 else 'hair','COSMETIC_BEARD',['head']*4,6)

# Everyday overshirt: sleeves and torso are a single skinned equipment object.
tube('Overshirt torso',[(0,0,.715),(0,0,.84),(0,0,.96),(0,0,1.075),(0,0,1.16)], [.314,.35,.352,.355,.13],[.205,.22,.218,.197,.12],'shirt','SHIRT',[{'pelvis':.8,'spine':.2},'spine',{'spine':.4,'chest':.6},'chest','chest'],12,False)
tube('Overshirt hem',[(0,0,.709),(0,0,.742)],[.317,.323],[.208,.212],'trim','SHIRT',['pelvis']*2,12,False)
for side,s in [('L',1),('R',-1)]:
    tube('Shirt sleeve '+side,[(s*.18,0,1.075),(s*.29,0,1.075),(s*.39,0,1.01),(s*.47,0,.90)],[.10,.15,.148,.12],[.14,.168,.148,.12],'shirt','SHIRT',['chest',{'chest':.35,'upper_arm.'+side:.65},'upper_arm.'+side,'upper_arm.'+side],10,True)
    tube('Shirt cuff '+side,[(s*.462,0,.91),(s*.485,0,.88)],[.123,.121],[.124,.122],'trim','SHIRT',['upper_arm.'+side]*2,10,False)
for z in [.87,.96,1.045]:
    ellipsoid('Shirt button',(0,-.222 if z<1 else -.204,z),(.012,.009,.012),'brass','SHIRT','chest' if z>.95 else 'spine',6,4)

tube('Trouser waist',[(0,0,.635),(0,0,.737)],[.276,.313],[.185,.207],'pants','TROUSERS',['pelvis']*2,12)
for side,s in [('L',1),('R',-1)]:
    th='thigh.'+side;sh='shin.'+side
    tube('Trouser leg '+side,[(s*.15,0,.66),(s*.16,0,.54),(s*.17,-.01,.43),(s*.17,-.015,.37),(s*.175,0,.28),(s*.18,0,.16)], [.155,.157,.13,.12,.126,.095],[.165,.163,.136,.122,.128,.10],'pants','TROUSERS',[th,th,{th:.8,sh:.2},{th:.5,sh:.5},sh,sh],10)
tube('Trouser belt',[(0,0,.704),(0,0,.751)],[.314,.317],[.209,.211],'darkleather','TROUSERS',['pelvis']*2,12,False)
box('Belt buckle',(0,-.216,.728),(.085,.025,.052),'brass','TROUSERS','pelvis',.008)
for side,s in [('L',1),('R',-1)]:
    tube('Boot shaft '+side,[(s*.18,0,.095),(s*.18,0,.22),(s*.178,0,.32)],[.105,.123,.128],[.11,.13,.135],'leather','BOOTS',['foot.'+side,'shin.'+side,'shin.'+side],10)
    tube('Boot cuff '+side,[(s*.178,0,.30),(s*.178,0,.34)],[.137,.137],[.143,.143],'darkleather','BOOTS',['shin.'+side]*2,10,False)
    box('Boot toe '+side,(s*.18,-.085,.085),(.231,.37,.145),'leather','BOOTS','foot.'+side,.045)
    box('Boot sole '+side,(s*.18,-.085,.032),(.24,.38,.054),'darkleather','BOOTS','foot.'+side,.018)
    box('Boot buckle '+side,(s*.18,-.14,.23),(.065,.025,.04),'brass','BOOTS','shin.'+side,.007)

# Armor is a fitted replacement torso, not a second shirt shell.
tube('Leather cuirass',[(0,0,.74),(0,0,.86),(0,0,.98),(0,0,1.08),(0,0,1.165)],[.329,.367,.369,.371,.14],[.22,.24,.235,.215,.13],'leather','ARMOR',[{'pelvis':.7,'spine':.3},'spine',{'spine':.35,'chest':.65},'chest','chest'],12,False)
tube('Cuirass lower band',[(0,0,.737),(0,0,.78)],[.333,.345],[.224,.23],'darkleather','ARMOR',['pelvis',{'pelvis':.7,'spine':.3}],12,False)
for side,s in [('L',1),('R',-1)]:
    tube('Armor sleeve '+side,[(s*.18,0,1.075),(s*.29,0,1.075),(s*.39,0,1.01),(s*.47,0,.90)],[.10,.15,.148,.12],[.14,.168,.148,.12],'leather','ARMOR',['chest',{'chest':.35,'upper_arm.'+side:.65},'upper_arm.'+side,'upper_arm.'+side],10,True)
    ellipsoid('Shoulder guard '+side,(s*.335,0,1.087),(.162,.169,.10),'metal','ARMOR','upper_arm.'+side,10,5)
    for z in [.84,.95,1.055]:
        ellipsoid('Cuirass rivet',(s*.20,-.205,z),(.014,.012,.014),'brass','ARMOR','spine' if z<.95 else 'chest',6,4)

# Helmet open at the bottom: no hidden full sphere through the face.
verts=[];faces=[];n=14
for z,rx,ry in [(1.415,.268,.225),(1.49,.265,.223),(1.61,.25,.215),(1.67,.08,.07)]:
    for i in range(n):
        a=2*math.pi*i/n;verts.append((rx*math.cos(a),ry*math.sin(a),z))
for j in range(3):
    for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
faces.append(tuple(3*n+i for i in range(n)))
mesh('Helmet shell',verts,faces,'metal','HELMET',['head']*len(verts))
tube('Helmet brow band',[(0,0,1.406),(0,0,1.445)],[.271,.271],[.23,.23],'edge','HELMET',['head']*2,14,False)
box('Helmet nasal',(0,-.237,1.39),(.038,.027,.105),'edge','HELMET','head',.009)
for s in [-1,1]:
    ellipsoid('Helmet rivet',(s*.16,-.195,1.427),(.015,.014,.015),'brass','HELMET','head',6,4)

# Handheld tools. All are authored around a common grip and exported in grip-local coordinates.
# At rest the right grip is (-.615,-.04,.585), handle points up along world Z.
grip=Vector(bones['slot_hand_r'][0])
def tool_tube(name, localpoints, widths, depths, mat, cat, n=8):
    return tube(name,[tuple(grip+Vector(p)) for p in localpoints],widths,depths,mat,cat,['hand.R']*len(localpoints),n)
tool_tube('Pick oak handle',[(0,0,-.25),(0,0,.32)],[.025,.023],[.025,.023],'wood','PICKAXE')
tool_tube('Pick grip wrap',[(0,0,-.085),(0,0,.07)],[.03,.03],[.03,.03],'darkleather','PICKAXE')
tool_tube('Pick iron head',[(-.29,0,.25),(-.18,0,.32),(0,0,.35),(.18,0,.32),(.29,0,.25)],[.008,.028,.047,.028,.008],[.009,.035,.044,.035,.009],'metal','PICKAXE')
tool_tube('Sword leather grip',[(0,0,-.12),(0,0,.075)],[.026,.026],[.023,.023],'darkleather','SWORD')
ellipsoid('Sword pommel',tuple(grip+Vector((0,0,-.13))),(.04,.035,.035),'brass','SWORD','hand.R',8,5)
box('Sword crossguard',tuple(grip+Vector((0,0,.095))),(.22,.045,.036),'brass','SWORD','hand.R',.01)
tool_tube('Sword blade',[(0,0,.12),(0,0,.43),(0,0,.58)],[.048,.039,0.001],[.015,.012,.001],'edge','SWORD',4)

# Shield center is forward of the left fist, so the fist sits behind its face.
lc=Vector(bones['slot_hand_l'][0]);center=lc+Vector((0,-.095,0))
outline=[(-.16,.19),(.16,.19),(.19,.10),(.16,-.14),(0,-.25),(-.16,-.14),(-.19,.10)]
verts=[]
for y in [-.035,.02]:
    verts.extend([tuple(center+Vector((x,y,z))) for x,z in outline])
n=len(outline);faces=[tuple(reversed(range(n))),tuple(n+i for i in range(n))]
for i in range(n):faces.append((i,(i+1)%n,(i+1)%n+n,i+n))
mesh('Shield oak panel',verts,faces,'woodlight','SHIELD',['hand.L']*len(verts))
for i in range(n):
    a=Vector(verts[i]);b=Vector(verts[(i+1)%n])
    tube('Shield iron rim',[tuple(a),tuple(b)],[.014,.014],[.014,.014],'metal','SHIELD',['hand.L']*2,6)
ellipsoid('Shield boss',tuple(center+Vector((0,-.052,0))),(.075,.04,.075),'metal','SHIELD','hand.L',10,5)
box('Shield grip',tuple(lc+Vector((0,-.008,0))),(.13,.035,.035),'darkleather','SHIELD','hand.L',.006)


for ob in list(parts['BASE']):
    if ob.name.startswith(('Underwear tank','Tank shoulder strap')):
        parts['BASE'].remove(ob);bpy.data.objects.remove(ob,do_unlink=True)

# Join by slot: many modeling pieces become a small number of runtime meshes.
joined={}
for category, objects in parts.items():
    if not objects:continue
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects:ob.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    ob=bpy.context.object;ob.name=category.lower()
    # Ensure outward-facing normals, including hand-built rings.
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
    # Joining repeats material slots; collapse identical ones without changing face assignments.
    old=list(ob.data.materials);unique=[];mapping={}
    for index,mat in enumerate(old):
        if mat not in unique:unique.append(mat)
        mapping[index]=unique.index(mat)
    assignments=[mapping[p.material_index] for p in ob.data.polygons]
    ob.data.materials.clear()
    for mat in unique:ob.data.materials.append(mat)
    for p,index in zip(ob.data.polygons,assignments):p.material_index=index
    joined[category]=ob


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

# Use face-corner vertex colors for the palette. Opaque organic and metallic surfaces
# each share one material across all characters; no per-character texture allocation.
palette_materials=[]
for name,metallic,rough in [('Dwarf palette matte',0,.8),('Dwarf palette metal',.65,.44)]:
    mat=material(name,(1,1,1),metallic,rough)
    node=mat.node_tree.nodes.new('ShaderNodeVertexColor');node.layer_name='Palette'
    mat.node_tree.links.new(node.outputs['Color'],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
    palette_materials.append(mat)
for ob in joined.values():
    old=list(ob.data.materials)
    colors=ob.data.color_attributes.new(name='Palette',type='FLOAT_COLOR',domain='CORNER')
    buckets=[];assignments=[]
    for p in ob.data.polygons:
        mat=old[p.material_index]
        for loop_index in p.loop_indices:colors.data[loop_index].color=mat.diffuse_color
        bucket=1 if mat.node_tree.nodes.get('Principled BSDF').inputs['Metallic'].default_value>.5 else 0
        if bucket not in buckets:buckets.append(bucket)
        assignments.append(buckets.index(bucket))
    ob.data.materials.clear()
    for bucket in buckets:ob.data.materials.append(palette_materials[bucket])
    for p,index in zip(ob.data.polygons,assignments):p.material_index=index

# In-place animation library. Root translation is reserved for the game's navigation.
# All clips key the same bones, so a weapon cannot inherit an old pose after a switch.
fps=24
bpy.context.scene.render.fps=fps
clips={}
def animate(name, length, fn, loop=True):
    rig.animation_data_create()
    action=bpy.data.actions.new(name)
    rig.animation_data.action=action
    frames=list(range(1,length+1))
    if length not in frames:frames.append(length)
    for frame in frames:
        # Evaluate authoring helpers without an active action. Otherwise dependency
        # graph updates inside world-space aiming reapply the first keyed pose.
        rig.animation_data.action=None
        bpy.context.scene.frame_set(frame)
        t=(frame-1)/(length-1)
        for pb in rig.pose.bones:
            pb.rotation_euler=(0,0,0);pb.location=(0,0,0);pb.scale=(1,1,1)
        fn(t)
        rig.animation_data.action=action
        for pb in rig.pose.bones:
            pb.keyframe_insert('rotation_euler',frame=frame,group=pb.name)
            if pb.name=='pelvis':pb.keyframe_insert('location',frame=frame,group=pb.name)
    # Explicit export as named NLA tracks, compatible with glTF and Godot.
    track=rig.animation_data.nla_tracks.new();track.name=name
    strip=track.strips.new(name,1,action);strip.action_frame_start=1;strip.action_frame_end=length
    track.mute=True
    clips[name]={'frames':length,'seconds':(length-1)/fps,'loop':loop}
    rig.animation_data.action=None

def rot(name,x=0,y=0,z=0):rig.pose.bones[name].rotation_euler=tuple(math.radians(v) for v in (x,y,z))
def reset_pose():
    for pb in rig.pose.bones:
        pb.rotation_euler=(0,0,0);pb.location=(0,0,0);pb.scale=(1,1,1)
def world_rotation(name, desired):
    pb=rig.pose.bones[name]
    rest=armdata.bones[name].matrix_local.to_quaternion()
    parent_delta=Quaternion()
    if pb.parent:
        parent_delta=pb.parent.matrix.to_quaternion() @ armdata.bones[pb.parent.name].matrix_local.to_quaternion().inverted()
    pb.rotation_euler=((parent_delta @ rest).inverted() @ desired).to_euler('XYZ')
    bpy.context.view_layer.update()
def aim(name, target):
    pb=rig.pose.bones[name]
    rest=armdata.bones[name]
    direction=(warp(target)-pb.head).normalized()
    swing=(rest.tail_local-rest.head_local).normalized().rotation_difference(direction)
    world_rotation(name,swing @ rest.matrix_local.to_quaternion())
def idle(t):
    a=math.sin(t*2*math.pi)
    rot('chest',x=a*1.6);rot('head',z=a*1.5)
    for side,s in [('L',1),('R',-1)]:rot('upper_arm.'+side,z=s*17);rot('forearm.'+side,x=-6)
def walk(t):
    a=math.sin(t*2*math.pi)
    for side,s in [('L',1),('R',-1)]:
        rot('thigh.'+side,x=a*s*27)
        rot('shin.'+side,x=-max(0,a*s)*36)
        rot('foot.'+side,x=max(0,a*s)*10)
        rot('upper_arm.'+side,x=-a*s*23,z=s*17)
        rot('forearm.'+side,x=-10-max(0,-a*s)*13)
    rig.pose.bones['pelvis'].location.z=.014*(1-math.cos(t*4*math.pi))
    rot('chest',y=a*3)
def mine(t):
    # One-handed overhead swing with a clear upstroke, impact and recovery.
    keys=[(0,-28),(.32,-145),(.44,-150),(.62,-38),(.73,-20),(1,-28)]
    angle=keys[-1][1]
    for (a,x),(b,y) in zip(keys,keys[1:]):
        if a<=t<=b:
            u=(t-a)/(b-a);u=u*u*(3-2*u);angle=x+(y-x)*u;break
    lift=max(0,min(1,(-angle-28)/117))
    # Aim the whole arm outside the head's silhouette. Authoring in global space
    # avoids relying on the mirrored upper-arm bone's local Euler axes.
    elbow=Vector((-.50,-.20,1.00)).lerp(Vector((-.55,-.02,1.28)),lift)
    wrist=Vector((-.48,-.43,.88)).lerp(Vector((-.53,-.10,1.50)),lift)
    rot('upper_arm.L',x=-12,z=22);rot('forearm.L',x=-28)
    rot('chest',x=10*math.sin(t*2*math.pi),y=-5)
    bpy.context.view_layer.update()
    aim('upper_arm.R',elbow);aim('forearm.R',wrist)
    world_rotation('hand.R',Quaternion((1,0,0),math.radians(72*(1-lift))) @ armdata.bones['hand.R'].matrix_local.to_quaternion())
def attack(t):
    stages=[(0,0),(.3,1),(.4,1),(.66,-.3),(1,0)]
    a=0
    for (ta,va),(tb,vb) in zip(stages,stages[1:]):
        if ta<=t<=tb:
            u=(t-ta)/(tb-ta);u=u*u*(3-2*u);a=va+(vb-va)*u;break
    lift=max(0,a)
    elbow=Vector((-.50,-.20,1.03)).lerp(Vector((-.55,-.02,1.28)),lift)
    wrist=Vector((-.49,-.43,1.0)).lerp(Vector((-.53,-.10,1.50)),lift)
    rot('chest',y=10*math.sin(2*math.pi*t),x=4*(1-lift))
    rot('upper_arm.L',x=-28,z=20);rot('forearm.L',x=-65)
    bpy.context.view_layer.update()
    aim('upper_arm.R',elbow);aim('forearm.R',wrist)
    world_rotation('hand.R',Quaternion((1,0,0),math.radians(75*(1-a))) @ armdata.bones['hand.R'].matrix_local.to_quaternion())
def carry(t):
    walk(t)
    for side,s in [('L',1),('R',-1)]:
        rot('upper_arm.'+side,x=-48,z=s*12)
        rot('forearm.'+side,x=-55)

animate('Idle',49,idle)
animate('Walk',25,walk)
animate('Mine',37,mine)
animate('Attack',25,attack,False)
animate('Carry',25,carry)
rig.animation_data.action=None
for pb in rig.pose.bones:pb.rotation_euler=(0,0,0);pb.location=(0,0,0)

def select(obs):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=rig if rig in obs else obs[0]

def export(path, obs, animations=False, yup=True):
    select(obs)
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,
        export_animations=animations,export_animation_mode='NLA_TRACKS',
        export_nla_strips=True,export_force_sampling=True,export_frame_range=False,
        export_anim_slide_to_zero=True,export_optimize_animation_size=False,
        export_def_bones=False,export_skins=True,export_yup=yup,
        export_apply=False,export_extras=True)

for track in rig.animation_data.nla_tracks:track.mute=False
export(OUT/'dwarf_base.glb',[rig,joined['BASE']],True)
for track in rig.animation_data.nla_tracks:track.mute=True
exports={}
for category,ob in joined.items():
    if category=='BASE':continue
    filename=category.lower()+'.glb'
    if category in ['PICKAXE','SWORD','SHIELD']:
        side='l' if category=='SHIELD' else 'r'
        socket='slot_hand_'+side
        # Remove skin and bring the mesh into the socket's local coordinate system.
        # At runtime attach this static mesh to the named skeleton bone.
        temp=ob.copy();temp.data=ob.data.copy();bpy.context.scene.collection.objects.link(temp)
        temp.parent=None;temp.modifiers.clear();temp.vertex_groups.clear()
        inverse=armdata.bones[socket].matrix_local.inverted()
        temp.data.transform(inverse)
        # Bone-local coordinates keep the bone's authored axes. They must not
        # receive the world-space Z-up to Y-up conversion a second time.
        export(OUT/filename,[temp],yup=False);bpy.data.objects.remove(temp,do_unlink=True)
        exports[category.lower()]={'file':filename,'mode':'attachment','socket':socket}
    else:
        export(OUT/filename,[rig,ob])
        exports[category.lower()]={'file':filename,'mode':'skinned','skeleton':'DwarfRig/Skeleton3D'}

# Body masks for replacement clothing. These keep the undergarments in the naked base.
# Separate body vertices carry region IDs here via the known vertical/horizontal partition.
# The masked alternatives are actual meshes, avoiding reliance on per-pixel alpha or shaders.
base=joined['BASE']
def masked_base(name, shirt=False, pants=False, boots=False):
    copy=base.copy();copy.data=base.data.copy();collections['BASE'].objects.link(copy)
    import bmesh
    bm=bmesh.new();bm.from_mesh(copy.data)
    remove=[]
    for face in bm.faces:
        c=unwarp(face.calc_center_median())
        # Hide only regions completely covered by the matching clothing. Avoid head,
        # hands and neck. Shirt includes sleeves; armor has its own larger torso shell.
        torso=abs(c.x)<.34 and .735<c.z<1.115
        sleeve=.14<abs(c.x)<.51 and .905<c.z<1.24
        lower=abs(c.x)<.325 and .155<c.z<.735
        foot=abs(c.x)<.33 and c.z<.325
        if (shirt and (torso or sleeve)) or (pants and lower) or (boots and foot):remove.append(face)
    bmesh.ops.delete(bm,geom=remove,context='FACES')
    bm.to_mesh(copy.data);bm.free()
    copy.name=name
    export(OUT/(name+'.glb'),[rig,copy],False)
    bpy.data.objects.remove(copy,do_unlink=True)
masked_base('body_clothed',True,True,True)
# Equip any individual slot without erasing unrelated skin.
for shirt in [False,True]:
    for pants in [False,True]:
        for boots in [False,True]:
            if not (shirt or pants or boots):continue
            masked_base('body_mask_'+''.join('1' if x else '0' for x in [shirt,pants,boots]),shirt,pants,boots)

stats={}
for category,ob in joined.items():
    ob.data.calc_loop_triangles()
    stats[category.lower()]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles),'materials':len(ob.data.materials)}
manifest={'version':1,'units':'meters','height_m':1.4844,'body_height_m':1.4144,
    'forward_godot':'+Z','blender_forward':'-Y','fps':fps,'bones':list(bones),
    'animations':clips,'equipment':exports,'geometry':stats,
    'body_masks':{'bits':['shirt_or_armor','trousers','boots'],'files':'body_mask_ABC.glb; 1 = hide covered body region'},
    'presets':{'base':['cosmetic_beard'],'casual':['cosmetic_beard','shirt','trousers','boots'],
    'miner':['cosmetic_beard','shirt','trousers','boots','helmet','pickaxe'],
    'guard':['cosmetic_beard','armor','trousers','boots','helmet','sword','shield']}}
reference_poses=[]
for name,func,t in [('Walk',walk,.25),('Mine',mine,.36),('Mine',mine,.65),('Attack',attack,.48),('Carry',carry,.2)]:
    # Compare actual authored frame times; continuous in-between motion can differ
    # slightly between Blender's Bezier curves and glTF quaternion interpolation.
    t=round(t*(clips[name]['frames']-1))/(clips[name]['frames']-1)
    rig.animation_data.action=None;reset_pose();func(t);bpy.context.view_layer.update()
    positions={}
    for key in ['hand.R','forearm.R','head','thigh.L','slot_hand_r']:
        p=rig.pose.bones[key].matrix.translation
        positions[key]=[p.x,p.z,-p.y]
    ref={'clip':name,'fraction':t,'positions':positions}
    if name in ['Mine','Attack']:
        tip=handheld_point(grip+Vector((0,0,.35 if name=='Mine' else .58)))
        local=armdata.bones['slot_hand_r'].matrix_local.inverted() @ tip
        world=rig.pose.bones['hand.R'].matrix @ armdata.bones['hand.R'].matrix_local.inverted() @ tip
        ref['tip_local']=list(local);ref['tip_expected']=[world.x,world.z,-world.y]
    reference_poses.append(ref)
manifest['reference_poses']=reference_poses
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')

# Save an editable asset file in its base-body state with outfits available in collections.
for category,ob in joined.items():
    ob.hide_render=category not in ['BASE','COSMETIC_BEARD']
    ob.hide_set(category not in ['BASE','COSMETIC_BEARD'])
rig.hide_render=True
select([rig]);rig.hide_set(False)
bpy.context.scene.frame_start=1;bpy.context.scene.frame_end=49
reset_pose();idle(0)
rig.animation_data.action=bpy.data.actions['Idle']
# Set an uncluttered material-colored viewport when opening the source file.
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.type='MATERIAL'
            area.spaces.active.region_3d.view_distance=3.6
            area.spaces.active.region_3d.view_location=(0,0,.85)
            area.spaces.active.region_3d.view_rotation=(Vector((0,0,.85))-Vector((2.8,-5.5,2.7))).to_track_quat('-Z','Y')
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'dwarf.blend'))
rig.animation_data.action=None
if '--skip-render' in sys.argv:
    print('DWARF_BUILD_COMPLETE '+json.dumps(stats))
    sys.exit(0)

# Studio contact sheet. Keep stage, camera and lights out of the exported resources.
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.samples=24
scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world.color=(.18,.18,.18)
scene.view_settings.view_transform='AgX'
def camera(location,target,scale):
    bpy.ops.object.camera_add(location=location)
    ob=bpy.context.object;ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
    ob.data.type='ORTHO';ob.data.ortho_scale=scale;scene.camera=ob
    return ob
def light(name,loc,energy,size):
    bpy.ops.object.light_add(type='AREA',location=loc)
    ob=bpy.context.object;ob.name=name;ob.data.energy=energy;ob.data.shape='DISK';ob.data.size=size
    ob.rotation_euler=(Vector((0,0,.8))-ob.location).to_track_quat('-Z','Y').to_euler()
light('Large warm key',(-3,-4,6),550,5)
light('Soft fill',(4,-2,3),300,4)
light('Rim',(0,3,4),650,3)
floor=material('Studio floor',(.075,.095,.10))
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.004));bpy.context.object.data.materials.append(floor)
cam=camera((2.8,-5.5,2.7),(0,0,.8),2.2)

# Blender renders verify the authored skinning. Godot verifies the exported skinning separately.
def show_preset(preset):
    visible={'BASE',*map(str.upper,manifest['presets'][preset])}
    for category,ob in joined.items():
        ob.hide_render=category not in visible
    return visible
for preset in manifest['presets']:
    show_preset(preset)
    reset_pose();idle(0);bpy.context.view_layer.update()
    scene.render.resolution_x=800;scene.render.resolution_y=900
    scene.render.filepath=str(HERE/(preset+'.png'))
    None

# Front/side/back and action poses are separate, inspectable captures.
show_preset('miner')
for label,func,t in [('walk',walk,.22),('mine_raise',mine,.36),('mine_strike',mine,.65),('carry',carry,.2)]:
    reset_pose();func(t);bpy.context.view_layer.update()
    scene.render.filepath=str(HERE/(label+'.png'));None
show_preset('guard');reset_pose();attack(.48);bpy.context.view_layer.update()
scene.render.filepath=str(HERE/'attack.png');None
# Keep the editable source ready both for viewport playback and for F12 rendering.
show_preset('base');reset_pose();idle(0)
rig.animation_data.action=bpy.data.actions['Idle'];scene.frame_set(1)
# Stage objects are useful for rendering but shouldn't obscure the modeling viewport.
for ob in [cam]+[ob for ob in scene.objects if ob.type=='LIGHT' or (ob.type=='MESH' and ob.name not in [o.name for o in joined.values()])]:
    for c in list(ob.users_collection):c.objects.unlink(ob)
    collections['PRESENTATION'].objects.link(ob);ob.hide_set(True)
for category,ob in joined.items():ob.hide_set(category not in ['BASE','COSMETIC_BEARD'])
select([rig]);bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'dwarf.blend'))
print('DWARF_BUILD_COMPLETE '+json.dumps(stats))
