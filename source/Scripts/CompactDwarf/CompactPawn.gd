extends Node2D
## One-direction animation study. Hands are children of the tool grip, not a body overlay.
const ROOT := "res://Assets/Characters/CompactPrototype/"
var torso: Sprite2D
var right_foot: Sprite2D
var left_foot: Sprite2D
var left_hand: Sprite2D
var right_hand: Sprite2D
var tool_root: Node2D
var tool: Sprite2D
var task := "idle"
var impact := 0.0
var body_height := 220.0
var hand_height := 54.0
var boot_height := 76.0
const UPPER_GRIP := Vector2(0,-52)

func _sprite(file: String, height: float, parent: Node) -> Sprite2D:
	var sprite := Sprite2D.new()
	sprite.texture = load(ROOT + file + ".png")
	sprite.scale = Vector2.ONE * height / sprite.texture.get_height()
	parent.add_child(sprite)
	return sprite

func _ready():
	right_foot = _sprite("boot", boot_height, self)
	left_foot = _sprite("boot", boot_height, self)
	left_foot.flip_h = true
	torso = _sprite("body", body_height, self)
	tool_root = Node2D.new()
	tool_root.name = "Grip"
	add_child(tool_root)
	tool = _sprite("pickaxe", 180, tool_root)
	# The authored grip is at 78% of the shaft's full image height.
	tool.position.y = -(0.78 - 0.5) * 180
	left_hand = _sprite("hand", hand_height, self)
	right_hand = _sprite("hand", hand_height, self)
	left_hand.name = "LeftHand"
	right_hand.name = "RightHand"
	left_hand.flip_h = true
	if task == "mine":
		left_hand.reparent(tool_root)
		right_hand.reparent(tool_root)
		right_hand.position = UPPER_GRIP
		left_hand.position = Vector2.ZERO
		right_hand.z_index = 2
		left_hand.z_index = 2
	tool_root.visible = task == "mine"
	sample(0.0)

func sample(time: float):
	var cycle := fmod(time, 1.45) / 1.45
	var step := time * TAU * 1.35
	impact = 0.0
	var body_bottom := -boot_height * .62
	torso.position = Vector2(0, body_bottom-body_height*.5)
	torso.rotation = 0.0
	torso.scale = Vector2.ONE * body_height / torso.texture.get_height()
	right_foot.position = Vector2(-boot_height*.53, -boot_height*.42)
	left_foot.position = Vector2(boot_height*.53, -boot_height*.42)
	right_foot.rotation = -.055
	left_foot.rotation = .055
	right_foot.scale = Vector2.ONE * boot_height / right_foot.texture.get_height()
	left_foot.scale = Vector2.ONE * boot_height / left_foot.texture.get_height()
	left_hand.scale = Vector2.ONE * hand_height / left_hand.texture.get_height()
	right_hand.scale = left_hand.scale
	if task != "mine":
		# Character-relative anatomy: right hand appears on screen LEFT in front view.
		right_hand.position = Vector2(-body_height*.355, body_bottom-body_height*.30)
		left_hand.position = Vector2(body_height*.355, body_bottom-body_height*.30)
		right_hand.rotation = -.12
		left_hand.rotation = .12
	if task == "walk":
		var stride := sin(step)
		var counter := -stride
		right_foot.position.y += stride * 9 - maxf(stride, 0) * 7
		left_foot.position.y += counter * 9 - maxf(counter, 0) * 7
		right_foot.scale *= 1 + stride * .075
		left_foot.scale *= 1 + counter * .075
		right_foot.rotation += stride * .12
		left_foot.rotation -= stride * .12
		torso.position += Vector2(stride * 4, -absf(cos(step)) * 5)
		torso.rotation = stride * .028
		right_hand.position += Vector2(stride * 3, -stride * 13)
		left_hand.position += Vector2(stride * 3, stride * 13)
		left_hand.rotation -= stride * .18
		right_hand.rotation -= stride * .18
	elif task == "mine":
		var keys := [0.0, .30, .46, .62, .70, 1.0]
		var positions := [Vector2(61,-112),Vector2(87,-178),Vector2(91,-185),Vector2(82,-138),Vector2(82,-138),Vector2(61,-112)]
		var angles := [22.0,-27.0,-35.0,108.0,108.0,22.0]
		var segment := 0
		while segment < keys.size()-2 and cycle > keys[segment+1]:segment += 1
		var u: float = inverse_lerp(keys[segment], keys[segment+1], cycle)
		# Slow anticipation, fast strike, impact hold, gradual recovery.
		if segment == 2:u = u*u
		else:u = smoothstep(0.0, 1.0, u)
		tool_root.position = positions[segment].lerp(positions[segment+1], u)
		tool_root.rotation = deg_to_rad(lerpf(angles[segment], angles[segment+1], u))
		var forward := clampf((tool_root.rotation_degrees + 35) / 143, 0, 1)
		torso.position += Vector2(lerpf(-5, 11, forward), lerpf(-4, 5, forward))
		torso.rotation = lerpf(-.035, .055, forward)
		torso.scale *= Vector2(1 + forward*.018, 1-forward*.025)
		if cycle >= .62 and cycle < .84:impact = (cycle-.62)/.22
		# Both gloves stay exactly on the handle throughout the complete swing.
		assert(right_hand.position.is_equal_approx(UPPER_GRIP))
		assert(left_hand.position.is_equal_approx(Vector2.ZERO))
	else:
		var breath := sin(time*2.4)
		torso.position.y -= breath * 1.4
		left_hand.position.y -= breath
		right_hand.position.y -= breath
	queue_redraw()

func _draw():
	# Contact shadows stay on the ground while the boots move.
	draw_set_transform(Vector2(0, 2), 0, Vector2(1, .16))
	draw_circle(Vector2.ZERO, 72, Color(0,0,0,.18))
	draw_set_transform(Vector2.ZERO)
	if task != "mine":return
	var rock := PackedVector2Array([Vector2(156,-9),Vector2(156,-37),Vector2(174,-59),Vector2(197,-55),Vector2(216,-31),Vector2(215,-8)])
	draw_colored_polygon(rock, Color("697875"))
	draw_polyline(PackedVector2Array([rock[0],rock[1],rock[2],rock[3],rock[4],rock[5],rock[0]]), Color("16252b"), 3, true)
	draw_line(Vector2(174,-59),Vector2(181,-31),Color("9ca99b"),2,true)
	draw_line(Vector2(181,-31),Vector2(197,-55),Color("455654"),2,true)
	if impact > 0:
		for i in range(6):
			var spread := Vector2(cos(i*2.1)*24, -18-absf(sin(i*1.7))*33)
			var point := Vector2(175,-59)+spread*impact+Vector2(0,42*impact*impact)
			draw_circle(point, 2.5*(1-impact), Color(0.81,.75,.55,1-impact))
