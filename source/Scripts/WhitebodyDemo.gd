extends Node3D
## A live demonstration using the production scenes, meshes and animation clips.
var models: Array[Node3D] = []
var elapsed := 0.0
var playing := true
var status: Label
var female := false
var recording := false
var camera: Camera3D

func _ready():
	get_window().title = "NoDwarf · 新矮人演示"
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color("1c2930")
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color("f4eee3")
	env.environment.ambient_light_energy = .35
	add_child(env)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-40,-30,0)
	light.light_energy = .7
	light.shadow_enabled = true
	add_child(light)
	var fill := DirectionalLight3D.new()
	fill.rotation_degrees = Vector3(-25,140,0)
	fill.light_energy = .15
	add_child(fill)
	for x in [-1.55,0.0,1.55]:
		var floor_mesh := MeshInstance3D.new()
		var cylinder := CylinderMesh.new()
		cylinder.top_radius = .72; cylinder.bottom_radius = .72; cylinder.height = .06
		cylinder.radial_segments = 48
		var material := StandardMaterial3D.new()
		material.albedo_color = Color("3b4b4b")
		cylinder.material = material
		floor_mesh.mesh = cylinder; floor_mesh.position = Vector3(x,-.04,0)
		add_child(floor_mesh)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 3.25
	camera.position = Vector3(-1.5,2.1,6.5)
	add_child(camera)
	camera.look_at(Vector3(0,.72,0))
	_build_models()
	var layer := CanvasLayer.new(); add_child(layer)
	var column := VBoxContainer.new(); column.position = Vector2(28,20); column.add_theme_constant_override("separation",10)
	var theme := Theme.new(); theme.default_font = load("res://Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf");theme.default_font_size=18
	column.theme = theme;layer.add_child(column)
	var heading := Label.new();heading.text="新矮人 · 白身 / 日常 / 工作与战斗";heading.add_theme_font_size_override("font_size",25);column.add_child(heading)
	var row := HBoxContainer.new();row.add_theme_constant_override("separation",12);column.add_child(row)
	var pause := Button.new();pause.text="暂停演示";pause.accessibility_name=pause.text
	pause.pressed.connect(func():playing=not playing;pause.text="暂停演示" if playing else "继续演示";pause.accessibility_name=pause.text)
	row.add_child(pause)
	var variant := Button.new();variant.text="切换女版";variant.accessibility_name=variant.text
	variant.pressed.connect(func():female=not female;_build_models();variant.text="切换男版" if female else "切换女版";variant.accessibility_name=variant.text)
	row.add_child(variant)
	var face := Button.new();face.text="查看背面";face.accessibility_name=face.text
	face.pressed.connect(func():
		for model in models:model.rotation.y=0 if absf(model.rotation.y)>.1 else PI
		face.text="查看正面" if absf(models[0].rotation.y)>.1 else "查看背面"
		face.accessibility_name=face.text)
	row.add_child(face)
	status=Label.new();column.add_child(status)
	_sample(0)
	if "--record-whitebody" in OS.get_cmdline_user_args():
		recording=true
		_record.call_deferred()

func _build_models():
	for model in models:model.free()
	models.clear()
	for index in range(3):
		var model=load("res://Scenes/Characters/DwarfFemale.tscn" if female else "res://Scenes/Characters/Dwarf.tscn").instantiate()
		add_child(model);model.position.x=(index-1)*1.55;models.append(model)
	models[1].preset("casual")
	models[2].preset("miner")

func _process(delta):
	if recording:return
	if playing:elapsed+=delta
	_sample(elapsed)

func _sample(time):
	var phase=int(time/3.0)%3
	var outfit=["miner","guard","guard"][phase]
	if models[2].get_meta("outfit","")!=outfit:
		models[2].preset(outfit);models[2].set_meta("outfit",outfit)
	var clips=["Idle","Walk",["Mine","Attack","Guard"][phase]]
	for index in range(3):
		var player: AnimationPlayer=models[index].player
		player.assigned_animation=clips[index]
		player.seek(fmod(time,player.get_animation(clips[index]).length),true)
	if status:status.text="白身站立  ·  日常行走  ·  "+["采矿","挥剑","举盾"][phase]

func _record():
	DirAccess.make_dir_recursive_absolute("res://.local/whitebody-film")
	for frame in range(216):
		_sample(frame/24.0)
		await get_tree().process_frame
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("res://.local/whitebody-film/frame_%04d.png"%frame)
		if frame in [0,96,168]:get_viewport().get_texture().get_image().save_png("res://.local/whitebody-demo-%d.png"%frame)
	print("WHITEBODY_FILM_OK: production models, walk, mine, attack, guard")
	get_tree().quit()
