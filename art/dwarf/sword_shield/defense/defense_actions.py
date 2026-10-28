"""Original, in-place sword/shield defense animation library for the v2 dwarf rig.
Blender API entry: build_actions(rig, fps=24). Does not alter mesh or rest bones.
"""
import math
import bpy
from mathutils import Vector, Quaternion

FPS=24

def reset(rig):
    for p in rig.pose.bones:
        p.rotation_mode='XYZ';p.rotation_euler=(0,0,0);p.location=(0,0,0);p.scale=(1,1,1)

def rotate(rig,name,x=0,y=0,z=0):
    from mathutils import Euler
    rig.pose.bones[name].rotation_euler=tuple(math.radians(a) for a in (x,y,z))

def world_rotation(rig,name,q):
    p=rig.pose.bones[name];r=rig.data.bones[name].matrix_local.to_quaternion()
    delta=Quaternion()
    if p.parent:delta=p.parent.matrix.to_quaternion() @ rig.data.bones[p.parent.name].matrix_local.to_quaternion().inverted()
    p.rotation_euler=((delta @ r).inverted() @ q).to_euler('XYZ')
    bpy.context.view_layer.update()

def aim(rig,name,target):
    p=rig.pose.bones[name];b=rig.data.bones[name]
    q=(b.tail_local-b.head_local).normalized().rotation_difference((Vector(target)-p.head).normalized())
    world_rotation(rig,name,q @ b.matrix_local.to_quaternion())

def arm(rig,side,wrist,pole):
    u='upper_arm.'+side;f='forearm.'+side
    bpy.context.view_layer.update();start=rig.pose.bones[u].head.copy()
    l1=rig.data.bones[u].length;l2=rig.data.bones[f].length
    direction=Vector(wrist)-start;distance=max(abs(l1-l2)+.001,min(direction.length,(l1+l2)*.985));direction.normalize()
    bend=Vector(pole);bend=(bend-direction*bend.dot(direction)).normalized()
    a=(l1*l1-l2*l2+distance*distance)/(2*distance);height=math.sqrt(max(0,l1*l1-a*a))
    elbow=start+direction*a+bend*height
    aim(rig,u,elbow);aim(rig,f,start+direction*distance)
    # Preserve authored hand/socket axes: the default shield face is Blender -Y.
    world_rotation(rig,'hand.'+side,rig.data.bones['hand.'+side].matrix_local.to_quaternion())

def smooth(x):
    x=max(0,min(1,x));return x*x*(3-2*x)

def ready_pose(rig):
    # Exact t=0 counterpart of attack_actions.RigPose.combat_idle.
    rotate(rig,'chest',x=2);rotate(rig,'head',x=-2)
    bpy.context.view_layer.update()
    aim(rig,'upper_arm.R',(-.54,-.12,.74));aim(rig,'forearm.R',(-.56,-.30,.76))
    world_rotation(rig,'hand.R',Quaternion((1,0,0),math.radians(-8)) @ rig.data.bones['hand.R'].matrix_local.to_quaternion())
    aim(rig,'upper_arm.L',(.52,-.12,.73));aim(rig,'forearm.L',(.38,-.32,.77))
    world_rotation(rig,'hand.L',rig.data.bones['hand.L'].matrix_local.to_quaternion())

def guard_pose(rig,amount=1,recoil=0):
    ready_pose(rig)
    start={n:rig.pose.bones[n].rotation_euler.to_quaternion() for n in ['upper_arm.L','forearm.L']}
    rotate(rig,'chest',x=2-7*recoil)
    rotate(rig,'head',x=-2+5*amount-4*recoil)
    bpy.context.view_layer.update()
    target=Vector((.14,-.37,1.04));target.y+=.055*recoil;target.z-=.025*recoil
    arm(rig,'L',target,(1,0,-.4))
    end={n:rig.pose.bones[n].rotation_euler.to_quaternion() for n in start}
    for n in start:rig.pose.bones[n].rotation_euler=start[n].slerp(end[n],amount).to_euler('XYZ')
    bpy.context.view_layer.update()
    world_rotation(rig,'hand.L',rig.data.bones['hand.L'].matrix_local.to_quaternion())

def pose(rig,name,t):
    reset(rig)
    if name=='block_raise':guard_pose(rig,smooth(t))
    elif name=='block_hold':guard_pose(rig,1,0)
    elif name=='block_hit':
        k=math.sin(math.pi*min(1,t/.55))**2 if t<.55 else 0
        guard_pose(rig,1,k)
    elif name=='block_lower':guard_pose(rig,1-smooth(t))
    elif name=='block_break':
        k=smooth(min(1,t/.55));guard_pose(rig,1-k,.5*math.sin(math.pi*t))
        rotate(rig,'chest',x=2-14*math.sin(math.pi*t),z=8*math.sin(math.pi*t))
    elif name=='hit':
        ready_pose(rig);k=math.sin(math.pi*t)**2
        rotate(rig,'spine',x=-10*k,z=-7*k);rotate(rig,'head',x=-12*k)
        rotate(rig,'thigh.L',x=5*k);rotate(rig,'shin.L',x=-10*k)
    elif name=='down':
        # Collapse backwards around pelvis, root stays fixed in simulation space.
        k=smooth(min(1,t/.85));ready_pose(rig)
        rotate(rig,'pelvis',x=-87*k)
        rig.pose.bones['pelvis'].location=rig.data.bones['pelvis'].matrix_local.to_3x3().inverted() @ Vector((0,.12*k,-.185*k))
        rotate(rig,'spine',x=-3*k);rotate(rig,'head',x=8*k)
        rotate(rig,'thigh.L',x=15*k,z=12*k);rotate(rig,'thigh.R',x=8*k,z=-10*k)
        rotate(rig,'shin.L',x=-15*k);rotate(rig,'shin.R',x=-8*k)
        rotate(rig,'upper_arm.L',x=-5*k,z=35*k);rotate(rig,'upper_arm.R',x=8*k,z=-35*k)
        rotate(rig,'forearm.L',x=-20*k);rotate(rig,'forearm.R',x=-12*k)
    else:raise ValueError(name)
    bpy.context.view_layer.update()
    if name=='down':
        deps=bpy.context.evaluated_depsgraph_get();minimum=100
        for ob in bpy.context.scene.objects:
            if ob.type=='MESH' and ob.name in ['base','boots','armor','helmet']:
                evaluated=ob.evaluated_get(deps);data=evaluated.to_mesh()
                minimum=min(minimum,min((evaluated.matrix_world @ v.co).z for v in data.vertices))
                evaluated.to_mesh_clear()
        if minimum<.003:
            rig.pose.bones['pelvis'].location+=rig.data.bones['pelvis'].matrix_local.to_3x3().inverted() @ Vector((0,0,.003-minimum))
            bpy.context.view_layer.update()

SPECS={
 'block_raise':(.5,False),'block_hold':(1,True),'block_hit':(.5,False),
 'hit':(.625,False),'down':(1.25,False),'block_lower':(.5,False),'block_break':(.75,False)}

def build_actions(rig,fps=24):
    rig.animation_data_create();rig.animation_data.action=None
    for tr in rig.animation_data.nla_tracks:tr.mute=True
    metadata={}
    for name,(duration,loop) in SPECS.items():
        # Idempotent within a source file, without touching other teams' tracks.
        for tr in list(rig.animation_data.nla_tracks):
            if tr.name==name:rig.animation_data.nla_tracks.remove(tr)
        old=bpy.data.actions.get(name)
        if old:bpy.data.actions.remove(old)
        action=bpy.data.actions.new(name);action.use_fake_user=True
        frames=round(duration*fps)
        for frame in range(frames+1):
            rig.animation_data.action=None;bpy.context.scene.frame_set(frame)
            pose(rig,name,frame/frames)
            rig.animation_data.action=action
            for p in rig.pose.bones:
                p.keyframe_insert('rotation_euler',frame=frame,group=p.name)
                p.keyframe_insert('location',frame=frame,group=p.name)
                p.keyframe_insert('scale',frame=frame,group=p.name)
        rig.animation_data.action=None
        track=rig.animation_data.nla_tracks.new();track.name=name
        strip=track.strips.new(name,0,action);strip.action_frame_start=0;strip.action_frame_end=frames;track.mute=True
        metadata[name]={'seconds':frames/fps,'frames':frames+1,'fps':fps,'loop':loop,'root_motion':False}
    reset(rig);bpy.context.view_layer.update()
    return metadata
