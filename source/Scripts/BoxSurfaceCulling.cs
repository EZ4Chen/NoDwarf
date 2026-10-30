using Godot;
namespace NoDwarf;

// Subtract only covered rectangles from the faces of axis-aligned box parts.
// Preserve exposed steps, UVs, materials and original picking bounds. Curved,
// rotated and alpha-cutout parts are deliberately not treated as opaque boxes.
internal static class BoxSurfaceCulling
{
    private const float Epsilon=.00001f;
    private readonly record struct Vertex(Vector3 Position,Vector2 Uv,Vector3 Normal)
    {
        public Vertex Lerp(Vertex b,float t)=>new(Position.Lerp(b.Position,t),Uv.Lerp(b.Uv,t),Normal.Lerp(b.Normal,t));
    }
    public static void Apply(Node root)
    {
        var parts=root.GetChildren().OfType<MeshInstance3D>()
            .Where(m=>m.Visible&&m.Mesh is not null&&(m.Mesh is BoxMesh||m.HasMeta("voxel_bounds"))&&m.Basis.IsEqualApprox(Basis.Identity))
            .Where(m=>Enumerable.Range(0,m.Mesh.GetSurfaceCount()).All(i=>m.GetActiveMaterial(i) is not BaseMaterial3D mat||mat.Transparency==BaseMaterial3D.TransparencyEnum.Disabled))
            .Select(m=>(Node:m,Bounds:m.HasMeta("voxel_bounds")?m.GetMeta("voxel_bounds").AsAabb():m.Mesh.GetAabb())).ToArray();
        if(parts.Length<2)return;
        for(int index=0;index<parts.Length;index++)
        {
            var (node,bounds)=parts[index];var original=node.Mesh;
            var neighbors=parts.Select((p,i)=>(Box:new Aabb(p.Bounds.Position+p.Node.Position-node.Position,p.Bounds.Size),Index:i)).Where(p=>p.Index!=index).ToArray();
            var result=new ArrayMesh();bool changed=false;
            for(int surfaceIndex=0;surfaceIndex<original.GetSurfaceCount();surfaceIndex++)
            {
                using var arrays=original.SurfaceGetArrays(surfaceIndex);
                var positions=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var uvs=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var indices=arrays[(int)Mesh.ArrayType.Index].VariantType==Variant.Type.Nil?Enumerable.Range(0,positions.Length).ToArray():arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                using var output=new SurfaceTool();output.Begin(Mesh.PrimitiveType.Triangles);output.SetMaterial(node.GetActiveMaterial(surfaceIndex));int vertices=0;
                for(int t=0;t<indices.Length;t+=3)
                {
                    Vertex V(int i)=>new(positions[i],uvs.Length>i?uvs[i]:Vector2.Zero,normals[i]);
                    var a=V(indices[t]);var b=V(indices[t+1]);var c=V(indices[t+2]);
                    int axis=(int)a.Normal.Abs().MaxAxisIndex();float sign=a.Normal[axis],plane=a.Position[axis];
                    if(Math.Abs(sign)<.999f)throw new InvalidOperationException("Box face must be axis aligned");
                    int u=(axis+1)%3,v=(axis+2)%3;var polygons=new List<List<Vertex>>{new(){a,b,c}};
                    foreach(var (box,otherIndex) in neighbors)
                    {
                        float near=box.Position[axis],far=box.End[axis];
                        bool outward=sign>0?near<=plane+Epsilon&&far>plane+Epsilon:far>=plane-Epsilon&&near<plane-Epsilon;
                        bool sameSurface=otherIndex<index&&(sign>0?Math.Abs(far-plane)<Epsilon&&near<plane:Math.Abs(near-plane)<Epsilon&&far>plane);
                        if(!outward&&!sameSurface)continue;
                        var remaining=new List<List<Vertex>>();
                        foreach(var polygon in polygons)
                        {
                            if(polygon.Max(p=>p.Position[u])<=box.Position[u]+Epsilon||polygon.Min(p=>p.Position[u])>=box.End[u]-Epsilon||
                                polygon.Max(p=>p.Position[v])<=box.Position[v]+Epsilon||polygon.Min(p=>p.Position[v])>=box.End[v]-Epsilon)
                            {remaining.Add(polygon);continue;}
                            changed=true;var inside=polygon;
                            foreach(var (dim,edge,positive) in new[]{(u,box.Position[u],true),(u,box.End[u],false),(v,box.Position[v],true),(v,box.End[v],false)})
                            {
                                var outside=Clip(inside,dim,edge,!positive);if(outside.Count>=3)remaining.Add(outside);
                                inside=Clip(inside,dim,edge,positive);if(inside.Count<3)break;
                            }
                        }
                        polygons=remaining;if(polygons.Count==0)break;
                    }
                    foreach(var polygon in polygons)for(int i=1;i+1<polygon.Count;i++)
                    {
                        if((polygon[i].Position-polygon[0].Position).Cross(polygon[i+1].Position-polygon[0].Position).LengthSquared()<1e-12f)continue;
                        foreach(var vertex in new[]{polygon[0],polygon[i],polygon[i+1]})
                        {output.SetNormal(vertex.Normal);output.SetUV(vertex.Uv);output.AddVertex(vertex.Position);vertices++;}
                    }
                }
                if(vertices>0)output.Commit(result);
            }
            if(changed)
            {
                node.SetMeta("voxel_bounds",bounds);node.Mesh=result;
                // Materials are now assigned to the surviving surfaces, whose indices can change.
                for(int s=0;s<result.GetSurfaceCount();s++)node.SetSurfaceOverrideMaterial(s,null);
            }
            else result.Dispose();
        }
    }
    private static List<Vertex> Clip(List<Vertex> polygon,int axis,float edge,bool positive)
    {
        var output=new List<Vertex>();if(polygon.Count==0)return output;
        float Distance(Vertex p)=>(p.Position[axis]-edge)*(positive?1:-1);
        var previous=polygon[^1];float before=Distance(previous);
        foreach(var current in polygon)
        {
            float after=Distance(current);
            if((before>=0)!=(after>=0))output.Add(previous.Lerp(current,before/(before-after)));
            if(after>=0)output.Add(current);previous=current;before=after;
        }
        return output;
    }
}
