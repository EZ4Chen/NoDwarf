extends SceneTree
func _initialize():
	call_deferred("bake")
func vector(v: Vector3):
	return [v.x,v.y,v.z]
func pose(model, bone):
	var t=model.skeleton.get_bone_global_pose(model.skeleton.find_bone(bone))
	return {"Origin":vector(t.origin),"X":vector(t.basis.x),"Y":vector(t.basis.y),"Z":vector(t.basis.z)}
func sample(model,clip,time):
	model.player.play(clip,0)
	model.player.advance(0)
	model.player.seek(time,true)
	model.skeleton.force_update_all_bone_transforms()
func bake():
	var model=load("res://Scenes/Characters/Dwarf.tscn").instantiate()
	root.add_child(model)
	var result={}
	for entry in [["sword","Attack",1.0],["pickaxe","Mine",1.5]]:
		var samples=[]
		for i in range(91):
			var time=entry[2]*i/90.0
			sample(model,entry[1],time)
			var p=pose(model,"slot_hand_r")
			p["Time"]=time
			samples.append(p)
		result[entry[0]]=samples
	sample(model,"Guard",.2)
	result["guard_right"]=[pose(model,"slot_hand_r")]
	result["guard_left"]=[pose(model,"slot_hand_l")]
	var file=FileAccess.open("res://Assets/Combat/grip-poses.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(result,"  "))
	print("GRIP_BAKE_OK: socket transforms only; no weapon dimensions")
	quit()
