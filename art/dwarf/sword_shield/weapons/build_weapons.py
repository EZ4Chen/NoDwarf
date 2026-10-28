"""Build standalone sword/shield meshes in slot-local coordinates; no world-up conversion."""
import bpy,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[4]
OUT=ROOT/'source/Assets/Characters/SwordShield/Weapons'
HERE=Path(__file__).resolve().parent
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art/dwarf/v2/male/dwarf.blend'))
rig=bpy.data.objects['DwarfRig']
refs={}
for name,side in [('sword','r'),('shield','l')]:
    source=bpy.data.objects[name]
    ob=source.copy();ob.data=source.data.copy();bpy.context.scene.collection.objects.link(ob)
    ob.name=name+'_mesh';ob.parent=None;ob.modifiers.clear();ob.vertex_groups.clear()
    ob.hide_set(False);ob.hide_render=False
    bone=rig.data.bones['slot_hand_'+side]
    if name=='shield':
        original_grip=Vector((.615,-.04,.585))
        for v in ob.data.vertices:
            x,y,z=v.co
            oz=z if z<=.12 else (.12+(z-.12)/.68 if z<=.5144 else z+.1856)
            width=1.10 if oz<.65 else (1.18 if oz<1.18 else 1.02)
            original=Vector((x/width,y/(1.10 if oz<1.18 else 1),oz))
            v.co=bone.head_local+(original-original_grip)*1.05
    ob.data.transform(bone.matrix_local.inverted())
    refs[name]=ob
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.export_scene.gltf(filepath=str(OUT/(name+'.glb')),export_format='GLB',use_selection=True,export_animations=False,export_yup=False,export_extras=True)
# Keep only the editable standalone weapons, no armature dependency or hidden clothes.
for ob in list(bpy.data.objects):
    if ob not in refs.values():bpy.data.objects.remove(ob,do_unlink=True)
for col in list(bpy.data.collections):
    if not col.objects:bpy.data.collections.remove(col)
manifest={'coordinate_space':'slot bone local; +Y forward, +Z up','sword':{'socket':'MainHandSocket','bone':'slot_hand_r','grip':[0,0,0],'blade_base':[0,0,.12],'blade_tip':[0,0,.58],'cutting_edges_axis':[0,1,0]},'shield':{'socket':'OffHandSocket','bone':'slot_hand_l','grip':[0,0,0],'front_normal':[0,1,0],'panel_center':[0,.107625,-.0315],'panel_size':[.399,.05775,.462]}}
for key,ob in refs.items():
    vs=[v.co for v in ob.data.vertices];ob.data.calc_loop_triangles()
    manifest[key]['bounds_min']=[min(v[i] for v in vs) for i in range(3)]
    manifest[key]['bounds_max']=[max(v[i] for v in vs) for i in range(3)]
    manifest[key]['triangles']=len(ob.data.loop_triangles)
    assert all(math.isfinite(c) for v in vs for c in v)
# These checks inspect actual vertices, so they catch a reverted sideways export.
sword=refs['sword'];span=[manifest['sword']['bounds_max'][i]-manifest['sword']['bounds_min'][i] for i in range(3)]
assert span[1]>3*span[0] and span[2]>.7
shield=refs['shield'];xs=[v.co.x for v in shield.data.vertices]
assert abs(max(xs)+min(xs))<.001,'Shield is not centered on grip'
(OUT/'weapon_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
bpy.context.scene.camera=None
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'weapons.blend'))
print('WEAPONS_BUILD_PASS '+json.dumps(manifest))
