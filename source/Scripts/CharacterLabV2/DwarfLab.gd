extends Node3D
## Run this scene directly. --dwarf-lab-test validates exported assets and captures poses.
const MODEL = preload("res://Scripts/CharacterLabV2/DwarfModel.gd")
var dwarf: Node3D
var pivot: Node3D
var camera: Camera3D
var status: Label
var animation_select: OptionButton
var outfit_select: OptionButton
var item_checks: Dictionary = {}
var syncing := false
var rotating := false
var test_mode := false
var output_dir: String = "res://.local/dwarf-v2-" + ("female" if "--female" in OS.get_cmdline_user_args() else "male")
var ui_layer: CanvasLayer
var preset_keys := ["base", "casual", "miner", "guard"]
var clip_keys := ["Idle", "Walk", "Mine", "Attack", "Carry"]

func _ready() -> void:
	test_mode = "--dwarf-lab-test" in OS.get_cmdline_user_args()
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color("233038")
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color("e2eced")
	env.environment.ambient_light_energy = .65
	env.environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	add_child(env)
	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-42, -35, 0)
	key.light_energy = 1.1
	key.shadow_enabled = true
	add_child(key)
	var fill := DirectionalLight3D.new()
	fill.rotation_degrees = Vector3(-25, 145, 0)
	fill.light_energy = .45
	add_child(fill)
	var floor_mesh := MeshInstance3D.new()
	var plane := CylinderMesh.new()
	plane.top_radius = 1.1; plane.bottom_radius = 1.1; plane.height = .065
	plane.radial_segments = 48
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color("465354"); mat.roughness = .9
	plane.material = mat; floor_mesh.mesh = plane
	floor_mesh.position.y = -.04
	add_child(floor_mesh)
	pivot = Node3D.new(); add_child(pivot)
	dwarf = MODEL.new(); pivot.add_child(dwarf)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 2.30
	camera.position = Vector3(-2.7, 2.25, 4.5)
	add_child(camera)
	camera.look_at(Vector3(0,.80,0))
	camera.h_offset = -.32
	_build_ui()
	dwarf.preset("base")
	_sync_checks()
	if test_mode:
		get_tree().create_timer(60).timeout.connect(func(): push_error("Dwarf validation timed out"); get_tree().quit(1))
		_validate.call_deferred()

func _build_ui() -> void:
	var layer := CanvasLayer.new(); add_child(layer); ui_layer = layer
	var panel := PanelContainer.new()
	panel.position = Vector2(22,22); panel.custom_minimum_size = Vector2(250,0)
	layer.add_child(panel)
	var margin := MarginContainer.new()
	for side in ["left","top","right","bottom"]: margin.add_theme_constant_override("margin_" + side,16)
	panel.add_child(margin)
	var list := VBoxContainer.new(); list.add_theme_constant_override("separation",10); margin.add_child(list)
	var heading := Label.new(); heading.text = "矮人 · 模型与换装"; heading.add_theme_font_size_override("font_size",22); list.add_child(heading)
	var outfit_label := Label.new(); outfit_label.text = "穿戴方案"; list.add_child(outfit_label)
	outfit_select = OptionButton.new()
	outfit_select.accessibility_name = "穿戴方案"
	for text in ["基础身体（内衣内裤）","日常衣着","矿工","守卫"]: outfit_select.add_item(text)
	outfit_select.item_selected.connect(func(index): dwarf.preset(preset_keys[index]); _sync_checks())
	list.add_child(outfit_select)
	var animation_label := Label.new(); animation_label.text = "动作"; list.add_child(animation_label)
	animation_select = OptionButton.new()
	animation_select.accessibility_name = "动作"
	for text in ["站立呼吸","行走","单手采矿","单手攻击","搬运行走"]: animation_select.add_item(text)
	animation_select.item_selected.connect(func(index): dwarf.play(clip_keys[index]))
	list.add_child(animation_select)
	var pause := CheckButton.new(); pause.text = "暂停动作"
	pause.accessibility_name = pause.text
	pause.toggled.connect(func(value): dwarf.player.speed_scale = 0 if value else 1)
	list.add_child(pause)
	for pair in [["cosmetic_beard","胡须"],["shirt","外衣"],["armor","皮甲与肩甲"],["trousers","长裤"],["boots","靴子"],["helmet","铁盔"],["pickaxe","镐（右手）"],["sword","剑（右手）"],["shield","盾（左手）"]]:
		var check := CheckBox.new(); check.text = pair[1]
		check.accessibility_name = check.text
		var item: String = pair[0]
		check.toggled.connect(func(value):
			if not syncing: dwarf.set_equipment(item,value); _sync_checks())
		item_checks[item] = check; list.add_child(check)
	var rotate := CheckButton.new(); rotate.text = "自动转身查看"
	rotate.accessibility_name = rotate.text
	rotate.toggled.connect(func(value): rotating = value)
	list.add_child(rotate)
	var turn := HSlider.new(); turn.min_value = -180; turn.max_value = 180; turn.step = 1
	turn.tooltip_text = "转动角色"; turn.custom_minimum_size.y = 24
	turn.accessibility_name = "查看角度"
	turn.value_changed.connect(func(value): pivot.rotation.y = deg_to_rad(value))
	var turn_label := Label.new(); turn_label.text = "查看角度"; list.add_child(turn_label); list.add_child(turn)
	status = Label.new(); status.add_theme_font_size_override("font_size",14); list.add_child(status)

func _sync_checks() -> void:
	syncing = true
	for key in item_checks: item_checks[key].button_pressed = dwarf.equipped.has(key)
	syncing = false
	var stats: Dictionary = dwarf.geometry_stats()
	status.text = "%d 三角形 · %d 绘制表面\n%d 骨骼（含 5 个挂点）" % [stats.triangles,stats.surfaces,stats.bones]

func _process(delta: float) -> void:
	if rotating: pivot.rotation.y += delta * .4

func _frame() -> void:
	await get_tree().process_frame
	await get_tree().process_frame

func _capture(file_name: String) -> void:
	if DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	var error := get_viewport().get_texture().get_image().save_png(output_dir + "/" + file_name + ".png")
	assert(error == OK, "Capture failed")

func _pose(clip: String, fraction: float) -> void:
	dwarf.player.stop()
	dwarf.skeleton.reset_bone_poses()
	dwarf.player.play(clip,0)
	dwarf.player.advance(0)
	dwarf.player.seek(dwarf.player.get_animation(clip).length * fraction,true)
	dwarf.player.pause()
	dwarf.skeleton.force_update_all_bone_transforms()

func _validate() -> void:
	DirAccess.make_dir_recursive_absolute(output_dir)
	var report := {"presets":{},"animation_samples":0,"individual_slots":0,"socket_checks":0}
	# Exercise arbitrary slot removal, not just the four predefined outfits.
	for item in dwarf.manifest.equipment:
		dwarf.preset("base"); dwarf.set_equipment("cosmetic_beard",false)
		dwarf.set_equipment(item,true)
		assert(dwarf.equipped.has(item))
		dwarf.set_equipment(item,false)
		assert(dwarf.body_bits == "000")
		report.individual_slots += 1
	for preset_index in preset_keys.size():
		var key: String = preset_keys[preset_index]
		dwarf.preset(key); outfit_select.select(preset_index); _sync_checks()
		report.presets[key] = dwarf.geometry_stats()
		for clip in clip_keys:
			dwarf.player.play(clip)
			dwarf.player.advance(0)
			var duration: float = dwarf.player.get_animation(clip).length
			for fraction in [0.0,.2,.4,.65,.85,1.0]:
				dwarf.player.seek(duration * fraction,true)
				dwarf.skeleton.force_update_all_bone_transforms()
				await _frame()
				for index in dwarf.skeleton.get_bone_count():
					var pose: Transform3D = dwarf.skeleton.get_bone_global_pose(index)
					assert(pose.is_finite(), "Invalid animation transform")
					assert(pose.origin.length() < 3, "Exploded rig")
				for socket_name in dwarf.sockets:
					var socket: BoneAttachment3D = dwarf.sockets[socket_name]
					var bone_index: int = dwarf.skeleton.find_bone(socket_name)
					var expected: Transform3D = dwarf.skeleton.global_transform * dwarf.skeleton.get_bone_global_pose(bone_index)
					assert(socket.global_transform.origin.distance_to(expected.origin) < .001, "Detached socket")
					report.socket_checks += 1
				report.animation_samples += 1
			dwarf.player.pause()
		_pose("Idle",0)
		await _frame(); await _capture(key)
	# Pose comparisons and orthographic turnarounds validate skinning and grip in-engine.
	for pose in [["miner","Walk",.2,"walk"],["miner","Mine",.36,"mine_raise"],["miner","Mine",.65,"mine_strike"],["guard","Attack",.48,"attack"],["casual","Carry",.2,"carry"]]:
		dwarf.preset(pose[0]); outfit_select.select(preset_keys.find(pose[0])); _sync_checks()
		animation_select.select(clip_keys.find(pose[1]))
		_pose(pose[1],pose[2])
		await _frame(); await _capture(pose[3])
	for angle in [90,180,270]:
		pivot.rotation_degrees.y = angle
		await _frame(); await _capture("turn_" + str(angle))
	# Loop seams and actual motion, using the exported clips rather than source poses.
	for clip in clip_keys:
		_pose(clip,0)
		var start: Array[Transform3D] = []
		for index in dwarf.skeleton.get_bone_count(): start.append(dwarf.skeleton.get_bone_pose(index))
		_pose(clip,.25)
		var moving := false
		for index in dwarf.skeleton.get_bone_count():
			if not start[index].is_equal_approx(dwarf.skeleton.get_bone_pose(index)): moving = true
		assert(moving, "Clip has no movement: " + clip)
		if dwarf.manifest.animations[clip].loop:
			_pose(clip,1)
			for index in dwarf.skeleton.get_bone_count():
				assert(start[index].is_equal_approx(dwarf.skeleton.get_bone_pose(index)), "Loop seam: " + clip)
	# No residual animation pose when returning to base. Root remains in place for navigation.
	dwarf.preset("base"); _pose("Walk",.25)
	assert(dwarf.skeleton.get_bone_pose_rotation(dwarf.skeleton.find_bone("thigh.L")).get_angle() > .1, "Walk has no leg motion")
	assert(dwarf.skeleton.get_bone_pose_position(dwarf.skeleton.find_bone("root")).length() < .001, "Unexpected root motion")
	for node in dwarf.find_children("*","MeshInstance3D",true,false):
		for surface in node.mesh.get_surface_count():
			assert(node.mesh.surface_get_material(surface).vertex_color_use_as_albedo,"Palette colors disabled")
	var max_reference_error := 0.0
	for reference in dwarf.manifest.reference_poses:
		_pose(reference.clip,reference.fraction)
		for bone_name in reference.positions:
			var p: Array = reference.positions[bone_name]
			var expected := Vector3(p[0],p[1],p[2])
			var actual: Vector3 = dwarf.skeleton.get_bone_global_pose(dwarf.skeleton.find_bone(bone_name)).origin
			max_reference_error = maxf(max_reference_error,actual.distance_to(expected))
			assert(actual.distance_to(expected) < .045,"Exported pose differs from Blender: %s / %s / %s" % [reference.clip,bone_name,actual.distance_to(expected)])
		if reference.has("tip_local"):
			var local: Array = reference.tip_local
			var target: Array = reference.tip_expected
			var slot_pose: Transform3D = dwarf.skeleton.get_bone_global_pose(dwarf.skeleton.find_bone("slot_hand_r"))
			var tip: Vector3 = slot_pose * Vector3(local[0],local[1],local[2])
			var error := tip.distance_to(Vector3(target[0],target[1],target[2]))
			assert(error < .045,"Tool direction differs from Blender: %s / %s" % [reference.clip,error])
	report["max_blender_reference_error_m"] = max_reference_error
	if "--dwarf-lab-film" in OS.get_cmdline_user_args(): await _record()
	await _gallery()
	var file := FileAccess.open(output_dir + "/validation.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t") + "\n"); file.close()
	print("DWARF_LAB_PASS " + JSON.stringify(report))
	get_tree().quit()

func _record() -> void:
	if DisplayServer.get_name() == "headless": return
	DirAccess.make_dir_recursive_absolute(output_dir + "/film")
	ui_layer.hide(); pivot.rotation.y = 0; camera.h_offset = 0
	var frame_index := 0
	for segment in [["base","Idle",24],["casual","Walk",24],["miner","Mine",36],["guard","Attack",24],["casual","Carry",24]]:
		dwarf.preset(segment[0])
		for frame in segment[2]:
			_pose(segment[1],float(frame)/segment[2])
			await _frame(); await _capture("film/frame_%04d" % frame_index)
			frame_index += 1

func _gallery() -> void:
	ui_layer.hide()
	dwarf.hide(); pivot.rotation.y = 0
	for node in get_children():
		if node is MeshInstance3D: node.hide()
	camera.size = 3.7; camera.h_offset = 0
	camera.position = Vector3(-.4,2.25,6)
	camera.look_at(Vector3(0,.86,0))
	for index in preset_keys.size():
		var model := MODEL.new(); add_child(model)
		model.position.x = (index - 1.5) * 1.3
		model.preset(preset_keys[index]); model.player.play("Idle",0); model.player.advance(0); model.player.seek(0,true); model.player.pause()
		var label := Label3D.new(); label.text = ["BASE","CASUAL","MINER","GUARD"][index]
		label.position = Vector3(model.position.x,-.14,0); label.font_size = 42; label.pixel_size = .003
		label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
		add_child(label)
	await _frame(); await _capture("lineup")
