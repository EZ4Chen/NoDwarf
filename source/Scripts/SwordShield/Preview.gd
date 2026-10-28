extends Node3D
const CLIPS := ["idle","walk","sword_shield_idle","sword_slash","block_raise","block_hold","block_hit","hit","down","block_lower","block_break"]
var actors: Array[Node3D]=[]
var players: Array[AnimationPlayer]=[]
var selected := "sword_shield_idle"
var elapsed := 0.0
var paused := false
var film := false
var status: Label
var animation_selector: OptionButton
func _ready() -> void:
	film="--sword-shield-test" in OS.get_cmdline_user_args()
	var env:=WorldEnvironment.new();env.environment=Environment.new();env.environment.background_mode=Environment.BG_COLOR;env.environment.background_color=Color("26333c");env.environment.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR;env.environment.ambient_light_color=Color("e2eced");env.environment.ambient_light_energy=.65;add_child(env)
	var light:=DirectionalLight3D.new();light.rotation_degrees=Vector3(-40,145,0);light.light_energy=1.0;light.shadow_enabled=true;add_child(light)
	var fill:=DirectionalLight3D.new();fill.rotation_degrees=Vector3(-20,-35,0);fill.light_energy=.35;add_child(fill)
	var floor:=MeshInstance3D.new();var plane:=PlaneMesh.new();plane.size=Vector2(6,6);var material:=StandardMaterial3D.new();material.albedo_color=Color("344347");material.roughness=1;plane.material=material;floor.mesh=plane;floor.position.y=-.006;add_child(floor)
	var camera:=Camera3D.new();camera.projection=Camera3D.PROJECTION_ORTHOGONAL;camera.size=2.7;camera.position=Vector3(-.6,2.1,-5);add_child(camera);camera.look_at(Vector3(0,.65,0))
	for index in 2:
		var sex: String=["male","female"][index]
		var actor: Node3D=load("res://Assets/Characters/SwordShield/Models/"+sex+"_clothed.tscn").instantiate();add_child(actor);actor.position.x=(index-.5)*1.65;actors.append(actor)
		var player: AnimationPlayer=actor.find_children("*","AnimationPlayer",true,false)[0];players.append(player)
		for pair in [["MainHandSocket","sword"],["OffHandSocket","shield"]]:
			var socket=actor.find_child(pair[0],true,false)
			assert(socket!=null)
			socket.add_child(load("res://Assets/Characters/SwordShield/Weapons/"+pair[1]+".tscn").instantiate())
	var layer:=CanvasLayer.new();add_child(layer)
	var row:=HBoxContainer.new();row.position=Vector2(20,20);layer.add_child(row)
	var label:=Label.new();label.text="剑盾动作";row.add_child(label)
	var select:=OptionButton.new();animation_selector=select;select.accessibility_name="剑盾动作"
	for text in ["站立","行走","剑盾戒备","挥剑","举盾","持盾","格挡受震","受击","倒地","收盾","破防"]:select.add_item(text)
	select.select(2);select.item_selected.connect(func(index): selected=CLIPS[index];elapsed=0);row.add_child(select)
	var pause:=CheckButton.new();pause.text="暂停";pause.toggled.connect(func(value):paused=value);row.add_child(pause)
	status=Label.new();status.position=Vector2(20,65);layer.add_child(status)
	if film:run_checks.call_deferred()
func pose(clip: String, time: float) -> void:
	for player in players:
		player.assigned_animation=clip;player.seek(minf(time,player.get_animation(clip).length),true)
func _process(delta: float) -> void:
	if film:return
	if not paused:elapsed+=delta
	var length: float=players[0].get_animation(selected).length
	var t:=fmod(elapsed,length) if selected in ["idle","walk","sword_shield_idle","block_hold"] else minf(elapsed,length)
	pose(selected,t);status.text=selected+"  %.2f s" % t
func run_checks() -> void:
	var dir:="res://.local/sword-shield";DirAccess.make_dir_recursive_absolute(dir+"/film")
	var samples:=0;var frame:=0
	for clip in CLIPS:
		animation_selector.select(CLIPS.find(clip))
		for p in players:assert(p.has_animation(clip))
		var length: float=players[0].get_animation(clip).length
		var first: Array=[]
		var moving:=false
		var count:=int(round(length*24))
		for step in count+1:
			var t:=minf(length,float(step)/24);pose(clip,t);status.text=clip+"  %.2f s" % t
			await get_tree().process_frame;await get_tree().process_frame
			for actor in actors:
				var skeleton: Skeleton3D=actor.find_children("*","Skeleton3D",true,false)[0]
				for b in skeleton.get_bone_count():
					assert(skeleton.get_bone_global_pose(b).is_finite())
					if actor==actors[0]:
						if step==0:first.append(skeleton.get_bone_pose(b))
						elif not first[b].is_equal_approx(skeleton.get_bone_pose(b)):moving=true
				assert(skeleton.get_bone_pose_position(skeleton.find_bone("root")).length()<.001)
				samples+=1
			if DisplayServer.get_name()!="headless":
				await RenderingServer.frame_post_draw
				var image:=get_viewport().get_texture().get_image()
				image.save_png(dir+"/film/frame_%04d.png"%frame)
				if step==count/2 or step==count:image.save_png(dir+"/"+clip+"_%d.png"%step)
			frame+=1
		assert(moving or clip=="block_hold","Animation does not move: "+clip)
	print("SWORD_SHIELD_PREVIEW_PASS samples=",samples)
	for actor in actors:actor.queue_free()
	await get_tree().process_frame
	get_tree().quit()
