"""Load established rigid weapons and fit them to the current DwarfRig rest sockets.

Usage from a Blender builder: tools = build(rig)
Returns {sword: [mesh], shield: [mesh], pickaxe: [mesh]}.
The output uses the SAME armature as the body, one hand bone per vertex.
No character proportions are applied to weapon vertices; only the socket rest transform.
"""
from pathlib import Path
import bpy
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[3]
SOURCES = {
    'sword': (ROOT/'art/dwarf/sword_shield/weapons/weapons.blend', 'sword_mesh', 'slot_hand_r', 'hand.R', True),
    'shield': (ROOT/'art/dwarf/sword_shield/weapons/weapons.blend', 'shield_mesh', 'slot_hand_l', 'hand.L', True),
    'pickaxe': (ROOT/'art/dwarf/v2/male/dwarf.blend', 'pickaxe', 'slot_hand_r', 'hand.R', False),
}

def build(rig):
    if rig.type != 'ARMATURE':raise TypeError('build_tools requires an Armature object')
    result={}
    collection=bpy.data.collections.get('V3Tools')
    if collection is None:
        collection=bpy.data.collections.new('V3Tools')
        bpy.context.scene.collection.children.link(collection)
    for category,(path,name,socket,hand,is_local) in SOURCES.items():
        if socket not in rig.data.bones or hand not in rig.data.bones:
            raise KeyError(f'{category}: missing {socket} or {hand}')
        if bpy.data.filepath and Path(bpy.data.filepath).resolve()==path.resolve():
            source=bpy.data.objects[name]
            ob=source.copy();ob.data=source.data.copy()
        else:
            with bpy.data.libraries.load(str(path),link=False) as (available,loaded):
                if name not in available.objects:raise KeyError(f'{path}: object {name} is missing')
                loaded.objects=[name]
            ob=loaded.objects[0]
        old_rig=ob.parent if ob.parent and ob.parent.type=='ARMATURE' else None
        # The standalone sword/shield source is already in socket-local space.
        # Old pickaxe source stores vertices in character rest/world coordinates.
        if not is_local:
            if old_rig is None or socket not in old_rig.data.bones:
                raise RuntimeError('Pickaxe source must retain its rest-socket reference')
            ob.data.transform(old_rig.data.bones[socket].matrix_local.inverted())
        local_positions=[v.co.copy() for v in ob.data.vertices]
        ob.modifiers.clear();ob.vertex_groups.clear();ob.parent=None
        ob.matrix_world=Matrix.Identity(4)
        ob.data.transform(rig.data.bones[socket].matrix_local)
        ob.name=category
        ob['category']=category
        ob['grip_socket']=socket
        ob['source_asset']=str(path.relative_to(ROOT))
        ob['rest_socket_local_front']=[0.0,1.0,0.0]
        ob['rest_socket_local_up']=[0.0,0.0,1.0]
        for col in list(ob.users_collection):col.objects.unlink(ob)
        collection.objects.link(ob)
        group=ob.vertex_groups.new(name=hand)
        group.add(list(range(len(ob.data.vertices))),1,'REPLACE')
        ob.parent=rig;ob.matrix_parent_inverse=Matrix.Identity(4)
        mod=ob.modifiers.new('Shared dwarf skeleton','ARMATURE');mod.object=rig
        ob.hide_set(False);ob.hide_render=True
        # This is a rigid coordinate conversion: lengths must survive unchanged.
        inverse=rig.data.bones[socket].matrix_local.inverted()
        max_error=max(((inverse@v.co)-before).length for v,before in zip(ob.data.vertices,local_positions))
        if max_error>1e-5:raise RuntimeError(f'{category} altered socket-local geometry: {max_error}')
        if category in ['sword','pickaxe']:
            spans=[max(v[i] for v in local_positions)-min(v[i] for v in local_positions) for i in range(3)]
            if spans[1]<3*spans[0]:raise RuntimeError(f'{category} cutting/head direction is sideways')
        # Remove only the unlinked source rig loaded as the pickaxe's dependency.
        if old_rig is not None and old_rig is not rig and not old_rig.users_collection:
            bpy.data.objects.remove(old_rig,do_unlink=True)
        result[category]=[ob]
    return result
