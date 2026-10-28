"""New unrigged study, authored only against the supplied orthographic sheets.
No prior dwarf mesh, rig, action or generator is loaded.
"""
import bpy, math, sys
from mathutils import Vector
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
SEX='female' if '--female' in sys.argv else 'male'
OUT=Path(__file__).resolve().parent/SEX;OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
raw=[]
def ell(name,p,scale):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=48,ring_count=32,location=p)
 o=bpy.context.object;o.name=name;o.scale=scale;bpy.ops.object.transform_apply(location=True,rotation=True,scale=True);raw.append(o);return o

def loft(name,rings,n=48):
 vs=[];fs=[]
 for x,y,z,rx,ry in rings:
  for i in range(n):
   a=math.tau*i/n;vs.append((x+rx*math.cos(a),y+ry*math.sin(a),z))
 for j in range(len(rings)-1):
  for i in range(n):a=j*n+i;b=j*n+(i+1)%n;fs.append((a,b,b+n,a+n))
 fs += [tuple(reversed(range(n))),tuple((len(rings)-1)*n+i for i in range(n))]
 d=bpy.data.meshes.new(name);d.from_pydata(vs,[],fs);d.update();o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);raw.append(o)
 bpy.context.view_layer.objects.active=o;o.select_set(True)
 m=o.modifiers.new('Continuous anatomical sections','SUBSURF');m.levels=2;bpy.ops.object.modifier_apply(modifier=m.name);o.select_set(False)
 return o
# Torso: broad rib cage, soft heavy abdomen, modest shoulders, low pelvis.
loft('Torso anatomical mass',[(0,0,.42,.15,.12),(0,0,.49,.23,.155),(0,-.015,.57,.255,.18),(0,-.03,.66,.266,.208),(0,-.015,.77,.255,.19),(0,0,.88,.275,.17),(0,0,.97,.28,.145),(0,.005,1.035,.19,.115),(0,.005,1.09,.09,.085)])
ell('Neck',(0,.005,1.09),(.105,.10,.11))
for s in [-1,1]:
 # The reference arms hang close to the sides. No inherited A-pose.
 loft('Arm',[(s*.28,0,1.02,.075,.09),(s*.315,0,.98,.101,.113),(s*.345,0,.91,.10,.11),(s*.363,0,.82,.084,.093),(s*.37,-.008,.755,.070,.077),(s*.39,-.014,.69,.082,.089),(s*.414,-.018,.60,.071,.073),(s*.427,-.022,.535,.047,.051)])
 ell('Deltoid',(s*.29,0,.985),(.10,.109,.09))
 ell('Elbow',(s*.37,.01,.753),(.070,.075,.061))
 ell('Palm',(s*.435,-.026,.493),(.061,.043,.065))
 for i in range(4):
  x=s*(.394+i*.025);base=.468+abs(i-1.3)*.008
  loft('Finger',[(x,-.028,base+.025,.014,.016),(x+s*.005,-.034,base-.008,.014,.014),(x+s*.006,-.052,base-.039,.012,.013),(x,-.072,base-.052,.009,.010),(x-s*.007,-.078,base-.047,.003,.004)],24)
 loft('Thumb',[(s*.393,-.036,.52,.022,.023),(s*.372,-.054,.49,.02,.022),(s*.371,-.076,.465,.015,.017),(s*.383,-.081,.453,.006,.008)],24)
 loft('Leg',[(s*.142,0,.52,.122,.136),(s*.153,0,.435,.125,.136),(s*.16,-.008,.35,.103,.109),(s*.165,-.012,.303,.086,.091),(s*.17,.012,.255,.10,.111),(s*.171,.026,.203,.093,.105),(s*.173,.01,.14,.068,.081),(s*.173,0,.083,.061,.065)])
 ell('Patella',(s*.165,-.078,.315),(.063,.032,.049))
 ell('Foot heel',(s*.173,.008,.059),(.075,.087,.062))
 ell('Foot instep',(s*.173,-.073,.056),(.088,.12,.058))
 ell('Forefoot',(s*.176,-.143,.034),(.100,.072,.033))
 for i in range(5):
  ell('Toe',(s*(.106+i*.034),-.205+i*.008,.027),(.021-i*.0017,.038-i*.003,.023-i*.0017))
# Head proportions follow the reference: strong jaw, cheek bones, large fleshy nose.
loft('Skull and jaw',[(0,-.015,1.045,.055,.060),(0,-.023,1.075,.102,.095),(0,-.006,1.125,.137,.125),(0,.006,1.18,.153,.137),(0,.014,1.235,.151,.133),(0,.018,1.29,.147,.13),(0,.023,1.335,.130,.112),(0,.026,1.37,.08,.073)])
ell('Chin',(0,-.10,1.086),(.077,.045,.041))
for s in [-1,1]:
 ell('Cheekbone',(s*.097,-.093,1.18),(.057,.039,.041))
 ell('Lower cheek',(s*.092,-.07,1.128),(.052,.047,.054))
 ell('Supraorbital ridge',(s*.063,-.103,1.267),(.065,.029,.023))
 ell('Ear',(s*.157,.0,1.221),(.035,.032,.061))
 ell('Nose ala',(s*.033,-.158,1.207),(.027,.032,.022))
ell('Nasal bridge',(0,-.124,1.252),(.030,.037,.056))
ell('Nose tip',(0,-.18,1.218),(.047,.048,.038))
ell('Upper lip support',(0,-.124,1.15),(.058,.029,.028))
for side in [-1,1]:
 ell('Upper eyelid',(side*.058,-.117,1.263),(.034,.019,.012))
 ell('Lower eyelid',(side*.058,-.118,1.244),(.032,.015,.008))
ell('Lower lip',(0,-.145,1.137),(.048,.012,.009))
# Union all anatomy into a high resolution sculptable surface.
bpy.ops.object.select_all(action='DESELECT')
for o in raw:o.select_set(True)
bpy.context.view_layer.objects.active=raw[0];bpy.ops.object.join();body=bpy.context.object;body.name='Anatomy high resolution sculpt'
m=body.modifiers.new('Sculpt voxel union','REMESH');m.mode='VOXEL';m.voxel_size=.0035;m.use_smooth_shade=True;bpy.ops.object.modifier_apply(modifier=m.name)
m=body.modifiers.new('Surface relaxation','SMOOTH');m.factor=.55;m.iterations=4;bpy.ops.object.modifier_apply(modifier=m.name)
raw=[]
# Separate underwear shells follow the latest references for both sexes.
loft('Cotton vest',[(0,-.018,.595,.269,.198),(0,-.027,.65,.278,.219),(0,-.013,.77,.269,.204),(0,0,.875,.287,.186),(0,0,.916,.278,.172)])
for s in [-1,1]:
 path=[(s*.20,-.154,.90),(s*.205,-.126,.965),(s*.19,-.074,1.018),(s*.17,.005,1.043),(s*.19,.09,1.006),(s*.20,.14,.92)]
 v=[];f=[]
 for x,y,z in path:v.extend([(x-.033,y,z),(x+.033,y,z)])
 for i in range(len(path)-1):f.append((2*i,2*i+1,2*i+3,2*i+2))
 d=bpy.data.meshes.new('Vest strap');d.from_pydata(v,[],f);o=bpy.data.objects.new('Vest shoulder strap',d);scene.collection.objects.link(o);raw.append(o)
 bpy.context.view_layer.objects.active=o;m=o.modifiers.new('Cloth curve','SUBSURF');m.levels=3;bpy.ops.object.modifier_apply(modifier=m.name)
 m=o.modifiers.new('Cloth thickness','SOLIDIFY');m.thickness=.003;bpy.ops.object.modifier_apply(modifier=m.name)
loft('Boxer waist',[(0,0,.43,.195,.145),(0,0,.49,.241,.166),(0,-.016,.565,.268,.195),(0,-.018,.588,.27,.198)])
for s in [-1,1]:
 loft('Boxer leg',[(s*.151,0,.405,.132,.146),(s*.151,0,.41,.135,.15),(s*.147,0,.48,.139,.158),(s*.14,0,.52,.14,.164)])
# Hair and beard volumes, independently sculptable; no old geometry.
ell('Hair crown',(0,.026,1.323),(.161,.144,.079))
for s in [-1,1]:
 ell('Temple hair',(s*.139,.028,1.286),(.043,.11,.09))
 ell('Nape hair',(s*.085,.10,1.20),(.095,.065,.10))
if SEX=='male':
 ell('Full beard',(0,-.103,1.086),(.139,.100,.105))
 for s in [-1,1]:ell('Jaw beard',(s*.106,-.073,1.14),(.052,.068,.09))
# The supplied three-view is used as a projection study texture, not baked detail.
# This is explicitly an initial sculpt study, not a production-ready texture atlas.
image=bpy.data.images.load(str(ROOT/('矮人-白身-三视图-女.png' if SEX=='female' else '矮人-白身-三视图.png')));image.pack()
mat=bpy.data.materials.new('Reference projection study');mat.use_nodes=True
nodes=mat.node_tree.nodes;p=nodes.get('Principled BSDF');p.inputs['Roughness'].default_value=.82
tex=nodes.new('ShaderNodeTexImage');tex.image=image;tex.interpolation='Linear';mat.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color'])
for o in [body]+raw:
 o.data.materials.clear();o.data.materials.append(mat)
 uv=o.data.uv_layers.new(name='ReferenceStudy');o.data.uv_layers.active=uv;uv.active_render=True
 for poly in o.data.polygons:
  poly.use_smooth=True
  # Front/back and side selection uses polygon normals on the original mesh.
  side=abs(poly.normal.x)>.72
  for li in poly.loop_indices:
   co=o.data.vertices[o.data.loops[li].vertex_index].co
   if side: px=755-co.y*629
   elif poly.normal.y>0:px=1231+co.x*629
   else:px=334+co.x*629
   py=940-co.z*629
   uv.data[li].uv=(px/1536,1-py/1024)
# Neutral studio.
scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True
scene.render.resolution_x=1000;scene.render.resolution_y=1200;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.22,.22,.22,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
for loc,energy,size in [((-3,-4,5),350,4),((3,-1,3),180,3),((0,4,4),350,3)]:
 bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.data.energy=energy;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(Vector((0,0,.7))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(0,-5,.78));cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=1.62;cam.rotation_euler=(Vector((0,0,.70))-cam.location).to_track_quat('-Z','Y').to_euler();scene.camera=cam
scene.view_settings.view_transform='AgX'
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(OUT/'front-study.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'sculpt.blend'))
bpy.ops.render.render(write_still=True)
clay=bpy.data.materials.new('Neutral sculpt clay');clay.diffuse_color=(.40,.36,.31,1);clay.use_nodes=True
p=clay.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(.40,.36,.31,1);p.inputs['Roughness'].default_value=.82
for o in [body]+raw:o.data.materials.clear();o.data.materials.append(clay)
scene.render.filepath=str(OUT/'clay-front.png');bpy.ops.render.render(write_still=True)
cam.location=(4,-.02,.78);cam.rotation_euler=(Vector((0,0,.70))-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(OUT/'clay-side.png');bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'sculpt-clay.blend'))
print('NEW_REFERENCE_SCULPT',SEX,len(body.data.vertices))
