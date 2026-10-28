import bpy, bmesh, json
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/ez4chen/NoDwarf/art/dwarf/asset_candidates/oga_dwarf')
scene=bpy.context.scene
body=bpy.data.objects['body']
for name in ['b_diff','a_diff','c_diff','leather_diffuse']:
    image=bpy.data.images.get(name)
    if image: image.colorspace_settings.name='sRGB'

# The legacy file has no active-render UV layer. Explicit mapping is essential.
for obj in scene.objects:
    if obj.type!='MESH' or not obj.data.uv_layers:
        continue
    uv=obj.data.uv_layers.get('UVTex') or obj.data.uv_layers[0]
    uv.active_render=True
    for mat in obj.data.materials:
        if mat and mat.node_tree:
            for tex in [n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE']:
                mapping=mat.node_tree.nodes.new('ShaderNodeUVMap'); mapping.uv_map=uv.name
                mat.node_tree.links.new(mapping.outputs['UV'],tex.inputs['Vector'])

def plain_material(name,color):
    mat=bpy.data.materials.new(name); mat.use_nodes=True
    shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value=(*color,1)
    shader.inputs['Roughness'].default_value=.9
    mat.diffuse_color=(*color,1)
    return mat

fabric=plain_material('Underwear - natural linen',(.54,.48,.37))
band_mat=plain_material('Underwear - waistband',(.35,.30,.22))

def body_shell(name,zmin,zmax,offset,mat):
    obj=body.copy(); obj.data=body.data.copy(); obj.name=name
    scene.collection.objects.link(obj)
    bm=bmesh.new(); bm.from_mesh(obj.data)
    # Remove distant arms before clipping; the shell retains deform weights.
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if abs(v.co.x)>.48],context='VERTS')
    for level,normal in [(zmin,(0,0,1)),(zmax,(0,0,-1))]:
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-6,
                              plane_co=(0,0,level),plane_no=normal,clear_inner=True,clear_outer=False)
    bm.normal_update()
    for v in bm.verts: v.co+=v.normal*offset
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    obj.data.materials.clear(); obj.data.materials.append(mat)
    for p in obj.data.polygons: p.material_index=0; p.use_smooth=True
    solid=obj.modifiers.new('Fabric thickness','SOLIDIFY'); solid.thickness=.004; solid.offset=0
    return obj

shorts=body_shell('Underwear shorts',.67,1.04,.012,fabric)
band=body_shell('Underwear waistband',1.005,1.041,.018,band_mat)

# Keep the original body topology and bind data unchanged.
body['asset_source']='https://opengameart.org/content/dwarf-fixed'
body['attribution']='thecubber; commissioned by OpenGameArt.org; modifications by Beast'
body['license']='CC BY 3.0 https://creativecommons.org/licenses/by/3.0/'
body['local_changes']='Equipment hidden; Blender 5 materials restored; separate weighted underwear added.'
scene['status']='Male base body with source rig; animation clips not included.'
scene.render.use_compositing=False

def aim(obj,at): obj.rotation_euler=(Vector(at)-obj.location).to_track_quat('-Z','Y').to_euler()
cam=scene.camera
for name,loc in [('whitebody-front',(0,-5,1.15)),('whitebody-back',(0,5,1.15)),('whitebody-review',(2.6,-5,2.25))]:
    cam.location=loc; aim(cam,(0,0,.95))
    scene.render.filepath=str(ROOT/(name+'.png'))
    bpy.ops.render.render(write_still=True)

for obj in bpy.context.selected_objects: obj.select_set(False)
body.hide_set(False); body.select_set(True); bpy.context.view_layer.objects.active=body
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.type='MATERIAL'
            area.spaces.active.region_3d.view_perspective='CAMERA'
note=bpy.data.texts.new('SOURCE AND MODIFICATIONS')
note.write((ROOT/'provenance.json').read_text()+'\n\nLocal modifications: equipment hidden, explicit UV mapping and modern shaders, weighted underwear added. Original body and rigs retained. No animation clips.\n')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'dwarf-male-whitebody.blend'))
print('WHITEBODY_SAVED',len(shorts.data.vertices),len(band.data.vertices))
