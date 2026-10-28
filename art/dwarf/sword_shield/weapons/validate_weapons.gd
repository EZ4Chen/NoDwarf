extends SceneTree
func _initialize() -> void:
 var report := {}
 for item in ["sword","shield"]:
  var scene: Node3D = load("res://Assets/Characters/SwordShield/Weapons/"+item+".tscn").instantiate()
  assert(scene.transform.is_equal_approx(Transform3D.IDENTITY))
  var surfaces := 0
  var triangles := 0
  var bounds := AABB()
  var first := true
  for node in scene.find_children("*","",true,false):
   assert(node.get_script()==null,"Weapon must be presentation-only")
   assert(not node is AnimationPlayer and not node is CollisionObject3D)
   if node is MeshInstance3D:
    assert(node.transform.is_equal_approx(Transform3D.IDENTITY))
    if first: bounds=node.mesh.get_aabb();first=false
    else: bounds=bounds.merge(node.mesh.get_aabb())
    for surface in node.mesh.get_surface_count():
     var mat: StandardMaterial3D = node.mesh.surface_get_material(surface)
     assert(mat.vertex_color_use_as_albedo,"Palette disabled")
     var arrays: Array = node.mesh.surface_get_arrays(surface)
     triangles += arrays[Mesh.ARRAY_INDEX].size()/3
     surfaces += 1
  if item=="sword":
   var tip: Vector3 = scene.get_node("BladeTip").position
   var base: Vector3 = scene.get_node("BladeBase").position
   assert(tip.z>base.z and absf(tip.x)<.001 and absf(tip.y)<.001)
   assert(absf(bounds.end.z-tip.z)<.002)
   assert(bounds.size.y>3*bounds.size.x,"Blade working plane must be forward/back")
  else:
   var normal: Vector3 = scene.get_node("FrontNormal").position-scene.get_node("PanelCenter").position
   assert(normal.normalized().is_equal_approx(Vector3.UP)) # +Y in BONE-local coordinates
   assert(absf(bounds.position.x+bounds.end.x)<.001,"Shield must be centered")
   assert(scene.get_node("PanelCenter").position.y>0)
  report[item]={"triangles":triangles,"surfaces":surfaces,"bounds":str(bounds),"socket":scene.get_meta("socket")}
  scene.free()
 print("WEAPON_SCENES_PASS "+JSON.stringify(report))
 var file := FileAccess.open(ProjectSettings.globalize_path("res://../art/dwarf/sword_shield/weapons/validation.json"),FileAccess.WRITE)
 file.store_string(JSON.stringify(report,"\t")+"\n")
 quit()
