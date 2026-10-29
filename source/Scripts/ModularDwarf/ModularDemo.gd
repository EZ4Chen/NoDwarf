extends Node2D
const Pawn=preload("res://Scripts/ModularDwarf/ModularPawn.gd")
const SAVE_PATH="res://.local/modular-dwarf.json"
var pawns: Array[Node2D]=[]
var appearance: Dictionary=Pawn.DEFAULT.duplicate()
var choices: Dictionary={}
var gloves: CheckBox
var boots: CheckBox
var status: Label
var pause_button: Button
var elapsed:=0.0
var playing:=true
var recording:=false
var syncing:=false
var speed:=1.0

func _ready():
	get_window().title="NoDwarf · 模块化矮人换装"
	RenderingServer.set_default_clear_color(Color("202d33"))
	var theme:=Theme.new();theme.default_font=load("res://Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf");theme.default_font_size=18
	var ui:=CanvasLayer.new();add_child(ui)
	var title:=Label.new();title.text="模块化矮人 · 身体 / 头型 / 毛发 / 穿戴";title.theme=theme;title.position=Vector2(28,20);title.add_theme_font_size_override("font_size",26);ui.add_child(title)
	var note:=Label.new();note.text="正面样机 · 同一套搭配，分别查看站立、行走和采矿";note.theme=theme;note.modulate=Color("a8b6b6");note.position=Vector2(30,62);ui.add_child(note)
	var panel:=VBoxContainer.new();panel.position=Vector2(28,115);panel.custom_minimum_size.x=265;panel.theme=theme;panel.add_theme_constant_override("separation",12);ui.add_child(panel)
	_choice(panel,"性别","sex",["male","female"],["男","女"])
	_choice(panel,"体型","body",["standard","broad","round"],["标准","宽壮","圆胖"])
	_choice(panel,"头型","head",["square","round","lean"],["方脸","圆脸","瘦长脸"])
	_choice(panel,"头发","hair",["none","swept","waves","braids"],["光头","侧分短发","蓬松卷发","双辫"])
	_choice(panel,"胡子","beard",["none","short","full","braids"],["无胡子","短胡须","浓密长须","双辫胡须"])
	_choice(panel,"衣服","outfit",["none","work","apron"],["内衣基础身","工作服","皮围裙"])
	gloves=CheckBox.new();gloves.text="穿手套";gloves.accessibility_name="穿手套，关闭显示裸手";panel.add_child(gloves)
	gloves.toggled.connect(func(value):if not syncing:appearance.gloves=value;_apply())
	boots=CheckBox.new();boots.text="穿靴子";boots.accessibility_name="穿靴子，关闭显示光脚";panel.add_child(boots)
	boots.toggled.connect(func(value):if not syncing:appearance.boots=value;_apply())
	var presets:=HBoxContainer.new();panel.add_child(presets)
	_button("查看白身",presets,func():appearance.outfit="none";appearance.gloves=false;appearance.boots=false;_apply())
	_button("光头无须",presets,func():appearance.hair="none";appearance.beard="none";_apply())
	var controls:=HBoxContainer.new();panel.add_child(controls)
	pause_button=_button("暂停",controls,func():playing=not playing;pause_button.text="暂停" if playing else "继续";pause_button.accessibility_name=pause_button.text)
	_button("慢放 / 正常",controls,func():speed=0.5 if speed==1 else 1.0)
	var actions:=HBoxContainer.new();panel.add_child(actions)
	_button("随机搭配",actions,_randomize)
	_button("保存搭配",actions,_save)
	status=Label.new();status.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART;status.custom_minimum_size=Vector2(265,70);status.modulate=Color("a8b6b6");status.add_theme_font_size_override("font_size",14);panel.add_child(status)
	for i in range(3):
		var pawn=Pawn.new();pawn.task=["idle","walk","mine"][i];pawn.position=Vector2(460+i*280,590);add_child(pawn);pawns.append(pawn)
		var label:=Label.new();label.theme=theme;label.text=["站立","行走","双手采矿"][i];label.position=Vector2(400+i*280,220);label.size.x=120;label.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER;ui.add_child(label)
	if FileAccess.file_exists(SAVE_PATH):
		var saved=JSON.parse_string(FileAccess.get_file_as_string(SAVE_PATH))
		if saved is Dictionary:appearance=Pawn.normalize_appearance(saved)
	if "--preview-whitebody" in OS.get_cmdline_user_args():
		appearance={"sex":"male","body":"standard","head":"square","hair":"none","beard":"none","outfit":"none","gloves":false,"boots":false}
	_apply()
	if "--record-modular" in OS.get_cmdline_user_args():recording=true;_record.call_deferred()

func _button(text: String,parent: Node,action: Callable)->Button:
	var button:=Button.new();button.text=text;button.accessibility_name=text;parent.add_child(button);button.pressed.connect(action);return button
func _choice(parent: Node,title: String,key: String,ids: Array,names: Array):
	var row:=HBoxContainer.new();parent.add_child(row)
	var label:=Label.new();label.text=title;label.custom_minimum_size.x=60;row.add_child(label)
	var option:=OptionButton.new();option.custom_minimum_size=Vector2(190,36);option.accessibility_name=title
	for name in names:option.add_item(name)
	row.add_child(option);choices[key]={"control":option,"ids":ids}
	option.item_selected.connect(func(index):if not syncing:appearance[key]=ids[index];_apply())
func _apply():
	syncing=true
	for key in choices:
		var ids: Array=choices[key].ids
		if appearance[key] not in ids:appearance[key]=ids[0]
		choices[key].control.select(ids.find(appearance[key]))
	gloves.button_pressed=appearance.gloves;boots.button_pressed=appearance.boots
	syncing=false
	for pawn in pawns:pawn.configure(appearance);pawn.sample(elapsed)
	status.text="手："+("手套" if appearance.gloves else "裸手")+"  ·  脚："+("靴子" if appearance.boots else "光脚")
func _randomize():
	for key in choices:appearance[key]=choices[key].ids.pick_random()
	appearance.gloves=randf()>.5;appearance.boots=randf()>.5;_apply()
func _save():
	var file:=FileAccess.open(SAVE_PATH,FileAccess.WRITE)
	if file:file.store_string(JSON.stringify(appearance,"\t"));status.text="搭配已保存，下次打开会恢复。"
	else:status.text="保存失败，当前搭配仍保留在窗口中。"
func _process(delta):
	if recording:return
	if playing:elapsed+=delta*speed
	for pawn in pawns:pawn.sample(elapsed)
func _draw():
	draw_line(Vector2(315,112),Vector2(315,700),Color("42565b"),1)
	for x in [460,740,1020]:draw_line(Vector2(x-110,595),Vector2(x+110,595),Color("394e54"),2)
func _record():
	DirAccess.make_dir_recursive_absolute("res://.local/modular-film")
	var bases=[
		{"sex":"male","body":"standard","head":"square","hair":"swept","beard":"short"},
		{"sex":"female","body":"standard","head":"round","hair":"braids","beard":"none"},
		{"sex":"male","body":"broad","head":"lean","hair":"waves","beard":"full"},
		{"sex":"female","body":"round","head":"round","hair":"swept","beard":"none"}]
	var presets=[]
	for equipped in [false,true]:
		for base in bases:
			var preset: Dictionary=base.duplicate()
			preset.outfit="work" if equipped else "none"
			preset.gloves=equipped;preset.boots=equipped
			presets.append(preset)
	for frame in range(presets.size()*60):
		if frame%60==0:appearance=presets[frame/60].duplicate();_apply()
		elapsed=frame/24.0
		for pawn in pawns:pawn.sample(elapsed)
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("res://.local/modular-film/%04d.png" % frame)
		await get_tree().process_frame
	print("MODULAR_DEMO_OK: eight combinations, shared body shapes, bare/equipped hands and feet, 480 animation frames")
	get_tree().quit()
