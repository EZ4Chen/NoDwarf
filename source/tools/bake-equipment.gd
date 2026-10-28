extends SceneTree
# Bake once, never at frame time. Pixel-silhouette extrusion matches item/generated.
var vertices := PackedVector3Array()
var normals := PackedVector3Array()
var uvs := PackedVector2Array()
var indices := PackedInt32Array()
var spec: Dictionary
var pixels: Image

func point(x: float, y: float, depth: float) -> Vector3:
	var u := (x - float(spec.Grip[0])) / 64.0
	var v := (float(spec.Grip[1]) - y) / 64.0
	return Vector3(depth, (v-u)*sqrt(0.5)*spec.PlaneSize, (u+v)*sqrt(0.5)*spec.PlaneSize)

func quad(points: Array[Vector3], texture_uv: Array[Vector2], normal: Vector3) -> void:
	var start := vertices.size()
	for i in 4:
		vertices.append(points[i]); normals.append(normal); uvs.append(texture_uv[i])
	for i in [0,1,2,0,2,3]: indices.append(start+i)

func opaque(x: int, y: int) -> bool:
	return x>=0 and y>=0 and x<64 and y<64 and pixels.get_pixel(x,y).a >= 0.4

func face(x: int, y: int, end: int, depth: float, normal: Vector3) -> void:
	quad([point(x,y,depth),point(end,y,depth),point(end,y+1,depth),point(x,y+1,depth)],
		[Vector2(x,y)/64,Vector2(end,y)/64,Vector2(end,y+1)/64,Vector2(x,y+1)/64],normal)

func _initialize() -> void:
	var catalog: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/Equipment/catalog.json"))
	var dimensions: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/Combat/equipment-geometry.json"))
	for id in catalog:
		spec = catalog[id]
		pixels = load(spec.Texture).get_image()
		if pixels.is_compressed(): pixels.decompress()
		assert(pixels.get_size()==Vector2i(64,64))
		vertices.clear(); normals.clear(); uvs.clear(); indices.clear()
		var half: float = spec.Thickness/2.0
		for y in 64:
			var x := 0
			while x<64:
				if not opaque(x,y): x+=1; continue
				var start := x
				while x<64 and opaque(x,y): x+=1
				face(start,y,x,half,Vector3.RIGHT)
				face(start,y,x,-half,Vector3.LEFT)
		for y in 64:
			for x in 64:
				if not opaque(x,y): continue
				for direction in [Vector2i.LEFT,Vector2i.RIGHT,Vector2i.UP,Vector2i.DOWN]:
					if opaque(x+direction.x,y+direction.y): continue
					var a := Vector2(x,y)
					var b := a
					if direction == Vector2i.LEFT: b+=Vector2(0,1)
					elif direction == Vector2i.RIGHT: a+=Vector2(1,0); b+=Vector2(1,1)
					elif direction == Vector2i.UP: b+=Vector2(1,0)
					else: a+=Vector2(0,1); b+=Vector2(1,1)
					var normal := (point(direction.x,direction.y,0)-point(0,0,0)).normalized()
					var uv := Vector2(x+0.5,y+0.5)/64.0
					quad([point(a.x,a.y,-half),point(b.x,b.y,-half),point(b.x,b.y,half),point(a.x,a.y,half)], [uv,uv,uv,uv],normal)
		var arrays := []
		arrays.resize(Mesh.ARRAY_MAX)
		arrays[Mesh.ARRAY_VERTEX]=vertices; arrays[Mesh.ARRAY_NORMAL]=normals
		arrays[Mesh.ARRAY_TEX_UV]=uvs; arrays[Mesh.ARRAY_INDEX]=indices
		var mesh := ArrayMesh.new()
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,arrays)
		var material := StandardMaterial3D.new()
		material.albedo_texture=load(spec.Texture)
		material.transparency=BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
		material.alpha_scissor_threshold=0.4
		material.cull_mode=BaseMaterial3D.CULL_DISABLED
		material.texture_filter=BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS_ANISOTROPIC
		material.roughness=0.85
		mesh.surface_set_material(0,material)
		assert(ResourceSaver.save(mesh,"res://Assets/Equipment/"+id+".res",ResourceSaver.FLAG_CHANGE_PATH)==OK)
		var holder := Node3D.new()
		holder.name=id
		var instance := MeshInstance3D.new()
		instance.name="ItemMesh"; instance.mesh=mesh
		holder.add_child(instance); instance.owner=holder
		var scene_file := FileAccess.open(spec.Scene,FileAccess.WRITE)
		scene_file.store_string('[gd_scene load_steps=2 format=3]\n\n[ext_resource type="ArrayMesh" path="res://Assets/Equipment/%s.res" id="1"]\n\n[node name="%s" type="Node3D"]\n\n[node name="ItemMesh" type="MeshInstance3D" parent="."]\nmesh = ExtResource("1")\n' % [id,id])
		scene_file.close()
		var bounds := mesh.get_aabb()
		var lo := bounds.position
		var hi := bounds.end
		var contact_y := clampf(0,lo.y,hi.y)
		if spec.Playable:
			dimensions[id]={"ReferenceMin":[lo.x,lo.y,lo.z],"ReferenceMax":[hi.x,hi.y,hi.z],
				"Size":[bounds.size.x,bounds.size.y,bounds.size.z],
				"ContactStart":[0,contact_y,hi.z-0.15],"ContactEnd":[0,contact_y,hi.z-0.02],
				"ContactRadius":minf(0.07,spec.Thickness),"MaterialId":"iron"}
		holder.free()
		print("EQUIPMENT_BAKED: ",id," triangles=",indices.size()/3," bounds=",bounds)
	var output := FileAccess.open("res://Assets/Combat/equipment-geometry.json",FileAccess.WRITE)
	output.store_string(JSON.stringify(dimensions,"  ")+"\n")
	quit()
