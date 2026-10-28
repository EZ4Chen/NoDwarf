extends "res://Scripts/CharacterLabV2/DwarfModel.gd"
## Production wrapper: reuse the validated name-bound rig and body masks.
@export var asset_directory := "res://Assets/Characters/Whitebody/male/"

func _ready() -> void:
	ASSETS = asset_directory
	super._ready()
	build_guard_pose()
	player.stop()

func sync_colony_equipment(items: PackedStringArray) -> void:
	var desired: Array[String] = []
	for item in items:
		if manifest.equipment.has(item):
			desired.append(item)
	for item in equipped.keys():
		if not desired.has(item):
			set_equipment(item, false)
	for item in desired:
		set_equipment(item, true)

func set_equipment_scale(item: String, value: Vector3) -> void:
	if equipped.has(item) and manifest.equipment[item].mode == "attachment":
		for node in equipped[item]:
			node.scale = value

# Temporary static guard, shared by renderer and offline grip baking.
# Two-bone positioning preserves arm lengths instead of floating equipment away from the hand.
func build_guard_pose() -> void:
	if player.has_animation("Guard"): return
	player.play("Idle",0)
	player.advance(0)
	player.seek(0,true)
	skeleton.force_update_all_bone_transforms()
	var shield_basis=Basis(Vector3.RIGHT,Vector3.FORWARD * -1,Vector3.DOWN)
	var blade_z=Vector3(.95,.3,0).normalized()
	var sword_basis=Basis(Vector3(0,0,-1),blade_z.cross(Vector3(0,0,-1)),blade_z)
	guard_arm("L",Vector3(.08,1.19,.45),shield_basis)
	guard_arm("R",Vector3(-.20,1.0,.38),sword_basis)
	var animation=Animation.new()
	animation.length=1
	var path=str(player.get_node(player.root_node).get_path_to(skeleton))
	for bone in range(skeleton.get_bone_count()):
		var bone_path=NodePath(path+":"+skeleton.get_bone_name(bone))
		var position_track=animation.add_track(Animation.TYPE_POSITION_3D)
		animation.track_set_path(position_track,bone_path)
		animation.position_track_insert_key(position_track,0,skeleton.get_bone_pose_position(bone))
		var rotation_track=animation.add_track(Animation.TYPE_ROTATION_3D)
		animation.track_set_path(rotation_track,bone_path)
		animation.rotation_track_insert_key(rotation_track,0,skeleton.get_bone_pose_rotation(bone))
	var library=player.get_animation_library("")
	if library.has_animation("Guard"): library.remove_animation("Guard")
	library.add_animation("Guard",animation)
	player.stop()

func guard_arm(side: String, target: Vector3, grip_basis: Basis) -> void:
	var upper=skeleton.find_bone("upper_arm."+side)
	var lower=skeleton.find_bone("forearm."+side)
	var hand=skeleton.find_bone("hand."+side)
	var slot=skeleton.find_bone("slot_hand_"+side.to_lower())
	var u=skeleton.get_bone_global_pose(upper)
	var l=skeleton.get_bone_global_pose(lower)
	var h=skeleton.get_bone_global_pose(hand)
	var local_slot=h.affine_inverse()*skeleton.get_bone_global_pose(slot)
	var hand_basis=grip_basis*local_slot.basis.inverse()
	var wrist=target-hand_basis*local_slot.origin
	var length1=u.origin.distance_to(l.origin)
	var length2=l.origin.distance_to(h.origin)
	var direction=(wrist-u.origin).normalized()
	var distance=clampf(wrist.distance_to(u.origin),absf(length1-length2)+.001,length1+length2-.001)
	wrist=u.origin+direction*distance
	var along=(length1*length1-length2*length2+distance*distance)/(2*distance)
	var bend=Vector3(1 if side=="L" else -1,-1,0)
	bend=(bend-direction*bend.dot(direction)).normalized()
	var elbow=u.origin+direction*along+bend*sqrt(maxf(0,length1*length1-along*along))
	var upper_basis=Basis(Quaternion((l.origin-u.origin).normalized(),(elbow-u.origin).normalized()))*u.basis
	var lower_basis=Basis(Quaternion((h.origin-l.origin).normalized(),(wrist-elbow).normalized()))*l.basis
	skeleton.set_bone_global_pose(upper,Transform3D(upper_basis,u.origin))
	skeleton.set_bone_global_pose(lower,Transform3D(lower_basis,elbow))
	skeleton.set_bone_global_pose(hand,Transform3D(hand_basis,wrist))
	skeleton.force_update_all_bone_transforms()
