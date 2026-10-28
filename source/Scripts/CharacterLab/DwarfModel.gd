extends Node3D
## Shared-skeleton modular character; standalone from colony simulation.
## Equipment meshes are rebound by bone NAME, never by assumed glTF bone ordering.

const ASSETS := "res://Assets/Characters/Dwarf/"
var skeleton: Skeleton3D
var player: AnimationPlayer
var manifest: Dictionary
var base_scene: Node3D
var body_meshes: Array[MeshInstance3D] = []
var equipped: Dictionary = {}
var sockets: Dictionary = {}
var body_bits := "000"
var current_clip := "Idle"

func _ready() -> void:
	manifest = JSON.parse_string(FileAccess.get_file_as_string(ASSETS + "manifest.json"))
	base_scene = load(ASSETS + "dwarf_base.glb").instantiate()
	add_child(base_scene)
	skeleton = base_scene.find_children("*", "Skeleton3D", true, false)[0]
	player = base_scene.find_children("*", "AnimationPlayer", true, false)[0]
	for node in base_scene.find_children("*", "MeshInstance3D", true, false):
		body_meshes.append(node)
	for bone_name in manifest.bones:
		assert(skeleton.find_bone(bone_name) >= 0, "Missing bone: " + bone_name)
		if bone_name.begins_with("slot_"):
			var attachment := BoneAttachment3D.new()
			attachment.name = bone_name
			attachment.bone_name = bone_name
			skeleton.add_child(attachment)
			sockets[bone_name] = attachment
	for clip in manifest.animations:
		assert(player.has_animation(clip), "Missing clip: " + clip)
		player.get_animation(clip).loop_mode = Animation.LOOP_LINEAR if manifest.animations[clip].loop else Animation.LOOP_NONE
	play("Idle")

func _skin_meshes(resource: String) -> Array[MeshInstance3D]:
	var imported: Node3D = load(ASSETS + resource).instantiate()
	# It must enter the tree for relative skeleton paths to resolve.
	add_child(imported)
	var result: Array[MeshInstance3D] = []
	for node: MeshInstance3D in imported.find_children("*", "MeshInstance3D", true, false):
		var old_skeleton := node.get_node_or_null(node.skeleton) as Skeleton3D
		assert(old_skeleton != null, "Equipment has no skeleton: " + resource)
		var skin := node.skin.duplicate() as Skin
		for bind in skin.get_bind_count():
			var bone_name := skin.get_bind_name(bind)
			if bone_name == &"":
				bone_name = old_skeleton.get_bone_name(skin.get_bind_bone(bind))
			assert(skeleton.find_bone(bone_name) >= 0, "Unknown bind: " + bone_name)
			skin.set_bind_name(bind, bone_name)
			skin.set_bind_bone(bind, skeleton.find_bone(bone_name))
		node.reparent(skeleton, false)
		node.transform = Transform3D.IDENTITY
		node.skin = skin
		node.skeleton = NodePath("..")
		result.append(node)
	imported.free()
	return result

func set_equipment(item: String, enabled: bool) -> void:
	assert(manifest.equipment.has(item), "Unknown item: " + item)
	if equipped.has(item) == enabled:
		return
	# These are alternatives for the same slot.
	if enabled:
		var alternatives: Array = []
		if item == "shirt": alternatives = ["armor"]
		if item == "armor": alternatives = ["shirt"]
		if item == "sword": alternatives = ["pickaxe"]
		if item == "pickaxe": alternatives = ["sword"]
		for other in alternatives: set_equipment(other, false)
		var spec: Dictionary = manifest.equipment[item]
		if spec.mode == "attachment":
			var attachment_mesh: Node3D = load(ASSETS + spec.file).instantiate()
			sockets[spec.socket].add_child(attachment_mesh)
			attachment_mesh.transform = Transform3D.IDENTITY
			equipped[item] = [attachment_mesh]
		else:
			equipped[item] = _skin_meshes(spec.file)
	else:
		for node in equipped[item]: node.free()
		equipped.erase(item)
	_update_body_mask()

func _update_body_mask() -> void:
	var next_bits := ("1" if equipped.has("shirt") or equipped.has("armor") else "0") + ("1" if equipped.has("trousers") else "0") + ("1" if equipped.has("boots") else "0")
	if next_bits == body_bits: return
	for mesh in body_meshes: mesh.free()
	body_meshes = _skin_meshes("dwarf_base.glb" if next_bits == "000" else "body_mask_" + next_bits + ".glb")
	body_bits = next_bits

func preset(key: String) -> void:
	for item in equipped.keys(): set_equipment(item, false)
	for item in manifest.presets[key]: set_equipment(item, true)

func play(clip: String) -> void:
	current_clip = clip
	player.play(clip, .12)

func geometry_stats() -> Dictionary:
	var triangles := 0
	var surfaces := 0
	for node in find_children("*", "MeshInstance3D", true, false):
		var mesh: Mesh = node.mesh
		for surface in mesh.get_surface_count():
			surfaces += 1
			var arrays: Array = mesh.surface_get_arrays(surface)
			var indices = arrays[Mesh.ARRAY_INDEX]
			triangles += (indices.size() if indices != null and indices.size() else arrays[Mesh.ARRAY_VERTEX].size()) / 3
	return {"triangles": triangles, "surfaces": surfaces, "bones": skeleton.get_bone_count()}
