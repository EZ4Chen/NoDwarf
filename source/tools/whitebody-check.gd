extends SceneTree

func _initialize():call_deferred("check_models")

func require(value: bool, message: String):
	if not value:
		push_error(message)
		quit(1)
		assert(value,message)

func sample(model,clip,time):
	model.player.assigned_animation=clip
	model.player.seek(time,true)
	model.skeleton.force_update_all_bone_transforms()

func check_models():
	var report={}
	for scene in ["Dwarf","DwarfFemale"]:
		var model=load("res://Scenes/Characters/"+scene+".tscn").instantiate()
		root.add_child(model)
		require(model.ASSETS.contains("Whitebody/"),"Old model still active: "+scene)
		require(model.skeleton.get_bone_count()==65,"Unexpected rig: "+scene)
		for entry in [["Idle",2.0],["Walk",1.0],["Mine",1.5],["Attack",1.0],["Carry",1.0]]:
			var clip=entry[0]
			require(absf(model.player.get_animation(clip).length-entry[1])<.001,"Clip duration changed: "+clip)
			sample(model,clip,0)
			var initial=[]
			for i in model.skeleton.get_bone_count():initial.append(model.skeleton.get_bone_pose(i))
			sample(model,clip,entry[1]*.25)
			var moves=false
			for i in model.skeleton.get_bone_count():
				var pose=model.skeleton.get_bone_pose(i)
				require(pose.is_finite(),"Nonfinite pose: "+clip)
				moves=moves or not pose.is_equal_approx(initial[i])
			require(moves,"Frozen clip: "+clip)
			if model.manifest.animations[clip].loop:
				sample(model,clip,entry[1]-.00001)
				for i in model.skeleton.get_bone_count():
					var end=model.skeleton.get_bone_pose(i)
					require(end.origin.distance_to(initial[i].origin)<.002,"Loop position seam: "+clip)
					require(end.basis.get_rotation_quaternion().angle_to(initial[i].basis.get_rotation_quaternion())<.01,"Loop rotation seam: "+clip)
		for bits in range(8):
			for i in range(3):model.set_equipment(["shirt","trousers","boots"][i],bool(bits & (1<<i)))
			require(model.find_children("*","Skeleton3D",true,false).size()==1,"Duplicate skeleton after change")
			require(model.body_meshes.size()>0,"Missing body mask")
			for mesh in model.find_children("*","MeshInstance3D",true,false):
				require(mesh.mesh.get_surface_count()>0,"Empty clothing mesh")
				if mesh.skin:
					for bind in mesh.skin.get_bind_count():
						require(model.skeleton.find_bone(mesh.skin.get_bind_name(bind))>=0,"Invalid skin binding")
		model.preset("guard")
		require(model.sockets.slot_hand_l.get_child_count()==1 and model.sockets.slot_hand_r.get_child_count()==1,"Missing hand equipment")
		model.preset("base")
		require(model.sockets.slot_hand_l.get_child_count()==0 and model.sockets.slot_hand_r.get_child_count()==0,"Equipment ghost after removal")
		report[scene]=model.geometry_stats()
		model.free()
	var file=FileAccess.open("res://.local/whitebody-validation.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"  "))
	print("WHITEBODY_CHECK_OK: new male/female assets, 65 bones, 5 moving clips, exact durations, loop seams, all 8 clothing masks, hand gear removal; ",JSON.stringify(report))
	quit()
