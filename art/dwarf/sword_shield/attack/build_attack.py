from pathlib import Path
import bpy,sys,json
ROOT=Path(__file__).resolve().parents[4]
sys.path.insert(0,str(Path(__file__).resolve().parent))
from attack_actions import build_actions,RigPose
sex='female' if '--female' in sys.argv else 'male'
OUT=ROOT/'art/dwarf/sword_shield/attack'/sex;OUT.mkdir(exist_ok=True)
ASSETS=ROOT/'source/Assets/Characters/SwordShield/AnimationAttack';ASSETS.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/dwarf/v2'/sex/'dwarf.blend'))
rig=bpy.data.objects['DwarfRig'];before={b.name:list(v for row in b.matrix_local for v in row) for b in rig.data.bones}
metadata=build_actions(rig)
assert before=={b.name:list(v for row in b.matrix_local for v in row) for b in rig.data.bones}
for name,m in metadata.items():
 if m['loop']:
  for bone in m['samples_blender'][0]:
   assert max(abs(a-b) for a,b in zip(m['samples_blender'][0][bone],m['samples_blender'][-1][bone]))<1e-5
assert len(rig.data.bones)==23
bpy.context.scene.render.fps=24
bpy.ops.object.select_all(action='DESELECT')
for ob in [rig,bpy.data.objects['base']]:ob.hide_set(False);ob.select_set(True)
bpy.context.view_layer.objects.active=rig
for tr in rig.animation_data.nla_tracks:tr.mute=tr.name not in metadata
bpy.ops.export_scene.gltf(filepath=str(ASSETS/(sex+'_attack.glb')),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='NLA_TRACKS',export_nla_strips=True,export_force_sampling=True,export_frame_range=False,export_anim_slide_to_zero=True,export_optimize_animation_size=False,export_def_bones=False,export_skins=True,export_yup=True)
for tr in rig.animation_data.nla_tracks:tr.mute=True
rig.animation_data.action=bpy.data.actions['sword_shield_idle'];bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'attack.blend'))
(OUT/'metadata.json').write_text(json.dumps(metadata,indent=2))
for ob in bpy.context.scene.objects:
 if ob.type=='MESH' and ob.name in ['base','cosmetic_beard','shirt','trousers','boots','armor','helmet','pickaxe','sword','shield']:
  ob.hide_render=ob.name not in ['base','sword','shield']+(['cosmetic_beard'] if sex=='male' else [])
scene=bpy.context.scene;scene.render.resolution_x=720;scene.render.resolution_y=810;scene.render.resolution_percentage=100;scene.cycles.samples=12
for name,frame in [('sword_shield_idle',1),('sword_slash',9),('sword_slash',14),('sword_slash',17)]:
 rig.animation_data.action=bpy.data.actions[name];scene.frame_set(frame)
 scene.render.filepath=str(OUT/f'{name}_{frame}.png');bpy.ops.render.render(write_still=True)
print('ATTACK_ASSET_PASS',sex,len(rig.data.bones),list(metadata))
