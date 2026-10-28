"""Reference-led medium-poly dwarf. Blender 5.x, no downloaded assets.
Preserves V2 rig rest / animation contract; replaces all visible anatomy.
"""
import bpy, math, json, sys, random, importlib.util
from pathlib import Path
from mathutils import Vector
R=Path(__file__).resolve().parents[3]
SEX='female' if '--female' in sys.argv else 'male'
OUT=R/'art/dwarf/v3'/SEX; OUT.mkdir(parents=True,exist_ok=True)
GAME=R/'source/Assets/Characters/DwarfV3'/SEX; GAME.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(R/'art/dwarf/sword_shield'/f'{SEX}.blend'))
rig=bpy.data.objects['DwarfRig'];rig.animation_data.action=None
for tr in rig.animation_data.nla_tracks:tr.mute=True
for b in rig.pose.bones:b.matrix_basis.identity()
for ob in list(bpy.data.objects):
    if ob!=rig:bpy.data.objects.remove(ob,do_unlink=True)
bpy.context.view_layer.update()
random.seed(27)
parts={}
def mat(name,c,rough=.65,metal=0):
    m=bpy.data.materials.new('V3 '+name);m.diffuse_color=(*c,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1)
    p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
    return m
skin=mat('warm skin',(.57,.292,.16));rose=mat('nose ears lips',(.55,.225,.14))
crease=mat('warm creases',(.24,.105,.065));linen=mat('ivory cotton',(.79,.735,.60),.9)
seam=mat('cotton piping',(.65,.58,.43),.9);white=mat('warm sclera',(.69,.64,.49))
iris=mat('hazel iris',(.105,.073,.035));pupil=mat('pupil',(.012,.008,.006),.3)
hairm=[mat('hair '+str(i),c) for i,c in enumerate([(.075,.054,.039),(.105,.082,.06),(.15,.128,.102),(.22,.205,.18)])]
beardm=[mat('salt pepper '+str(i),c) for i,c in enumerate([(.11,.10,.086),(.19,.18,.16),(.27,.26,.23)])]
def register(ob,material,category):
    ob.data.materials.append(material);ob['category']=category;parts.setdefault(category,[]).append(ob)
    for p in ob.data.polygons:p.use_smooth=True
    return ob
def activate(ob):
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
def bind(ob,weights):
    groups={}
    for v in ob.data.vertices:
        w=weights(v.co) if callable(weights) else weights
        if isinstance(w,str):w={w:1}
        for name,value in w.items():
            if value<=0:continue
            if name not in groups:groups[name]=ob.vertex_groups.new(name=name)
            groups[name].add([v.index],value,'REPLACE')
    ob.parent=rig;mod=ob.modifiers.new('Shared skeleton','ARMATURE');mod.object=rig
    return ob
def ell(name,c,s,material=skin,cat='skin_raw',bone=None,seg=24,rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg,ring_count=rings,location=c)
    ob=bpy.context.object;ob.name=name;ob.scale=s;bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    register(ob,material,cat)
    if bone:bind(ob,bone)
    return ob
def mesh(name,v,f,material,cat,bone=None):
    d=bpy.data.meshes.new(name);d.from_pydata(v,[],f);d.update();o=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(o)
    register(o,material,cat)
    if bone:bind(o,bone)
    return o
def tube(name,points,width,depth,material=skin,cat='skin_raw',bone=None,n=16,sub=1,groove=False):
    vs=[];fs=[]
    for j,p in enumerate(points):
        p=Vector(p);t=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(j-1,0)])
        t.normalize();axis=Vector((0,1,0))
        if abs(t.dot(axis))>.95:axis=Vector((1,0,0))
        a=axis.cross(t).normalized();b=t.cross(a).normalized()
        for i in range(n):
            ang=math.tau*i/n;g=1+(.075*math.cos(ang*5) if groove else 0)
            vs.append(p+a*(math.cos(ang)*width[j]*g)+b*(math.sin(ang)*depth[j]*g))
    for j in range(len(points)-1):
        for i in range(n):a=j*n+i;b=j*n+(i+1)%n;fs.append((a,b,b+n,a+n))
    fs.extend([tuple(reversed(range(n))),tuple((len(points)-1)*n+i for i in range(n))])
    ob=mesh(name,vs,fs,material,cat)
    if sub:
        activate(ob);m=ob.modifiers.new('Sculpted contour','SUBSURF');m.levels=sub;bpy.ops.object.modifier_apply(modifier=m.name)
    if bone:bind(ob,bone)
    return ob
def loft(name,rings,material=skin,cat='skin_raw',bone=None,n=32):
    # horizontal cross-sections with a broad, slightly squared front plane
    v=[];f=[]
    for z,rx,ry,cy in rings:
        for i in range(n):
            a=math.tau*i/n;x=math.cos(a);y=math.sin(a)
            v.append((rx*math.copysign(abs(x)**.92,x),cy+ry*math.copysign(abs(y)**.92,y),z))
    for j in range(len(rings)-1):
        for i in range(n):a=j*n+i;b=j*n+(i+1)%n;f.append((a,b,b+n,a+n))
    f.extend([tuple(reversed(range(n))),tuple((len(rings)-1)*n+i for i in range(n))])
    ob=mesh(name,v,f,material,cat);activate(ob)
    m=ob.modifiers.new('Soft anatomical planes','SUBSURF');m.levels=2;bpy.ops.object.modifier_apply(modifier=m.name)
    if bone:bind(ob,bone)
    return ob
def blend(a,b,t):
    t=max(0,min(1,t));return {a:1-t,b:t}
def torso_w(p):
    if p.z<.58:return 'pelvis'
    if p.z<.72:return blend('pelvis','spine',(p.z-.58)/.14)
    if p.z<.88:return blend('spine','chest',(p.z-.72)/.16)
    return 'chest'
def skin_w(p):
    x,y,z=p;side='L' if x>0 else 'R';ax=abs(x)
    if z>1.07:return blend('neck','head',(z-1.07)/.045)
    if z>1.00 and ax<.14:return blend('chest','neck',(z-1.0)/.07)
    if ax>.29 and z>.30:
        # Continuous nearest-segment weights; x-only thresholds cross the wrong joint.
        names=['upper_arm.'+side,'forearm.'+side,'hand.'+side]
        scores={}
        for name in names:
            b=rig.data.bones[name];a=b.head_local;v=b.tail_local-a
            t=max(0,min(1,(p-a).dot(v)/v.length_squared))
            d=(p-(a+v*t)).length
            scores[name]=1/(d*d+.0012)**2
        total=sum(scores.values());w={k:v/total for k,v in scores.items()}
        mix=max(0,min(1,(ax-.29)/.10))
        w={k:v*mix for k,v in w.items()}
        tw=torso_w(p);tw={tw:1} if isinstance(tw,str) else tw
        for k,v in tw.items():w[k]=w.get(k,0)+v*(1-mix)
        return w
    if z<.48:
        if z<.12:return 'foot.'+side
        if z<.18:return blend('foot.'+side,'shin.'+side,(z-.12)/.06)
        if z<.26:return 'shin.'+side
        if z<.32:return blend('shin.'+side,'thigh.'+side,(z-.26)/.06)
        return 'thigh.'+side
    return torso_w(p)

# Continuous body volume; muscles share a surface rather than visible spheres.
loft('Trunk',[(.43,.17,.12,0),(.49,.225,.15,0),(.56,.25,.175,-.012),(.65,.265,.205,-.025),(.76,.27,.19,-.005),(.88,.30,.165,0),(.96,.29,.145,0),(1.01,.20,.12,0),(1.055,.105,.09,0)])
ell('Neck',(0,.005,1.07),(.105,.098,.115))
for side,s in [('L',1),('R',-1)]:
    tube('Shoulder upper arm '+side,[(s*.245,0,.965),(s*.35,0,.92),(s*.445,0,.80),(s*.51,0,.68),(s*.56,-.01,.61),(s*.66,-.017,.49)], [.10,.108,.102,.078,.093,.062],[.12,.121,.11,.082,.095,.06],sub=2)
    ell('Pectoral '+side,(s*.145,-.105,.905),(.15,.040,.075))
    tube('Leg '+side,[(s*.16,0,.50),(s*.18,.01,.425),(s*.187,-.006,.315),(s*.187,-.012,.285),(s*.195,.016,.23),(s*.198,.01,.16),(s*.198,0,.095)],[.12,.124,.095,.09,.11,.079,.065],[.13,.134,.10,.10,.115,.09,.067],sub=2)
    ell('Kneecap '+side,(s*.187,-.08,.293),(.066,.032,.056))
    ell('Heel and instep '+side,(s*.198,-.035,.07),(.10,.145,.073))
    ell('Forefoot '+side,(s*.198,-.13,.044),(.11,.085,.042))
    for i in range(5):
        x=s*(.132+i*.033);ell('Toe '+side+str(i),(x,-.214+i*.007,.037),(.018-i*.001,.043-i*.004,.026-i*.002),seg=16,rings=10)
    # Palm and individually readable, gently curled fingers at the rest socket.
    ell('Palm '+side,(s*.69,-.023,.445),(.069,.051,.070))
    for i in range(4):
        x=s*(.644+i*.029);z=.407+(abs(i-1.4)*.008)
        tube('Finger '+side+str(i),[(x,-.025,z+.025),(x+s*.006,-.04,z-.02),(x+s*.004,-.063,z-.045),(x,-.077,z-.039)],[.014,.014,.011,.007],[.016,.015,.012,.007],sub=1)
    tube('Thumb '+side,[(s*.646,-.015,.473),(s*.617,-.044,.449),(s*.61,-.073,.418),(s*.622,-.085,.407)],[.029,.027,.022,.011],[.026,.025,.02,.01],sub=1)

# Head: jaw, cheek planes, temples, brow ridge and nose blended as anatomy.
loft('Head',[(1.045,.055,.058,-.025),(1.07,.10,.09,-.027),(1.115,.137,.115,-.013),(1.16,.157,.127,0),(1.205,.16,.123,.008),(1.255,.151,.12,.014),(1.30,.15,.118,.018),(1.34,.122,.10,.023),(1.365,.065,.065,.025)])
ell('Chin',(0,-.105,1.088),(.086,.044,.042))
for s in [-1,1]:
    ell('Cheek plane',(s*.105,-.095,1.175),(.064,.046,.054))
    ell('Brow bone',(s*.065,-.099,1.26),(.070,.035,.029))
    ell('Outer ear',(s*.167,.005,1.222),(.04,.035,.062))
    ell('Nose wing',(s*.032,-.16,1.195),(.031,.030,.023))
ell('Nose bridge',(0,-.12,1.236),(.034,.038,.062))
ell('Nose tip',(0,-.177,1.204),(.049 if SEX=='male' else .041,.051,.041 if SEX=='male' else .034))
ell('Muzzle',(0,-.115,1.139),(.069,.033,.034))

raw=parts.pop('skin_raw');activate(raw[0])
for o in raw:o.select_set(True)
bpy.ops.object.join();body=bpy.context.object;body.name='Body continuous anatomy';body['category']='base'
rem=body.modifiers.new('Union sculpted anatomical volumes','REMESH');rem.mode='VOXEL';rem.voxel_size=.0065;rem.use_smooth_shade=True
bpy.ops.object.modifier_apply(modifier=rem.name)
sm=body.modifiers.new('Relax voxel surface','SMOOTH');sm.factor=.65;sm.iterations=5;bpy.ops.object.modifier_apply(modifier=sm.name)
de=body.modifiers.new('Game topology reduction','DECIMATE');de.ratio=.17;bpy.ops.object.modifier_apply(modifier=de.name)
parts.setdefault('base',[]).append(body);bind(body,skin_w)

# Eyes sit inside the brow / cheek planes. Upper lids partially cover sclera.
for s in [-1,1]:
    ell('Eye socket shade',(s*.066,-.119,1.238),(.032,.008,.017),crease,'base','head',24,12)
    ell('Eye',(s*.066,-.126,1.239),(.024,.008,.011),white,'base','head',24,12)
    ell('Iris',(s*.064,-.134,1.239),(.009,.003,.010),iris,'base','head',20,10)
    ell('Pupil',(s*.064,-.137,1.239),(.0045,.002,.006),pupil,'base','head',16,8)
    ell('Eye glint',(s*.064-.003,-.139,1.242),(.002,.001,.002),white,'base','head',8,6)
    for upper in [True,False]:
        pts=[]
        for i in range(7):
            a=math.pi*i/6;pts.append((s*.066-.026*math.cos(a),-.130-.002*math.sin(a),1.239+(1 if upper else -1)*.011*math.sin(a)))
        tube('Upper eyelid' if upper else 'Lower eyelid',pts,[.003]*7,[.003]*7,skin,'base','head',8,1)
    tube('Heavy expressive brow',[(s*.025,-.129,1.269),(s*.052,-.139,1.275),(s*.082,-.13,1.273),(s*.109,-.111,1.262)],[.009,.012,.011,.003],[.008,.009,.008,.002],hairm[1],'hair','head',10,1)
    ell('Ear concha',(s*.178,-.022,1.223),(.021,.013,.038),rose,'base','head',20,12)
    ell('Ear inner shadow',(s*.178,-.034,1.229),(.011,.004,.023),crease,'base','head',16,10)
    ell('Ear tragus',(s*.16,-.036,1.215),(.011,.010,.018),skin,'base','head',16,10)
    ell('Nostril',(s*.026,-.193,1.182),(.010,.004,.005),crease,'base','head',16,8)
    tube('Smile fold',[(s*.043,-.151,1.171),(s*.061,-.141,1.151),(s*.065,-.135,1.130)],[.002,.003,.001],[.002,.002,.001],rose,'base','head',8,1)
tube('Mouth line',[(-.055,-.142,1.126),(-.027,-.153,1.129),(0,-.156,1.127),(.027,-.153,1.129),(.055,-.142,1.126)],[.002,.003,.003,.003,.002],[.002]*5,crease,'base','head',8,1)
tube('Lower lip',[(-.048,-.142,1.122),(0,-.153,1.117),(.048,-.142,1.122)],[.002,.008,.002],[.002,.005,.002],rose,'base','head',10,2)

# Underwear remains opaque and part of the base; waistband and hems are geometry.
loft('Underwear hip',[(.435,.207,.14,0),(.48,.236,.162,-.003),(.55,.26,.183,-.014),(.59,.265,.19,-.02)],linen,'shorts_raw',torso_w)
loft('Waist stitched band',[(.572,.266,.192,-.02),(.585,.269,.194,-.02),(.599,.266,.192,-.02)],seam,'base',torso_w)
for side,s in [('L',1),('R',-1)]:
    tube('Boxer leg '+side,[(s*.17,0,.52),(s*.175,0,.50),(s*.18,0,.45),(s*.18,0,.404),(s*.18,0,.400)],[.145,.146,.14,.137,.137],[.16,.16,.153,.151,.151],linen,'shorts_raw',lambda p,side=side:blend('thigh.'+side,'pelvis',(p.z-.43)/.10),24,2)
    tube('Boxer hem '+side,[(s*.18,0,.409),(s*.18,0,.419)],[.14,.141],[.154,.156],seam,'base','thigh.'+side,24,1)
# Fuse the boxer waist and legs into one continuous cloth shell.
raw=parts.pop('shorts_raw');activate(raw[0])
for ob in raw:
    for mod in list(ob.modifiers):ob.modifiers.remove(mod)
    ob.select_set(True)
bpy.ops.object.join();shorts=bpy.context.object;shorts.name='Boxers continuous cloth'
rem=shorts.modifiers.new('Seamless cloth junction','REMESH');rem.mode='VOXEL';rem.voxel_size=.006
bpy.ops.object.modifier_apply(modifier=rem.name)
sm=shorts.modifiers.new('Cloth relaxation','SMOOTH');sm.factor=.6;sm.iterations=3;bpy.ops.object.modifier_apply(modifier=sm.name)
de=shorts.modifiers.new('Cloth topology','DECIMATE');de.ratio=.22;bpy.ops.object.modifier_apply(modifier=de.name)
shorts.vertex_groups.clear();bind(shorts,lambda p: torso_w(p) if p.z>.52 else blend('thigh.'+('L' if p.x>0 else 'R'),'pelvis',(p.z-.43)/.10))
shorts['category']='base';parts['base'].append(shorts)
if SEX=='female':
    loft('Cotton undershirt',[(.587,.271,.201,-.022),(.65,.276,.214,-.026),(.76,.282,.202,-.012),(.85,.302,.195,-.023),(.90,.296,.183,-.022),(.925,.275,.166,-.016)],linen,'base',torso_w)
    for s in [-1,1]:
        # Flat tailored ribbon: constant X width avoids the flipping tube frame at the shoulder.
        path=[(s*.20,-.160,.91),(s*.21,-.143,.94),(s*.21,-.103,.986),(s*.19,-.045,1.023),(s*.18,.02,1.029),(s*.20,.09,.994),(s*.21,.132,.945),(s*.20,.145,.91)]
        v=[];f=[]
        for x,y,z in path:v.extend([(x-.037,y,z),(x+.037,y,z)])
        for i in range(len(path)-1):f.append((i*2,i*2+1,i*2+3,i*2+2))
        strap=mesh('Tank strap',v,f,linen,'base');activate(strap)
        mod=strap.modifiers.new('Tailored strap','SUBSURF');mod.levels=2;bpy.ops.object.modifier_apply(modifier=mod.name)
        mod=strap.modifiers.new('Cotton thickness','SOLIDIFY');mod.thickness=.004;bpy.ops.object.modifier_apply(modifier=mod.name)
        bind(strap,'chest')

# Hair is modeled in layered tapered locks, with lengthwise fluting.
ell('Hair cap',(0,.025,1.304),(.16,.137,.086),hairm[0],'hair','head',32,20)
def lock(name,pts,w,material,cat='hair'):
    ob=tube(name,pts,[w*.45,w,w*.83,w*.5,.002],[w*.25,w*.45,w*.43,w*.25,.001],material,cat,'head',10,1,True)
    # Fine dark channels on forward-facing locks retain strand direction at medium zoom.
    if 'fringe' in name or cat=='beard':
        for off in [-.24,.24]:
            pp=[(x+w*off,y-w*.4,z) for x,y,z in pts]
            tube(name+' strand',pp,[.0007,.0013,.0012,.0009,.0003],[.0005,.0008,.0008,.0005,.0002],hairm[0],cat,'head',6,0)
    return ob
for layer in range(3):
    count=14 if layer<2 else 11
    for i in range(count):
        a=math.pi*i/(count-1) # side-to-side through the back (+Y)
        x=math.cos(a);y=math.sin(a);z=1.34-layer*.075
        pts=[(.12*x,.025+.09*y,z+.025),(.153*x,.025+.13*y,z),(.17*x,.035+.145*y,z-.04),(.165*x,.045+.145*y,z-.077),(.146*x,.055+.137*y,z-.09)]
        lock('Layered nape lock',pts,.033,hairm[(i+layer)%3])
for i in range(11):
    x=-.145+i*.027
    pts=[(x*.35+.025,.035,1.379),(x*.75+.015,-.025,1.378),(x,-.088,1.354),(x-.025,-.12,1.319),(x-.039,-.122,1.29+abs(x)*.12)]
    lock('Swept fringe',pts,.031,hairm[(i+1)%3])
if SEX=='male':
    for i in range(13):
        x=-.144+i*.024;side=abs(x)/.144;z=1.155+side*.035
        pts=[(x,-.10,z),(x,-.14,z-.035),(x*.9,-.181,z-.086),(x*.7,-.184,1.005+side*.035),(x*.57,-.16,.977+side*.06)]
        lock('Beard layered lock',pts,.031,beardm[i%3],'beard')
    for s in [-1,1]:
        for i in range(3):
            lock('Swept moustache',[(s*.006,-.185,1.172-i*.004),(s*.033,-.190,1.158-i*.006),(s*.064,-.183,1.149-i*.006),(s*.094,-.166,1.153-i*.006),(s*.117,-.146,1.166-i*.005)],.012,beardm[1+i%2],'beard')

# Optional outfits are built against this silhouette and the same rig.
gp=R/'art/dwarf/v3/garments.py'
if gp.exists():
    spec=importlib.util.spec_from_file_location('garments',gp);g=importlib.util.module_from_spec(spec);spec.loader.exec_module(g)
    for cat,obs in g.build(rig).items():
        for ob in obs:ob['category']=cat
        parts.setdefault(cat,[]).extend(obs)

tp=R/'art/dwarf/v3/build_tools.py'
if tp.exists():
    spec=importlib.util.spec_from_file_location('tools_v3',tp);t=importlib.util.module_from_spec(spec);spec.loader.exec_module(t)
    for cat,obs in t.build(rig).items():
        for ob in obs:ob['category']=cat
        parts.setdefault(cat,[]).extend(obs)

# Reduce draw calls: one mesh per slot, retain a small color palette as vertex colors.
palette=mat('shared vertex palette',(.8,.8,.8))
nt=palette.node_tree;p=nt.nodes.get('Principled BSDF');vc=nt.nodes.new('ShaderNodeVertexColor');vc.layer_name='Palette';nt.links.new(vc.outputs['Color'],p.inputs['Base Color'])
for cat,obs in list(parts.items()):
    if not obs:continue
    for ob in obs:
        if len(ob.data.polygons)>500 and ob!=body:
            activate(ob);de=ob.modifiers.new('Game mesh detail budget','DECIMATE');de.ratio=.48;bpy.ops.object.modifier_apply(modifier=de.name)
        attr=ob.data.color_attributes.new(name='Palette',type='FLOAT_COLOR',domain='CORNER')
        for poly in ob.data.polygons:
            c=ob.data.materials[poly.material_index].diffuse_color
            for li in poly.loop_indices:attr.data[li].color=c
        ob.data.materials.clear();ob.data.materials.append(palette)
        for poly in ob.data.polygons:poly.material_index=0
    activate(obs[0])
    for ob in obs:ob.select_set(True)
    bpy.ops.object.join();ob=bpy.context.object;ob.name=cat;ob['category']=cat;parts[cat]=[ob]
    if cat in ('base','hair','beard'):
        de=ob.modifiers.new('Final game triangle budget','DECIMATE');de.ratio=.50 if cat=='base' else .43;bpy.ops.object.modifier_apply(modifier=de.name)
    ob.hide_render=cat not in ('base','hair','beard');ob.hide_set(ob.hide_render)

# Clothing masks remove covered skin rather than relying on coincident shells.
import bmesh
base=parts['base'][0];masked=base.copy();masked.data=base.data.copy();bpy.context.collection.objects.link(masked)
masked.name='base_clothed';masked['category']='base_clothed'
bm=bmesh.new();bm.from_mesh(masked.data)
covered=[]
for face in bm.faces:
    p=face.calc_center_median();x=abs(p.x);z=p.z
    if (z<.59 and x<.36) or (.58<=z<1.045 and x<.30) or (.72<z<1.05 and .30<=x<.55):covered.append(face)
bmesh.ops.delete(bm,geom=covered,context='FACES');bm.to_mesh(masked.data);bm.free()
masked.hide_render=True;masked.hide_set(True);parts['base_clothed']=[masked]

# Studio is deliberately neutral so shapes can be judged against the supplied sheets.
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=32
scene.cycles.use_denoising=True;scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world.color=(.25,.25,.25);scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
def track(ob,at):ob.rotation_euler=(Vector(at)-ob.location).to_track_quat('-Z','Y').to_euler()
for name,loc,power,size in [('Key',(-3,-4,5),450,4),('Fill',(3,-2,3),240,3),('Rim',(0,3,4),400,3)]:
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.name=name;o.data.energy=power;o.data.shape='DISK';o.data.size=size;track(o,(0,0,.7))
bpy.ops.object.camera_add(location=(2.4,-5,2));cam=bpy.context.object;cam.name='Review Camera';cam.data.type='ORTHO';cam.data.ortho_scale=1.70;track(cam,(0,0,.72));scene.camera=cam
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.006));floor=bpy.context.object;floor.name='Studio floor';floor.data.materials.append(mat('studio',(.34,.32,.28)))
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(OUT/'beauty.png')
rig.animation_data.action=None
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update()
stats={}
for cat,obs in parts.items():
    ob=obs[0];ob.data.calc_loop_triangles();stats[cat]={'vertices':len(ob.data.vertices),'triangles':len(ob.data.loop_triangles)}
# Save editable source in rest pose, export character and independent gear.
activate(rig);rig.hide_set(False)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'dwarf.blend'))
if '--no-export' not in sys.argv:
    for cat,obs in parts.items():
        activate(rig);ob=obs[0];ob.hide_set(False);ob.select_set(True)
        for tr in rig.animation_data.nla_tracks:tr.mute=False
        bpy.ops.export_scene.gltf(filepath=str(GAME/(('dwarf_base' if cat=='base' else cat)+'.glb')),export_format='GLB',use_selection=True,export_animations=cat=='base',export_animation_mode='NLA_TRACKS',export_force_sampling=True,export_frame_range=False,export_anim_slide_to_zero=True,export_optimize_animation_size=False,export_def_bones=False,export_skins=True,export_yup=True)
        for tr in rig.animation_data.nla_tracks:tr.mute=True
        ob.hide_set(cat not in ('base','hair','beard'))
(OUT/'manifest.json').write_text(json.dumps({'sex':SEX,'reference':f'矮人-白身-三视图{"-女" if SEX=="female" else ""}.png','rig':'unchanged V2 rest, 23 bones','geometry':stats,'actions':[t.name for t in rig.animation_data.nla_tracks]},ensure_ascii=False,indent=2))
rig.animation_data.action=None
for tr in rig.animation_data.nla_tracks:tr.mute=True
scene.frame_set(1)
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update()
if '--render' in sys.argv:bpy.ops.render.render(write_still=True)
print('V3_REFERENCE_BUILT',SEX,json.dumps(stats))
