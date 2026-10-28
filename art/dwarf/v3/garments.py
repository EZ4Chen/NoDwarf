"""V3 fitted clothing, built from anatomical ring profiles (no legacy block meshes).
Blender entry: build(rig) -> {shirt, trousers, boots, armor, helmet: [MeshObject]}.
Coordinates: metres, Blender +Z up, -Y forward. Never edits the armature or visibility.
"""
import math
import bpy
from mathutils import Vector

BODY_RINGS=[(.46,.20,.14,0),(.55,.25,.175,-.012),(.65,.265,.205,-.025),(.76,.27,.19,-.005),(.88,.30,.165,0),(.98,.275,.14,0),(1.035,.13,.105,0)]
COLORS={'shirt':(.15,.245,.18,1),'trim':(.36,.265,.12,1),'pants':(.13,.17,.19,1),'leather':(.235,.12,.065,1),'edge':(.10,.055,.03,1),'metal':(.31,.365,.38,1),'brass':(.48,.32,.12,1)}

def torso_weights(z):
    if z<=.58:return {'pelvis':1}
    if z<.72:
        t=(z-.58)/.14;return {'pelvis':1-t,'spine':t}
    if z<.88:
        t=(z-.72)/.16;return {'spine':1-t,'chest':t}
    return {'chest':1}

def skin_weights(p):
    x,y,z=p;side='L' if x>0 else 'R';ax=abs(x)
    def blend(a,b,t):
        t=max(0,min(1,t));return {a:1-t,b:t}
    if z>1.07:return blend('neck','head',(z-1.07)/.045)
    if z>1 and ax<.14:return blend('chest','neck',(z-1)/.07)
    if ax>.31 and z>.4:
        if ax>.65:return {'hand.'+side:1}
        if ax>.59:return blend('forearm.'+side,'hand.'+side,(ax-.59)/.075)
        if ax>.52:return {'forearm.'+side:1}
        if ax>.46:return blend('upper_arm.'+side,'forearm.'+side,(ax-.46)/.09)
        return blend('chest','upper_arm.'+side,(ax-.31)/.07)
    if z<.48:
        if z<.12:return {'foot.'+side:1}
        if z<.18:return blend('foot.'+side,'shin.'+side,(z-.12)/.06)
        if z<.26:return {'shin.'+side:1}
        if z<.32:return blend('shin.'+side,'thigh.'+side,(z-.26)/.06)
        return {'thigh.'+side:1}
    return torso_weights(z)

def _material(metal):
    name='Dwarf palette V3 metal' if metal else 'Dwarf palette V3 fabric'
    if bpy.data.materials.get(name):return bpy.data.materials[name]
    m=bpy.data.materials.new(name);m.use_nodes=True
    p=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
    if p is None:
        p=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled');out=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(p.outputs['BSDF'],out.inputs['Surface'])
    p.inputs['Metallic'].default_value=.65 if metal else 0;p.inputs['Roughness'].default_value=.48 if metal else .84
    color=m.node_tree.nodes.new('ShaderNodeVertexColor');color.layer_name='Palette'
    m.node_tree.links.new(color.outputs['Color'],p.inputs['Base Color'])
    m.diffuse_color=(.3,.3,.3,1)
    return m

class Surface:
    def __init__(self,name,rig):
        self.name=name;self.rig=rig;self.v=[];self.f=[];self.w=[];self.colors=[];self.metal=[]
    def add(self,verts,faces,weights,color,metal=False):
        offset=len(self.v);self.v.extend(verts)
        self.w.extend([weights]*len(verts) if isinstance(weights,dict) else weights)
        for face in faces:
            self.f.append(tuple(offset+i for i in face));self.colors.append(COLORS[color]);self.metal.append(metal)
    def tube(self,points,widths,depths,weights,color,n=12,caps=False,metal=False):
        v=[];f=[];w=[]
        for j,p in enumerate(points):
            p=Vector(p)
            direction=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])
            direction.normalize();a=Vector((0,1,0)).cross(direction).normalized();b=direction.cross(a).normalized()
            for i in range(n):
                theta=2*math.pi*i/n
                v.append(tuple(p+a*(math.cos(theta)*widths[j])+b*(math.sin(theta)*depths[j])));w.append(weights[j])
        for j in range(len(points)-1):
            for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
        if caps:f.extend([tuple(reversed(range(n))),tuple((len(points)-1)*n+i for i in range(n))])
        self.add(v,f,w,color,metal)
    def rings(self,rings,weights,color,n=16,caps=False,metal=False):
        v=[];f=[];w=[]
        for j,r in enumerate(rings):
            z,rx,ry,cy=r[:4];cx=r[4] if len(r)>4 else 0
            for i in range(n):
                t=math.tau*i/n
                a=math.cos(t);b=math.sin(t)
                power=.65 if self.name=='boots' else .92
                v.append((cx+rx*math.copysign(abs(a)**power,a),cy+ry*math.copysign(abs(b)**power,b),z));w.append(weights[j])
        for j in range(len(rings)-1):
            for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
        if caps:f.extend([tuple(reversed(range(n))),tuple((len(rings)-1)*n+i for i in range(n))])
        self.add(v,f,w,color,metal)
    def plate(self,center,rx,rz,depth,color,bone,metal=False):
        # Small chamfered clasp/rivet shape, not a cube body component.
        cx,cy,cz=center;outline=[(-rx*.75,-rz),(rx*.75,-rz),(rx,-rz*.65),(rx,rz*.65),(rx*.75,rz),(-rx*.75,rz),(-rx,rz*.65),(-rx,-rz*.65)]
        v=[(cx+x,cy+y,cz+z) for y in [-depth/2,depth/2] for x,z in outline]
        f=[tuple(reversed(range(8))),tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
        self.add(v,f,{bone:1},color,metal)
    def finish(self):
        data=bpy.data.meshes.new('V3_'+self.name);data.from_pydata(self.v,[],self.f);data.update()
        ob=bpy.data.objects.new('V3_'+self.name,data);bpy.context.scene.collection.objects.link(ob)
        types=sorted(set(self.metal));data.materials.clear()
        for metal in types:data.materials.append(_material(metal))
        color=data.color_attributes.new(name='Palette',type='FLOAT_COLOR',domain='CORNER')
        for p,c,m in zip(data.polygons,self.colors,self.metal):
            p.material_index=types.index(m);p.use_smooth=True
            for loop in p.loop_indices:color.data[loop].color=c
        groups={}
        # Retain per-ring anatomical weights: position-only classification mislabels trouser hips as arms.
        for i,weights in enumerate(self.w):
            total=sum(weights.values());assert total>0
            for bone,value in weights.items():
                assert bone in self.rig.data.bones,bone
                if value<=0:continue
                if bone not in groups:groups[bone]=ob.vertex_groups.new(name=bone)
                groups[bone].add([i],value/total,'REPLACE')
        ob.parent=self.rig;mod=ob.modifiers.new('V3 shared skeleton','ARMATURE');mod.object=self.rig
        # Outward normals on hand-authored caps and gently bent ring shells.
        import bmesh
        bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free()
        data.calc_loop_triangles();ob['triangles']=len(data.loop_triangles)
        ob['garment_category']=self.name
        return ob

def _sleeves(s,rig,color,clearance=.015):
    for side,sign in [('L',1),('R',-1)]:
        u='upper_arm.'+side;f='forearm.'+side
        shoulder=rig.data.bones[u].head_local;elbow=rig.data.bones[u].tail_local
        points=[Vector((sign*.22,0,.91)),shoulder,shoulder.lerp(elbow,.45),shoulder.lerp(elbow,.78)]
        s.tube(points,[.15+clearance,.14+clearance,.125+clearance,.112+clearance],[.18+clearance,.16+clearance,.13+clearance,.115+clearance],[{'chest':1},{'chest':.25,u:.75},{u:1},{u:.9,f:.1}],color,12,True)
        a=shoulder.lerp(elbow,.75);b=shoulder.lerp(elbow,.82)
        s.tube([a,b],[.114+clearance]*2,[.117+clearance]*2,[{u:.92,f:.08},{u:.86,f:.14}],'trim',12,False)

def _torso(s,color,clearance):
    rings=[(z,rx+clearance,ry+clearance+(.035 if .76<=z<=.98 else 0),cy) for z,rx,ry,cy in BODY_RINGS[1:]]
    s.rings(rings,[torso_weights(z) for z,_,_,_ in rings],color)
    # A fitted hem follows the belly instead of a rigid rectangular belt shell.
    s.rings([(.545,.25+clearance,.175+clearance,-.012),(.568,.255+clearance,.182+clearance,-.015)],[torso_weights(.545),torso_weights(.568)],'trim')
    s.rings([(1.028,.143+clearance,.113+clearance,0),(1.04,.13+clearance,.105+clearance,0)],[{'chest':1}]*2,'trim')

def build(rig):
    assert rig.type=='ARMATURE'
    result={};shirt=Surface('shirt',rig);_torso(shirt,'shirt',.015);_sleeves(shirt,rig,'shirt')
    for z,y in [(.62,-.242),(.75,-.221),(.86,-.194)]:shirt.plate((0,y,z),.009,.009,.008,'brass','spine' if z<.76 else 'chest',True)
    result['shirt']=[shirt.finish()]
    pants=Surface('trousers',rig)
    pants.rings([(.43,.218,.16,0),(.49,.24,.174,0),(.555,.269,.194,-.012)],[{'pelvis':1}]*3,'pants',16,True)
    for side,sign in [('L',1),('R',-1)]:
        t='thigh.'+side;k='shin.'+side
        hip=rig.data.bones[t].head_local;knee=rig.data.bones[t].tail_local;ankle=rig.data.bones[k].tail_local
        points=[hip+Vector((0,0,.085)),hip,hip.lerp(knee,.5),knee,knee.lerp(ankle,.55),ankle+Vector((0,0,.02))]
        pants.tube(points,[.16,.15,.14,.125,.13,.105],[.19,.17,.148,.128,.135,.11],[{'pelvis':.65,t:.35},{'pelvis':.2,t:.8},{t:1},{t:.5,k:.5},{k:1},{k:1}],'pants',12,True)
    pants.rings([(.535,.267,.195,-.012),(.564,.273,.199,-.012)],[{'pelvis':1}]*2,'edge')
    pants.plate((0,-.215,.55),.035,.021,.012,'brass','pelvis',True)
    result['trousers']=[pants.finish()]
    boots=Surface('boots',rig)
    for side,sign in [('L',1),('R',-1)]:
        foot='foot.'+side;shin='shin.'+side;ankle=rig.data.bones[shin].tail_local;x=ankle.x
        # Broad toe, raised instep, narrow ankle: continuous boot upper and sole.
        rings=[(.018,.137,.207,-.065,x),(.05,.14,.207,-.065,x),(.09,.14,.201,-.062,x),(.14,.13,.172,-.045,x),(.185,.147,.15,-.006,x),(.245,.147,.15,0,x)]
        weights=[{foot:1},{foot:1},{foot:1},{foot:.8,shin:.2},{foot:.2,shin:.8},{shin:1}]
        boots.rings(rings,weights,'leather',16,True)
        boots.rings([(.009,.141,.21,-.065,x),(.04,.143,.212,-.065,x)],[{foot:1}]*2,'edge',16,True)
        boots.rings([(.232,.153,.156,0,x),(.257,.153,.156,0,x)],[{shin:1}]*2,'edge',12)
        boots.plate((x,-.137,.208),.028,.017,.014,'brass',shin,True)
    result['boots']=[boots.finish()]
    armor=Surface('armor',rig);_torso(armor,'leather',.028);_sleeves(armor,rig,'leather',.025)
    # Tailored reinforcement bands and shallow shoulder shells, not cube pauldrons.
    armor.rings([(.58,.287,.219,-.015),(.608,.289,.23,-.02)],[torso_weights(.58),torso_weights(.608)],'edge')
    for side,sign in [('L',1),('R',-1)]:
        u='upper_arm.'+side;shoulder=rig.data.bones[u].head_local;elbow=rig.data.bones[u].tail_local
        armor.tube([Vector((sign*.27,0,.925)),shoulder+Vector((0,0,.025)),shoulder.lerp(elbow,.35)],[.135,.161,.145],[.161,.171,.154],[{'chest':.7,u:.3},{'chest':.2,u:.8},{u:1}],'metal',12,True,True)
        for z,y in [(.67,-.224),(.81,-.20)]:armor.plate((sign*.13,y,z),.009,.009,.008,'brass','spine' if z<.75 else 'chest',True)
    result['armor']=[armor.finish()]
    helmet=Surface('helmet',rig)
    # Head/hair bounds supplied by body author: x +/- .19, y -.15..+.17, top1.415.
    # Open-bottom dome, enough side clearance for both hair variants.
    rings=[(1.205,.218,.195,.01),(1.28,.225,.202,.01),(1.36,.219,.195,.01),(1.415,.18,.16,.01),(1.451,.10,.085,.01),(1.465,.008,.008,.01)]
    helmet.rings(rings,[{'head':1}]*len(rings),'metal',20,False,True)
    helmet.rings([(1.202,.223,.2,.01),(1.23,.226,.203,.01)],[{'head':1}]*2,'brass',20,False,True)
    result['helmet']=[helmet.finish()]
    assert sum(o['triangles'] for objects in result.values() for o in objects)<6000
    return result
