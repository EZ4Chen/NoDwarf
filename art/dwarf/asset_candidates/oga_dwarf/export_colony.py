"""Non-destructive game export; run on dwarf-male-whitebody.blend in background Blender."""
import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Matrix, Vector, Quaternion
ROOT=Path(__file__).resolve().parents[4]
OUT=ROOT/'source/Assets/Characters/Whitebody/male'; OUT.mkdir(parents=True,exist_ok=True)
rig=bpy.data.objects['Deform_armature']; body=bpy.data.objects['body']
# Old Blender layers contain hidden rig collections; give export objects a visible home.
export_collection=bpy.data.collections.new('Colony export')
bpy.context.scene.collection.children.link(export_collection)
for obj in [rig]+[o for o in bpy.data.objects if o.type=='MESH']:
 for collection in list(obj.users_collection):collection.objects.unlink(obj)
 export_collection.objects.link(obj)
 obj.hide_viewport=False;obj.hide_render=False;obj.hide_select=False;obj.hide_set(False)
# Eliminate control-rig dependencies in the exported copy only.
for p in rig.pose.bones:
 for c in list(p.constraints):p.constraints.remove(c)
 p.matrix_basis=Matrix.Identity(4)
rig.animation_data_clear()
rename={'body.cr':'root','pelvis.cr':'pelvis','spine.cr.01':'spine','spine.cr.02':'chest','spine.cr.03':'upper_chest','neck.cr':'neck','head.cr':'head'}
for side in ['L','R']:
 for part in ['upper_arm','forearm','hand','thigh','shin']:rename[f'{part}.cr.{side}']=f'{part}.{side}'
 rename[f'tarsal.cr.{side}']=f'foot.{side}'
for old,new in rename.items():
 rig.data.bones[old].name=new
 for obj in bpy.data.objects:
  if obj.type=='MESH' and old in obj.vertex_groups:obj.vertex_groups[old].name=new
# Normalize the actual body to 1.4 m with feet at zero; all geometry/bind data in metres.
lo=min(v.co.z for v in body.data.vertices); hi=max(v.co.z for v in body.data.vertices)
scale=1.4/(hi-lo); norm=Matrix.Scale(scale,4) @ Matrix.Translation((0,0,-lo))
for obj in list(bpy.data.objects):
 if obj.type=='MESH':obj.data.transform(norm)
rig.data.transform(norm)
with bpy.data.libraries.load(str(ROOT/'art/dwarf/v2/male/dwarf.blend'),link=False) as (src,dst):dst.objects=['DwarfRig']
oldrig=dst.objects[0];bpy.context.scene.collection.objects.link(oldrig)
# Hand attachments retain the established local equipment axes. Position at palm centres.
bpy.ops.object.select_all(action='DESELECT');rig.hide_set(False);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
for side in ['L','R']:
 hand=rig.data.edit_bones['hand.'+side]; oldslot=oldrig.data.bones['slot_hand_'+side.lower()]
 slot=rig.data.edit_bones.new('slot_hand_'+side.lower());slot.parent=hand
 # Source palm and fingers extend beyond this very short hand bone.
 slot.head=hand.head+(hand.tail-hand.head).normalized()*.065
 slot.tail=slot.head+Vector((0,-.11,0));slot.matrix=Matrix.Translation(slot.head) @ oldslot.matrix_local.to_3x3().to_4x4();slot.length=.11
for name,parent,offset in [('slot_head','head',(0,0,.12)),('slot_back','chest',(0,.12,0)),('slot_belt','pelvis',(0,-.15,0))]:
 b=rig.data.edit_bones.new(name);b.parent=rig.data.edit_bones[parent];b.head=b.parent.head+Vector(offset);b.tail=b.head+Vector((0,-.1,0))
bpy.ops.object.mode_set(mode='OBJECT')
# Bake local rotations from source world-space rotation deltas, preserving new bone lengths.
rig.animation_data_create();oldrig.animation_data_create();bpy.context.scene.render.fps=24
for p in rig.pose.bones:p.rotation_mode='QUATERNION'
clips={}
for clip in ['Idle','Walk','Mine','Attack','Carry']:
 action=bpy.data.actions.get(clip)
 if not action: raise RuntimeError('Missing source action '+clip)
 oldrig.animation_data.action=action
 start,end=map(int,action.frame_range)
 baked=bpy.data.actions.new('Colony_'+clip)
 for frame in range(start,end+1):
  rig.animation_data.action=None
  bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
  desired={}
  for p in rig.pose.bones:
   rest=p.bone.matrix_local
   parent=p.parent
   local=parent.bone.matrix_local.inverted() @ rest if parent else rest
   base=desired[parent.name] @ local if parent else rest.copy()
   if p.name in oldrig.pose.bones:
    src=oldrig.pose.bones[p.name]
    rotation=src.matrix.to_quaternion() @ src.bone.matrix_local.to_quaternion().inverted() @ rest.to_quaternion()
    base=rotation.to_matrix().to_4x4();base.translation=(desired[parent.name] @ local).translation if parent else rest.translation
    if p.name=='root':base.translation+=src.matrix.translation-src.bone.matrix_local.translation
   if p.name.startswith(('finger_','thumb.')):
    axis=rest.to_3x3().inverted() @ (rest.to_3x3().col[1].cross(Vector((0,-1,0))))
    if axis.length>.001:
     amount=.55 if p.name.startswith('thumb.') else (.65 if '.01.' in p.name else .85)
     base=base @ Quaternion(axis.normalized(),amount).to_matrix().to_4x4()
   desired[p.name]=base
  # Keep the established tool paths using two-bone IK, not scaled arm translations.
  if clip in ['Mine','Attack']:
   for side in ['L','R']:
    upper='upper_arm.'+side;lower='forearm.'+side;hand='hand.'+side;slot='slot_hand_'+side.lower()
    old_desired={k:v.copy() for k,v in desired.items()}
    local_slot=rig.data.bones[hand].matrix_local.inverted() @ rig.data.bones[slot].matrix_local
    target=oldrig.pose.bones[slot].matrix.copy()
    hand_basis=target.to_3x3() @ local_slot.to_3x3().inverted()
    wrist=target.translation-hand_basis @ local_slot.translation
    shoulder=desired[upper].translation
    length1=(rig.data.bones[lower].head_local-rig.data.bones[upper].head_local).length
    length2=(rig.data.bones[hand].head_local-rig.data.bones[lower].head_local).length
    direction=(wrist-shoulder).normalized();distance=min((wrist-shoulder).length,length1+length2-.0001)
    distance=max(distance,abs(length1-length2)+.0001);wrist=shoulder+direction*distance
    along=(length1*length1-length2*length2+distance*distance)/(2*distance)
    bend=oldrig.pose.bones[lower].matrix.translation-shoulder;bend-=direction*bend.dot(direction)
    if bend.length<.001:bend=Vector((1 if side=='L' else -1,0,-1))
    elbow=shoulder+direction*along+bend.normalized()*math.sqrt(max(0,length1*length1-along*along))
    for name,origin,endpoint in [(upper,shoulder,elbow),(lower,elbow,wrist)]:
     basis=desired[name].to_3x3();swing=basis.col[1].normalized().rotation_difference((endpoint-origin).normalized())
     desired[name]=swing.to_matrix().to_4x4() @ basis.to_4x4();desired[name].translation=origin
    desired[hand]=hand_basis.to_4x4();desired[hand].translation=wrist
    descendants={hand}
    for p in rig.pose.bones:
     if p.parent and p.parent.name in descendants:
      desired[p.name]=desired[p.parent.name] @ old_desired[p.parent.name].inverted() @ old_desired[p.name];descendants.add(p.name)
  for p in rig.pose.bones:
   parent=p.parent
   p.matrix_basis=p.bone.convert_local_to_pose(desired[p.name],p.bone.matrix_local,parent_matrix=desired[parent.name] if parent else Matrix.Identity(4),parent_matrix_local=parent.bone.matrix_local if parent else Matrix.Identity(4),invert=True)
  rig.animation_data.action=baked
  for p in rig.pose.bones:
   p.keyframe_insert('location',frame=frame);p.keyframe_insert('rotation_quaternion',frame=frame);p.keyframe_insert('scale',frame=frame)
 clips[clip]={'loop':clip in ['Idle','Walk','Mine','Carry'],'frames':[start,end],'seconds':(end-start)/24}
 baked.name='Whitebody_'+clip
 track=rig.animation_data.nla_tracks.new();track.name=clip;track.strips.new(clip,start,baked);track.mute=True
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
# Work copies: fitted simple clothes and masked body variants; never alter the source blend.
def material(name,color):
 m=bpy.data.materials.new(name);m.use_nodes=True;p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=.85;return m
mats={'shirt':material('Colony linen',(.22,.32,.36)),'trousers':material('Colony trousers',(.23,.15,.09)),'boots':material('Colony boots',(.09,.065,.045))}
def region(v):
 groups={body.vertex_groups[g.group].name:g.weight for g in v.groups if body.vertex_groups[g.group].name in rig.data.bones}
 major=max(groups,key=groups.get) if groups else ''
 if major in ['spine','chest','upper_chest','pelvis'] or major.startswith(('upper_arm.','shoulder.')):
  return 'shirt' if v.co.z>.74 else 'trousers'
 if major.startswith('thigh.'):return 'trousers'
 if major.startswith(('shin.','foot.','toe.')):return 'boots' if v.co.z<.29 else 'trousers'
 return ''
# Largest connected island is skin; the beard must never become part of a shirt.
adj=[set() for _ in body.data.vertices]
for e in body.data.edges:
 a,b=e.vertices;adj[a].add(b);adj[b].add(a)
remaining=set(range(len(adj)));islands=[]
while remaining:
 stack=[remaining.pop()];island=set(stack)
 while stack:
  for other in adj[stack.pop()] & remaining:
   remaining.remove(other);island.add(other);stack.append(other)
 islands.append(island)
skin_island=max(islands,key=len)
def covered_by(v,key):
 if v.index not in skin_island:return False
 if any(body.vertex_groups[g.group].name=='head' and g.weight>.35 for g in v.groups):return False
 x,y,z=v.co
 if key=='shirt':return .78<z<1.20 and abs(x)<.445
 if key=='trousers':return .205<z<.81 and abs(x)<.36
 if key=='boots':return z<.235
 return False
regions=[next((key for key in ['shirt','boots','trousers'] if covered_by(v,key)),'') for v in body.data.vertices]
def copy_subset(name,keep,mat=None,inflate=0):
 obj=body.copy();obj.data=body.data.copy();obj.name=name;bpy.context.scene.collection.objects.link(obj)
 bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
 remove=[f for f in bm.faces if not keep([regions[v.index] for v in f.verts])];bmesh.ops.delete(bm,geom=remove,context='FACES')
 bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS');bm.normal_update()
 for v in bm.verts:v.co+=v.normal*inflate
 bm.to_mesh(obj.data);bm.free()
 if mat:
  obj.data.materials.clear();obj.data.materials.append(mat)
  for p in obj.data.polygons:p.material_index=0
 return obj
# GLTF export only selected meshes and one rig; export every action with named NLA strips.
def export(name,meshes,animations=False):
 bpy.ops.object.select_all(action='DESELECT');rig.hide_viewport=False;rig.hide_render=False;rig.hide_select=False;rig.hide_set(False);rig.select_set(True)
 for o in meshes:o.hide_set(False);o.hide_render=False;o.select_set(True)
 bpy.context.view_layer.objects.active=rig
 for track in rig.animation_data.nla_tracks:track.mute=False
 assert rig in bpy.context.selected_objects, 'Rig was not selected'
 bpy.ops.export_scene.gltf(filepath=str(OUT/(name+'.glb')),export_format='GLB',use_selection=True,export_animations=animations,export_animation_mode='NLA_TRACKS',export_skins=True,export_yup=True,export_apply=False,export_force_sampling=True,export_anim_slide_to_zero=True,export_nla_strips_merged_animation_name='Animation')
 for track in rig.animation_data.nla_tracks:track.mute=True
underwear=[bpy.data.objects[n] for n in ['Underwear shorts','Underwear waistband']]
export('dwarf_base',[body]+underwear,True)
for bits in ['001','010','011','100','101','110','111']:
 covered={key for i,key in enumerate(['shirt','trousers','boots']) if bits[i]=='1'}
 masked=copy_subset('body_mask_'+bits,lambda regions:not all(r in covered for r in regions))
 export('body_mask_'+bits,[masked]+([] if bits[1]=='1' else underwear))
def shell(key,planes,inflate):
 obj=body.copy();obj.data=body.data.copy();obj.name=key;bpy.context.scene.collection.objects.link(obj)
 bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
 head_vertices={v.index for v in body.data.vertices if any(body.vertex_groups[g.group].name=='head' and g.weight>.35 for g in v.groups)}
 bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in skin_island or v.index in head_vertices],context='VERTS')
 for co,no in planes:
  bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-6,plane_co=co,plane_no=no,clear_inner=True)
 if key=='boots':
  bmesh.ops.delete(bm,geom=list(bm.faces),context='FACES_ONLY')
  for sign in [-1,1]:
   vertices=[v for v in bm.verts if v.co.x*sign>0]
   bmesh.ops.convex_hull(bm,input=vertices,use_existing_faces=False)
  bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
 bm.normal_update()
 for v in bm.verts:v.co+=v.normal*inflate
 bm.to_mesh(obj.data);bm.free();obj.data.materials.clear();obj.data.materials.append(mats[key])
 for p in obj.data.polygons:p.material_index=0
 return obj
clothes={}
clothes['shirt']=shell('shirt',[((0,0,.775),(0,0,1)),((0,0,1.205),(0,0,-1)),((.45,0,0),(-1,0,0)),((-.45,0,0),(1,0,0))],.013)
clothes['trousers']=shell('trousers',[((0,0,.20),(0,0,1)),((0,0,.82),(0,0,-1)),((.36,0,0),(-1,0,0)),((-.36,0,0),(1,0,0))],.006)
clothes['boots']=shell('boots',[((0,0,.245),(0,0,-1))],.011)
for key,obj in clothes.items():export(key,[obj])
# Reuse the source helmet and torso armor as separately wearable pieces.
armor=bpy.data.objects['armor']
armor_adj=[set() for v in armor.data.vertices]
for e in armor.data.edges:
 a,b=e.vertices;armor_adj[a].add(b);armor_adj[b].add(a)
remaining=set(range(len(armor_adj)));armor_parts=[]
while remaining:
 stack=[remaining.pop()];part=set(stack)
 while stack:
  for other in armor_adj[stack.pop()] & remaining:
   remaining.remove(other);part.add(other);stack.append(other)
 weights={}
 for i in part:
  for group in armor.data.vertices[i].groups:
   name=armor.vertex_groups[group.group].name
   if name in rig.data.bones:weights[name]=weights.get(name,0)+group.weight
 dominant=max(weights,key=weights.get)
 armor_parts.append((part,dominant))
mat=armor.data.materials[0];nodes=mat.node_tree.nodes
shader=next(n for n in nodes if n.type=='BSDF_PRINCIPLED');tex=next(n for n in nodes if n.type=='TEX_IMAGE')
tint=nodes.new('ShaderNodeMixRGB');tint.blend_type='MULTIPLY';tint.inputs[0].default_value=1;tint.inputs[2].default_value=(.24,.30,.36,1)
mat.node_tree.links.new(tex.outputs['Color'],tint.inputs[1]);mat.node_tree.links.new(tint.outputs[0],shader.inputs['Base Color'])
shader.inputs['Metallic'].default_value=.65;shader.inputs['Roughness'].default_value=.5
for key,head_part in [('helmet',True),('armor',False)]:
 obj=armor.copy();obj.data=armor.data.copy();obj.name=key+'_fitted';bpy.context.scene.collection.objects.link(obj)
 keep=set().union(*(part for part,dominant in armor_parts if (dominant=='head' if head_part else dominant in ['pelvis','spine','chest','upper_chest','upper_arm.L','upper_arm.R','shoulder.cr.L','shoulder.cr.R'])))
 bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
 bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in keep],context='VERTS')
 bm.to_mesh(obj.data);bm.free();export(key,[obj]+([] if head_part else [clothes['shirt']]))
# Keep established weapons (physical dimensions unchanged); fresh skeleton-specific clothes only.
import shutil
for key in ['sword','shield','pickaxe']:shutil.copy2(ROOT/f'source/Assets/Characters/DwarfV2/male/{key}.glb',OUT/f'{key}.glb')
equipment={k:{'mode':'skin','file':k+'.glb'} for k in list(mats)+['armor','helmet']}
equipment.update({k:{'mode':'attachment','file':k+'.glb','socket':'slot_hand_l' if k=='shield' else 'slot_hand_r'} for k in ['sword','shield','pickaxe']})
manifest={'bones':[b.name for b in rig.data.bones],'animations':clips,'equipment':equipment,'presets':{'base':[],'casual':['shirt','trousers','boots'],'miner':['shirt','trousers','boots','helmet','pickaxe'],'guard':['armor','trousers','boots','helmet','sword','shield']},'height_m':1.4,'fps':24}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
(OUT/'SOURCE.txt').write_text('Dwarf - fixed: thecubber; commissioned by OpenGameArt.org; modifications by Beast.\nhttps://opengameart.org/content/dwarf-fixed\nCC BY 3.0: https://creativecommons.org/licenses/by/3.0/\nLocal changes: modern materials, underwear, metre normalization, retargeted colony animations, fitted clothes and game export.\n')
# Female compatibility variant uses the same calibrated skeleton, no beard and an undershirt.
# This is a wardrobe variant of the accepted base, not a separate reference sculpt.
male_out=OUT
OUT=male_out.parent/'female';OUT.mkdir(parents=True,exist_ok=True)
female_body=body.copy();female_body.data=body.data.copy();female_body.name='body_female';bpy.context.scene.collection.objects.link(female_body)
beard_island=next(i for i in islands if len(i)==98)
bm=bmesh.new();bm.from_mesh(female_body.data);bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index in beard_island],context='VERTS');bm.to_mesh(female_body.data);bm.free()
top=shell('shirt',[((0,0,.96),(0,0,1)),((0,0,1.205),(0,0,-1)),((.27,0,0),(-1,0,0)),((-.27,0,0),(1,0,0))],.007)
top.name='Underwear top';top.data.materials.clear();top.data.materials.append(underwear[0].data.materials[0])
export('dwarf_base',[female_body,top]+underwear,True)
for bits in ['001','010','011','100','101','110','111']:
 covered={key for i,key in enumerate(['shirt','trousers','boots']) if bits[i]=='1'}
 masked=copy_subset('female_mask_'+bits,lambda r:not all(k in covered for k in r))
 # Identify detached beard geometrically after subset indices have changed.
 bm=bmesh.new();bm.from_mesh(masked.data)
 deform=bm.verts.layers.deform.active
 # Original beard occupies its own connected island; remove it by original coordinates.
 beard_points={tuple(round(c,6) for c in body.data.vertices[i].co) for i in beard_island}
 bmesh.ops.delete(bm,geom=[v for v in bm.verts if tuple(round(c,6) for c in v.co) in beard_points],context='VERTS')
 bm.to_mesh(masked.data);bm.free()
 export('body_mask_'+bits,[masked]+([] if bits[1]=='1' else underwear)+([] if bits[0]=='1' else [top]))
for key in equipment:shutil.copy2(male_out/(key+'.glb'),OUT/(key+'.glb'))
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
shutil.copy2(male_out/'SOURCE.txt',OUT/'SOURCE.txt')
# Share identical texture resources between body masks instead of importing a new GPU image per mask.
import struct,hashlib
texture_dir=OUT.parent/'textures';texture_dir.mkdir(exist_ok=True)
for path in list(male_out.glob('*.glb'))+list(OUT.glob('*.glb')):
 raw=path.read_bytes();json_length=struct.unpack_from('<I',raw,12)[0]
 doc=json.loads(raw[20:20+json_length]);binary=raw[28+json_length:]
 removed=set()
 for image in doc.get('images',[]):
  if 'bufferView' not in image:continue
  index=image.pop('bufferView');view=doc['bufferViews'][index];offset=view.get('byteOffset',0)
  data=binary[offset:offset+view['byteLength']];suffix='.png' if image.get('mimeType')=='image/png' else '.jpg'
  filename=hashlib.sha256(data).hexdigest()[:16]+suffix
  (texture_dir/filename).write_bytes(data);image['uri']='../textures/'+filename;removed.add(index)
 if not removed:continue
 views=[];new_binary=bytearray();remap={}
 for i,view in enumerate(doc['bufferViews']):
  if i in removed:continue
  while len(new_binary)%4:new_binary.append(0)
  start=view.get('byteOffset',0);data=binary[start:start+view['byteLength']]
  remap[i]=len(views);view['byteOffset']=len(new_binary);views.append(view);new_binary.extend(data)
 def reindex(node):
  if isinstance(node,dict):
   for key,value in list(node.items()):
    if key=='bufferView':node[key]=remap[value]
    else:reindex(value)
  elif isinstance(node,list):
   for child in node:reindex(child)
 reindex(doc);doc['bufferViews']=views;doc['buffers'][0]['byteLength']=len(new_binary)
 while len(new_binary)%4:new_binary.append(0)
 encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*((-len(encoded))%4)
 total=12+8+len(encoded)+8+len(new_binary)
 path.write_bytes(struct.pack('<4sII',b'glTF',2,total)+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(new_binary),0x004e4942)+new_binary)
print('WHITEBODY_EXPORT_OK',len(rig.data.bones),scale)
