@tool
extends EditorScenePostImport
## Godot's glTF importer can disable vertex colors on a reused palette material.
## Enforce the authored material once on import, rather than making per-resident copies.
func _post_import(scene: Node) -> Object:
	for node in scene.find_children("*", "MeshInstance3D", true, false):
		for surface in node.mesh.get_surface_count():
			var mat: Material = node.mesh.surface_get_material(surface)
			if mat is StandardMaterial3D and mat.resource_name.begins_with("Dwarf palette"):
				mat.vertex_color_use_as_albedo = true
				mat.vertex_color_is_srgb = false
				mat.cull_mode = BaseMaterial3D.CULL_BACK
	return scene
