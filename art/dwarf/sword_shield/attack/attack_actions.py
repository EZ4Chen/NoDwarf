"""Sword/shield display actions for the unchanged 23-bone V2 dwarf rig.
Run inside Blender. Public entry: build_actions(rig, fps=24).
All targets are in the existing V2 Blender armature space (front = -Y).
"""
import bpy, math
from mathutils import Vector, Quaternion

class RigPose:
    def __init__(self, rig):
        self.rig=rig
        for b in rig.pose.bones:b.rotation_mode='XYZ'
    def reset(self):
        for b in self.rig.pose.bones:
            b.location=(0,0,0);b.rotation_euler=(0,0,0);b.scale=(1,1,1)
    def rot(self,name,x=0,y=0,z=0):
        self.rig.pose.bones[name].rotation_euler=tuple(math.radians(v) for v in (x,y,z))
    def update(self):bpy.context.view_layer.update()
    def world_rotation(self,name,q):
        b=self.rig.pose.bones[name];rest=b.bone.matrix_local.to_quaternion()
        parent=Quaternion()
        if b.parent:parent=b.parent.matrix.to_quaternion() @ b.parent.bone.matrix_local.to_quaternion().inverted()
        b.rotation_euler=((parent @ rest).inverted() @ q).to_euler('XYZ');self.update()
    def aim(self,name,target):
        b=self.rig.pose.bones[name];rest=b.bone
        q=(rest.tail_local-rest.head_local).normalized().rotation_difference((Vector(target)-b.head).normalized())
        self.world_rotation(name,q @ rest.matrix_local.to_quaternion())
    def arm(self,side,elbow,wrist,hand_angle=0,hand_roll=0):
        self.aim('upper_arm.'+side,elbow);self.aim('forearm.'+side,wrist)
        q=Quaternion((1,0,0),math.radians(hand_angle)) @ Quaternion((0,1,0),math.radians(hand_roll))
        self.world_rotation('hand.'+side,q @ self.rig.data.bones['hand.'+side].matrix_local.to_quaternion())
    def combat_idle(self,t=0):
        a=math.sin(t*math.tau)
        self.rot('chest',x=2+a*.7);self.rot('head',x=-2)
        self.update()
        self.arm('R',(-.54,-.12,.74),(-.56,-.30,.76),-8)
        self.arm('L',(.52,-.12,.73),(.38,-.32,.77),0)


def smooth_keys(t,keys):
    for (a,x),(b,y) in zip(keys,keys[1:]):
        if a<=t<=b:
            u=(t-a)/(b-a);u=u*u*(3-2*u)
            return x+(y-x)*u
    return keys[-1][1]


def bake_action(rig,name,seconds,pose,fps=24,loop=False):
    """Add a fully sampled pose action and a muted NLA export track. Never changes rest."""
    rig.animation_data_create()
    rig.animation_data.action=None
    for tr in list(rig.animation_data.nla_tracks):
        if tr.name==name:rig.animation_data.nla_tracks.remove(tr)
        else:tr.mute=True
    old=bpy.data.actions.get(name)
    if old:bpy.data.actions.remove(old)
    action=bpy.data.actions.new(name);action.use_fake_user=True
    n=round(seconds*fps)
    controller=RigPose(rig)
    samples=[]
    for i in range(n+1):
        rig.animation_data.action=None;bpy.context.scene.frame_set(i+1)
        controller.reset();pose(i/n);controller.update()
        assert rig.pose.bones['root'].location.length<1e-7
        samples.append({bone:list(rig.pose.bones[bone].matrix.translation) for bone in ['slot_hand_r','slot_hand_l','head']})
        rig.animation_data.action=action
        for b in rig.pose.bones:
            b.keyframe_insert('rotation_euler',frame=i+1,group=b.name)
            b.keyframe_insert('location',frame=i+1,group=b.name)
            b.keyframe_insert('scale',frame=i+1,group=b.name)
    track=rig.animation_data.nla_tracks.new();track.name=name
    strip=track.strips.new(name,1,action);strip.action_frame_start=1;strip.action_frame_end=n+1;track.mute=True
    rig.animation_data.action=None
    return {'seconds':seconds,'frames':n+1,'fps':fps,'loop':loop,'samples_blender':samples}


def build_actions(rig,fps=24):
    p=RigPose(rig)
    def idle(t):
        a=math.sin(t*math.tau)
        p.rot('chest',x=a*1.2);p.rot('head',z=a*.8)
        for side,s in [('L',1),('R',-1)]:p.rot('upper_arm.'+side,z=s*17);p.rot('forearm.'+side,x=-6)
    def walk(t):
        a=math.sin(t*math.tau)
        for side,s in [('L',1),('R',-1)]:
            p.rot('thigh.'+side,x=a*s*24)
            p.rot('shin.'+side,x=-max(0,a*s)*32)
            p.rot('foot.'+side,x=max(0,a*s)*8)
            p.rot('upper_arm.'+side,x=-a*s*18,z=s*17)
            p.rot('forearm.'+side,x=-10-max(0,-a*s)*10)
        rig.pose.bones['pelvis'].location.z=.008*(1-math.cos(t*math.tau*2))
        p.rot('chest',y=a*2)
    def slash(t):
        # 0-.36 preparation, .36-.62 effective swing, .62-1 recovery.
        lift=smooth_keys(t,[(0,0),(.30,1),(.36,1),(.62,0),(1,0)])
        strike=smooth_keys(t,[(0,0),(.36,0),(.62,1),(.70,1),(1,0)])
        p.rot('chest',x=2+6*strike,y=-7*lift+10*strike)
        p.rot('head',x=-2-3*strike,y=4*lift-4*strike)
        p.update()
        elbow=Vector((-.54,-.12,.74)).lerp(Vector((-.60,-.02,1.04)),lift)
        wrist=Vector((-.56,-.30,.76)).lerp(Vector((-.59,-.12,1.22)),lift)
        elbow=elbow.lerp(Vector((-.48,-.25,.78)),strike)
        wrist=wrist.lerp(Vector((-.37,-.51,.73)),strike)
        p.arm('R',elbow,wrist,-8*(1-lift)+98*strike)
        # Shield stays offset to the left; no implied omnidirectional blocking.
        p.arm('L',(.52,-.12,.73),(.38,-.32,.77),0)
    specs=[('idle',2,idle,True),('walk',1,walk,True),('sword_shield_idle',2,p.combat_idle,True),('sword_slash',1,slash,False)]
    result={}
    for name,duration,fn,loop in specs:result[name]=bake_action(rig,name,duration,fn,fps,loop)
    result['sword_slash']['phases_seconds']={'windup':[0,.36],'active_visual':[.36,.62],'recovery':[.62,1]}
    result['sword_slash']['damage_callbacks']=False
    p.reset();p.combat_idle(0)
    return result
