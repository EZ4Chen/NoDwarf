import bpy, json, math
from pathlib import Path
from mathutils import Vector

ROOT = Path('/Users/ez4chen/NoDwarf/art/dwarf/asset_candidates/oga_dwarf')
scene = bpy.context.scene
body = bpy.data.objects['body']

# Retain source equipment and original rigs, but separate their visibility.
archive = bpy.data.collections.new('Equipment - hidden, reversible')
scene.collection.children.link(archive)
for name in ['armor', 'cloth', 'leather']:
    obj = bpy.data.objects[name]
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)
    archive.objects.link(obj)
    obj.hide_render = True
    obj.hide_set(True)

# Reconnect the original packed color maps to modern Blender shader nodes.
for mat_name, tex_name in [('body_material','b_diff'), ('armor_material','a_diff'),
                           ('cloth_mat','c_diff'), ('leather_mat','leather_diffuse')]:
    mat = bpy.data.materials[mat_name]
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    out = nodes.new('ShaderNodeOutputMaterial')
    shader = nodes.new('ShaderNodeBsdfPrincipled')
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images[tex_name]
    shader.inputs['Roughness'].default_value = .78
    mat.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    mat.node_tree.links.new(shader.outputs['BSDF'], out.inputs['Surface'])

for obj in list(scene.objects):
    if obj.type == 'LIGHT' or obj.name == 'Plane':
        obj.hide_render = True
        obj.hide_set(True)
for obj in scene.objects:
    if obj.type == 'ARMATURE':
        obj.hide_render = True
        obj.hide_set(True)

world = bpy.data.worlds.new('Whitebody studio')
world.use_nodes = True
next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND').inputs[0].default_value = (.11,.13,.16,1)
next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND').inputs[1].default_value = .5
scene.world = world

def aim(obj, at):
    obj.rotation_euler = (Vector(at)-obj.location).to_track_quat('-Z','Y').to_euler()

for name, loc, power, size in [('Studio key',(-3,-4,5),500,4),('Studio fill',(3,-2,3),300,3),('Studio rim',(1,3,4),600,3)]:
    data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
    obj=bpy.data.objects.new(name,data); scene.collection.objects.link(obj); obj.location=loc; aim(obj,(0,0,1))

cam=scene.camera
cam.data.type='ORTHO'; cam.data.ortho_scale=2.22
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=800; scene.render.resolution_y=900; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='AgX'
scene.view_settings.exposure=0
scene.render.film_transparent=False
scene.render.filepath=str(ROOT/'body-inspection.png')
cam.location=(2.6,-5,2.4); aim(cam,(0,0,.95))
bpy.ops.render.render(write_still=True)

# Connected islands reveal whether boots or headgear are baked into body geometry.
mesh=body.data
neighbors=[set() for _ in mesh.vertices]
for e in mesh.edges:
    a,b=e.vertices; neighbors[a].add(b); neighbors[b].add(a)
remaining=set(range(len(mesh.vertices))); components=[]
while remaining:
    seed=remaining.pop(); stack=[seed]; comp=[seed]
    while stack:
        for other in neighbors[stack.pop()] & remaining:
            remaining.remove(other); stack.append(other); comp.append(other)
    components.append({'vertices':len(comp),'min':[min(mesh.vertices[i].co[j] for i in comp) for j in range(3)],'max':[max(mesh.vertices[i].co[j] for i in comp) for j in range(3)]})
(ROOT/'body-inspection.json').write_text(json.dumps(components,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'whitebody-work.blend'))
print('COMPONENTS',json.dumps(components))
