import bpy,sys,json,importlib.util
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/ez4chen/NoDwarf');HERE=ROOT/'art/dwarf/sword_shield/defense'
spec=importlib.util.spec_from_file_location('defense',HERE/'defense_actions.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
if '--female' in sys.argv:
 HERE=HERE/'female';HERE.mkdir(exist_ok=True)
rig=bpy.data.objects['DwarfRig'];rest={b.name:list(v for row in b.matrix_local for v in row) for b in rig.data.bones}
metadata=m.build_actions(rig)
assert rest=={b.name:list(v for row in b.matrix_local for v in row) for b in rig.data.bones}
def snapshot(name,t):
 rig.animation_data.action=None;m.pose(rig,name,t)
 return {b.name:b.matrix.copy() for b in rig.pose.bones}
def difference(a,b):return max(abs(a[n][i][j]-b[n][i][j]) for n in a for i in range(4) for j in range(4))
seams={}
for name,t,other,u in [('block_raise',1,'block_hold',0),('block_hit',0,'block_hold',0),('block_hit',1,'block_hold',0),('block_hold',0,'block_hold',1),('block_lower',0,'block_hold',0)]:
 d=difference(snapshot(name,t),snapshot(other,u));assert d<1e-5,(name,other,d);seams[name+str(t)+'->'+other+str(u)]=d
samples=0;mindot=1
# Shield front is slot local +Y; should remain forward in ready, raise, hold, hit.
for name in m.SPECS:
 for i in range(25):
  snapshot(name,i/24);samples+=1
  assert rig.pose.bones['root'].location.length<1e-7
  if name.startswith('block') and name!='block_break':
   direction=rig.pose.bones['slot_hand_l'].matrix.to_3x3() @ Vector((0,1,0));dot=direction.normalized().dot(Vector((0,-1,0)));mindot=min(mindot,dot);assert dot>.98,(name,i,dot)
end=snapshot('down',1);prev=snapshot('down',.9);assert difference(end,prev)<1e-5
# Exact matching ready pose with independent attack team's module.
spec=importlib.util.spec_from_file_location('attack',ROOT/'art/dwarf/sword_shield/attack/attack_actions.py');a=importlib.util.module_from_spec(spec);spec.loader.exec_module(a)
base=snapshot('block_raise',0);p=a.RigPose(rig);p.reset();p.combat_idle(0);p.update();other={b.name:b.matrix.copy() for b in rig.pose.bones};assert difference(base,other)<1e-5
for name,t in ([] if '--no-render' in sys.argv else [('block_raise',.5),('block_hold',0),('block_hit',.22),('hit',.5),('down',1)]):
 snapshot(name,t)
 for ob in bpy.context.scene.objects:
  if ob.type=='MESH' and ob.name in ['base','cosmetic_beard','armor','trousers','boots','helmet','sword','shield']:
   ob.hide_render=False;ob.hide_set(False)
  elif ob.type=='MESH' and ob.name in ['shirt','pickaxe']:ob.hide_render=True
 bpy.context.scene.render.resolution_x=800;bpy.context.scene.render.resolution_y=900;bpy.context.scene.cycles.samples=16
 bpy.context.scene.render.filepath=str(HERE/(name+'.png'));bpy.ops.render.render(write_still=True)
rig.animation_data.action=None;m.reset(rig)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'defense.blend'))
report={'actions':metadata,'samples':samples,'root_motion':False,'rest_unchanged':True,'shield_forward_dot_min':mindot,'seams_max':max(seams.values()),'combat_idle_start_error':difference(base,other)}
(HERE/'validation.json').write_text(json.dumps(report,indent=2))
print('DEFENSE_PASS '+json.dumps(report))
