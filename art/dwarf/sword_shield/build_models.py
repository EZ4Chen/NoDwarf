"""Merge independently authored action modules into both shared-rig source models."""
from pathlib import Path
import bpy, sys, importlib.util, json
ROOT=Path(__file__).resolve().parents[3]
sex='female' if '--female' in sys.argv else 'male'
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/dwarf/v2'/sex/'dwarf.blend'))
rig=bpy.data.objects['DwarfRig']
metadata={}
for name,path in [('attack','attack/attack_actions.py'),('defense','defense/defense_actions.py')]:
    spec=importlib.util.spec_from_file_location(name,ROOT/'art/dwarf/sword_shield'/path)
    mod=importlib.util.module_from_spec(spec);sys.modules[name]=mod;spec.loader.exec_module(mod)
    result=mod.build_actions(rig)
    metadata[name]=result
rig.animation_data.action=None
wanted={'idle','walk','sword_shield_idle','sword_slash','block_raise','block_hold','block_hit','hit','down','block_lower','block_break','Mine'}
for track in list(rig.animation_data.nla_tracks):
    if track.name not in wanted:rig.animation_data.nla_tracks.remove(track)
    else:track.mute=False
for pb in rig.pose.bones:
    pb.rotation_mode='XYZ';pb.rotation_euler=(0,0,0);pb.location=(0,0,0);pb.scale=(1,1,1)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
rig.hide_set(False);rig.select_set(True)
base=bpy.data.objects['base'];base.hide_set(False);base.hide_render=False;base.select_set(True)
# Beard stays modular; source default visibility does not bake equipment into the body.
bpy.context.view_layer.objects.active=rig
out=ROOT/'source/Assets/Characters/SwordShield/Models'
bpy.ops.export_scene.gltf(filepath=str(out/(sex+'.glb')),export_format='GLB',use_selection=True,
 export_animations=True,export_animation_mode='NLA_TRACKS',export_nla_strips=True,
 export_force_sampling=True,export_frame_range=False,export_anim_slide_to_zero=True,
 export_optimize_animation_size=False,export_def_bones=False,export_skins=True,export_yup=True)
for track in rig.animation_data.nla_tracks:track.mute=True
rig.animation_data.action=bpy.data.actions.get('sword_shield_idle')
bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'art/dwarf/sword_shield'/(sex+'.blend')))
(ROOT/'art/dwarf/sword_shield'/(sex+'-clips.json')).write_text(json.dumps(metadata,indent=2,default=str))
print('SWORD_SHIELD_MODEL_EXPORTED',sex)
