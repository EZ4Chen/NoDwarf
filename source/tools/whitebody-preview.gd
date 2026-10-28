extends SceneTree

var model: Node3D
var camera: Camera3D

func _initialize():
	call_deferred("show_model")

func show_model():
	var stage = Node3D.new()
	root.add_child(stage)
	var world = WorldEnvironment.new()
	world.environment = Environment.new()
	world.environment.background_mode = Environment.BG_COLOR
	world.environment.background_color = Color("263039")
	world.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	world.environment.ambient_light_color = Color.WHITE
	world.environment.ambient_light_energy = .6
	stage.add_child(world)
	var light = DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-35,-25,0)
	light.light_energy = .9
	stage.add_child(light)
	model = load("res://Scenes/Characters/Dwarf.tscn").instantiate()
	stage.add_child(model)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 2.1
	camera.position = Vector3(-2,1.6,4)
	stage.add_child(camera)
	camera.look_at(Vector3(0,.7,0))
	for clip in ["Idle","Walk","Mine","Attack","Carry","Guard"]:
		print("CLIP ",clip," ",model.player.get_animation(clip).length)
	model.set_equipment("shirt",true)
	model.set_equipment("trousers",true)
	model.set_equipment("boots",true)
	model.set_equipment("sword",true)
	model.set_equipment("shield",true)
	model.player.play("Idle",0)
	model.player.advance(0)
	model.player.pause()
	for i in range(4): await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://.local/whitebody-gear-check.png")
	model.set_equipment("shirt",false)
	model.set_equipment("trousers",false)
	model.set_equipment("boots",false)
	for i in range(4): await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://.local/whitebody-base-check.png")
	print("WHITEBODY_PREVIEW_OK")
	quit()
