extends Node2D
const Pawn = preload("res://Scripts/CompactDwarf/CompactPawn.gd")
var pawns: Array[Node2D] = []
var elapsed := 0.0
var playing := true
var recording := false
var speed := 1.0
var old_tool: Node2D
var pause_button: Button
var speed_button: Button
var step_button: Button
var body_size := 220.0
var hand_size := 54.0
var boot_size := 76.0

func _ready():
	get_window().title = "NoDwarf · 紧凑矮人动作样机"
	RenderingServer.set_default_clear_color(Color("202d33"))
	var font: Font = load("res://Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf")
	var theme := Theme.new();theme.default_font=font;theme.default_font_size=18
	var ui := CanvasLayer.new();add_child(ui)
	var column := VBoxContainer.new();column.position=Vector2(30,22);column.theme=theme;column.add_theme_constant_override("separation",10);ui.add_child(column)
	var title := Label.new();title.text="紧凑身体 · 独立手套 · 交替短靴";title.add_theme_font_size_override("font_size",27);column.add_child(title)
	var subtitle := Label.new();subtitle.text="正面动作样机  /  先验证行走与采矿";subtitle.modulate=Color("a8b6b6");column.add_child(subtitle)
	var row := HBoxContainer.new();row.add_theme_constant_override("separation",12);column.add_child(row)
	pause_button=_button("暂停",row,func():playing=not playing;_controls())
	speed_button=_button("0.5× 慢放",row,func():speed=0.5 if speed==1.0 else 1.0;_controls())
	step_button=_button("步进一帧",row,func():elapsed+=1.0/24.0;_sample(elapsed))
	_controls()
	var sizes := HBoxContainer.new();sizes.add_theme_constant_override("separation",20);column.add_child(sizes)
	_size_slider("身体",268.0,220.0,70,110,sizes,func(value):body_size=value)
	_size_slider("手套",39.0,54.0,90,180,sizes,func(value):hand_size=value)
	_size_slider("靴子",58.0,76.0,90,170,sizes,func(value):boot_size=value)
	var x_positions := [190.0,465.0,745.0,1030.0]
	for i in range(4):
		var label := Label.new();label.text=["原版 · 工具单独摆动","样机 · 站立","样机 · 行走","样机 · 双手采矿"][i];label.theme=theme;label.position=Vector2(x_positions[i]-120,196);label.size.x=240;label.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER;ui.add_child(label)
		var line := Label.new();line.text=["完整手臂和腿仍固定","手套贴近身体轮廓","脚步、手摆、重心一起动","两只手固定在镐柄上"][i];line.theme=theme;line.add_theme_font_size_override("font_size",15);line.modulate=Color("a8b6b6");line.position=Vector2(x_positions[i]-135,665);line.size.x=270;line.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER;ui.add_child(line)
		if i>0:
			var pawn=Pawn.new();pawn.task=["idle","walk","mine"][i-1];pawn.position=Vector2(x_positions[i],590);add_child(pawn);pawns.append(pawn)
	var old := Node2D.new();old.position=Vector2(x_positions[0],590);add_child(old)
	var body := Sprite2D.new();body.texture=load("res://Assets/Characters/CompactPrototype/old-reference.png");body.scale=Vector2.ONE*280/body.texture.get_height();body.position.y=-140;old.add_child(body)
	old_tool=Node2D.new();old_tool.position=Vector2(-73,-103);old.add_child(old_tool)
	var pick := Sprite2D.new();pick.texture=load("res://Assets/Characters/Sprites4/pickaxe-held.png");pick.scale=Vector2.ONE*140/pick.texture.get_height();pick.position.y=-45;old_tool.add_child(pick)
	_sample(0)
	if "--record-compact" in OS.get_cmdline_user_args():recording=true;_record.call_deferred()

func _button(text: String,parent: Node,callback: Callable) -> Button:
	var button:=Button.new();button.text=text;button.accessibility_name=text;button.pressed.connect(callback);parent.add_child(button);return button
func _size_slider(title: String, baseline: float, initial: float, low: float, high: float, parent: Node, change: Callable):
	var group:=HBoxContainer.new();group.add_theme_constant_override("separation",10);parent.add_child(group)
	var label:=Label.new();label.custom_minimum_size.x=104;label.text="%s  %d%%" % [title,roundi(initial/baseline*100)];group.add_child(label)
	var slider:=HSlider.new();slider.min_value=low;slider.max_value=high;slider.step=1;slider.value=initial/baseline*100;slider.custom_minimum_size=Vector2(130,26);slider.accessibility_name=title+"大小";group.add_child(slider)
	slider.value_changed.connect(func(value):change.call(value*baseline/100.0);label.text="%s  %d%%" % [title,value];_sample(elapsed))
func _controls():
	pause_button.text="暂停" if playing else "继续";pause_button.accessibility_name=pause_button.text
	speed_button.text="0.5× 慢放" if speed==1.0 else "恢复 1×";speed_button.accessibility_name=speed_button.text
	step_button.disabled=playing
func _process(delta):
	if recording:return
	if playing:elapsed+=delta*speed
	_sample(elapsed)
func _sample(time: float):
	for pawn in pawns:
		pawn.body_height=body_size;pawn.hand_height=hand_size;pawn.boot_height=boot_size;pawn.sample(time)
	old_tool.rotation=-.4+sin(time*5.0)*.8
	queue_redraw()
func _draw():
	draw_line(Vector2(310,244),Vector2(310,635),Color("44575c"),1)
	for x in [190,465,745,1030]:draw_line(Vector2(x-108,595),Vector2(x+108,595),Color("394e54"),2)
func _record():
	DirAccess.make_dir_recursive_absolute("res://.local/compact-film")
	# The same clock samples every layer; render speed never changes animation timing.
	for frame in range(192):
		_sample(frame/24.0)
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("res://.local/compact-film/%04d.png" % frame)
		await get_tree().process_frame
	print("COMPACT_DEMO_OK: 192 frames; fixed two-hand grips; deterministic shared clock")
	get_tree().quit()
