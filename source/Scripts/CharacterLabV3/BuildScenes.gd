extends SceneTree
## Run after importing the V3 GLBs: godot --headless --path source --script res://Scripts/CharacterLabV3/BuildScenes.gd
const DEST := "res://Assets/Characters/DwarfV3/"
const REQUIRED := ["idle", "walk", "sword_shield_idle", "sword_slash", "block_raise", "block_hold", "block_hit", "hit", "down"]
const LOOPS := ["idle", "walk", "sword_shield_idle", "block_hold", "Mine", "Carry"]

func _initialize() -> void:
	build.call_deferred()

func own(node: Node, scene: Node) -> void:
	node.scene_file_path = ""
	for child in node.get_children():
		child.owner = scene
		own(child, scene)

func add_skin(path: String, skeleton: Skeleton3D, facing: Node3D) -> void:
	var imported: Node3D = load(path).instantiate()
	facing.add_child(imported)
	for mesh: MeshInstance3D in imported.find_children("*", "MeshInstance3D", true, false):
		assert(mesh.skin != null, "Expected skinned mesh: " + path)
		var previous := mesh.get_node(mesh.skeleton) as Skeleton3D
		assert(previous != null, "Missing source skeleton: " + path)
		var skin := mesh.skin.duplicate() as Skin
		for bind in skin.get_bind_count():
			var bone_name := skin.get_bind_name(bind)
			if bone_name == &"":
				bone_name = previous.get_bone_name(skin.get_bind_bone(bind))
			var bone := skeleton.find_bone(bone_name)
			assert(bone >= 0, "Unknown bind bone: " + str(bone_name))
			skin.set_bind_name(bind, bone_name)
			skin.set_bind_bone(bind, bone)
		# Both imported roots share Facing, so keep the authored mesh transform.
		mesh.reparent(skeleton, true)
		mesh.skin = skin
		mesh.skeleton = NodePath("..")
		mesh.visible = true
	imported.free()

func rest_bounds(facing: Node3D) -> AABB:
	var bounds := AABB()
	var initialized := false
	for mesh: MeshInstance3D in facing.find_children("*", "MeshInstance3D", true, false):
		var local: Transform3D = facing.global_transform.affine_inverse() * mesh.global_transform
		var box: AABB = local * mesh.get_aabb()
		bounds = bounds.merge(box) if initialized else box
		initialized = true
	assert(initialized and bounds.size.y > 0.01, "Cannot normalize an empty character")
	return bounds

func build() -> void:
	# Resolve every dependency before saving any scene.
	for sex in ["male", "female"]:
		var files := ["dwarf_base", "base_clothed", "hair", "shirt", "trousers", "boots"]
		if sex == "male": files.append("beard")
		for file in files:
			var path: String = DEST + sex + "/" + file + ".glb"
			if not ResourceLoader.exists(path):
				push_error("Missing imported V3 asset: " + path)
				quit(1)
				return
	for sex in ["male", "female"]:
		for outfit in ["base", "clothed"]:
			var assets: String = DEST + sex + "/"
			var scene := Node3D.new()
			scene.name = "Dwarf"
			root.add_child(scene)
			var facing := Node3D.new()
			facing.name = "Facing"
			facing.rotation.y = PI
			scene.add_child(facing)
			var model: Node3D = load(assets + "dwarf_base.glb").instantiate()
			facing.add_child(model)
			var skeletons := model.find_children("*", "Skeleton3D", true, false)
			var players := model.find_children("*", "AnimationPlayer", true, false)
			assert(skeletons.size() == 1 and players.size() == 1)
			var skeleton: Skeleton3D = skeletons[0]
			var player: AnimationPlayer = players[0]
			player.autoplay = ""
			player.stop()
			skeleton.reset_bone_poses()
			for clip in REQUIRED:
				assert(player.has_animation(clip), "Missing animation: " + clip)
			for clip in player.get_animation_list():
				player.get_animation(clip).loop_mode = Animation.LOOP_LINEAR if clip in LOOPS else Animation.LOOP_NONE
			var original_meshes := model.find_children("*", "MeshInstance3D", true, false)
			# Use identical base+hair bounds for both outfits; clothes cannot change scale.
			add_skin(assets + "hair.glb", skeleton, facing)
			if sex == "male": add_skin(assets + "beard.glb", skeleton, facing)
			var bounds := rest_bounds(facing)
			if outfit == "clothed":
				for mesh in original_meshes:
					mesh.free()
				for file in ["base_clothed", "shirt", "trousers", "boots"]:
					add_skin(assets + file + ".glb", skeleton, facing)
			var normalization := 1.4 / bounds.size.y
			facing.scale = Vector3.ONE * normalization
			facing.position.y = -bounds.position.y * normalization
			scene.set_meta("normalized_height_with_hair_m", 1.4)
			for pair in [["slot_hand_r", "MainHandSocket"], ["slot_hand_l", "OffHandSocket"]]:
				assert(skeleton.find_bone(pair[0]) >= 0)
				var attachment := BoneAttachment3D.new()
				attachment.name = pair[1] + "Bone"
				attachment.bone_name = pair[0]
				skeleton.add_child(attachment)
				var socket := Node3D.new()
				socket.name = pair[1]
				attachment.add_child(socket)
			assert(scene.find_children("*", "Skeleton3D", true, false).size() == 1)
			assert(scene.find_children("*", "AnimationPlayer", true, false).size() == 1)
			own(scene, scene)
			var packed := PackedScene.new()
			assert(packed.pack(scene) == OK)
			assert(ResourceSaver.save(packed, assets + outfit + ".tscn") == OK)
			scene.free()
	print("V3_SCENES_PACKED")
	quit()
