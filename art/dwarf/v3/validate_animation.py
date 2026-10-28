"""Validate reference-body replacements against the existing sword/shield rig.

Blender --background --python art/dwarf/v3/validate_animation.py
Blender --background --python art/dwarf/v3/validate_animation.py -- --sex female
Optional: --input FILE --reference FILE --output FILE --geometry-samples 5.
Missing candidates fail explicitly; this script never edits or saves a .blend.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[3]
REQUIRED = ('idle', 'walk', 'sword_shield_idle', 'sword_slash',
            'block_raise', 'block_hold', 'block_hit', 'hit', 'down')
LOOPS = {'idle', 'walk', 'sword_shield_idle', 'block_hold', 'Mine', 'Carry'}
SOCKETS = ('slot_hand_r', 'slot_hand_l', 'slot_head', 'slot_back', 'slot_belt')


def flat(matrix):
    return [float(v) for row in matrix for v in row]


def finite(values):
    return all(math.isfinite(v) for v in values)


def difference(a, b):
    return max(abs(x-y) for x, y in zip(a, b))


def rig_in_scene():
    rigs = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if len(rigs) != 1:
        raise ValueError(f'Expected one armature; found {len(rigs)}')
    return rigs[0]


def bone_signature(rig):
    return {b.name: {'parent': b.parent.name if b.parent else None,
                     'rest': flat(b.matrix_local)} for b in rig.data.bones}


def actions_for(rig):
    found = {}
    if rig.animation_data:
        for track in rig.animation_data.nla_tracks:
            for strip in track.strips:
                if strip.action:
                    found[strip.action.name] = strip.action
        if rig.animation_data.action:
            found[rig.animation_data.action.name] = rig.animation_data.action
    # Required actions may exist as fake-user actions without an NLA strip.
    for name in REQUIRED:
        action = bpy.data.actions.get(name)
        if action:
            found[name] = action
    return found


def reset_pose(rig):
    for p in rig.pose.bones:
        p.location = (0, 0, 0)
        p.rotation_euler = (0, 0, 0)
        p.rotation_quaternion = (1, 0, 0, 0)
        p.scale = (1, 1, 1)


def bound_meshes(rig):
    """Exclude the studio floor; include skinned meshes and bone-parented props."""
    result = []
    for ob in bpy.context.scene.objects:
        if ob.type != 'MESH':
            continue
        if any(m.type == 'ARMATURE' and m.object == rig for m in ob.modifiers):
            result.append(ob)
        elif ob.parent == rig and ob.parent_type == 'BONE':
            result.append(ob)
    return result


def mesh_bounds(meshes):
    deps = bpy.context.evaluated_depsgraph_get()
    report = {}
    for ob in meshes:
        evaluated = ob.evaluated_get(deps)
        data = evaluated.to_mesh()
        try:
            low = [math.inf]*3
            high = [-math.inf]*3
            invalid = 0
            for vertex in data.vertices:
                point = evaluated.matrix_world @ vertex.co
                if not finite(point):
                    invalid += 1
                    continue
                for axis in range(3):
                    low[axis] = min(low[axis], point[axis])
                    high[axis] = max(high[axis], point[axis])
            if not len(data.vertices) or invalid == len(data.vertices):
                low = high = [0, 0, 0]
            report[ob.name] = {'minimum': low, 'maximum': high,
                               'vertices': len(data.vertices),
                               'invalid_vertices': invalid,
                               'hidden_in_render': bool(ob.hide_render)}
        finally:
            evaluated.to_mesh_clear()
    return report


def check_candidate(candidate, reference, sample_count):
    result = {'candidate': str(candidate), 'reference': str(reference),
              'errors': [], 'warnings': [], 'actions': {}}
    errors = result['errors']
    warnings = result['warnings']
    if not candidate.exists() or not reference.exists():
        errors.append('Missing candidate or reference .blend file')
        result['status'] = 'FAIL'
        return result

    bpy.ops.wm.open_mainfile(filepath=str(reference))
    baseline = bone_signature(rig_in_scene())
    bpy.ops.wm.open_mainfile(filepath=str(candidate))
    rig = rig_in_scene()
    signature = bone_signature(rig)
    result['bone_count'] = len(signature)
    if set(signature) != set(baseline):
        errors.append({'bone_names_changed': {'missing': sorted(set(baseline)-set(signature)),
                                              'added': sorted(set(signature)-set(baseline))}})
    for name in set(signature) & set(baseline):
        if signature[name]['parent'] != baseline[name]['parent']:
            errors.append(f'Changed bone parent: {name}')
        delta = difference(signature[name]['rest'], baseline[name]['rest'])
        if delta > 1e-5:
            errors.append(f'Changed rest matrix: {name} ({delta})')
    for socket in SOCKETS:
        if socket not in signature:
            errors.append(f'Missing equipment socket: {socket}')
    if 'root' not in signature:
        errors.append('Missing root bone')
        result['status'] = 'FAIL'
        return result

    actions = actions_for(rig)
    for name in REQUIRED:
        if name not in actions:
            errors.append(f'Missing required action: {name}')
    rig.animation_data_create()
    rig.animation_data.action = None
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    meshes = bound_meshes(rig)
    result['bound_meshes'] = [ob.name for ob in meshes]
    if not meshes:
        errors.append('No meshes bound to the character skeleton')
    object_matrix = flat(rig.matrix_world)
    total_samples = 0
    for name, action in sorted(actions.items()):
        start, end = action.frame_range
        # Include real endpoints and every integer sample inside the action.
        frames = sorted({float(start), float(end), *map(float, range(math.ceil(start), math.floor(end)+1))})
        if end <= start:
            errors.append(f'Zero-length action: {name}')
            continue
        geometry_frames = {frames[round(i*(len(frames)-1)/max(1, sample_count-1))]
                           for i in range(sample_count)}
        rig.animation_data.action = None
        reset_pose(rig)
        rig.animation_data.action = action
        first = last = None
        max_motion = max_root = max_extent = 0.0
        socket_samples = []
        geometry = []
        for frame in frames:
            bpy.context.scene.frame_set(int(frame), subframe=frame-int(frame))
            bpy.context.view_layer.update()
            pose = {p.name: flat(p.matrix) for p in rig.pose.bones}
            if first is None:
                first = pose
            last = pose
            for bone, matrix in pose.items():
                if not finite(matrix):
                    errors.append(f'Non-finite bone transform: {name}/{frame}/{bone}')
                max_motion = max(max_motion, difference(first[bone], matrix))
            root_error = difference(flat(rig.pose.bones['root'].matrix_basis), flat(Matrix.Identity(4)))
            max_root = max(max_root, root_error)
            if root_error > 1e-5:
                errors.append(f'Root motion: {name}/{frame} ({root_error})')
            if difference(flat(rig.matrix_world), object_matrix) > 1e-5:
                errors.append(f'Armature object moves: {name}/{frame}')
            socket_samples.append({'frame': frame, 'positions_blender':
                                   {n: list(rig.pose.bones[n].matrix.translation) for n in SOCKETS if n in pose}})
            if frame in geometry_frames:
                bounds = mesh_bounds(meshes)
                geometry.append({'frame': frame, 'meshes': bounds})
                for ob_name, b in bounds.items():
                    if b['invalid_vertices']:
                        errors.append(f'Non-finite skinned vertices: {name}/{frame}/{ob_name}')
                    extent = max(abs(x) for x in b['minimum']+b['maximum'])
                    max_extent = max(max_extent, extent)
                    if extent > 5:
                        errors.append(f'Exploded or misplaced mesh: {name}/{frame}/{ob_name} ({extent} m)')
                    if b['minimum'][2] < -.025 and not b['hidden_in_render']:
                        warnings.append(f'Visible mesh below ground: {name}/{frame}/{ob_name}: {b["minimum"][2]:.4f} m')
            total_samples += 1
        seam = max(difference(first[n], last[n]) for n in first)
        if name in LOOPS and seam > 1e-4:
            errors.append(f'Loop seam: {name} ({seam})')
        if name != 'block_hold' and max_motion < 1e-5:
            errors.append(f'Action has no pose motion: {name}')
        result['actions'][name] = {'frame_range': [start, end], 'samples': len(frames),
                                  'max_root_error': max_root, 'max_pose_change': max_motion,
                                  'loop_seam_error': seam, 'max_mesh_extent_m': max_extent,
                                  'socket_samples': socket_samples, 'geometry_samples': geometry}
    result['total_pose_samples'] = total_samples
    result['warnings'] = sorted(set(warnings))
    result['status'] = 'FAIL' if errors else ('PASS_WITH_WARNINGS' if warnings else 'PASS')
    result['limits'] = 'Checks finite deformation, ground penetration, unchanged rig and animation sampling. Does not prove absence of body/weapon/garment interpenetration; review rendered poses separately.'
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--sex', choices=['male', 'female', 'both'], default='both')
    parser.add_argument('--input', type=Path)
    parser.add_argument('--reference', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--geometry-samples', type=int, default=5)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.input and args.sex == 'both':
        parser.error('--input requires --sex male or female')
    sexes = ['male', 'female'] if args.sex == 'both' else [args.sex]
    report = {'checks': {}, 'status': 'PASS'}
    for sex in sexes:
        candidate = args.input or ROOT/'art/dwarf/v3'/sex/'dwarf.blend'
        reference = args.reference or ROOT/'art/dwarf/sword_shield'/f'{sex}.blend'
        try:
            report['checks'][sex] = check_candidate(candidate, reference, max(2, args.geometry_samples))
        except Exception as exc:
            report['checks'][sex] = {'status': 'FAIL', 'errors': [repr(exc)]}
    statuses = [r['status'] for r in report['checks'].values()]
    report['status'] = 'FAIL' if 'FAIL' in statuses else ('PASS_WITH_WARNINGS' if 'PASS_WITH_WARNINGS' in statuses else 'PASS')
    output = args.output or ROOT/'art/dwarf/v3'/('animation-validation.json' if args.sex == 'both' else f'{args.sex}-animation-validation.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    # Invalid source transforms must still produce a usable failure report.
    def json_safe(value):
        if isinstance(value, dict):
            return {k: json_safe(v) for k, v in value.items()}
        if isinstance(value, (list, tuple)):
            return [json_safe(v) for v in value]
        if isinstance(value, float) and not math.isfinite(value):
            return str(value)
        return value
    output.write_text(json.dumps(json_safe(report), indent=2, allow_nan=False)+'\n')
    print('V3_ANIMATION_VALIDATION', report['status'], str(output))
    for sex, check in report['checks'].items():
        print(sex, check['status'], 'samples=', check.get('total_pose_samples', 0),
              'errors=', len(check.get('errors', [])), 'warnings=', len(check.get('warnings', [])))
        for error in check.get('errors', []):
            print('ERROR', error)
    if report['status'] == 'FAIL':
        sys.exit(1)


if __name__ == '__main__':
    main()
