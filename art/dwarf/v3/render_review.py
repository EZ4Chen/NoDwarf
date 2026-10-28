"""Blender review renders for V3, without modifying authoring files.

Blender --background --factory-startup --python art/dwarf/v3/render_review.py -- --quick
Inputs: art/dwarf/v3/{male,female}/dwarf.blend
Outputs: review/orthographic-six.png, review/equipment-pair.png, named action stills,
         review/report.json. ffmpeg is used only to tile our own rendered PNGs.
"""
import argparse
import json
import math
import shutil
import subprocess
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--sex', choices=['male', 'female', 'both'], default='both')
parser.add_argument('--quick', action='store_true', help='12 samples / 480x600 instead of 48 / 720x900')
parser.add_argument('--bodies-only', action='store_true')
parser.add_argument('--source-root', type=Path, default=HERE)
parser.add_argument('--output', type=Path, default=HERE / 'review')
parser.add_argument('--ortho-scale', type=float, default=2.2, help='Shared body-view vertical span in meters')
parser.add_argument('--old-root', type=Path, help='Optional previous male/female .blend folders for same-scale comparison')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
OUT = args.output
OUT.mkdir(parents=True, exist_ok=True)
SEXES = ['male', 'female'] if args.sex == 'both' else [args.sex]
report = {'sources': {}, 'rendered': [], 'missing_actions': [], 'missing_categories': {}, 'tool_axes': {}, 'missing_body_masks': []}
BODY = {'base', 'hair', 'beard'}
CASUAL = BODY | {'shirt', 'trousers', 'boots'}
GUARD = BODY | {'armor', 'trousers', 'boots', 'helmet', 'sword', 'shield'}
MINER = CASUAL | {'helmet', 'pickaxe'}
KNOWN = GUARD | MINER | {'base_clothed'}
def category_of(ob):
    value = str(ob.get('category','')).lower()
    if value:return value
    name = ob.name.lower()
    if name == 'cosmetic_beard':return 'beard'
    return name if name in KNOWN else ''


def mat(name, color):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = .85
    return m


def setup_stage():
    scene = bpy.context.scene
    # Keep a source's studio, alternate models and hidden accessories out of reviews.
    # Category-tagged character meshes are the only user objects we render.
    for ob in scene.objects:
        ob.hide_render = True
        if ob.type == 'ARMATURE':
            ob.hide_set(False)
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 12 if args.quick else 48
    scene.render.resolution_x = 480 if args.quick else 720
    scene.render.resolution_y = 600 if args.quick else 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'AgX'
    scene.world = bpy.data.worlds.new('ReviewWorld')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.12, .15, .17, 1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = .55
    for name, loc, energy, size in [
        ('Key', (-3,-4,5), 450, 4), ('Fill', (4,-1,3), 260, 4), ('Rim', (0,4,4), 450, 3)]:
        bpy.ops.object.light_add(type='AREA', location=loc)
        ob=bpy.context.object; ob.name='Review'+name; ob.hide_render=False
        ob.data.energy=energy; ob.data.shape='DISK'; ob.data.size=size
        ob.rotation_euler=(Vector((0,0,.75))-ob.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-.007))
    floor=bpy.context.object; floor.name='ReviewFloor'; floor.data.materials.append(mat('ReviewFloor',(.10,.13,.14)))
    bpy.ops.object.camera_add()
    camera=bpy.context.object; camera.name='ReviewCamera'
    camera.data.type='ORTHO'; camera.data.ortho_scale=args.ortho_scale
    scene.camera=camera
    bpy.ops.object.text_add()
    label=bpy.context.object; label.name='ReviewLabel'; label.data.align_x='CENTER'
    label.data.align_y='CENTER'; label.data.size=.067
    label.data.materials.append(mat('ReviewLabel',(.80,.86,.85)))
    # Emission prevents angle/light differences from affecting captions.
    p=label.data.materials[0].node_tree.nodes.get('Principled BSDF')
    p.inputs['Emission Color'].default_value=(.80,.86,.85,1)
    p.inputs['Emission Strength'].default_value=1
    return camera,label


def pose_rest(rig):
    rig.animation_data_create()
    rig.animation_data.action=None
    for track in rig.animation_data.nla_tracks: track.mute=True
    rig.data.pose_position='POSE'
    for bone in rig.pose.bones: bone.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()


def action_by_names(names):
    for name in names:
        for action in bpy.data.actions:
            if action.name.lower()==name.lower():return action
    return None


def pose(rig, names, fraction):
    pose_rest(rig)
    action=action_by_names(names)
    if action is None:return None
    rig.animation_data.action=action
    if hasattr(action,'slots') and len(action.slots):
        rig.animation_data.action_slot=action.slots[0]
    start,end=action.frame_range
    frame=start+(end-start)*fraction
    bpy.context.scene.frame_set(int(frame), subframe=frame-int(frame))
    bpy.context.view_layer.update()
    return action.name


def outfit(categories, label):
    categories=set(categories)
    if 'female' in label:categories.discard('beard')
    if 'helmet' in categories:categories.discard('hair')
    dressed=('shirt' in categories or 'armor' in categories) and {'trousers','boots'}.issubset(categories)
    if dressed:
        masks=[ob for ob in bpy.context.scene.objects if category_of(ob)=='base_clothed']
        if not masks:
            report['missing_body_masks'].append(label)
            return False
        categories.discard('base');categories.add('base_clothed')
    present=set()
    for ob in bpy.context.scene.objects:
        category=category_of(ob)
        if category:
            present.add(category)
            ob.hide_render=category not in categories
            ob.hide_set(False)
    report['missing_categories'][label]=sorted(categories-present)
    return True


def capture(sex, name, camera, label, direction='front', caption=None):
    loc={'front':(0,-6,.73),'side':(6,0,.73),'back':(0,6,.73),'threequarter':(3,-6,2.7)}[direction]
    camera.data.ortho_scale=max(args.ortho_scale,2.55) if direction=='threequarter' else args.ortho_scale
    camera.location=loc
    camera.rotation_euler=(Vector((0,0,.73))-camera.location).to_track_quat('-Z','Y').to_euler()
    bpy.context.view_layer.update()
    label.location=camera.matrix_world @ Vector((0,-.87,-3))
    label.rotation_euler=camera.rotation_euler
    label.data.body=caption or (sex.upper()+' / '+name.upper().replace('_',' '))
    bpy.context.scene.render.filepath=str(OUT/(sex+'-'+name+'.png'))
    bpy.ops.render.render(write_still=True)
    report['rendered'].append(sex+'-'+name+'.png')


def inspect_axes(rig, sex):
    result={}
    for side in ['r','l']:
        name='slot_hand_'+side
        if name not in rig.data.bones:continue
        basis=rig.data.bones[name].matrix_local.to_3x3()
        result[name]={'rest_local_forward':list(basis.inverted() @ Vector((0,-1,0))),
                      'rest_local_up':list(basis.inverted() @ Vector((0,0,1)))}
    # World-rest geometry spans provide a useful sideways-tool diagnostic.
    for category in ['sword','pickaxe','shield']:
        points=[ob.matrix_world@v.co for ob in bpy.context.scene.objects
                if ob.type=='MESH' and category_of(ob)==category for v in ob.data.vertices]
        if points:
            result[category]={'rest_span_xyz':[max(v[i] for v in points)-min(v[i] for v in points) for i in range(3)]}
    report['tool_axes'][sex]=result


for sex in SEXES:
    source=args.source_root/sex/'dwarf.blend'
    if not source.exists():
        raise FileNotFoundError('Build the '+sex+' model first: '+str(source))
    bpy.ops.wm.open_mainfile(filepath=str(source))
    report['sources'][sex]=str(source)
    rig=bpy.data.objects.get('DwarfRig')
    if not rig or rig.type!='ARMATURE':raise RuntimeError('DwarfRig is required')
    camera,label=setup_stage()
    pose_rest(rig);outfit(BODY,sex+'-body');inspect_axes(rig,sex)
    if pose(rig,['idle','Idle'],0) is None:report['missing_actions'].append(sex+': idle for orthographic views')
    for direction in ['front','side','back']:
        capture(sex,direction,camera,label,direction)
    if args.bodies_only:continue
    if outfit(GUARD,sex+'-guard'):
        clip=pose(rig,['sword_shield_idle','combat_idle','Idle'],0)
        if clip is None:report['missing_actions'].append(sex+': sword_shield_idle')
        capture(sex,'guard',camera,label,'threequarter')
    for name,names,fraction,categories in [
        ('walk',['walk','Walk'],.25,CASUAL),
        ('slash_windup',['sword_slash','Attack'],.28,GUARD),
        ('slash_contact',['sword_slash','Attack'],.60,GUARD),
        ('block_hold',['block_hold'],.5,GUARD),
        ('block_hit',['block_hit'],.35,GUARD),
        ('mine_raise',['Mine','work'],.36,MINER),
        ('mine_strike',['Mine','work'],.65,MINER),
        ('down',['down'],1,GUARD)]:
        if not outfit(categories,sex+'-'+name):continue
        clip=pose(rig,names,fraction)
        if clip is None:
            report['missing_actions'].append(sex+': '+name)
            continue
        capture(sex,name,camera,label,'threequarter')

if args.old_root:
    for sex in SEXES:
        source=args.old_root/sex/'dwarf.blend'
        if not source.exists():raise FileNotFoundError(source)
        bpy.ops.wm.open_mainfile(filepath=str(source))
        camera,label=setup_stage()
        rig=bpy.data.objects['DwarfRig'];pose_rest(rig);outfit(BODY,'old-'+sex);pose(rig,['idle','Idle'],0)
        capture('old-'+sex,'front',camera,label,'front','PREVIOUS / '+sex.upper())

ffmpeg=shutil.which('ffmpeg') or '/opt/homebrew/bin/ffmpeg'
def tile(files, output, layout):
    if not all((OUT/f).exists() for f in files):return
    cmd=[ffmpeg,'-y','-loglevel','error']
    for file in files:cmd+=['-i',str(OUT/file)]
    cmd+=['-filter_complex',f'xstack=inputs={len(files)}:layout={layout}', '-frames:v','1',str(OUT/output)]
    subprocess.run(cmd,check=True)
if SEXES==['male','female']:
    tile([sex+'-'+view+'.png' for sex in SEXES for view in ['front','side','back']],
         'orthographic-six.png','0_0|w0_0|w0+w1_0|0_h0|w0_h0|w0+w1_h0')
    if not args.bodies_only:tile(['male-guard.png','female-guard.png'],'equipment-pair.png','0_0|w0_0')
if args.old_root and SEXES==['male','female']:
    tile(['old-male-front.png','male-front.png','old-female-front.png','female-front.png'],
         'old-new-front.png','0_0|w0_0|0_h0|w0_h0')
(OUT/'report.json').write_text(json.dumps(report,indent=2)+'\n')
print('V3_REVIEW_COMPLETE '+json.dumps(report))
