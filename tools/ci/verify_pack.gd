extends SceneTree

# Runs in a temporary empty project, outside the exported game.
func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1 or not ProjectSettings.load_resource_pack(args[0]):
		fail("Cannot mount exported pack")
		return
	for scene in ["res://Scenes/Startup.tscn", "res://Scenes/Main.tscn"]:
		if not ResourceLoader.exists(scene):
			fail("Missing runtime scene: " + scene)
			return
	var pending: Array[String] = ["res://"]
	var count := 0
	while not pending.is_empty():
		var path: String = pending.pop_back()
		var dir := DirAccess.open(path)
		if dir == null:
			fail("Cannot read pack directory: " + path)
			return
		dir.include_hidden = true
		for folder in dir.get_directories():
			if folder in ["Development", "tools", "checks", "CompactPrototype", "ModularFour", "ModularDwarf"]:
				fail("Development resources exported: " + path + folder)
				return
			pending.append(path.path_join(folder))
		count += dir.get_files().size()
	print("PACKAGE_RESOURCES_OK: %d files; runtime scenes present; development resources absent" % count)
	quit()

func fail(message: String) -> void:
	push_error(message)
	quit(1)
