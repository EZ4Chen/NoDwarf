extends SceneTree
const DEST := "res://Assets/Characters/SwordShield/Models/"
func _initialize() -> void:
	build.call_deferred()
func own(node: Node, scene: Node) -> void:
	node.scene_file_path=""
	for child in node.get_children():
		child.owner = scene
		own(child,scene)
func add_skin(path: String, skeleton: Skeleton3D) -> void:
	var imported: Node3D = load(path).instantiate()
	root.add_child(imported)
	for mesh: MeshInstance3D in imported.find_children("*","MeshInstance3D",true,false):
		var previous := mesh.get_node(mesh.skeleton) as Skeleton3D
		var skin := mesh.skin.duplicate() as Skin
		for bind in skin.get_bind_count():
			var name := skin.get_bind_name(bind)
			if name == &"": name = previous.get_bone_name(skin.get_bind_bone(bind))
			assert(skeleton.find_bone(name)>=0)
			skin.set_bind_name(bind,name)
			skin.set_bind_bone(bind,skeleton.find_bone(name))
		mesh.reparent(skeleton,false);mesh.transform=Transform3D.IDENTITY
		mesh.skin=skin;mesh.skeleton=NodePath("..")
	imported.free()
func build() -> void:
	for sex in ["male","female"]:
		for outfit in ["base","clothed"]:
			var scene := Node3D.new();scene.name="Dwarf";root.add_child(scene)
			var facing := Node3D.new();facing.name="Facing";facing.rotation.y=PI;scene.add_child(facing)
			var model: Node3D=load(DEST+sex+".glb").instantiate()
			for ap in model.find_children("*","AnimationPlayer",true,false):ap.autoplay=""
			facing.add_child(model)
			var body: MeshInstance3D=model.find_children("*","MeshInstance3D",true,false)[0]
			var bounds:=body.get_aabb()
			var normalization:=1.4 / bounds.size.y
			facing.scale=Vector3.ONE*normalization
			facing.position.y=-bounds.position.y*normalization
			scene.set_meta("authored_body_height_m",1.4)
			var skeleton: Skeleton3D=model.find_children("*","Skeleton3D",true,false)[0]
			var player: AnimationPlayer=model.find_children("*","AnimationPlayer",true,false)[0]
			player.stop()
			for clip in ["idle","walk","sword_shield_idle","sword_slash","block_raise","block_hold","block_hit","hit","down"]:
				assert(player.has_animation(clip),"Missing "+clip)
				player.get_animation(clip).loop_mode=Animation.LOOP_LINEAR if clip in ["idle","walk","sword_shield_idle","block_hold"] else Animation.LOOP_NONE
			for pair in [["slot_hand_r","MainHandSocket"],["slot_hand_l","OffHandSocket"]]:
				var attachment := BoneAttachment3D.new();attachment.name=pair[1]+"Bone";attachment.bone_name=pair[0];skeleton.add_child(attachment)
				var socket := Node3D.new();socket.name=pair[1];attachment.add_child(socket)
			var assets: String = "res://Assets/Characters/DwarfV2/"+sex+"/"
			if outfit=="clothed":
				for mesh in model.find_children("*","MeshInstance3D",true,false):mesh.free()
				for file in ["body_mask_111","shirt","trousers","boots"]:add_skin(assets+file+".glb",skeleton)
			if sex=="male":add_skin(assets+"cosmetic_beard.glb",skeleton)
			own(scene,scene)
			var packed := PackedScene.new();assert(packed.pack(scene)==OK)
			assert(ResourceSaver.save(packed,DEST+sex+"_"+outfit+".tscn")==OK)
			scene.free()
	print("SWORD_SHIELD_SCENES_PACKED")
	quit()
