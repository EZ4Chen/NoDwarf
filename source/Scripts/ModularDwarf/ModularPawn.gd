extends "res://Scripts/CompactDwarf/CompactPawn.gd"
## Reuses the approved animation and anatomical hand convention; appearance is pure data.
const PARTS := "res://Assets/Characters/ModularDwarf/"
const HEAD_CHIN_Y := 36.0
const DEFAULT := {"sex":"male","body":"standard","head":"square","hair":"swept","beard":"full","outfit":"work","gloves":true,"boots":true}
var appearance: Dictionary = DEFAULT.duplicate()
var pieces: Dictionary = {}

func _ready():
	super._ready()
	# This invisible canvas defines a stable shared coordinate system for all parts.
	var canvas := Image.create(320,500,false,Image.FORMAT_RGBA8)
	torso.texture=ImageTexture.create_from_image(canvas)
	for key in ["body","underwear","outfit","head","hair","beard"]:
		var sprite:=Sprite2D.new();sprite.name=key;torso.add_child(sprite);pieces[key]=sprite
		if key in ["body","underwear","outfit"]:
			sprite.position=Vector2(0,120)
		else:
			# Shared chin attachment overlaps the shoulder silhouette by about seven screen pixels.
			sprite.scale=Vector2.ONE*.74
			sprite.position=Vector2(0,HEAD_CHIN_Y-80*.74)
	configure(appearance)

static func normalize_appearance(data: Dictionary) -> Dictionary:
	var result: Dictionary=DEFAULT.duplicate()
	for key in DEFAULT:
		if data.has(key):result[key]=data[key]
	# Read older saved appearances without coupling sex to body shape again.
	if result.body=="female":
		result.body="standard";result.sex="female"
	elif result.body=="male":result.body="standard"
	if result.body not in ["standard","broad","round"]:result.body="standard"
	if result.sex not in ["male","female"]:result.sex="male"
	return result

func configure(data: Dictionary):
	appearance=normalize_appearance(data)
	if pieces.is_empty():return
	var body_id: String=appearance.body
	var head_id: String = appearance.head if appearance.head in ["square","round","lean"] else "square"
	pieces.body.texture=load(PARTS+"body-shared-"+body_id+".png")
	pieces.body.position=Vector2(0,140)
	pieces.body.scale=Vector2.ONE
	pieces.underwear.texture=load(PARTS+"underwear-shared.png")
	pieces.underwear.position=Vector2(0,140)
	var body_width: float={"standard":238.0,"broad":274.0,"round":286.0}[body_id]
	pieces.underwear.scale=Vector2(body_width/238.0,1.0)
	pieces.head.texture=load(PARTS+"head-"+head_id+".png")
	for key in ["hair","beard"]:
		var id: String=appearance[key]
		var path: String=PARTS+key+"-"+id+".png"
		pieces[key].visible=id!="none" and ResourceLoader.exists(path)
		if pieces[key].visible:pieces[key].texture=load(path)
	# Clothing is selected by body shape, never sex.
	var outfit_id: String="male" if body_id=="standard" else "broad"
	var outfit_path: String=PARTS+"outfit-"+appearance.outfit+"-"+outfit_id+".png"
	# The garment hem follows the shared base-body hem, including with bare feet.
	pieces.outfit.position=Vector2(0,140)
	pieces.outfit.scale=Vector2(1.04 if body_id=="round" else 1.0,280.0/244.0)
	pieces.outfit.visible=appearance.outfit!="none" and ResourceLoader.exists(outfit_path)
	if pieces.outfit.visible:pieces.outfit.texture=load(outfit_path)
	# Full torso garments own the silhouette; underwear must not leak around their hem.
	pieces.body.visible=not pieces.outfit.visible
	pieces.underwear.visible=appearance.sex=="female" and not pieces.outfit.visible
	var hand_texture: Texture2D=load(ROOT+"hand.png" if appearance.gloves else PARTS+"bare-hand-v2.png")
	left_hand.texture=hand_texture;right_hand.texture=hand_texture
	var foot_texture: Texture2D=load(ROOT+"boot.png" if appearance.boots else PARTS+"bare-foot-v2.png")
	left_foot.texture=foot_texture;right_foot.texture=foot_texture
	body_height=220.0;hand_height=54.0 if appearance.gloves else 36.0;boot_height=76.0
	# Mirroring belongs to anatomical side, never to gear type.
	right_hand.flip_h=false;left_hand.flip_h=true
	right_foot.flip_h=false;left_foot.flip_h=true

func sample(time: float):
	super.sample(time)
	if pieces.is_empty():return
	if not appearance.boots:
		# Foot size is independent of stance height: removing a boot does not move the head or grip.
		for foot in [right_foot,left_foot]:
			var gait: Vector2=foot.scale/(76.0/foot.texture.get_height())
			foot.scale=Vector2(50.0/foot.texture.get_width(),50.0/foot.texture.get_height())*gait
			foot.position.y+=(76.0-50.0)*.5
			foot.position.x*=.70
	if not appearance.gloves and task!="mine":
		# Bare palms sit closer to the smaller torso; held-tool grip points stay fixed.
		right_hand.position.x+=18
		left_hand.position.x-=18
