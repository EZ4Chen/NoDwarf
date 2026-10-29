extends SceneTree

# Atlas packing only: source illustrations are generated, never redrawn here.
const ART = "res://../art/dwarf/flat-pawn-five/"
const OUT = "res://Assets/Characters/FlatPawn/"
const TYPES = ["male", "female", "thin", "hulk", "fat"]
const WIDTHS = [132, 116, 102, 152, 150]
const SIDE_WIDTHS = [94, 88, 80, 102, 116]
var sources: Dictionary = {}
var packed: Dictionary = {}

func _initialize():
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT))
	for filename in ["bodies", "work", "apron", "armor", "heads", "hair", "accessories", "underwear", "trousers"]:
		var source = Image.load_from_file(ProjectSettings.globalize_path(ART + filename + ("-source-v2.png" if filename == "hair" else "-source.png")))
		assert(source != null and not source.is_empty(), filename)
		source.convert(Image.FORMAT_RGBA8)
		sources[filename] = source
	for row in range(5):
		for kind in ["body", "work", "apron", "armor"]:
			var frames: Array[Image] = []
			for d in range(3):
				var frame = blank()
				var w: int = SIDE_WIDTHS[row] if d == 1 else WIDTHS[row]
				place(frame, cell("bodies" if kind == "body" else kind, row, d), Rect2i(100-w/2,110,w,150))
				frames.append(frame)
			atlas(kind + "-" + TYPES[row], frames)
		for kind in ["underwear","trousers"]:
			var frames: Array[Image]=[]
			for d in range(3):
				var frame=blank()
				var w: int = SIDE_WIDTHS[row] if d==1 else WIDTHS[row]
				var rect=Rect2i(100-w/2,208,w,52) if kind=="trousers" else Rect2i(100-int(w*.92)/2,143,int(w*.92),32)
				place(frame,cell(kind,row,d),rect)
				frames.append(frame)
			atlas(kind+"-"+TYPES[row],frames)
	for row in range(3):
		var frames: Array[Image] = []
		for d in range(3):
			var frame = blank()
			place(frame, cell("heads",row,d), Rect2i(48 if d != 1 else 54,28,104 if d != 1 else 100,100))
			frames.append(frame)
		atlas("head-"+["round","square","lean"][row],frames)
	for row in range(3):
		var frames: Array[Image] = []
		for d in range(3):
			var frame = blank()
			var rect: Rect2i
			if d == 0:
				rect = [Rect2i(48,17,104,74),Rect2i(43,18,114,111),Rect2i(47,18,106,134)][row]
			elif d == 1:
				rect = [Rect2i(51,16,87,88),Rect2i(48,18,96,113),Rect2i(50,18,92,135)][row]
			else:
				rect = [Rect2i(48,17,104,112),Rect2i(43,18,114,116),Rect2i(47,18,106,139)][row]
			place(frame,cell("hair",row,d),rect)
			frames.append(frame)
		atlas("hair-"+["swept","waves","braids"][row],frames)
	for row in range(4):
		var frames: Array[Image] = []
		for d in range(3):
			var frame = blank()
			if row >= 2 or d != 2:
				var rect: Rect2i
				if row < 2:
					rect = Rect2i(50,78 if row == 0 else 90,100,86 if row == 0 else 61) if d == 0 else Rect2i(95,79 if row == 0 else 89,59,82 if row == 0 else 60)
				else:
					rect = Rect2i(47 if d != 1 else 52,15,106 if d != 1 else 95,66)
				place(frame,cell("accessories",row,d),rect)
			frames.append(frame)
		atlas(["beard-full","beard-short","helmet","cap"][row],frames)
	for row in range(4,6):
		for d in range(3):
			var part=cell("accessories",row,d)
			part.resize(64,50,Image.INTERPOLATE_LANCZOS)
			part.save_png(OUT+"hand-"+("bare" if row==4 else "glove")+"-"+str(d)+".png")
	print("FLAT_PAWN_PACK_OK: five bodies, three directions, three garments, shared modular heads/hair/beards/headgear/hands")
	quit()

func blank() -> Image:
	return Image.create(200,280,false,Image.FORMAT_RGBA8)

func cell(name: String,row: int,col: int) -> Image:
	var source: Image=sources[name]
	var xs: Array = [0.0,1.0/3,2.0/3,1.0]
	var ys: Array = [0.0,1.0/3,2.0/3,1.0]
	if name in ["bodies","work","apron","armor","underwear","trousers"]:
		xs=[0.0,350.0/971,620.0/971,1.0]
		ys=[0.0,325.0/1619,650.0/1619,950.0/1619,1250.0/1619,1.0]
	elif name == "accessories":
		xs=[0.0,344.0/887,588.0/887,1.0]
		ys=[0.0,400.0/1774,680.0/1774,950.0/1774,1220.0/1774,1490.0/1774,1.0]
	var box=Rect2i(int(xs[col]*source.get_width()),int(ys[row]*source.get_height()),int((xs[col+1]-xs[col])*source.get_width()),int((ys[row+1]-ys[row])*source.get_height()))
	var part=source.get_region(box)
	# Ignore nearly invisible generation fringe when finding the placement box.
	# Keep original RGBA pixels, including their antialiasing, within that box.
	var lo=Vector2i(part.get_width(),part.get_height())
	var hi=Vector2i.ZERO
	for y in range(part.get_height()):
		for x in range(part.get_width()):
			if part.get_pixel(x,y).a > 0.35:
				lo.x=mini(lo.x,x);lo.y=mini(lo.y,y)
				hi.x=maxi(hi.x,x);hi.y=maxi(hi.y,y)
	return part.get_region(Rect2i(lo,hi-lo+Vector2i.ONE))

func place(frame: Image,part: Image,rect: Rect2i):
	part.resize(rect.size.x,rect.size.y,Image.INTERPOLATE_LANCZOS)
	frame.blend_rect(part,Rect2i(Vector2i.ZERO,part.get_size()),rect.position)

func atlas(name: String,frames: Array[Image]):
	var image=Image.create(600,280,false,Image.FORMAT_RGBA8)
	for d in range(3):
		image.blit_rect(frames[d],Rect2i(0,0,200,280),Vector2i(d*200,0))
	assert(image.save_png(OUT+name+".png")==OK)
